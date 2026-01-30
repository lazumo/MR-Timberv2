using Unity.Netcode;
using UnityEngine;

public class HouseColorFactoryPlacer : NetworkBehaviour
{
    [Header("Prefabs (index = colorIndex)")]
    public GameObject[] colorFactoryPrefabs;

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

        if (colorFactoryPrefabs == null || colorFactoryPrefabs.Length == 0)
        {
            Debug.LogError("[HouseColorFactoryPlacer] colorFactoryPrefabs not assigned!");
            return null;
        }

        int safeIndex = Mathf.Abs(colorIndex) % colorFactoryPrefabs.Length;
        GameObject prefab = colorFactoryPrefabs[safeIndex];

        if (prefab == null)
        {
            Debug.LogError($"[HouseColorFactoryPlacer] prefab at index {safeIndex} is null!");
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

        GameObject obj = Instantiate(prefab, spawnPos, spawnRot);
        NetworkObject netObj = obj.GetComponent<NetworkObject>();

        if (netObj == null)
        {
            Debug.LogError("[HouseColorFactoryPlacer] prefab missing NetworkObject!");
            Destroy(obj);
            return null;
        }

        netObj.Spawn(true);
        var colorComp = netObj.GetComponent<ColorFactory>();
        if (colorComp != null)
        {
            colorComp.factoryColor = colorIndex;
            colorComp.ownerHouse = this.gameObject; // 這棟 house
        }
        Debug.Log($"[HouseColorFactoryPlacer] ColorFactory spawned index={safeIndex} id={netObj.NetworkObjectId}");

        return netObj;
    }

    // ===== existing functions unchanged =====

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
