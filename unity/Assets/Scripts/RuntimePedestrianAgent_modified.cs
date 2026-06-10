using System.Collections.Generic;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
public class RuntimePedestrianAgent : Agent
{
    [Header("Runtime Target")]
    public Transform currentTargetGate;

    [Header("Simulation Mapping")]
    public SimulationSpace simulationSpace;

    [Tooltip("用于 FBX 场景中的越界判断。建议拖入一个位于出站通道中心的空物体 SimulationOrigin，Scale 保持 (1,1,1)。如果拖入的是带缩放的 WalkableArea，本脚本会忽略它的 Scale，只使用位置和旋转。")]
    public Transform boundsCenter;

    [Header("Movement")]
    public float moveSpeed = 3.0f;
    public float turnSpeed = 180f;
    public float minForwardRatio = 0.0f;
    public float fixedY = 0.9f;
    public bool useSpawnY = true;
    public float yOffsetFromSpawn = 0.0f;
    public bool scaleMovementBySimulationSpace = true;

    [Header("Corridor Bounds")]
    public float corridorHalfLength = 150f;
    public float corridorHalfWidth = 7.5f;
    public float boundaryMargin = 1.0f;
    public bool useOutOfBoundsCheck = true;

    [Tooltip("调试阶段可勾选。勾选后越界只打印日志，不会回收 Agent。正式仿真必须取消勾选。")]
    public bool doNotFailOnOutOfBoundsForDebug = false;

    [Tooltip("调试阶段可勾选。会输出 World/Local 坐标和边界范围，用于定位为什么 OutOfBounds。")]
    public bool logOutOfBoundsDetails = false;

    [Header("Observation")]
    public Transform westGate;
    public Transform eastGate;

    [Header("Neighbor Observation")]
    public LayerMask pedestrianLayer;
    public int maxNeighbors = 6;
    public float neighborRadius = 5.0f;

    private Rigidbody rb;
    private FinalSceneController controller;

    private int sourceExitIndex = -1;
    private float spawnTime;
    private float targetSideFlag; // west = -1, east = +1
    private bool runtimeActive = false;
    private float runtimeY;

