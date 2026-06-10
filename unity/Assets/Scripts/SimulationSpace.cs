using UnityEngine;

/// <summary>
/// Maps arbitrary FBX world coordinates into the normalized corridor coordinate system
/// used by the trained pedestrian policy.
///
/// Local simulation convention:
/// - local X: west gate -> east gate
/// - local Z: corridor width direction. Put sideAnchor on the spawn/platform side and set sideAnchorLocalZ to -corridorHalfWidth if that side corresponds to training negative Z.
/// - local origin: midpoint of west/east gates.
/// </summary>
public class SimulationSpace : MonoBehaviour
{
    [Header("Anchors")]
    public Transform westGate;
    public Transform eastGate;
    public Transform sideAnchor;

    [Header("Training Coordinate Size")]
    public float corridorHalfLength = 150f;
    public float corridorHalfWidth = 7.5f;

    [Tooltip("Local Z value represented by sideAnchor. For spawn/platform side in your original training scene, usually use -7.5.")]
    public float sideAnchorLocalZ = -7.5f;

    [Header("Runtime")]
    public bool rebuildEveryFrame = false;

    private Vector3 origin;
    private Vector3 xAxis = Vector3.right;
    private Vector3 zAxis = Vector3.forward;
    private Vector3 upAxis = Vector3.up;

    private float xScale = 1f; // local training units per world unit along xAxis
    private float zScale = 1f; // local training units per world unit along zAxis; may be negative
    private bool initialized = false;

    private void Awake()
    {
        Rebuild();
    }

    private void Update()
    {
        if (rebuildEveryFrame)
        {
            Rebuild();
        }
    }

    public void Rebuild()
    {
        if (westGate == null || eastGate == null)
        {
            initialized = false;
            return;
        }

        Vector3 west = westGate.position;
        Vector3 east = eastGate.position;

        origin = (west + east) * 0.5f;

        Vector3 eastWest = east - west;
        eastWest = Vector3.ProjectOnPlane(eastWest, upAxis);

        if (eastWest.sqrMagnitude < 0.0001f)
        {
            eastWest = Vector3.right;
        }

        xAxis = eastWest.normalized;

        float worldGateDistance = Mathf.Abs(Vector3.Dot(east - west, xAxis));
        xScale = (corridorHalfLength * 2f) / Mathf.Max(0.001f, worldGateDistance);

        if (sideAnchor != null)
        {
            Vector3 sideVector = sideAnchor.position - origin;
            sideVector = Vector3.ProjectOnPlane(sideVector, upAxis);
            sideVector -= xAxis * Vector3.Dot(sideVector, xAxis);

            if (sideVector.sqrMagnitude > 0.0001f)
            {
                zAxis = sideVector.normalized;
                float worldSideDistance = Vector3.Dot(sideAnchor.position - origin, zAxis);
                zScale = sideAnchorLocalZ / Mathf.Max(0.001f, Mathf.Abs(worldSideDistance));

                // Keep the intended sign from sideAnchorLocalZ while allowing sideVector to define direction.
                if (worldSideDistance < 0f)
                {
                    zScale = -zScale;
                }
            }
            else
            {
                zAxis = Vector3.Cross(upAxis, xAxis).normalized;
                zScale = 1f;
            }
        }
        else
        {
            zAxis = Vector3.Cross(upAxis, xAxis).normalized;
            zScale = 1f;
        }

        initialized = true;
    }

    private void EnsureReady()
    {
        if (!initialized || rebuildEveryFrame)
        {
            Rebuild();
        }
    }

    public Vector3 WorldToSimulationLocal(Vector3 worldPosition)
    {
        EnsureReady();

        Vector3 d = worldPosition - origin;

        float localX = Vector3.Dot(d, xAxis) * xScale;
        float localZ = Vector3.Dot(d, zAxis) * zScale;
        float localY = Vector3.Dot(d, upAxis);

        return new Vector3(localX, localY, localZ);
    }

    public Vector3 SimulationLocalToWorld(Vector3 localPosition)
    {
        EnsureReady();

        Vector3 world = origin;
        world += xAxis * (localPosition.x / Mathf.Max(0.0001f, xScale));
        world += zAxis * (localPosition.z / Mathf.Sign(zScale) / Mathf.Max(0.0001f, Mathf.Abs(zScale)));
        world += upAxis * localPosition.y;
        return world;
    }

    public Vector3 WorldOffsetFromLocalOffset(float localXOffset, float localZOffset)
    {
        EnsureReady();

        Vector3 offset = Vector3.zero;
        offset += xAxis * (localXOffset / Mathf.Max(0.0001f, xScale));
        offset += zAxis * (localZOffset / Mathf.Sign(zScale) / Mathf.Max(0.0001f, Mathf.Abs(zScale)));
        return offset;
    }

    public float LocalDistance(Vector3 worldA, Vector3 worldB)
    {
        Vector3 a = WorldToSimulationLocal(worldA);
        Vector3 b = WorldToSimulationLocal(worldB);

        a.y = 0f;
        b.y = 0f;

        return Vector3.Distance(a, b);
    }

    public Vector3 WorldDisplacementToTrainingScaledWorldDisplacement(Vector3 worldDisplacement)
    {
        EnsureReady();

        Vector3 planar = Vector3.ProjectOnPlane(worldDisplacement, upAxis);
        float localX = Vector3.Dot(planar, xAxis) * xScale;
        float localZ = Vector3.Dot(planar, zAxis) * zScale;
        float localY = Vector3.Dot(worldDisplacement, upAxis);

        // Reconstruct a world-direction vector whose magnitudes are in training-local units.
        return xAxis * localX + zAxis * localZ + upAxis * localY;
    }

    public float LocalDistanceToWorldDistanceAlongWorldDirection(float localDistance, Vector3 worldDirection)
    {
        EnsureReady();

        Vector3 dir = Vector3.ProjectOnPlane(worldDirection, upAxis);

        if (dir.sqrMagnitude < 0.0001f)
        {
            return localDistance;
        }

        dir.Normalize();

        float localPerWorldX = Vector3.Dot(dir, xAxis) * xScale;
        float localPerWorldZ = Vector3.Dot(dir, zAxis) * zScale;
        float localPerWorld = Mathf.Sqrt(localPerWorldX * localPerWorldX + localPerWorldZ * localPerWorldZ);

        return localDistance / Mathf.Max(0.0001f, localPerWorld);
    }

    public float LocalRadiusToConservativeWorldRadius(float localRadius)
    {
        EnsureReady();

        float minScale = Mathf.Min(Mathf.Abs(xScale), Mathf.Abs(zScale));
        return localRadius / Mathf.Max(0.0001f, minScale);
    }

    private void OnDrawGizmosSelected()
    {
        Rebuild();

        Gizmos.color = Color.red;
        Gizmos.DrawLine(origin, origin + xAxis * 10f);

        Gizmos.color = Color.blue;
        Gizmos.DrawLine(origin, origin + zAxis * 10f);

        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(origin, 0.5f);
    }
}
