using System;
using System.IO;
using System.Text;
using UnityEngine;

public class FBXTrafficControlManager : MonoBehaviour
{
    public enum ControlLevel
    {
        Level0_NoControl = 0,
        Level1_BeltCongestion = 1,
        Level2_CorePersistent = 2,
        Level3_Spillover = 3
    }

    public enum GatePenaltyDirection
    {
        West,
        East
    }

    [Header("References")]
    public FBXFinalSceneController sceneController;

    [Header("Bottleneck Zones")]
    public BottleneckZoneMonitor coreZoneX8;
    public BottleneckZoneMonitor beltZoneX6_8;
    public BottleneckZoneMonitor spillZoneX9_10;
    public BottleneckZoneMonitor secondaryZoneX19;

    [Header("Control Switch")]
    public bool enableControl = true;
    public bool useSpawnMetering = true;
    public bool useGateGuidance = true;
    public bool useSecondaryMildGuidance = true;

    [Header("Main Bottleneck Direction")]
    public GatePenaltyDirection congestedGateDirection = GatePenaltyDirection.West;

    [Header("Spawn Metering Zones")]
    public int[] primaryMeteredSpawnZones = new int[] { 1, 3 };
    public int[] secondaryMeteredSpawnZones = new int[] { 5 };

    [Header("Escalation Rule")]
    public float coreEscalationDuration = 8.0f;

    [Header("Level 1: Belt Congestion")]
    public float level1PrimaryRate = 0.70f;
    public float level1SecondaryRate = 0.85f;
    public float level1GatePenalty = 15f;

    [Header("Level 2: Persistent Core Congestion")]
    public float level2PrimaryRate = 0.50f;
    public float level2SecondaryRate = 0.75f;
    public float level2GatePenalty = 30f;

    [Header("Level 3: Spillover")]
    public float level3PrimaryRate = 0.35f;
    public float level3SecondaryRate = 0.65f;
    public float level3GatePenalty = 40f;

    [Header("Secondary Zone Mild Guidance")]
    public float secondaryZoneGatePenalty = 10f;

    [Header("Runtime State")]
    public ControlLevel currentLevel = ControlLevel.Level0_NoControl;
    public int interventionCount = 0;
    public int levelChangeCount = 0;
    public float totalControlActiveTime = 0f;

    public bool primaryMeteringActive = false;
    public bool secondaryMeteringActive = false;
    public bool gateGuidanceActive = false;
    public bool secondaryGuidanceActive = false;

    [Header("Excel / CSV Export")]
    public bool exportOnScenarioFinished = true;
    public string exportFolderName = "ControlExport";
    public string experimentName = "Burst_Control";
    public bool exportOncePerPlay = true;

    private ControlLevel lastLevel = ControlLevel.Level0_NoControl;
    private bool lastAnyControlActive = false;
    private bool hasExportedThisPlay = false;

    private void Update()
    {
        if (sceneController == null)
        {
            return;
        }

        // 手动导出：按 F8 立即导出当前结果
        if (Input.GetKeyDown(KeyCode.F8))
        {
            ExportResultsToCsv();
            Debug.Log("Manual CSV export triggered by F8.");
        }

        if (!enableControl)
        {
            ClearControl();
            currentLevel = ControlLevel.Level0_NoControl;
        }
        else
        {
            UpdateControlLevel();
            ApplyControlLevel();
            UpdateControlStatistics();
        }

        TryExportWhenFinished();
    }

    private void UpdateControlLevel()
    {
        bool beltCritical = beltZoneX6_8 != null && beltZoneX6_8.isCritical;
        bool coreCritical = coreZoneX8 != null && coreZoneX8.isCritical;
        bool spillCritical = spillZoneX9_10 != null && spillZoneX9_10.isCritical;

        bool corePersistent =
            coreCritical &&
            coreZoneX8 != null &&
            coreZoneX8.criticalDuration >= coreEscalationDuration;

        if (coreCritical && spillCritical)
        {
            currentLevel = ControlLevel.Level3_Spillover;
        }
        else if (corePersistent)
        {
            currentLevel = ControlLevel.Level2_CorePersistent;
        }
        else if (beltCritical || coreCritical)
        {
            currentLevel = ControlLevel.Level1_BeltCongestion;
        }
        else
        {
            currentLevel = ControlLevel.Level0_NoControl;
        }
    }

