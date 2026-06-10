using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

public class CrowdStateLogger : MonoBehaviour
{
    [Header("References")]
    public Transform simulationFrame;

    [Header("Sampling")]
    public string scenarioName = "Normal";
    public float sampleInterval = 0.5f;
    public string outputFileName = "crowd_state_log.csv";

    [Header("Grid Settings")]
    public float corridorHalfLength = 150f;
    public float corridorHalfWidth = 15f;
    public int xCells = 30;
    public int zCells = 3;

    [Header("Agent Settings")]
    public string pedestrianTag = "Pedestrian";

    private float timer = 0f;
    private string filePath;

    private Dictionary<int, Vector3> lastLocalPositions = new Dictionary<int, Vector3>();
    private Dictionary<int, float> lastSampleTimes = new Dictionary<int, float>();

    private class CellAccumulator
    {
        public int count = 0;
        public float speedSum = 0f;
        public float vxSum = 0f;
        public float vzSum = 0f;
    }

    private void Start()
    {
        filePath = Path.Combine(Application.dataPath, "..", outputFileName);

        using (StreamWriter sw = new StreamWriter(filePath, false))
        {
            sw.WriteLine("scenario,time,cell_x,cell_z,count,density,mean_speed,mean_vx,mean_vz");
        }

        Debug.Log($"CrowdStateLogger output: {filePath}");
    }

    private void Update()
    {
        timer += Time.deltaTime;

        if (timer >= sampleInterval)
        {
            timer = 0f;
            SampleAndWrite();
        }
    }

    private void SampleAndWrite()
    {
        if (simulationFrame == null)
        {
            Debug.LogWarning("CrowdStateLogger: simulationFrame is null.");
            return;
        }

        CellAccumulator[,] grid = new CellAccumulator[xCells, zCells];

        for (int x = 0; x < xCells; x++)
        {
            for (int z = 0; z < zCells; z++)
            {
                grid[x, z] = new CellAccumulator();
            }
        }

        GameObject[] agents = GameObject.FindGameObjectsWithTag(pedestrianTag);
        float now = Time.time;

        foreach (GameObject agent in agents)
        {
            if (!agent.activeInHierarchy)
            {
                continue;
            }

            Vector3 localPos = simulationFrame.InverseTransformPoint(agent.transform.position);

            int cellX = GetCellIndex(
                localPos.x,
                -corridorHalfLength,
                corridorHalfLength,
                xCells
            );

            int cellZ = GetCellIndex(
                localPos.z,
                -corridorHalfWidth,
                corridorHalfWidth,
                zCells
            );

            if (cellX < 0 || cellX >= xCells || cellZ < 0 || cellZ >= zCells)
            {
                continue;
            }

            int id = agent.GetInstanceID();

            float speed = 0f;
            float vx = 0f;
            float vz = 0f;

            if (lastLocalPositions.ContainsKey(id) && lastSampleTimes.ContainsKey(id))
            {
                Vector3 lastPos = lastLocalPositions[id];
                float lastTime = lastSampleTimes[id];

                float dt = Mathf.Max(0.001f, now - lastTime);
                Vector3 delta = localPos - lastPos;
                delta.y = 0f;

                speed = delta.magnitude / dt;
                vx = delta.x / dt;
                vz = delta.z / dt;
            }

            lastLocalPositions[id] = localPos;
            lastSampleTimes[id] = now;

            CellAccumulator cell = grid[cellX, cellZ];

            cell.count++;
            cell.speedSum += speed;
            cell.vxSum += vx;
            cell.vzSum += vz;
        }

        float cellLength = (2f * corridorHalfLength) / xCells;
        float cellWidth = (2f * corridorHalfWidth) / zCells;
        float cellArea = cellLength * cellWidth;

        using (StreamWriter sw = new StreamWriter(filePath, true))
        {
            for (int x = 0; x < xCells; x++)
            {
                for (int z = 0; z < zCells; z++)
                {
                    CellAccumulator cell = grid[x, z];

                    float density = cell.count / cellArea;
                    float meanSpeed = cell.count > 0 ? cell.speedSum / cell.count : 0f;
                    float meanVx = cell.count > 0 ? cell.vxSum / cell.count : 0f;
                    float meanVz = cell.count > 0 ? cell.vzSum / cell.count : 0f;

                    sw.WriteLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0},{1:F2},{2},{3},{4},{5:F4},{6:F4},{7:F4},{8:F4}",
                        scenarioName,
                        now,
                        x,
                        z,
                        cell.count,
                        density,
                        meanSpeed,
                        meanVx,
                        meanVz
                    ));
                }
            }
        }
    }

    private int GetCellIndex(float value, float minValue, float maxValue, int cellCount)
    {
        if (value < minValue || value > maxValue)
        {
            return -1;
        }

        float normalized = (value - minValue) / (maxValue - minValue);
        int index = Mathf.FloorToInt(normalized * cellCount);

        if (index == cellCount)
        {
            index = cellCount - 1;
        }

        return index;
    }
}