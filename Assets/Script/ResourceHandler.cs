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

        GameObject resObj = Instantiate(resourcePrefab, spawnPos, Quaternion.identity);
        NetworkObject netObj = resObj.GetComponent<NetworkObject>();
        netObj.Spawn(true);

        if (HouseSpawnerNetworked.Instance != null &&
            HouseSpawnerNetworked.Instance.TryGetNextHouse(out var house))
        {
            NetworkResourceController rc = resObj.GetComponent<NetworkResourceController>();
            if (rc != null)
            {
                rc.AssignJob(house.Id, house.Position);
            }
        }
    }
}