    private void ApplyControlLevel()
    {
        ClearControl();

        primaryMeteringActive = false;
        secondaryMeteringActive = false;
        gateGuidanceActive = false;
        secondaryGuidanceActive = false;

        switch (currentLevel)
        {
            case ControlLevel.Level0_NoControl:
                ApplySecondaryMildGuidanceIfNeeded();
                break;

            case ControlLevel.Level1_BeltCongestion:
                ApplyMetering(level1PrimaryRate, level1SecondaryRate);
                ApplyGatePenalty(level1GatePenalty);
                break;

            case ControlLevel.Level2_CorePersistent:
                ApplyMetering(level2PrimaryRate, level2SecondaryRate);
                ApplyGatePenalty(level2GatePenalty);
                break;

            case ControlLevel.Level3_Spillover:
                ApplyMetering(level3PrimaryRate, level3SecondaryRate);
                ApplyGatePenalty(level3GatePenalty);
                break;
        }
    }

    private void ClearControl()
    {
        if (sceneController == null)
        {
            return;
        }

        sceneController.ResetSpawnZoneRateMultipliers();
        sceneController.westGateControlPenalty = 0f;
        sceneController.eastGateControlPenalty = 0f;

        primaryMeteringActive = false;
        secondaryMeteringActive = false;
        gateGuidanceActive = false;
        secondaryGuidanceActive = false;
    }

    private void ApplyMetering(float primaryRate, float secondaryRate)
    {
        if (!useSpawnMetering)
        {
            return;
        }

        for (int i = 0; i < primaryMeteredSpawnZones.Length; i++)
        {
            sceneController.SetSpawnZoneRateMultiplier(primaryMeteredSpawnZones[i], primaryRate);
        }

        for (int i = 0; i < secondaryMeteredSpawnZones.Length; i++)
        {
            sceneController.SetSpawnZoneRateMultiplier(secondaryMeteredSpawnZones[i], secondaryRate);
        }

        primaryMeteringActive = primaryMeteredSpawnZones.Length > 0;
        secondaryMeteringActive = secondaryMeteredSpawnZones.Length > 0;
    }

    private void ApplyGatePenalty(float penalty)
    {
        if (!useGateGuidance)
        {
            return;
        }

        if (congestedGateDirection == GatePenaltyDirection.West)
        {
            sceneController.westGateControlPenalty = penalty;
            sceneController.eastGateControlPenalty = 0f;
        }
        else
        {
            sceneController.eastGateControlPenalty = penalty;
            sceneController.westGateControlPenalty = 0f;
        }

        gateGuidanceActive = penalty > 0f;
    }

    private void ApplySecondaryMildGuidanceIfNeeded()
    {
        if (!useSecondaryMildGuidance)
        {
            return;
        }

        if (secondaryZoneX19 == null)
        {
            return;
        }

        if (!secondaryZoneX19.isCritical)
        {
            return;
        }

        if (!useGateGuidance)
        {
            return;
        }

        if (congestedGateDirection == GatePenaltyDirection.West)
        {
            sceneController.westGateControlPenalty = secondaryZoneGatePenalty;
            sceneController.eastGateControlPenalty = 0f;
        }
        else
        {
            sceneController.eastGateControlPenalty = secondaryZoneGatePenalty;
            sceneController.westGateControlPenalty = 0f;
        }

        secondaryGuidanceActive = true;
    }

    private void UpdateControlStatistics()
    {
        bool anyControlActive =
            currentLevel != ControlLevel.Level0_NoControl ||
            secondaryGuidanceActive;

        if (anyControlActive)
        {
            totalControlActiveTime += Time.deltaTime;
        }

        if (anyControlActive && !lastAnyControlActive)
        {
            interventionCount++;
        }

        if (currentLevel != lastLevel)
        {
            levelChangeCount++;
        }

        lastAnyControlActive = anyControlActive;
        lastLevel = currentLevel;
    }