    public int SourceExitIndex => sourceExitIndex;
    public float SpawnTime => spawnTime;

    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();

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
        FinalSceneController owner,
        Transform west,
        Transform east,
        SimulationSpace simSpace,
        Transform runtimeBoundsCenter
    )
    {
        controller = owner;
        sourceExitIndex = exitIndex;
        currentTargetGate = targetGate;
        westGate = west;
        eastGate = east;
        simulationSpace = simSpace;
        boundsCenter = runtimeBoundsCenter;

        spawnTime = Time.time;
        runtimeActive = true;

        if (rb == null)
        {
            rb = GetComponent<Rigidbody>();
        }

        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        runtimeY = useSpawnY ? spawnPosition.y + yOffsetFromSpawn : fixedY;
        spawnPosition.y = runtimeY;
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

    // Backward-compatible overload: old controller with SimulationSpace but no bounds center.
    public void RuntimeActivate(
        Vector3 spawnPosition,
        Transform targetGate,
        int exitIndex,
        FinalSceneController owner,
        Transform west,
        Transform east,
        SimulationSpace simSpace
    )
    {
        RuntimeActivate(spawnPosition, targetGate, exitIndex, owner, west, east, simSpace, boundsCenter);
    }

    // Backward-compatible overload: old controller without SimulationSpace.
    public void RuntimeActivate(
        Vector3 spawnPosition,
        Transform targetGate,
        int exitIndex,
        FinalSceneController owner,
        Transform west,
        Transform east
    )
    {
        RuntimeActivate(spawnPosition, targetGate, exitIndex, owner, west, east, simulationSpace, boundsCenter);
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
        if (currentTargetGate == null)
        {
            for (int i = 0; i < 8; i++)
            {
                sensor.AddObservation(0f);
            }

            return;
        }

        Vector3 targetDeltaWorld = currentTargetGate.position - transform.position;
        Vector3 scaledTargetDeltaWorld = ScaleWorldDisplacement(targetDeltaWorld);
        Vector3 localTarget = transform.InverseTransformDirection(scaledTargetDeltaWorld);

        Vector3 scaledVelocityWorld = ScaleWorldDisplacement(rb.velocity);
        Vector3 localVelocity = transform.InverseTransformDirection(scaledVelocityWorld);

        float distanceToTarget = GetLocalDistance(transform.position, currentTargetGate.position);
        Vector3 simLocalPosition = GetSimulationLocalPosition(transform.position);

        sensor.AddObservation(localTarget.x / corridorHalfLength);
        sensor.AddObservation(localTarget.z / corridorHalfWidth);
        sensor.AddObservation(distanceToTarget / corridorHalfLength);

        sensor.AddObservation(localVelocity.x / moveSpeed);
        sensor.AddObservation(localVelocity.z / moveSpeed);

        sensor.AddObservation(simLocalPosition.x / corridorHalfLength);
        sensor.AddObservation(simLocalPosition.z / corridorHalfWidth);

        sensor.AddObservation(targetSideFlag);
    }

    private void AddNeighborObservations(VectorSensor sensor)
    {
        float worldSearchRadius = neighborRadius;

        if (simulationSpace != null)
        {
            worldSearchRadius = simulationSpace.LocalRadiusToConservativeWorldRadius(neighborRadius);
        }

        Collider[] hits = Physics.OverlapSphere(transform.position, worldSearchRadius, pedestrianLayer);

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
            float da = GetLocalSqrDistance(a.transform.position, transform.position);
            float db = GetLocalSqrDistance(b.transform.position, transform.position);
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

        Vector3 worldDelta = other.position - transform.position;
        Vector3 scaledWorldDelta = ScaleWorldDisplacement(worldDelta);
        Vector3 localPos = transform.InverseTransformDirection(scaledWorldDelta);

        Vector3 relVel = Vector3.zero;

        if (otherRb != null)
        {
            Vector3 scaledRelVelWorld = ScaleWorldDisplacement(otherRb.velocity - rb.velocity);
            relVel = transform.InverseTransformDirection(scaledRelVelWorld);
        }

        float distance = GetLocalDistance(transform.position, other.position);
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

        float localMoveDistance = forwardRatio * moveSpeed * Time.fixedDeltaTime;
        float worldMoveDistance = localMoveDistance;

        if (scaleMovementBySimulationSpace && simulationSpace != null)
        {
            worldMoveDistance = simulationSpace.LocalDistanceToWorldDistanceAlongWorldDirection(localMoveDistance, moveDirection);
        }

        Vector3 nextPosition = rb.position + moveDirection * worldMoveDistance;
        nextPosition.y = runtimeY;

        rb.MovePosition(nextPosition);

        if (useOutOfBoundsCheck && IsOutOfBounds(out Vector3 localPos, out string reason))
        {
            if (logOutOfBoundsDetails)
            {
                Debug.LogWarning(
                    $"{name} OutOfBounds. Reason={reason}, " +
                    $"World={transform.position}, Local={localPos}, " +
                    $"XLimit={corridorHalfLength + boundaryMargin:F2}, " +
                    $"ZLimit={corridorHalfWidth + boundaryMargin:F2}, " +
                    $"BoundsCenter={(boundsCenter != null ? boundsCenter.name : "None")}, " +
                    $"SimulationSpace={(simulationSpace != null ? simulationSpace.name : "None")}"
                );
            }

            if (!doNotFailOnOutOfBoundsForDebug)
            {
                if (controller != null)
                {
                    controller.ReportAgentFailed(this, "OutOfBounds");
                }
                else
                {
                    RuntimeDeactivate();
                }
            }
        }
    }

    private bool IsOutOfBounds(out Vector3 local, out string reason)
    {
        local = GetSimulationLocalPosition(transform.position);

        float xLimit = corridorHalfLength + boundaryMargin;
        float zLimit = corridorHalfWidth + boundaryMargin;

        if (local.x < -xLimit)
        {
            reason = "X smaller than minimum";
            return true;
        }

        if (local.x > xLimit)
        {
            reason = "X greater than maximum";
            return true;
        }

        if (local.z < -zLimit)
        {
            reason = "Z smaller than minimum";
            return true;
        }

        if (local.z > zLimit)
        {
            reason = "Z greater than maximum";
            return true;
        }

        reason = "Inside";
        return false;
    }

    private Vector3 GetSimulationLocalPosition(Vector3 worldPosition)
    {
        if (simulationSpace != null)
        {
            return simulationSpace.WorldToSimulationLocal(worldPosition);
        }

        if (boundsCenter != null)
        {
            // Ignore boundsCenter scale. This is important if the user drags a scaled WalkableArea.
            Vector3 delta = worldPosition - boundsCenter.position;
            return Quaternion.Inverse(boundsCenter.rotation) * delta;
        }

        return new Vector3(worldPosition.x, worldPosition.y, worldPosition.z);
    }

    private Vector3 ScaleWorldDisplacement(Vector3 worldDisplacement)
    {
        if (simulationSpace != null)
        {
            return simulationSpace.WorldDisplacementToTrainingScaledWorldDisplacement(worldDisplacement);
        }

        if (boundsCenter != null)
        {
            return Quaternion.Inverse(boundsCenter.rotation) * worldDisplacement;
        }

        return worldDisplacement;
    }

    private float GetLocalDistance(Vector3 a, Vector3 b)
    {
        if (simulationSpace != null)
        {
            return simulationSpace.LocalDistance(a, b);
        }

        Vector3 la = GetSimulationLocalPosition(a);
        Vector3 lb = GetSimulationLocalPosition(b);

        la.y = 0f;
        lb.y = 0f;

        return Vector3.Distance(la, lb);
    }

    private float GetLocalSqrDistance(Vector3 a, Vector3 b)
    {
        Vector3 la = GetSimulationLocalPosition(a);
        Vector3 lb = GetSimulationLocalPosition(b);

        la.y = 0f;
        lb.y = 0f;
        return Vector3.SqrMagnitude(la - lb);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!runtimeActive)
        {
            return;
        }

        if (other.CompareTag("Gate"))
        {
            if (controller != null)
            {
                controller.ReportAgentArrived(this, other.transform);
            }
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
            if (controller != null)
            {
                controller.ReportWallCollision();
            }
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
