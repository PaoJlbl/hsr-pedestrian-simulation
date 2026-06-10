using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
public class PedestrianAgent : Agent
{
    [Header("Gates")]
    public Transform westGate;
    public Transform eastGate;

    [Header("Spawn Zones")]
    public Transform[] spawnZones;
    public bool assignNearestGate = true;
    public bool randomGateAssignment = false;

    [Header("Movement")]
    public float moveSpeed = 3.0f;
    public float turnSpeed = 180f;
    public float minForwardRatio = 0.0f;
    public float fixedY = 0.9f;

    [Header("Corridor Bounds")]
    public float corridorHalfLength = 150f;
    public float corridorHalfWidth = 7.5f;
    public float boundaryMargin = 2.0f;

    [Header("Training")]
    public int maxEpisodeSteps = 3500;
    public float targetReward = 2.0f;
    public float wrongGatePenalty = -1.0f;
    public float wallPenalty = -0.3f;
    public float outOfBoundsPenalty = -1.0f;
    public float stepPenalty = -0.001f;
    public float progressRewardScale = 0.03f;

    [Header("Debug")]
    public bool debugLogEpisode = false;

    private Rigidbody rb;
    private Transform currentTargetGate;
    private int currentSpawnIndex;
    private float lastDistanceToTarget;
    private float targetSideFlag; // west = -1, east = +1

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

    public override void OnEpisodeBegin()
    {
        ResetAgent();
        lastDistanceToTarget = DistanceToTargetGate();
    }

    private void ResetAgent()
    {
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        ChooseSpawnZone();
        ChooseTargetGate();
        FaceTargetGate();

        if (debugLogEpisode)
        {
            Debug.Log($"{name} spawn={currentSpawnIndex}, target={currentTargetGate.name}");
        }
    }

    private void ChooseSpawnZone()
    {
        if (spawnZones == null || spawnZones.Length == 0)
        {
            transform.position = new Vector3(0f, fixedY, -corridorHalfWidth + 1f);
            currentSpawnIndex = -1;
            return;
        }

        currentSpawnIndex = Random.Range(0, spawnZones.Length);
        Transform spawn = spawnZones[currentSpawnIndex];

        Vector3 pos = spawn.position;
        pos.y = fixedY;

        transform.position = pos;
    }

    private void ChooseTargetGate()
    {
        if (westGate == null || eastGate == null)
        {
            currentTargetGate = westGate != null ? westGate : eastGate;
            targetSideFlag = 0f;
            return;
        }

        if (randomGateAssignment)
        {
            bool goEast = Random.value > 0.5f;
            currentTargetGate = goEast ? eastGate : westGate;
        }
        else if (assignNearestGate)
        {
            float distToWest = HorizontalDistance(transform.position, westGate.position);
            float distToEast = HorizontalDistance(transform.position, eastGate.position);

            currentTargetGate = distToWest <= distToEast ? westGate : eastGate;
        }
        else
        {
            // 简单规则：X < 0 去西闸机，X >= 0 去东闸机
            currentTargetGate = transform.position.x < 0f ? westGate : eastGate;
        }

        targetSideFlag = currentTargetGate == eastGate ? 1f : -1f;
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

        // 1-3：目标闸机相对信息
        sensor.AddObservation(localTarget.x / corridorHalfLength);
        sensor.AddObservation(localTarget.z / corridorHalfWidth);
        sensor.AddObservation(DistanceToTargetGate() / corridorHalfLength);

        // 4-5：自身速度
        sensor.AddObservation(localVelocity.x / moveSpeed);
        sensor.AddObservation(localVelocity.z / moveSpeed);

        // 6-7：当前位置归一化
        sensor.AddObservation(transform.position.x / corridorHalfLength);
        sensor.AddObservation(transform.position.z / corridorHalfWidth);

        // 8：目标方向标记，西 = -1，东 = +1
        sensor.AddObservation(targetSideFlag);
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
        moveDirection.Normalize();

        Vector3 nextPosition = rb.position + moveDirection * forwardRatio * moveSpeed * Time.fixedDeltaTime;
        nextPosition.y = fixedY;

        rb.MovePosition(nextPosition);

        ApplyReward();

        if (IsOutOfBounds())
        {
            AddReward(outOfBoundsPenalty);
            EndEpisode();
            return;
        }

        if (StepCount >= maxEpisodeSteps)
        {
            AddReward(-0.5f);
            EndEpisode();
        }
    }

    private void ApplyReward()
    {
        float currentDistance = DistanceToTargetGate();
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

    private float DistanceToTargetGate()
    {
        if (currentTargetGate == null)
        {
            return 999f;
        }

        return HorizontalDistance(transform.position, currentTargetGate.position);
    }

    private float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private bool IsCorrectGate(Collider other)
    {
        if (currentTargetGate == null)
        {
            return false;
        }

        return other.transform == currentTargetGate ||
               other.transform.IsChildOf(currentTargetGate) ||
               other.transform.root == currentTargetGate.root;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Gate"))
        {
            return;
        }

        if (IsCorrectGate(other))
        {
            AddReward(targetReward);
            EndEpisode();
        }
        else
        {
            AddReward(wrongGatePenalty);
            EndEpisode();
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.collider.CompareTag("Wall"))
        {
            AddReward(wallPenalty);
        }

        if (collision.collider.CompareTag("Obstacle"))
        {
            AddReward(-0.2f);
        }

        if (collision.collider.CompareTag("Gate"))
        {
            if (IsCorrectGate(collision.collider))
            {
                AddReward(targetReward);
                EndEpisode();
            }
            else
            {
                AddReward(wrongGatePenalty);
                EndEpisode();
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