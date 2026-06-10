using System.Collections.Generic;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
public class FBXRuntimePedestrianAgent : Agent
{
    [Header("Runtime References")]
    public Transform simulationFrame;
    public Transform currentTargetGate;
    public Transform westGate;
    public Transform eastGate;

    [Header("Movement")]
    public float moveSpeed = 3.0f;
    public float turnSpeed = 180f;
    public float minForwardRatio = 0.0f;

    [Header("Local Bounds")]
    public float corridorHalfLength = 150f;
    public float corridorHalfWidth = 15f;
    public float boundaryMargin = 1.5f;

    [Header("Neighbor Observation")]
    public LayerMask pedestrianLayer;
    public int maxNeighbors = 6;
    public float neighborRadius = 5f;

    [Header("Wrong Exit Check")]
    public LayerMask wrongExitLayer;
    public float wrongExitCheckRadius = 0.5f;

    private Rigidbody rb;
    private FBXFinalSceneController controller;

    private bool runtimeActive = false;
    private int sourceExitIndex = -1;
    private float spawnTime;
    private float lockedY;
    private float targetSideFlag;

    public int SourceExitIndex => sourceExitIndex;
    public float SpawnTime => spawnTime;

    public override void Initialize()
    {
        SetupRigidbody();
    }

    private void SetupRigidbody()
    {
        if (rb == null)
        {
            rb = GetComponent<Rigidbody>();
        }

        rb.useGravity = false;
        rb.isKinematic = false;

        rb.constraints =
            RigidbodyConstraints.FreezePositionY |
            RigidbodyConstraints.FreezeRotationX |
            RigidbodyConstraints.FreezeRotationZ;
    }

    public void RuntimeActivate(
        Vector3 spawnPosition,
        Transform targetGate,
        int exitIndex,
        FBXFinalSceneController owner,
        Transform frame,
        Transform west,
        Transform east
    )
    {
        SetupRigidbody();

        controller = owner;
        simulationFrame = frame;
        westGate = west;
        eastGate = east;
        currentTargetGate = targetGate;
        sourceExitIndex = exitIndex;
        spawnTime = Time.time;
        runtimeActive = true;

        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        lockedY = spawnPosition.y;
        transform.position = spawnPosition;

        if (currentTargetGate == eastGate)
        {
            targetSideFlag = 1f;
        }
        else if (currentTargetGate == westGate)
        {
            targetSideFlag = -1f;
        }
        else
        {
            targetSideFlag = 0f;
        }

        FaceTargetGate();

        gameObject.SetActive(true);
    }

    public void RuntimeDeactivate()
    {
        runtimeActive = false;

        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        gameObject.SetActive(false);
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

    public override void CollectObservations(VectorSensor sensor)
    {
        AddSelfAndTargetObservations(sensor);
        AddNeighborObservations(sensor);
    }

    private void AddSelfAndTargetObservations(VectorSensor sensor)
    {
        if (!runtimeActive || currentTargetGate == null || simulationFrame == null)
        {
            for (int i = 0; i < 8; i++)
            {
                sensor.AddObservation(0f);
            }

            return;
        }

        Vector3 localTarget = transform.InverseTransformPoint(currentTargetGate.position);
        Vector3 localVelocity = transform.InverseTransformDirection(rb.velocity);
        Vector3 localPosInFrame = simulationFrame.InverseTransformPoint(transform.position);

        sensor.AddObservation(localTarget.x / corridorHalfLength);
        sensor.AddObservation(localTarget.z / corridorHalfWidth);
        sensor.AddObservation(HorizontalDistance(transform.position, currentTargetGate.position) / corridorHalfLength);

        sensor.AddObservation(localVelocity.x / moveSpeed);
        sensor.AddObservation(localVelocity.z / moveSpeed);

        sensor.AddObservation(localPosInFrame.x / corridorHalfLength);
        sensor.AddObservation(localPosInFrame.z / corridorHalfWidth);

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
        if (!runtimeActive || currentTargetGate == null)
        {
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
        nextPosition.y = lockedY;

        rb.MovePosition(nextPosition);

        if (IsInsideWrongExit())
        {
            controller.ReportAgentFailed(this, "WrongExit");
            return;
        }

        if (IsOutOfBounds())
        {
            controller.ReportAgentFailed(this, "OutOfBounds");
            return;
        }
    }

    private bool IsInsideWrongExit()
    {
        Collider[] hits = Physics.OverlapSphere(
            transform.position,
            wrongExitCheckRadius,
            wrongExitLayer,
            QueryTriggerInteraction.Collide
        );

        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i] != null && hits[i].CompareTag("WrongExit"))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsOutOfBounds()
    {
        if (simulationFrame == null)
        {
            return false;
        }

        Vector3 localPos = simulationFrame.InverseTransformPoint(transform.position);

        float xLimit = corridorHalfLength + boundaryMargin;
        float zLimit = corridorHalfWidth + boundaryMargin;

        return localPos.x < -xLimit ||
               localPos.x > xLimit ||
               localPos.z < -zLimit ||
               localPos.z > zLimit;
    }

    private float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!runtimeActive)
        {
            return;
        }

        if (other.CompareTag("Gate"))
        {
            controller.ReportAgentArrived(this, other.transform);
        }

        if (other.CompareTag("WrongExit"))
        {
            controller.ReportAgentFailed(this, "WrongExit");
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!runtimeActive)
        {
            return;
        }

        if (collision.collider.CompareTag("Wall"))
        {
            controller.ReportWallCollision();
        }

        if (collision.collider.CompareTag("Obstacle"))
        {
            controller.ReportObstacleCollision();
        }

        if (collision.collider.CompareTag("Pedestrian"))
        {
            controller.ReportPedestrianCollision();
        }
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