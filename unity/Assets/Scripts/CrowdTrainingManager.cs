using UnityEngine;

public class CrowdTrainingManager : MonoBehaviour
{
    [Header("Agent")]
    public CrowdPedestrianAgent agentPrefab;
    public Transform agentRoot;
    public int trainingAgentCount = 30;

    [Header("Scene References")]
    public Transform[] spawnZones;
    public Transform westGate;
    public Transform eastGate;

    [Header("Training Exit Sampling")]
    public int[] trainingExitIndices = new int[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };

    [Header("Spawn Jitter")]
    public float spawnJitterX = 0.5f;
    public float spawnJitterZ = 0.5f;
    public float fixedY = 0.9f;

    [Header("Gate Allocation")]
    public float distanceWeight = 1.0f;
    public float activeLoadWeight = 0.05f;
    public float randomNoiseWeight = 1.0f;
    public float maxExtraDistanceForBalancing = 35f;

    [Header("Debug")]
    public bool showOnGUI = true;

    private int activeAssignedWest = 0;
    private int activeAssignedEast = 0;
    private int episodeFinishedCount = 0;
    private int correctArrivalCount = 0;
    private int wrongGateCount = 0;
    private int outOfBoundsCount = 0;
    private int wallCollisionCount = 0;
    private int pedestrianCollisionCount = 0;

    private void Start()
    {
        CreateTrainingAgents();
    }

    private void CreateTrainingAgents()
    {
        if (agentPrefab == null)
        {
            Debug.LogError("CrowdTrainingManager: Agent Prefab is not assigned.");
            return;
        }

        if (agentRoot == null)
        {
            GameObject root = new GameObject("CrowdTrainingAgents");
            agentRoot = root.transform;
        }

        for (int i = 0; i < trainingAgentCount; i++)
        {
            CrowdPedestrianAgent agent = Instantiate(agentPrefab, agentRoot);
            agent.name = $"CrowdTrainAgent_{i:000}";
            agent.trainingManager = this;
            agent.gameObject.SetActive(true);
        }

        Debug.Log($"Crowd training agents created: {trainingAgentCount}");
    }

    public void GetSpawnAndTarget(out Vector3 spawnPosition, out Transform targetGate)
    {
        spawnPosition = Vector3.zero;
        targetGate = eastGate;

        if (spawnZones == null || spawnZones.Length == 0)
        {
            Debug.LogError("CrowdTrainingManager: No spawn zones assigned.");
            return;
        }

        int spawnZoneIndex = ChooseSpawnZoneIndex();
        Transform spawn = spawnZones[spawnZoneIndex];

        spawnPosition = spawn.position;
        spawnPosition.y = fixedY;
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

    public void ReportCorrectArrival()
    {
        correctArrivalCount++;
        episodeFinishedCount++;
    }

    public void ReportWrongGate()
    {
        wrongGateCount++;
        episodeFinishedCount++;
    }

    public void ReportOutOfBounds()
    {
        outOfBoundsCount++;
        episodeFinishedCount++;
    }

    public void ReportWallCollision()
    {
        wallCollisionCount++;
    }

    public void ReportPedestrianCollision()
    {
        pedestrianCollisionCount++;
    }

    private float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private void OnGUI()
    {
        if (!showOnGUI)
        {
            return;
        }

        GUILayout.BeginArea(new Rect(10, 10, 360, 250), GUI.skin.box);

        GUILayout.Label("Crowd Training Manager");
        GUILayout.Label($"Training Agents: {trainingAgentCount}");
        GUILayout.Label($"Active Assigned West: {activeAssignedWest}");
        GUILayout.Label($"Active Assigned East: {activeAssignedEast}");
        GUILayout.Label($"Episodes Finished: {episodeFinishedCount}");
        GUILayout.Label($"Correct Arrivals: {correctArrivalCount}");
        GUILayout.Label($"Wrong Gate: {wrongGateCount}");
        GUILayout.Label($"Out Of Bounds: {outOfBoundsCount}");
        GUILayout.Label($"Wall Collisions: {wallCollisionCount}");
        GUILayout.Label($"Pedestrian Collisions: {pedestrianCollisionCount}");

        GUILayout.EndArea();
    }
}