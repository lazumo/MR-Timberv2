using Unity.Netcode;
using UnityEngine;

public class HouseColorFactoryPlacer : NetworkBehaviour
{
    [Header("Prefab")]
    [SerializeField] private GameObject colorFactoryPrefab;

    [Header("Layers")]
    [SerializeField] private LayerMask groundLayer;

    [Header("Offsets")]
    [SerializeField] private float forwardOffset = 0.3f;   // ceiling: forward, wall: up
    [SerializeField] private float raycastDistance = 10f;

    [Header("Floor Side Spawn")]
    [SerializeField] private float floorSideDistance = 1f;
    [SerializeField] private float floorUpOffset = 0.05f;

    // =========================
    // Public API
    // =========================
    public NetworkObject SpawnColorFactory(int colorIndex)
    {
        if (!IsServer)
        {
            Debug.LogWarning("[HouseColorFactoryPlacer] Spawn called on non-server");
            return null;
        }

        if (colorFactoryPrefab == null)
        {
            Debug.LogError("[HouseColorFactoryPlacer] colorFactoryPrefab not assigned!");
            return null;
        }

        if (!TryGetSpawnPose(out Vector3 spawnPos, out Quaternion spawnRot))
            return null;

        // ===== Instantiate =====
        GameObject obj = Instantiate(colorFactoryPrefab, spawnPos, spawnRot);

        if (!obj.TryGetComponent(out NetworkObject netObj))
        {
            Debug.LogError("[HouseColorFactoryPlacer] prefab missing NetworkObject!");
            Destroy(obj);
            return null;
        }

        // ===== Spawn =====
        netObj.Spawn(true);

        // ===== Init color =====
        if (netObj.TryGetComponent(out ColorFactoryData data))
            data.ServerInit(colorIndex);

        // ===== ownerHouse =====
        if (netObj.TryGetComponent(out ColorFactory factory))
            factory.ownerHouse = this.gameObject;

        Debug.Log($"[HouseColorFactoryPlacer] Spawned ColorFactory colorIndex={colorIndex} id={netObj.NetworkObjectId}");
        return netObj;
    }

    // =========================
    // Spawn Pose Selection
    // =========================
    private enum HouseSurface
    {
        Floor,
        Wall,
        Ceiling
    }

    private bool TryGetSpawnPose(out Vector3 pos, out Quaternion rot)
    {
        var surface = GetHouseSurface();

        switch (surface)
        {
            case HouseSurface.Ceiling:
                return TryGetRaycastPose(GetCeilingRayOrigin(), out pos, out rot);

            case HouseSurface.Wall:
                return TryGetRaycastPose(GetWallRayOrigin(), out pos, out rot);

            case HouseSurface.Floor:
            default:
                GetFloorSidePose(out pos, out rot);
                return true;
        }
    }

    private HouseSurface GetHouseSurface()
    {
        float dot = Vector3.Dot(transform.up, Vector3.up);

        // floor ≈ +1, wall ≈ 0, ceiling ≈ -1
        if (dot < -0.7f) return HouseSurface.Ceiling;
        if (dot < 0.7f) return HouseSurface.Wall;
        return HouseSurface.Floor;
    }

    // =========================
    // Ray Origins
    // =========================
    private Vector3 GetCeilingRayOrigin()
    {
        // Ceiling: z( forward ) offset, then raycast down
        return transform.position + transform.forward * forwardOffset + transform.up * forwardOffset;
    }

    private Vector3 GetWallRayOrigin()
    {
        // Wall: y( up ) offset, then raycast down
        return transform.position + transform.up * forwardOffset * 1.5f;
    }

    // =========================
    // Raycast Pose
    // =========================
    private bool TryGetRaycastPose(Vector3 rayOrigin, out Vector3 pos, out Quaternion rot)
    {
        pos = Vector3.zero;
        rot = Quaternion.identity;

        // optional debug
        Debug.DrawRay(rayOrigin, Vector3.down * 2f, Color.red, 1f);

        if (!Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, raycastDistance, groundLayer))
        {
            Debug.LogWarning($"[HouseColorFactoryPlacer] Raycast failed from {rayOrigin}");
            return false;
        }

        Vector3 up = hit.normal;

        // keep forward aligned with house direction but projected on surface
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, up).normalized;
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.ProjectOnPlane(transform.right, up).normalized;

        rot = Quaternion.LookRotation(forward, up);
        pos = hit.point;
        pos.y += 0.1f;
        return true;
    }

    // =========================
    // Floor Pose
    // =========================
    private void GetFloorSidePose(out Vector3 pos, out Quaternion rot)
    {
        Vector3 sideDir = transform.right;

        pos = transform.position + sideDir * floorSideDistance;
        pos += Vector3.up * floorUpOffset;

        rot = Quaternion.LookRotation(-sideDir.normalized, Vector3.up);
    }
}
