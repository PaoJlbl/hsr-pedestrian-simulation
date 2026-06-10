using System.Collections.Generic;
using UnityEngine;

public class FinalSceneController : MonoBehaviour
{
    public enum ScenarioType
    {
        Normal,
        Burst
    }

    [Header("Scene References")]
    public RuntimePedestrianAgent agentPrefab;
    public Transform agentPoolRoot;
    public Transform[] spawnZones;
    public Transform westGate;
    public Transform eastGate;
    public SimulationSpace simulationSpace;

    [Header("Scenario Selection")]
    public ScenarioType defaultScenario = ScenarioType.Normal;
    public bool autoStartOnPlay = false;

    [Header("Normal Scenario")]
    public int[] normalExitIndices = new int[] { 0, 1, 2 };
    public int[] normalExitCounts = new int[] { 256, 280, 12 };
    public float normalArrivalDuration = 300f;
    public int normalMaxActiveAgents = 200;

    [Header("Burst Scenario")]
    public int[] burstExitIndices = new int[] { 0, 1, 2, 3 };
    public int[] burstExitCounts = new int[] { 1104, 1295, 1437, 863 };
    public float burstArrivalDuration = 600f;
    public int burstMaxActiveAgents = 500;

    [Header("Pool Settings")]
    public int poolSize = 600;
    public int maxSpawnsPerFrame = 20;

    [Header("Spawn Jitter - measured in training-local corridor units")]
    public float spawnJitterX = 0.5f;
    public float spawnJitterZ = 0.5f;

    [Header("Gate Allocation")]
    public float distanceWeight = 1.0f;
    public float activeLoadWeight = 0.05f;
    public float passedLoadWeight = 0.001f;
    public float randomNoiseWeight = 1.0f;
    public float maxExtraDistanceForBalancing = 35f;

    [Header("Runtime Debug")]
    public bool showOnGUI = true;
    public bool logScenarioEvents = true;

    private readonly Queue<RuntimePedestrianAgent> availableAgents = new Queue<RuntimePedestrianAgent>();
    private readonly List<RuntimePedestrianAgent> activeAgents = new List<RuntimePedestrianAgent>();

    private bool scenarioRunning = false;
    private string currentScenarioName = "None";

    private int[] currentExitIndices;
    private int[] currentExitCounts;
    private int[] generatedPerExit;
    private float[] spawnAccumulators;
    private float[] spawnRates;

    private int currentMaxActiveAgents;
    private int totalTarget;
    private int totalGenerated;
    private int totalArrived;
    private int totalFailed;

    private int westPassed;
    private int eastPassed;
    private int activeAssignedWest;
    private int activeAssignedEast;

    private int wallCollisionCount;
    private float totalTravelTime;

    private float scenarioStartTime;

    private void Start()
    {
        if (simulationSpace != null)
        {
            simulationSpace.Rebuild();
        }

        BuildAgentPool();

        if (autoStartOnPlay)
        {
            StartScenario(defaultScenario);
        }
    }

    private void Update()
    {
        if (!scenarioRunning)
        {
            return;
        }

        SpawnArrivals();

        if (totalArrived + totalFailed >= totalTarget && totalGenerated >= totalTarget)
        {
            scenarioRunning = false;

            if (logScenarioEvents)
            {
                Debug.Log($"Scenario finished: {currentScenarioName}");
                Debug.Log($"Generated={totalGenerated}, Arrived={totalArrived}, Failed={totalFailed}");
                Debug.Log($"West={westPassed}, East={eastPassed}, AvgTime={GetAverageTravelTime():F2}s");
            }
        }
    }

    private void BuildAgentPool()
    {
        if (agentPrefab == null)
        {
            Debug.LogError("FinalSceneController: agentPrefab is not assigned.");
            return;
        }

        if (agentPoolRoot == null)
        {
            GameObject root = new GameObject("AgentPool_Runtime");
            agentPoolRoot = root.transform;
        }

        availableAgents.Clear();
        activeAgents.Clear();

        for (int i = 0; i < poolSize; i++)
        {
            RuntimePedestrianAgent agent = Instantiate(agentPrefab, agentPoolRoot);
            agent.name = $"RuntimeAgent_{i:000}";
            agent.gameObject.SetActive(false);
            availableAgents.Enqueue(agent);
        }

        if (logScenarioEvents)
        {
            Debug.Log($"Agent pool created. Pool Size = {poolSize}");
        }
    }

    public void StartNormalScenario()
    {
        StartScenario(ScenarioType.Normal);
    }

    public void StartBurstScenario()
    {
        StartScenario(ScenarioType.Burst);
    }

