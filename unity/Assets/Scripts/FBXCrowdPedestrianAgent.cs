using System.Collections.Generic;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
public class FBXCrowdPedestrianAgent : Agent
{
    [Header("Manager")]
    public FBXCrowdTrainingManager trainingManager;

    [Header("Frame")]
    public Transform simulationFrame;

    [Header("Movement")]
    public float moveSpeed = 3.0f;
    public float turnSpeed = 180f;
    public float minForwardRatio = 0.0f;

    [Header("Local Bounds")]
    public float corridorHalfLength = 150f;
    public float corridorHalfWidth = 7.5f;
    public float boundaryMargin = 1.5f;

    [Header("Neighbor Observation")]
    public LayerMask pedestrianLayer;
    public int maxNeighbors = 6;
    public float neighborRadius = 5f;

    [Header("Reward")]
    public float targetReward = 2.5f;
    public float wrongGatePenalty = -1.0f;
    public float wrongExitPenalty = -1.2f;
    public float outOfBoundsPenalty = -1.0f;
    public float wallPenalty = -0.25f;
    public float obstaclePenalty = -0.25f;
    public float pedestrianCollisionPenalty = -0.05f;
    public float stepPenalty = -0.001f;
    public float progressRewardScale = 0.035f;
    public float crowdingPenaltyScale = -0.001f;

    [Header("Episode")]
    public int maxEpisodeSteps = 2500;

    [Header("Improved Stuck Check")]
    public bool enableStuckCheck = true;

    // 前若干步不做卡住判断，避免刚出生、旋转、碰撞调整时被误杀
    public int stuckWarmupSteps = 200;

    // 每隔多少次 OnActionReceived 检查一次位置变化
    public int stuckCheckInterval = 30;

    // 每个检查周期内，至少移动这么多米才认为没有卡住
    public float minMoveDistancePerCheck = 0.05f;

    // 如果目标距离在检查周期内至少减少这么多，也认为没有卡住
    public float minTargetProgressPerCheck = 0.03f;

    // 连续多少次检查都没有明显移动或接近目标，才判定卡住
    public int maxConsecutiveStuckChecks = 8;

    public float stuckPenalty = -0.2f;

    private Rigidbody rb;
    private Transform currentTargetGate;

    private float lastDistanceToTarget;
    private float targetSideFlag;
    private bool hasGateAssignment = false;

    private float lockedY;

    private bool episodeTerminated = false;