    public void ResetControlMetrics()
    {
        currentLevel = ControlLevel.Level0_NoControl;
        lastLevel = ControlLevel.Level0_NoControl;
        lastAnyControlActive = false;

        interventionCount = 0;
        levelChangeCount = 0;
        totalControlActiveTime = 0f;

        primaryMeteringActive = false;
        secondaryMeteringActive = false;
        gateGuidanceActive = false;
        secondaryGuidanceActive = false;

        hasExportedThisPlay = false;

        if (coreZoneX8 != null)
        {
            coreZoneX8.ResetMetrics();
        }

        if (beltZoneX6_8 != null)
        {
            beltZoneX6_8.ResetMetrics();
        }

        if (spillZoneX9_10 != null)
        {
            spillZoneX9_10.ResetMetrics();
        }

        if (secondaryZoneX19 != null)
        {
            secondaryZoneX19.ResetMetrics();
        }

        ClearControl();
    }

    private void TryExportWhenFinished()
    {
        if (!exportOnScenarioFinished)
        {
            return;
        }

        if (sceneController == null)
        {
            return;
        }

        if (!sceneController.scenarioFinished)
        {
            return;
        }

        if (exportOncePerPlay && hasExportedThisPlay)
        {
            return;
        }

        ExportResultsToCsv();
        hasExportedThisPlay = true;
    }

    [ContextMenu("Export Results To CSV")]
    public void ExportResultsToCsv()
    {
        string root = Path.Combine(Application.dataPath, "..", exportFolderName);

        if (!Directory.Exists(root))
        {
            Directory.CreateDirectory(root);
        }

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string prefix = $"{experimentName}_{timestamp}";

        string summaryPath = Path.Combine(root, $"{prefix}_summary.csv");
        string zonePath = Path.Combine(root, $"{prefix}_zone_metrics.csv");

        WriteSummaryCsv(summaryPath);
        WriteZoneCsv(zonePath);

        Debug.Log($"Control summary exported to: {summaryPath}");
        Debug.Log($"Zone metrics exported to: {zonePath}");
    }

