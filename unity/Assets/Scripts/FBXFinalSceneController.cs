using System.Collections.Generic;
using UnityEngine;

public class FBXFinalSceneController : MonoBehaviour
{
    public enum ScenarioType
    {
        Normal,
        Burst
    }

    [Header("Scene References")]
    public Transform simulationFrame;
    public FBXRuntimePedestrianAgent agentPrefab;
    public Transform agentPoolRoot;
    public Transform[] spawnZones;
    public Transform westGate;
    public Transform eastGate;

    [Header("Scenario Selection")]
    public ScenarioType defaultScenario = ScenarioType.Normal;
    public bool autoStartOnPlay = false;

    [Header("Normal Scenario")]
    public int[] normalExitIndices = new int[] { 2, 4, 6 };
    public int[] normalExitCounts = new int[] { 256, 280, 12 };
    public float normalArrivalDuration = 180f;
    public int normalMaxActiveAgents = 250;

    [Header("Burst Scenario")]
    public int[] burstExitIndices = new int[] { 1, 3, 5, 7 };
    public int[] burstExitCounts = new int[] { 1104, 1295, 1437, 863 };
    public float burstArrivalDuration = 600f;
    public int burstMaxActiveAgents = 550;

    [Header("Pool Settings")]
    public int poolSize = 650;
    public int maxSpawnsPerFrame = 30;

    [Header("Spawn Height")]
    public bool useSpawnZoneHeight = true;
    public float spawnHeightOffset = 0.9f;

    [Header("Spawn Jitter")]
    public float spawnJitterX = 0.5f;
    public float spawnJitterZ = 0.5f;

    [Header("Gate Allocation")]
    public float distanceWeight = 1.0f;
    public float activeLoadWeight = 0.05f;
    public float passedLoadWeight = 0.001f;
    public float randomNoiseWeight = 1.0f;
    public float maxExtraDistanceForBalancing = 35f;

    [Header("Control Modifiers")]
    public float westGateControlPenalty = 0f;
    public float eastGateControlPenalty = 0f;

    [Header("Runtime Display")]
    public bool showOnGUI = true;
    public bool logScenarioEvents = false;

    [Header("Runtime Metrics")]
    public int peakActiveAgents = 0;
    public float totalEvacuationTime = 0f;
    public bool scenarioFinished = false;

    private readonly Queue<FBXRuntimePedestrianAgent> availableAgents = new Queue<FBXRuntimePedestrianAgent>();
    private readonly List<FBXRuntimePedestrianAgent> activeAgents = new List<FBXRuntimePedestrianAgent>();
    private readonly Dictionary<int, float> spawnZoneRateMultipliers = new Dictionary<int, float>();

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

    private int wrongExitCount;
    private int outOfBoundsCount;
    private int wallCollisionCount;
    private int obstacleCollisionCount;
    private int pedestrianCollisionCount;

    private float totalTravelTime;
    private float scenarioStartTime;

    public int TotalTarget => totalTarget;
    public int TotalGenerated => totalGenerated;
    public int TotalArrived => totalArrived;
    public int TotalFailed => totalFailed;
    public int ActiveCount => activeAgents.Count;
    public int WestPassed => westPassed;
    public int EastPassed => eastPassed;
    public int WrongExitCount => wrongExitCount;
    public int OutOfBoundsCount => outOfBoundsCount;
    public int WallCollisionCount => wallCollisionCount;
    public int ObstacleCollisionCount => obstacleCollisionCount;
    public int PedestrianCollisionCount => pedestrianCollisionCount;
    public bool IsScenarioRunning => scenarioRunning;
    public string CurrentScenarioName => currentScenarioName;