    public void StartScenario(ScenarioType type)
    {
        StopAndRecycleAllAgents();

        if (simulationSpace != null)
        {
            simulationSpace.Rebuild();
        }

        if (type == ScenarioType.Normal)
        {
            currentScenarioName = "Normal";
            currentExitIndices = CopyArray(normalExitIndices);
            currentExitCounts = CopyArray(normalExitCounts);
            currentMaxActiveAgents = normalMaxActiveAgents;
            PrepareScenario(normalArrivalDuration);
        }
        else
        {
            currentScenarioName = "Burst";
            currentExitIndices = CopyArray(burstExitIndices);
            currentExitCounts = CopyArray(burstExitCounts);
            currentMaxActiveAgents = burstMaxActiveAgents;
            PrepareScenario(burstArrivalDuration);
        }

        scenarioRunning = true;
        scenarioStartTime = Time.time;

        if (logScenarioEvents)
        {
            Debug.Log($"Start Scenario: {currentScenarioName}, Total={totalTarget}, MaxActive={currentMaxActiveAgents}");
        }
    }

    private void PrepareScenario(float arrivalDuration)
    {
        if (currentExitIndices == null || currentExitCounts == null)
        {
            Debug.LogError("Scenario config is null.");
            return;
        }

        if (currentExitIndices.Length != currentExitCounts.Length)
        {
            Debug.LogError("Exit Indices and Exit Counts length mismatch.");
            return;
        }

        int n = currentExitCounts.Length;

        generatedPerExit = new int[n];
        spawnAccumulators = new float[n];
        spawnRates = new float[n];

        totalTarget = 0;

        for (int i = 0; i < n; i++)
        {
            totalTarget += currentExitCounts[i];
            spawnRates[i] = currentExitCounts[i] / Mathf.Max(1f, arrivalDuration);
        }

        totalGenerated = 0;
        totalArrived = 0;
        totalFailed = 0;

        westPassed = 0;
        eastPassed = 0;
        activeAssignedWest = 0;
        activeAssignedEast = 0;

        wallCollisionCount = 0;
        totalTravelTime = 0f;
    }

    private void SpawnArrivals()
    {
        int spawnedThisFrame = 0;

        for (int i = 0; i < currentExitCounts.Length; i++)
        {
            if (spawnedThisFrame >= maxSpawnsPerFrame)
            {
                break;
            }

            if (totalGenerated >= totalTarget)
            {
                break;
            }

            if (activeAgents.Count >= currentMaxActiveAgents)
            {
                break;
            }

            if (generatedPerExit[i] >= currentExitCounts[i])
            {
                continue;
            }

            spawnAccumulators[i] += spawnRates[i] * Time.deltaTime;

            while (
                spawnAccumulators[i] >= 1f &&
                generatedPerExit[i] < currentExitCounts[i] &&
                activeAgents.Count < currentMaxActiveAgents &&
                availableAgents.Count > 0 &&
                spawnedThisFrame < maxSpawnsPerFrame
            )
            {
                SpawnOneAgentFromExit(i);

                spawnAccumulators[i] -= 1f;
                spawnedThisFrame++;
            }
        }
    }

    private void SpawnOneAgentFromExit(int localExitArrayIndex)
    {
        int spawnZoneIndex = currentExitIndices[localExitArrayIndex];

        if (spawnZoneIndex < 0 || spawnZoneIndex >= spawnZones.Length)
        {
            Debug.LogError($"Invalid spawnZoneIndex: {spawnZoneIndex}");
            return;
        }

        if (availableAgents.Count == 0)
        {
            return;
        }

        Transform spawn = spawnZones[spawnZoneIndex];

        Vector3 spawnPosition = spawn.position;

        if (simulationSpace != null)
        {
            spawnPosition += simulationSpace.WorldOffsetFromLocalOffset(
                Random.Range(-spawnJitterX, spawnJitterX),
                Random.Range(-spawnJitterZ, spawnJitterZ)
            );
        }
        else
        {
            spawnPosition.x += Random.Range(-spawnJitterX, spawnJitterX);
            spawnPosition.z += Random.Range(-spawnJitterZ, spawnJitterZ);
        }

        Transform targetGate = ChooseGate(spawnPosition);

        RuntimePedestrianAgent agent = availableAgents.Dequeue();
        activeAgents.Add(agent);

        if (targetGate == eastGate)
        {
            activeAssignedEast++;
        }
        else
        {
            activeAssignedWest++;
        }

        agent.RuntimeActivate(
            spawnPosition,
            targetGate,
            spawnZoneIndex,
            this,
            westGate,
            eastGate,
            simulationSpace
        );

        generatedPerExit[localExitArrayIndex]++;
        totalGenerated++;
    }

