using System.Collections.Generic;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
public class CrowdPedestrianAgent : Agent
{
    [Header("Manager")]
    public CrowdTrainingManager trainingManager;

    [Header("Movement")]
    public float moveSpeed = 3.0f;
    public float turnSpeed = 180f;
    public float minForwardRatio = 0.0f;
    public float fixedY = 0.9f;

    [Header("Corridor Bounds")]
    public float corridorHalfLength = 150f;
    public float corridorHalfWidth = 7.5f;
    public float boundaryMargin = 1.0f;

    [Header("Neighbor Observation")]
    public LayerMask pedestrianLayer;
    public int maxNeighbors = 6;
    public float neighborRadius = 5.0f;

    [Header("Reward")]
    public float targetReward = 2.0f;
    public float wrongGatePenalty = -1.0f;
    public float outOfBoundsPenalty = -1.0f;
    public float wallPenalty = -0.2f;
    public float pedestrianCollisionPenalty = -0.03f;
    public float stepPenalty = -0.001f;
    public float progressRewardScale = 0.03f;
    public float crowdingPenaltyScale = -0.002f;

    [Header("Episode")]
    public int maxEpisodeSteps = 2500;

    private Rigidbody rb;
    private Transform currentTargetGate;
    private float lastDistanceToTarget;
    private float targetSideFlag;
    private bool hasGateAssignment = false;

    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();

        rb.useGravity = false;
        rb.isKinematic = false;

        rb.constraints =
            RigidbodyConstraints.FreezePositionY |
            RigidbodyConstraints.FreezeRotationX |
            RigidbodyConstraints.FreezeRotationZ;

