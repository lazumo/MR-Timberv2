using UnityEngine;
using Unity.Netcode;
using System.Collections;

public class ResourceManager : NetworkBehaviour
{
    public static ResourceManager Instance;

    [Header("Settings")]
    public GameObject resourcePrefab;

    private void Awake()
    {
        if (Instance == null) Instance = this;
    }

    public void SpawnResource(Vector3 spawnPos)
    {
        if (!IsServer) return;

        if (resourcePrefab == null)
        {
            Debug.LogError("[ResourceManager] resourcePrefab missing!");
            return;
        }

        HouseSpawnerNetworked.HouseData house = default;
        bool hasHouse = false;

        if (HouseSpawnerNetworked.Instance != null)
        {
            hasHouse = HouseSpawnerNetworked.Instance.TryGetNextHouse(out house);
        }

        // ===== rotation =====
        Quaternion rotation = Quaternion.identity;

        if (hasHouse)
        {
            Vector3 dir = house.Position - spawnPos;
            if (dir.sqrMagnitude > 0.0001f)
            {
                rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            }
        }

        // ===== spawn =====
        GameObject resObj = Instantiate(resourcePrefab, spawnPos, rotation);
        NetworkObject netObj = resObj.GetComponent<NetworkObject>();
        netObj.Spawn(true);

        // ===== assign job =====
        if (hasHouse)
        {
            NetworkResourceController rc = resObj.GetComponent<NetworkResourceController>();
            if (rc != null)
            {
                rc.AssignJob(house.Id, house.Position);
            }
        }
    }
}