    private void WriteSummaryCsv(string path)
    {
        StringBuilder sb = new StringBuilder();

        sb.AppendLine("Section,Metric,Value,Description");

        float tTotal = sceneController != null ? sceneController.totalEvacuationTime : 0f;
        float avgTravelTime = sceneController != null ? sceneController.GetAverageTravelTime() : 0f;
        float tCore = coreZoneX8 != null ? coreZoneX8.totalCriticalTime : 0f;
        float tBelt = beltZoneX6_8 != null ? beltZoneX6_8.totalCriticalTime : 0f;
        float rhoPeakCore = coreZoneX8 != null ? coreZoneX8.peakObservedDensity : 0f;
        float imbalance = sceneController != null ? sceneController.GetGateImbalance() : 0f;

        sb.AppendLine($"CoreMetrics,T_total,{tTotal:F3},总疏散时间");
        sb.AppendLine($"CoreMetrics,AvgTravelTime,{avgTravelTime:F3},平均通行时间");
        sb.AppendLine($"CoreMetrics,T_core,{tCore:F3},核心瓶颈持续时间");
        sb.AppendLine($"CoreMetrics,T_belt,{tBelt:F3},主拥堵带持续时间");
        sb.AppendLine($"CoreMetrics,rho_peak_core,{rhoPeakCore:F4},核心区峰值密度");
        sb.AppendLine($"CoreMetrics,Imbalance,{imbalance:F4},闸机不均衡系数");

        if (sceneController != null)
        {
            sb.AppendLine($"Scenario,ExperimentName,{experimentName},实验名称");
            sb.AppendLine($"Scenario,Scenario,{sceneController.CurrentScenarioName},仿真工况");
            sb.AppendLine($"Scenario,EnableControl,{enableControl},是否启用管控");
            sb.AppendLine($"Scenario,UseSpawnMetering,{useSpawnMetering},是否启用出口节流");
            sb.AppendLine($"Scenario,UseGateGuidance,{useGateGuidance},是否启用闸机诱导");
            sb.AppendLine($"Scenario,CurrentLevel,{currentLevel},结束时控制等级");

            sb.AppendLine($"Control,InterventionCount,{interventionCount},管控触发次数");
            sb.AppendLine($"Control,LevelChangeCount,{levelChangeCount},控制等级变化次数");
            sb.AppendLine($"Control,TotalControlActiveTime,{totalControlActiveTime:F3},管控累计生效时间");

            sb.AppendLine($"Flow,TotalTarget,{sceneController.TotalTarget},目标总人数");
            sb.AppendLine($"Flow,TotalGenerated,{sceneController.TotalGenerated},累计生成人数");
            sb.AppendLine($"Flow,TotalArrived,{sceneController.TotalArrived},累计到达人数");
            sb.AppendLine($"Flow,TotalFailed,{sceneController.TotalFailed},异常终止人数");
            sb.AppendLine($"Flow,PeakActiveAgents,{sceneController.peakActiveAgents},最大同时在场人数");

            sb.AppendLine($"Gate,WestPassed,{sceneController.WestPassed},西闸机通过人数");
            sb.AppendLine($"Gate,EastPassed,{sceneController.EastPassed},东闸机通过人数");
            sb.AppendLine($"Gate,GateImbalance,{sceneController.GetGateImbalance():F4},闸机不均衡系数");

            sb.AppendLine($"Failure,WrongExitCount,{sceneController.WrongExitCount},误入出口次数");
            sb.AppendLine($"Failure,OutOfBoundsCount,{sceneController.OutOfBoundsCount},越界次数");
            sb.AppendLine($"Failure,WallCollisionCount,{sceneController.WallCollisionCount},撞墙次数");
            sb.AppendLine($"Failure,ObstacleCollisionCount,{sceneController.ObstacleCollisionCount},撞障碍物次数");
            sb.AppendLine($"Failure,PedestrianCollisionCount,{sceneController.PedestrianCollisionCount},行人碰撞次数");
        }

        sb.AppendLine($"ControlParameters,PrimaryMeteredSpawnZones,{ArrayToString(primaryMeteredSpawnZones)},主节流出口");
        sb.AppendLine($"ControlParameters,SecondaryMeteredSpawnZones,{ArrayToString(secondaryMeteredSpawnZones)},次节流出口");

        sb.AppendLine($"ControlParameters,Level1PrimaryRate,{level1PrimaryRate},一级控制主出口倍率");
        sb.AppendLine($"ControlParameters,Level1SecondaryRate,{level1SecondaryRate},一级控制次出口倍率");
        sb.AppendLine($"ControlParameters,Level1GatePenalty,{level1GatePenalty},一级控制闸机惩罚");

        sb.AppendLine($"ControlParameters,Level2PrimaryRate,{level2PrimaryRate},二级控制主出口倍率");
        sb.AppendLine($"ControlParameters,Level2SecondaryRate,{level2SecondaryRate},二级控制次出口倍率");
        sb.AppendLine($"ControlParameters,Level2GatePenalty,{level2GatePenalty},二级控制闸机惩罚");

        sb.AppendLine($"ControlParameters,Level3PrimaryRate,{level3PrimaryRate},三级控制主出口倍率");
        sb.AppendLine($"ControlParameters,Level3SecondaryRate,{level3SecondaryRate},三级控制次出口倍率");
        sb.AppendLine($"ControlParameters,Level3GatePenalty,{level3GatePenalty},三级控制闸机惩罚");

        WriteUtf8BomCsv(path, sb.ToString());
    }

