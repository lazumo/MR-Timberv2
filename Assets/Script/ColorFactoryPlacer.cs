using Unity.Netcode;
using UnityEngine;

public class HouseColorFactoryPlacer : NetworkBehaviour
{
    [Header("Single Factory Prefab")]
    public GameObject colorFactoryPrefab;   // ⭐ 改成單一 prefab

    [Header("Layers")]
    public LayerMask groundLayer;

    [Header("Offsets")]
    public float forwardOffset = 0.3f;

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

        Vector3 spawnPos;
        Quaternion spawnRot;

        if (IsHouseOnWall())
        {
            if (!TryCalculatePose(out spawnPos, out spawnRot))
                return null;
        }
        else
        {
            CalculateSidePose(out spawnPos, out spawnRot);
        }

        // ===== Instantiate =====
        GameObject obj = Instantiate(colorFactoryPrefab, spawnPos, spawnRot);
        NetworkObject netObj = obj.GetComponent<NetworkObject>();

        if (netObj == null)
        {
            Debug.LogError("[HouseColorFactoryPlacer] prefab missing NetworkObject!");
            Destroy(obj);
            return null;
        }

        // ===== Spawn =====
        netObj.Spawn(true);

        // ===== 初始化顏色（NetworkVariable）=====
        var data = netObj.GetComponent<ColorFactoryData>();
        if (data != null)
        {
            data.ServerInit(colorIndex);
        }

        // ===== 保留你原本的 ownerHouse =====
        var factory = netObj.GetComponent<ColorFactory>();
        if (factory != null)
        {
            factory.ownerHouse = this.gameObject;
        }

        Debug.Log($"[HouseColorFactoryPlacer] ColorFactory spawned colorIndex={colorIndex} id={netObj.NetworkObjectId}");
        return netObj;
    }

    // ===== 以下維持你原本的 =====

    bool TryCalculatePose(out Vector3 pos, out Quaternion rot)
    {
        pos = Vector3.zero;
        rot = Quaternion.identity;

        Vector3 rayOrigin = transform.position + transform.up * forwardOffset;

        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 10f, groundLayer))
        {
            Vector3 up = hit.normal;

            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, up).normalized;
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.ProjectOnPlane(transform.right, up).normalized;

            rot = Quaternion.LookRotation(forward, up);
            pos = hit.point;
            return true;
        }

        return false;
    }

    bool IsHouseOnWall()
    {
        float dot = Vector3.Dot(transform.up, Vector3.up);
        return dot < 0.7f;
    }

    void CalculateSidePose(out Vector3 pos, out Quaternion rot)
    {
        float sideDistance = 1f;
        Vector3 sideDir = transform.right;

        pos = transform.position + sideDir * sideDistance;
        pos += Vector3.up * 0.05f;

        Vector3 forward = -sideDir.normalized;
        rot = Quaternion.LookRotation(forward, Vector3.up);
    }
}