    private void Start()
    {
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

        peakActiveAgents = Mathf.Max(peakActiveAgents, activeAgents.Count);

        if (totalGenerated >= totalTarget && activeAgents.Count == 0)
        {
            scenarioRunning = false;
            scenarioFinished = true;
            totalEvacuationTime = Time.time - scenarioStartTime;

            if (logScenarioEvents)
            {
                Debug.Log($"Scenario finished: {currentScenarioName}");
                Debug.Log($"Generated={totalGenerated}, Arrived={totalArrived}, Failed={totalFailed}");
                Debug.Log($"West={westPassed}, East={eastPassed}, AvgTime={GetAverageTravelTime():F2}s");
                Debug.Log($"TotalEvacuationTime={totalEvacuationTime:F2}s, PeakActive={peakActiveAgents}");
            }
        }
    }

    private void BuildAgentPool()
    {
        if (agentPrefab == null)
        {
            Debug.LogError("FBXFinalSceneController: agentPrefab is not assigned.");
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
            FBXRuntimePedestrianAgent agent = Instantiate(agentPrefab, agentPoolRoot);
            agent.name = $"FBXRuntimeAgent_{i:000}";
            agent.gameObject.SetActive(false);
            availableAgents.Enqueue(agent);
        }

        if (logScenarioEvents)
        {
            Debug.Log($"FBX runtime agent pool created. Pool Size = {poolSize}");
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
        scenarioFinished = false;
        scenarioStartTime = Time.time;
        totalEvacuationTime = 0f;

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

        wrongExitCount = 0;
        outOfBoundsCount = 0;
        wallCollisionCount = 0;
        obstacleCollisionCount = 0;
        pedestrianCollisionCount = 0;

        totalTravelTime = 0f;
        peakActiveAgents = 0;
        totalEvacuationTime = 0f;
        scenarioFinished = false;

        ResetSpawnZoneRateMultipliers();
        westGateControlPenalty = 0f;
        eastGateControlPenalty = 0f;
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

            int spawnZoneIndexForRate = currentExitIndices[i];
            float rateMultiplier = GetSpawnZoneRateMultiplier(spawnZoneIndexForRate);
            spawnAccumulators[i] += spawnRates[i] * rateMultiplier * Time.deltaTime;

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

        if (spawn == null)
        {
            Debug.LogError($"SpawnZone[{spawnZoneIndex}] is null.");
            return;
        }

        Vector3 spawnPosition = spawn.position;

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

        Transform targetGate = ChooseGate(spawnPosition);

        FBXRuntimePedestrianAgent agent = availableAgents.Dequeue();
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
            simulationFrame,
            westGate,
            eastGate
        );

        generatedPerExit[localExitArrayIndex]++;
        totalGenerated++;
    }