    private Transform ChooseGate(Vector3 spawnPosition)
    {
        float distanceToWest = GetDistance(spawnPosition, westGate.position);
        float distanceToEast = GetDistance(spawnPosition, eastGate.position);

        float westCost =
            distanceWeight * distanceToWest +
            activeLoadWeight * activeAssignedWest +
            passedLoadWeight * westPassed +
            Random.Range(0f, randomNoiseWeight);

        float eastCost =
            distanceWeight * distanceToEast +
            activeLoadWeight * activeAssignedEast +
            passedLoadWeight * eastPassed +
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

    public void ReportAgentArrived(RuntimePedestrianAgent agent, Transform actualGate)
    {
        if (agent == null)
        {
            return;
        }

        totalArrived++;
        totalTravelTime += Time.time - agent.SpawnTime;

        if (IsGateOrChild(actualGate, eastGate))
        {
            eastPassed++;
        }
        else if (IsGateOrChild(actualGate, westGate))
        {
            westPassed++;
        }

        DecreaseActiveAssignedGate(agent.currentTargetGate);
        RecycleAgent(agent);
    }

    public void ReportAgentFailed(RuntimePedestrianAgent agent, string reason)
    {
        if (agent == null)
        {
            return;
        }

        totalFailed++;

        DecreaseActiveAssignedGate(agent.currentTargetGate);
        RecycleAgent(agent);

        if (logScenarioEvents)
        {
            Debug.LogWarning($"Agent failed: {reason}");
        }
    }

    public void ReportWallCollision()
    {
        wallCollisionCount++;
    }

    private void RecycleAgent(RuntimePedestrianAgent agent)
    {
        activeAgents.Remove(agent);
        agent.RuntimeDeactivate();
        availableAgents.Enqueue(agent);
    }

    private void StopAndRecycleAllAgents()
    {
        for (int i = activeAgents.Count - 1; i >= 0; i--)
        {
            RuntimePedestrianAgent agent = activeAgents[i];
            agent.RuntimeDeactivate();
            availableAgents.Enqueue(agent);
        }

        activeAgents.Clear();
        activeAssignedWest = 0;
        activeAssignedEast = 0;
    }

    private void DecreaseActiveAssignedGate(Transform gate)
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

    private float GetDistance(Vector3 a, Vector3 b)
    {
        if (simulationSpace != null)
        {
            return simulationSpace.LocalDistance(a, b);
        }

        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private bool IsGateOrChild(Transform candidate, Transform gateRoot)
    {
        if (candidate == null || gateRoot == null)
        {
            return false;
        }

        return candidate == gateRoot || candidate.IsChildOf(gateRoot) || candidate.root == gateRoot.root;
    }

    private float GetAverageTravelTime()
    {
        if (totalArrived <= 0)
        {
            return 0f;
        }

        return totalTravelTime / totalArrived;
    }

    private int[] CopyArray(int[] source)
    {
        if (source == null)
        {
            return new int[0];
        }

        int[] copy = new int[source.Length];

        for (int i = 0; i < source.Length; i++)
        {
            copy[i] = source[i];
        }

        return copy;
    }

    private void OnGUI()
    {
        if (!showOnGUI)
        {
            return;
        }

        GUILayout.BeginArea(new Rect(10, 10, 390, 330), GUI.skin.box);

        GUILayout.Label("Final Scene Controller");

        GUILayout.BeginHorizontal();

        if (GUILayout.Button("Start Normal", GUILayout.Height(35)))
        {
            StartNormalScenario();
        }

        if (GUILayout.Button("Start Burst", GUILayout.Height(35)))
        {
            StartBurstScenario();
        }

        GUILayout.EndHorizontal();

        if (GUILayout.Button("Stop / Recycle All", GUILayout.Height(28)))
        {
            scenarioRunning = false;
            StopAndRecycleAllAgents();
        }

        GUILayout.Space(10);

        GUILayout.Label($"Scenario: {currentScenarioName}");
        GUILayout.Label($"Running: {scenarioRunning}");
        GUILayout.Label($"Generated: {totalGenerated} / {totalTarget}");
        GUILayout.Label($"Arrived: {totalArrived}");
        GUILayout.Label($"Failed: {totalFailed}");
        GUILayout.Label($"Active: {activeAgents.Count} / {currentMaxActiveAgents}");
        GUILayout.Label($"Available Pool: {availableAgents.Count}");

        GUILayout.Space(5);

        GUILayout.Label($"West Gate Passed: {westPassed}");
        GUILayout.Label($"East Gate Passed: {eastPassed}");
        GUILayout.Label($"Active Assigned West: {activeAssignedWest}");
        GUILayout.Label($"Active Assigned East: {activeAssignedEast}");
        GUILayout.Label($"Wall Collisions: {wallCollisionCount}");
        GUILayout.Label($"Avg Travel Time: {GetAverageTravelTime():F2}s");
        GUILayout.Label($"Elapsed: {(Time.time - scenarioStartTime):F1}s");

        GUILayout.EndArea();
    }
}
