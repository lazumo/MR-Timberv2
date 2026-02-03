using Unity.Netcode;
using UnityEngine;

public class SpawnCWhenRequirementMet : NetworkBehaviour
{
    [Header("Refs on A")]
    [SerializeField] private ColorFactory factory;
    [SerializeField] private BarShowWhenEnoughMatchingFruits requirement;

    [Header("C Prefabs (must have NetworkObject)")]
    [SerializeField] private NetworkObject[] cPrefabs;

    [Header("Index source")]
    [SerializeField] private ColorFactoryData factoryData;

    private NetworkObject spawnedC;

    private void Reset()
    {
        factory = GetComponent<ColorFactory>();
        requirement = GetComponent<BarShowWhenEnoughMatchingFruits>();
        factoryData = GetComponent<ColorFactoryData>();
    }

    private void Update()
    {
        if (!IsServer)
        {
            Debug.Log($"[SpawnC] Not server -> skip (owner: {gameObject.name})");
            return;
        }

        if (spawnedC != null)
        {
            Debug.Log($"[SpawnC] Already spawned -> skip");
            return;
        }

        if (factory == null)
        {
            Debug.LogError("[SpawnC] factory is null!");
            return;
        }

        if (requirement == null)
        {
            Debug.LogError("[SpawnC] requirement is null!");
            return;
        }

        if (factoryData == null)
        {
            Debug.LogError("[SpawnC] factoryData is null!");
            return;
        }

        if (cPrefabs == null || cPrefabs.Length == 0)
        {
            Debug.LogError("[SpawnC] cPrefabs not assigned or empty!");
            return;
        }

        if (!requirement.IsRequirementMet())
        {
            Debug.Log($"[SpawnC] Requirement not met yet");
            return;
        }

        var b = factory.ownerHouse;
        if (b == null)
        {
            Debug.LogError("[SpawnC] ownerHouse (B) is null!");
            return;
        }

        int idx = factoryData.color.Value;
        Debug.Log($"[SpawnC] Raw index from factoryData: {idx}");

        idx = Mathf.Clamp(idx, 0, cPrefabs.Length - 1);
        Debug.Log($"[SpawnC] Clamped index: {idx}");

        var chosenPrefab = cPrefabs[idx];

        if (chosenPrefab == null)
        {
            Debug.LogError($"[SpawnC] Prefab at index {idx} is null!");
            return;
        }

        Debug.Log($"[SpawnC] Chosen prefab name: {chosenPrefab.name}");

        Vector3 aPos = transform.position;
        Vector3 bPos = b.transform.position;

        Debug.Log($"[SpawnC] A pos: {aPos}, B pos: {bPos}");

        Vector3 spawnPos = new Vector3(aPos.x, bPos.y, aPos.z);
        Debug.Log($"[SpawnC] Calculated spawnPos: {spawnPos}");

        Vector3 dir = bPos - spawnPos;
        dir = -dir;
        dir.y = 0f;

        if (dir.sqrMagnitude < 1e-6f)
        {
            Debug.LogWarning("[SpawnC] Direction too small, skip rotation");
            return;
        }

        Vector3 right = -dir.normalized;
        Vector3 up = Vector3.up;
        Vector3 forward = Vector3.Cross(up, right).normalized;

        if (forward.sqrMagnitude < 1e-6f)
            forward = Vector3.forward;

        Quaternion rot = Quaternion.LookRotation(forward, up);

        Debug.Log($"[SpawnC] Spawning object now...");

        spawnedC = Instantiate(chosenPrefab, spawnPos, rot);
        spawnedC.Spawn(true);

        Debug.Log($"[SpawnC] Spawned successfully: {spawnedC.name}");
    }
}