    private Vector3 lastStuckCheckPosition;
    private float lastStuckCheckDistance;
    private int lastStuckCheckStep;
    private int consecutiveStuckChecks;

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
            trainingManager = FindObjectOfType<FBXCrowdTrainingManager>();
        }

        if (trainingManager != null && simulationFrame == null)
        {
            simulationFrame = trainingManager.simulationFrame;
        }
    }

    public override void OnEpisodeBegin()
    {
        episodeTerminated = false;

        ReleaseCurrentGateAssignment();

        if (trainingManager == null)
        {
            trainingManager = FindObjectOfType<FBXCrowdTrainingManager>();
        }

        if (trainingManager == null)
        {
            Debug.LogError($"{name}: trainingManager is null. Please enable FBXCrowdTrainingManager or assign it manually.");
            currentTargetGate = null;
            hasGateAssignment = false;
            return;
        }

        if (simulationFrame == null)
        {
            simulationFrame = trainingManager.simulationFrame;
        }

        if (simulationFrame == null)
        {
            Debug.LogError($"{name}: simulationFrame is null. Please assign SimulationFrame in FBXCrowdTrainingManager.");
            currentTargetGate = null;
            hasGateAssignment = false;
            return;
        }

        if (trainingManager.spawnZones == null || trainingManager.spawnZones.Length == 0)
        {
            Debug.LogError($"{name}: spawnZones are not assigned in FBXCrowdTrainingManager.");
            currentTargetGate = null;
            hasGateAssignment = false;
            return;
        }

        if (trainingManager.westGate == null || trainingManager.eastGate == null)
        {
            Debug.LogError($"{name}: westGate or eastGate is not assigned in FBXCrowdTrainingManager.");
            currentTargetGate = null;
            hasGateAssignment = false;
            return;
        }

        Vector3 spawnPosition;
        Transform targetGate;

        trainingManager.GetSpawnAndTarget(out spawnPosition, out targetGate);

        if (targetGate == null)
        {
            Debug.LogError($"{name}: targetGate is null after GetSpawnAndTarget.");
            currentTargetGate = null;
            hasGateAssignment = false;
            return;
        }

        currentTargetGate = targetGate;
        hasGateAssignment = true;

        if (rb == null)
        {
            rb = GetComponent<Rigidbody>();
        }

        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        lockedY = spawnPosition.y;
        transform.position = spawnPosition;

        FaceTargetGate();

        targetSideFlag = GetTargetSideFlag();

        lastDistanceToTarget = DistanceToTarget();

        ResetStuckCheck();
    }

    private void ResetStuckCheck()
    {
        lastStuckCheckPosition = transform.position;
        lastStuckCheckDistance = DistanceToTarget();
        lastStuckCheckStep = StepCount;
        consecutiveStuckChecks = 0;
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
        if (currentTargetGate == null || simulationFrame == null)
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
        sensor.AddObservation(DistanceToTarget() / corridorHalfLength);

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
        if (episodeTerminated)
        {
            return;
        }

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
        nextPosition.y = lockedY;

        rb.MovePosition(nextPosition);

        ApplyMovementReward();

        if (enableStuckCheck)
        {
            ApplyImprovedStuckCheck();
        }

        if (IsOutOfBounds())
        {
            if (trainingManager != null)
            {
                trainingManager.ReportOutOfBounds();
            }

            FinishEpisode(outOfBoundsPenalty);
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

    private void ApplyImprovedStuckCheck()
    {
        if (StepCount < stuckWarmupSteps)
        {
            ResetStuckCheck();
            return;
        }

        if (StepCount - lastStuckCheckStep < stuckCheckInterval)
        {
            return;
        }

        Vector3 delta = transform.position - lastStuckCheckPosition;
        delta.y = 0f;

        float movedDistance = delta.magnitude;

        float currentDistance = DistanceToTarget();
        float targetProgress = lastStuckCheckDistance - currentDistance;

        bool hasMovedEnough = movedDistance >= minMoveDistancePerCheck;
        bool hasApproachedTarget = targetProgress >= minTargetProgressPerCheck;

        if (hasMovedEnough || hasApproachedTarget)
        {
            consecutiveStuckChecks = 0;
        }
        else
        {
            consecutiveStuckChecks++;
        }

        lastStuckCheckPosition = transform.position;
        lastStuckCheckDistance = currentDistance;
        lastStuckCheckStep = StepCount;

        if (consecutiveStuckChecks >= maxConsecutiveStuckChecks)
        {
            FinishEpisode(stuckPenalty);
        }
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
        if (episodeTerminated)
        {
            return;
        }

        if (other.CompareTag("Gate"))
        {
            if (IsTargetGate(other.transform))
            {
                if (trainingManager != null)
                {
                    trainingManager.ReportCorrectArrival();
                }

                FinishEpisode(targetReward);
            }
            else
            {
                if (trainingManager != null)
                {
                    trainingManager.ReportWrongGate();
                }

                FinishEpisode(wrongGatePenalty);
            }
        }

        if (other.CompareTag("WrongExit"))
        {
            if (trainingManager != null)
            {
                trainingManager.ReportWrongExit();
            }

            FinishEpisode(wrongExitPenalty);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (episodeTerminated)
        {
            return;
        }

        if (collision.collider.CompareTag("Wall"))
        {
            AddReward(wallPenalty);

            if (trainingManager != null)
            {
                trainingManager.ReportWallCollision();
            }
        }

        if (collision.collider.CompareTag("Obstacle"))
        {
            AddReward(obstaclePenalty);

            if (trainingManager != null)
            {
                trainingManager.ReportObstacleCollision();
            }
        }

        if (collision.collider.CompareTag("Pedestrian"))
        {
            AddReward(pedestrianCollisionPenalty);

            if (trainingManager != null)
            {
                trainingManager.ReportPedestrianCollision();
            }
        }
    }

    private void FinishEpisode(float finalReward)
    {
        if (episodeTerminated)
        {
            return;
        }

        episodeTerminated = true;

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