        if (trainingManager == null)
        {
            trainingManager = FindObjectOfType<CrowdTrainingManager>();
        }
    }

    public override void OnEpisodeBegin()
    {
        ReleaseCurrentGateAssignment();

        if (trainingManager == null)
        {
            trainingManager = FindObjectOfType<CrowdTrainingManager>();
        }

        Vector3 spawnPosition;
        Transform targetGate;

        trainingManager.GetSpawnAndTarget(out spawnPosition, out targetGate);

        currentTargetGate = targetGate;
        hasGateAssignment = true;

        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        spawnPosition.y = fixedY;
        transform.position = spawnPosition;

        FaceTargetGate();

        targetSideFlag = GetTargetSideFlag();

        lastDistanceToTarget = DistanceToTarget();
    }

    private void FaceTargetGate()
    {
        if (currentTargetGate == null)
        {
            return;
        }

        Vector3 direction = currentTargetGate.position - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude > 0.001f)
        {
            transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }
    }

    private float GetTargetSideFlag()
    {
        if (trainingManager == null || currentTargetGate == null)
        {
            return 0f;
        }

        if (currentTargetGate == trainingManager.eastGate)
        {
            return 1f;
        }

        if (currentTargetGate == trainingManager.westGate)
        {
            return -1f;
        }

        return 0f;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        AddSelfAndTargetObservations(sensor);
        AddNeighborObservations(sensor);
    }

    private void AddSelfAndTargetObservations(VectorSensor sensor)
    {
        if (currentTargetGate == null)
        {
            for (int i = 0; i < 8; i++)
            {
                sensor.AddObservation(0f);
            }

            return;
        }

        Vector3 localTarget = transform.InverseTransformPoint(currentTargetGate.position);
        Vector3 localVelocity = transform.InverseTransformDirection(rb.velocity);

        sensor.AddObservation(localTarget.x / corridorHalfLength);
        sensor.AddObservation(localTarget.z / corridorHalfWidth);
        sensor.AddObservation(DistanceToTarget() / corridorHalfLength);

        sensor.AddObservation(localVelocity.x / moveSpeed);
        sensor.AddObservation(localVelocity.z / moveSpeed);

        sensor.AddObservation(transform.position.x / corridorHalfLength);
        sensor.AddObservation(transform.position.z / corridorHalfWidth);

        sensor.AddObservation(targetSideFlag);
    }

    private void AddNeighborObservations(VectorSensor sensor)
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, neighborRadius, pedestrianLayer);

        List<Collider> neighbors = new List<Collider>();

        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i] == null)
            {
                continue;
            }

            if (hits[i].gameObject == gameObject)
            {
                continue;
            }

            neighbors.Add(hits[i]);
        }

        neighbors.Sort((a, b) =>
        {
            float da = Vector3.SqrMagnitude(a.transform.position - transform.position);
            float db = Vector3.SqrMagnitude(b.transform.position - transform.position);
            return da.CompareTo(db);
        });

        int observedCount = Mathf.Min(maxNeighbors, neighbors.Count);

        for (int i = 0; i < maxNeighbors; i++)
        {
            if (i < observedCount)
            {
                AddOneNeighborObservation(sensor, neighbors[i]);
            }
            else
            {
                AddEmptyNeighborObservation(sensor);
            }
        }

        int closeNeighborCount = 0;

        for (int i = 0; i < neighbors.Count; i++)
        {
            float d = Vector3.Distance(transform.position, neighbors[i].transform.position);

            if (d < 0.8f)
            {
                closeNeighborCount++;
            }
        }

        if (closeNeighborCount > 0)
        {
            AddReward(crowdingPenaltyScale * closeNeighborCount);
        }
    }

    private void AddOneNeighborObservation(VectorSensor sensor, Collider neighborCollider)
    {
        Transform other = neighborCollider.transform;
        Rigidbody otherRb = other.GetComponent<Rigidbody>();

        Vector3 localPos = transform.InverseTransformPoint(other.position);
        Vector3 relVel = Vector3.zero;

        if (otherRb != null)
        {
            relVel = transform.InverseTransformDirection(otherRb.velocity - rb.velocity);
        }

        float distance = Vector3.Distance(transform.position, other.position);
        float headingDot = Vector3.Dot(transform.forward, other.forward);

        sensor.AddObservation(localPos.x / neighborRadius);
        sensor.AddObservation(localPos.z / neighborRadius);
        sensor.AddObservation(relVel.x / moveSpeed);
        sensor.AddObservation(relVel.z / moveSpeed);
        sensor.AddObservation(distance / neighborRadius);
        sensor.AddObservation(headingDot);
    }

    private void AddEmptyNeighborObservation(VectorSensor sensor)
    {
        sensor.AddObservation(0f);
        sensor.AddObservation(0f);
        sensor.AddObservation(0f);
        sensor.AddObservation(0f);
        sensor.AddObservation(0f);
        sensor.AddObservation(0f);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (currentTargetGate == null)
        {
            AddReward(-0.01f);
            return;
        }

        float turnAction = Mathf.Clamp(actions.ContinuousActions[0], -1f, 1f);
        float forwardRaw = Mathf.Clamp(actions.ContinuousActions[1], -1f, 1f);

        float forward01 = (forwardRaw + 1f) * 0.5f;
        float forwardRatio = Mathf.Lerp(minForwardRatio, 1.0f, forward01);

        Quaternion deltaRotation = Quaternion.Euler(
            0f,
            turnAction * turnSpeed * Time.fixedDeltaTime,
            0f
        );

        rb.MoveRotation(rb.rotation * deltaRotation);

        Vector3 moveDirection = transform.forward;
        moveDirection.y = 0f;

        if (moveDirection.sqrMagnitude > 0.0001f)
        {
            moveDirection.Normalize();
        }

        Vector3 nextPosition = rb.position + moveDirection * forwardRatio * moveSpeed * Time.fixedDeltaTime;
        nextPosition.y = fixedY;

        rb.MovePosition(nextPosition);

        ApplyMovementReward();

        if (IsOutOfBounds())
        {
            FinishEpisode(outOfBoundsPenalty);
            trainingManager.ReportOutOfBounds();
            return;
        }

        if (StepCount >= maxEpisodeSteps)
        {
            FinishEpisode(-0.5f);
        }
    }

    private void ApplyMovementReward()
    {
        float currentDistance = DistanceToTarget();
        float progress = lastDistanceToTarget - currentDistance;

        AddReward(progress * progressRewardScale);
        AddReward(stepPenalty);

        lastDistanceToTarget = currentDistance;
    }

    private bool IsOutOfBounds()
    {
        float xLimit = corridorHalfLength + boundaryMargin;
        float zLimit = corridorHalfWidth + boundaryMargin;

        if (transform.position.x < -xLimit || transform.position.x > xLimit)
        {
            return true;
        }

        if (transform.position.z < -zLimit || transform.position.z > zLimit)
        {
            return true;
        }

        return false;
    }

    private float DistanceToTarget()
    {
        if (currentTargetGate == null)
        {
            return 999f;
        }

        Vector3 a = transform.position;
        Vector3 b = currentTargetGate.position;

        a.y = 0f;
        b.y = 0f;

        return Vector3.Distance(a, b);
    }

    private bool IsTargetGate(Transform gateTransform)
    {
        if (currentTargetGate == null || gateTransform == null)
        {
            return false;
        }

        return gateTransform == currentTargetGate || gateTransform.IsChildOf(currentTargetGate);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Gate"))
        {
            return;
        }

        if (IsTargetGate(other.transform))
        {
            FinishEpisode(targetReward);
            trainingManager.ReportCorrectArrival();
        }
        else
        {
            FinishEpisode(wrongGatePenalty);
            trainingManager.ReportWrongGate();
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.collider.CompareTag("Wall"))
        {
            AddReward(wallPenalty);
            trainingManager.ReportWallCollision();
        }

        if (collision.collider.CompareTag("Pedestrian"))
        {
            AddReward(pedestrianCollisionPenalty);
            trainingManager.ReportPedestrianCollision();
        }

        if (collision.collider.CompareTag("Obstacle"))
        {
            AddReward(-0.1f);
        }
    }

    private void FinishEpisode(float finalReward)
    {
        AddReward(finalReward);
        ReleaseCurrentGateAssignment();
        EndEpisode();
    }

    private void ReleaseCurrentGateAssignment()
    {
        if (!hasGateAssignment)
        {
            return;
        }

        if (trainingManager != null)
        {
            trainingManager.ReleaseGateAssignment(currentTargetGate);
        }

        hasGateAssignment = false;
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        ActionSegment<float> actions = actionsOut.ContinuousActions;

        float turn = 0f;
        float forward = -1f;

        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
        {
            turn = -1f;
        }

        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
        {
            turn = 1f;
        }

        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
        {
            forward = 1f;
        }

        actions[0] = turn;
        actions[1] = forward;
    }
}