using UnityEngine;

public class FBXCrowdTrainingManager : MonoBehaviour
{
    [Header("Frame")]
    public Transform simulationFrame;

    [Header("Agent")]
    public FBXCrowdPedestrianAgent agentPrefab;
    public Transform agentRoot;
    public int trainingAgentCount = 40;

    [Header("Scene References")]
    public Transform[] spawnZones;
    public Transform westGate;
    public Transform eastGate;

    [Header("Training Exit Sampling")]
    public int[] trainingExitIndices = new int[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };

    [Header("Spawn Height")]
    public bool useSpawnZoneHeight = true;
    public float spawnHeightOffset = 0.9f;

    [Header("Spawn Jitter")]
    public float spawnJitterX = 0.5f;
    public float spawnJitterZ = 0.5f;

    [Header("Gate Allocation")]
    public float distanceWeight = 1.0f;
    public float activeLoadWeight = 0.03f;
    public float randomNoiseWeight = 0.5f;
    public float maxExtraDistanceForBalancing = 25f;

    [Header("Debug")]
    public bool showOnGUI = true;

    private int activeAssignedWest = 0;
    private int activeAssignedEast = 0;

    private int correctArrivalCount = 0;
    private int wrongGateCount = 0;
    private int wrongExitCount = 0;
    private int outOfBoundsCount = 0;
    private int wallCollisionCount = 0;
    private int obstacleCollisionCount = 0;
    private int pedestrianCollisionCount = 0;

    private void Start()
    {
        CreateTrainingAgents();
    }

    private void CreateTrainingAgents()
    {
        if (agentPrefab == null)
        {
            Debug.LogError("FBXCrowdTrainingManager: Agent Prefab is not assigned.");
            return;
        }

        if (agentRoot == null)
        {
            GameObject root = new GameObject("FBXTrainingAgents");
            agentRoot = root.transform;
        }

        for (int i = 0; i < trainingAgentCount; i++)
        {
            FBXCrowdPedestrianAgent agent = Instantiate(agentPrefab, agentRoot);
            agent.name = $"FBXTrainAgent_{i:000}";
            agent.trainingManager = this;
            agent.simulationFrame = simulationFrame;
            agent.gameObject.SetActive(true);
        }

        Debug.Log($"FBX crowd training agents created: {trainingAgentCount}");
    }

    public void GetSpawnAndTarget(out Vector3 spawnPosition, out Transform targetGate)
    {
        int spawnZoneIndex = ChooseSpawnZoneIndex();
        Transform spawn = spawnZones[spawnZoneIndex];

        spawnPosition = spawn.position;

        if (useSpawnZoneHeight)
        {
            spawnPosition.y = spawn.position.y + spawnHeightOffset;
        }
        else
        {
            spawnPosition.y = spawnHeightOffset;
        }

        spawnPosition.x += Random.Range(-spawnJitterX, spawnJitterX);
        spawnPosition.z += Random.Range(-spawnJitterZ, spawnJitterZ);

        targetGate = ChooseGate(spawnPosition);

        if (targetGate == eastGate)
        {
            activeAssignedEast++;
        }
        else
        {
            activeAssignedWest++;
        }
    }

    private int ChooseSpawnZoneIndex()
    {
        if (spawnZones == null || spawnZones.Length == 0)
        {
            return 0;
        }

        if (trainingExitIndices == null || trainingExitIndices.Length == 0)
        {
            return Random.Range(0, spawnZones.Length);
        }

        int localIndex = Random.Range(0, trainingExitIndices.Length);
        int spawnZoneIndex = trainingExitIndices[localIndex];

        if (spawnZoneIndex < 0 || spawnZoneIndex >= spawnZones.Length)
        {
            return Random.Range(0, spawnZones.Length);
        }

        return spawnZoneIndex;
    }

    private Transform ChooseGate(Vector3 spawnPosition)
    {
        float distanceToWest = HorizontalDistance(spawnPosition, westGate.position);
        float distanceToEast = HorizontalDistance(spawnPosition, eastGate.position);

        float westCost =
            distanceWeight * distanceToWest +
            activeLoadWeight * activeAssignedWest +
            Random.Range(0f, randomNoiseWeight);

        float eastCost =
            distanceWeight * distanceToEast +
            activeLoadWeight * activeAssignedEast +
            Random.Range(0f, randomNoiseWeight);

        Transform nearestGate = distanceToWest <= distanceToEast ? westGate : eastGate;
        Transform costChosenGate = eastCost < westCost ? eastGate : westGate;

        float nearestDistance = Mathf.Min(distanceToWest, distanceToEast);
        float chosenDistance = costChosenGate == eastGate ? distanceToEast : distanceToWest;
        float extraDistance = chosenDistance - nearestDistance;

        if (extraDistance > maxExtraDistanceForBalancing)
        {
            return nearestGate;
        }

        return costChosenGate;
    }

    public void ReleaseGateAssignment(Transform gate)
    {
        if (gate == eastGate)
        {
            activeAssignedEast = Mathf.Max(0, activeAssignedEast - 1);
        }
        else if (gate == westGate)
        {
            activeAssignedWest = Mathf.Max(0, activeAssignedWest - 1);
        }
    }

    private float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    public void ReportCorrectArrival() => correctArrivalCount++;
    public void ReportWrongGate() => wrongGateCount++;
    public void ReportWrongExit() => wrongExitCount++;
    public void ReportOutOfBounds() => outOfBoundsCount++;
    public void ReportWallCollision() => wallCollisionCount++;
    public void ReportObstacleCollision() => obstacleCollisionCount++;
    public void ReportPedestrianCollision() => pedestrianCollisionCount++;

    private void OnGUI()
    {
        if (!showOnGUI)
        {
            return;
        }

        GUILayout.BeginArea(new Rect(10, 10, 380, 280), GUI.skin.box);

        GUILayout.Label("FBX Crowd Training");
        GUILayout.Label($"Agents: {trainingAgentCount}");
        GUILayout.Label($"Active West: {activeAssignedWest}");
        GUILayout.Label($"Active East: {activeAssignedEast}");
        GUILayout.Label($"Correct Arrival: {correctArrivalCount}");
        GUILayout.Label($"Wrong Gate: {wrongGateCount}");
        GUILayout.Label($"Wrong Exit: {wrongExitCount}");
        GUILayout.Label($"Out Of Bounds: {outOfBoundsCount}");
        GUILayout.Label($"Wall Collision: {wallCollisionCount}");
        GUILayout.Label($"Obstacle Collision: {obstacleCollisionCount}");
        GUILayout.Label($"Pedestrian Collision: {pedestrianCollisionCount}");

        GUILayout.EndArea();
    }
}