    private Transform ChooseGate(Vector3 spawnPosition)
    {
        float distanceToWest = HorizontalDistance(spawnPosition, westGate.position);
        float distanceToEast = HorizontalDistance(spawnPosition, eastGate.position);

        float westCost =
            distanceWeight * distanceToWest +
            activeLoadWeight * activeAssignedWest +
            passedLoadWeight * westPassed +
            westGateControlPenalty +
            Random.Range(0f, randomNoiseWeight);

        float eastCost =
            distanceWeight * distanceToEast +
            activeLoadWeight * activeAssignedEast +
            passedLoadWeight * eastPassed +
            eastGateControlPenalty +
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

    public void ReportAgentArrived(FBXRuntimePedestrianAgent agent, Transform actualGate)
    {
        if (agent == null)
        {
            return;
        }

        if (!activeAgents.Contains(agent))
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

    public void ReportAgentFailed(FBXRuntimePedestrianAgent agent, string reason)
    {
        if (agent == null)
        {
            return;
        }

        if (!activeAgents.Contains(agent))
        {
            return;
        }

        totalFailed++;

        if (reason == "WrongExit")
        {
            wrongExitCount++;
        }
        else if (reason == "OutOfBounds")
        {
            outOfBoundsCount++;
        }

        DecreaseActiveAssignedGate(agent.currentTargetGate);
        RecycleAgent(agent);
    }

    public void ReportWallCollision()
    {
        wallCollisionCount++;
    }

    public void ReportObstacleCollision()
    {
        obstacleCollisionCount++;
    }

    public void ReportPedestrianCollision()
    {
        pedestrianCollisionCount++;
    }

    private void RecycleAgent(FBXRuntimePedestrianAgent agent)
    {
        activeAgents.Remove(agent);
        agent.RuntimeDeactivate();
        availableAgents.Enqueue(agent);
    }

    private void StopAndRecycleAllAgents()
    {
        for (int i = activeAgents.Count - 1; i >= 0; i--)
        {
            FBXRuntimePedestrianAgent agent = activeAgents[i];

            if (agent != null)
            {
                agent.RuntimeDeactivate();
                availableAgents.Enqueue(agent);
            }
        }

        activeAgents.Clear();
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

    private float HorizontalDistance(Vector3 a, Vector3 b)
    {
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

    public void SetSpawnZoneRateMultiplier(int spawnZoneIndex, float multiplier)
    {
        spawnZoneRateMultipliers[spawnZoneIndex] = Mathf.Clamp(multiplier, 0f, 2f);
    }

    public void ResetSpawnZoneRateMultipliers()
    {
        spawnZoneRateMultipliers.Clear();
    }

    public float GetSpawnZoneRateMultiplier(int spawnZoneIndex)
    {
        if (spawnZoneRateMultipliers.ContainsKey(spawnZoneIndex))
        {
            return spawnZoneRateMultipliers[spawnZoneIndex];
        }

        return 1f;
    }

    public float GetAverageTravelTime()
    {
        if (totalArrived <= 0)
        {
            return 0f;
        }

        return totalTravelTime / totalArrived;
    }

    public float GetGateImbalance()
    {
        int totalPassed = westPassed + eastPassed;

        if (totalPassed <= 0)
        {
            return 0f;
        }

        return Mathf.Abs(westPassed - eastPassed) / (float)totalPassed;
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

        GUILayout.BeginArea(new Rect(10, 10, 410, 405), GUI.skin.box);

        GUILayout.Label("FBX Final Scene Controller");

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

        GUILayout.Space(8);

        GUILayout.Label($"Scenario: {currentScenarioName}");
        GUILayout.Label($"Running: {scenarioRunning}");
        GUILayout.Label($"Generated: {totalGenerated} / {totalTarget}");
        GUILayout.Label($"Arrived: {totalArrived}");
        GUILayout.Label($"Failed: {totalFailed}");
        GUILayout.Label($"Active: {activeAgents.Count} / {currentMaxActiveAgents}");
        GUILayout.Label($"Peak Active: {peakActiveAgents}");
        GUILayout.Label($"Available Pool: {availableAgents.Count}");

        GUILayout.Space(5);

        GUILayout.Label($"West Gate Passed: {westPassed}");
        GUILayout.Label($"East Gate Passed: {eastPassed}");
        GUILayout.Label($"Gate Imbalance: {GetGateImbalance():F3}");
        GUILayout.Label($"Active Assigned West: {activeAssignedWest}");
        GUILayout.Label($"Active Assigned East: {activeAssignedEast}");

        GUILayout.Space(5);

        GUILayout.Label($"Wrong Exit: {wrongExitCount}");
        GUILayout.Label($"Out Of Bounds: {outOfBoundsCount}");
        GUILayout.Label($"Wall Collisions: {wallCollisionCount}");
        GUILayout.Label($"Obstacle Collisions: {obstacleCollisionCount}");
        GUILayout.Label($"Pedestrian Collisions: {pedestrianCollisionCount}");
        GUILayout.Label($"Avg Travel Time: {GetAverageTravelTime():F2}s");
        GUILayout.Label($"Elapsed: {(Time.time - scenarioStartTime):F1}s");
        GUILayout.Label($"Total Evacuation Time: {totalEvacuationTime:F1}s");

        GUILayout.EndArea();
    }
}