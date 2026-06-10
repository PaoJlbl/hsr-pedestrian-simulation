using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class BottleneckZoneMonitor : MonoBehaviour
{
    [Header("Zone Info")]
    public string zoneName = "Zone";

    [Header("Thresholds")]
    public float densityThreshold = 0.30f;
    public float speedThreshold = 0.50f;
    public float criticalDurationThreshold = 3.0f;

    [Header("Runtime State")]
    public int currentCount = 0;
    public float currentDensity = 0f;
    public float currentMeanSpeed = 0f;
    public bool isCritical = false;
    public float criticalDuration = 0f;

    [Header("Accumulated Metrics")]
    public float totalCriticalTime = 0f;
    public int criticalEpisodeCount = 0;
    public float peakObservedDensity = 0f;
    public float minObservedMeanSpeed = 999f;
    public int maxObservedCount = 0;

    [Header("Debug")]
    public bool showGizmos = true;
    public Color normalColor = new Color(0f, 0.5f, 1f, 0.18f);
    public Color criticalColor = new Color(1f, 0f, 0f, 0.25f);

    private BoxCollider boxCollider;

    private readonly HashSet<GameObject> agentsInZone = new HashSet<GameObject>();
    private readonly Dictionary<GameObject, Vector3> lastPositions = new Dictionary<GameObject, Vector3>();
    private readonly Dictionary<GameObject, float> lastTimes = new Dictionary<GameObject, float>();

    private bool wasCriticalLastFrame = false;

    private void Awake()
    {
        boxCollider = GetComponent<BoxCollider>();
        boxCollider.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Pedestrian"))
        {
            return;
        }

        GameObject agent = other.gameObject;

        agentsInZone.Add(agent);
        lastPositions[agent] = agent.transform.position;
        lastTimes[agent] = Time.time;
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Pedestrian"))
        {
            return;
        }

        GameObject agent = other.gameObject;

        agentsInZone.Remove(agent);
        lastPositions.Remove(agent);
        lastTimes.Remove(agent);
    }

    private void Update()
    {
        UpdateZoneState();
    }

    private void UpdateZoneState()
    {
        float now = Time.time;

        float speedSum = 0f;
        int validSpeedCount = 0;

        List<GameObject> invalidAgents = new List<GameObject>();

        foreach (GameObject agent in agentsInZone)
        {
            if (agent == null || !agent.activeInHierarchy)
            {
                invalidAgents.Add(agent);
                continue;
            }

            Vector3 currentPos = agent.transform.position;

            if (lastPositions.ContainsKey(agent) && lastTimes.ContainsKey(agent))
            {
                float dt = Mathf.Max(0.001f, now - lastTimes[agent]);
                Vector3 delta = currentPos - lastPositions[agent];
                delta.y = 0f;

                float speed = delta.magnitude / dt;

                speedSum += speed;
                validSpeedCount++;
            }

            lastPositions[agent] = currentPos;
            lastTimes[agent] = now;
        }

        for (int i = 0; i < invalidAgents.Count; i++)
        {
            GameObject agent = invalidAgents[i];

            agentsInZone.Remove(agent);
            lastPositions.Remove(agent);
            lastTimes.Remove(agent);
        }

        currentCount = agentsInZone.Count;

        float area = GetZoneArea();
        currentDensity = currentCount / area;
        currentMeanSpeed = validSpeedCount > 0 ? speedSum / validSpeedCount : 0f;

        maxObservedCount = Mathf.Max(maxObservedCount, currentCount);
        peakObservedDensity = Mathf.Max(peakObservedDensity, currentDensity);

        if (currentCount > 0)
        {
            minObservedMeanSpeed = Mathf.Min(minObservedMeanSpeed, currentMeanSpeed);
        }

        bool criticalNow =
            currentCount > 0 &&
            currentDensity >= densityThreshold &&
            currentMeanSpeed <= speedThreshold;

        if (criticalNow)
        {
            criticalDuration += Time.deltaTime;
        }
        else
        {
            criticalDuration = Mathf.Max(0f, criticalDuration - Time.deltaTime);
        }

        isCritical = criticalDuration >= criticalDurationThreshold;

        if (isCritical)
        {
            totalCriticalTime += Time.deltaTime;
        }

        if (isCritical && !wasCriticalLastFrame)
        {
            criticalEpisodeCount++;
        }

        wasCriticalLastFrame = isCritical;
    }

    private float GetZoneArea()
    {
        if (boxCollider == null)
        {
            boxCollider = GetComponent<BoxCollider>();
        }

        float sizeX = Mathf.Abs(boxCollider.size.x * transform.lossyScale.x);
        float sizeZ = Mathf.Abs(boxCollider.size.z * transform.lossyScale.z);

        return Mathf.Max(0.01f, sizeX * sizeZ);
    }

    public float GetSafeMinSpeed()
    {
        if (minObservedMeanSpeed >= 999f)
        {
            return 0f;
        }

        return minObservedMeanSpeed;
    }

    public void ResetMetrics()
    {
        currentCount = 0;
        currentDensity = 0f;
        currentMeanSpeed = 0f;
        isCritical = false;
        criticalDuration = 0f;

        totalCriticalTime = 0f;
        criticalEpisodeCount = 0;
        peakObservedDensity = 0f;
        minObservedMeanSpeed = 999f;
        maxObservedCount = 0;

        wasCriticalLastFrame = false;

        agentsInZone.Clear();
        lastPositions.Clear();
        lastTimes.Clear();
    }

    public string GetSummary()
    {
        return $"{zoneName}: Count={currentCount}, Density={currentDensity:F3}, Speed={currentMeanSpeed:F2}, " +
               $"Critical={isCritical}, CriticalTime={totalCriticalTime:F1}s, " +
               $"PeakDensity={peakObservedDensity:F3}, MinSpeed={GetSafeMinSpeed():F2}";
    }

    private void OnDrawGizmos()
    {
        if (!showGizmos)
        {
            return;
        }

        BoxCollider bc = GetComponent<BoxCollider>();

        if (bc == null)
        {
            return;
        }

        Gizmos.color = isCritical ? criticalColor : normalColor;

        Matrix4x4 oldMatrix = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;

        Gizmos.DrawCube(bc.center, bc.size);

        Gizmos.matrix = oldMatrix;
    }
}