    private void WriteZoneCsv(string path)
    {
        StringBuilder sb = new StringBuilder();

        sb.AppendLine("ZoneName,Role,CurrentCount,CurrentDensity,CurrentMeanSpeed,IsCritical,CriticalDuration,TotalCriticalTime,CriticalEpisodeCount,PeakObservedDensity,MinObservedMeanSpeed,MaxObservedCount");

        AppendZoneRow(sb, coreZoneX8, "Core bottleneck cell_x=8");
        AppendZoneRow(sb, beltZoneX6_8, "Main bottleneck belt cell_x=6-8");
        AppendZoneRow(sb, spillZoneX9_10, "Downstream spillover cell_x=9-10");
        AppendZoneRow(sb, secondaryZoneX19, "Secondary pressure cell_x=19");

        WriteUtf8BomCsv(path, sb.ToString());
    }

    private void AppendZoneRow(StringBuilder sb, BottleneckZoneMonitor zone, string role)
    {
        if (zone == null)
        {
            return;
        }

        sb.AppendLine(
            $"{zone.zoneName}," +
            $"{role}," +
            $"{zone.currentCount}," +
            $"{zone.currentDensity:F4}," +
            $"{zone.currentMeanSpeed:F4}," +
            $"{zone.isCritical}," +
            $"{zone.criticalDuration:F3}," +
            $"{zone.totalCriticalTime:F3}," +
            $"{zone.criticalEpisodeCount}," +
            $"{zone.peakObservedDensity:F4}," +
            $"{zone.GetSafeMinSpeed():F4}," +
            $"{zone.maxObservedCount}"
        );
    }

    private void WriteUtf8BomCsv(string path, string content)
    {
        UTF8Encoding utf8Bom = new UTF8Encoding(true);
        File.WriteAllText(path, content, utf8Bom);
    }

    private string ArrayToString(int[] values)
    {
        if (values == null || values.Length == 0)
        {
            return "";
        }

        return string.Join("|", values);
    }

    private void OnGUI()
    {
        if (!enableControl)
        {
            return;
        }

        GUILayout.BeginArea(new Rect(430, 10, 450, 350), GUI.skin.box);

        GUILayout.Label("FBX Traffic Control Manager");

        GUILayout.Label($"Current Level: {currentLevel}");
        GUILayout.Label($"Primary Metering: {primaryMeteringActive}");
        GUILayout.Label($"Secondary Metering: {secondaryMeteringActive}");
        GUILayout.Label($"Gate Guidance: {gateGuidanceActive}");
        GUILayout.Label($"Secondary Guidance: {secondaryGuidanceActive}");

        GUILayout.Space(5);

        GUILayout.Label($"Intervention Count: {interventionCount}");
        GUILayout.Label($"Level Change Count: {levelChangeCount}");
        GUILayout.Label($"Total Control Active Time: {totalControlActiveTime:F1}s");

        GUILayout.Space(5);

        if (sceneController != null)
        {
            GUILayout.Label($"West Gate Penalty: {sceneController.westGateControlPenalty:F1}");
            GUILayout.Label($"East Gate Penalty: {sceneController.eastGateControlPenalty:F1}");
        }

        GUILayout.Space(5);

        if (coreZoneX8 != null)
        {
            GUILayout.Label($"Core X8: Critical={coreZoneX8.isCritical}, T={coreZoneX8.totalCriticalTime:F1}s, rhoPeak={coreZoneX8.peakObservedDensity:F3}");
        }

        if (beltZoneX6_8 != null)
        {
            GUILayout.Label($"Belt X6-8: Critical={beltZoneX6_8.isCritical}, T={beltZoneX6_8.totalCriticalTime:F1}s");
        }

        if (spillZoneX9_10 != null)
        {
            GUILayout.Label($"Spill X9-10: Critical={spillZoneX9_10.isCritical}, T={spillZoneX9_10.totalCriticalTime:F1}s");
        }

        if (secondaryZoneX19 != null)
        {
            GUILayout.Label($"Secondary X19: Critical={secondaryZoneX19.isCritical}, T={secondaryZoneX19.totalCriticalTime:F1}s");
        }

        if (GUILayout.Button("Export Results To CSV", GUILayout.Height(28)))
        {
            ExportResultsToCsv();
        }

        GUILayout.EndArea();
    }
}