using Unity.Netcode;
using UnityEngine;

public class SpawnCWhenRequirementMet : NetworkBehaviour
{
    [Header("Refs on A")]
    [SerializeField] private ColorFactory factory; // A
    [SerializeField] private BarShowWhenEnoughMatchingFruits requirement;

    [Header("C Prefabs (must have NetworkObject)")]
    [SerializeField] private NetworkObject[] cPrefabs; // <--- 改成陣列

    [Header("Index source")]
    [SerializeField] private ColorFactoryData factoryData; // <--- 用來拿 index (color)

    private NetworkObject spawnedC;

    private void Reset()
    {
        factory = GetComponent<ColorFactory>();
        requirement = GetComponent<BarShowWhenEnoughMatchingFruits>();
        factoryData = GetComponent<ColorFactoryData>();
    }

    private void Update()
    {
        if (!IsServer) return;
        if (spawnedC != null) return;

        if (factory == null || requirement == null || factoryData == null) return;
        if (cPrefabs == null || cPrefabs.Length == 0) return;
        if (!requirement.IsRequirementMet()) return;

        var b = factory.ownerHouse; // B
        if (b == null) return;

        // 依據 index 選 prefab（這裡用 color.Value 當 index）
        int idx = factoryData.color.Value;

        // 安全：避免 idx 越界（兩種常用策略擇一）
        // 策略 A：Clamp（超出就用邊界值）
        idx = Mathf.Clamp(idx, 0, cPrefabs.Length - 1);

        // 策略 B：Wrap（超出就用循環取餘數）
        // idx = ((idx % cPrefabs.Length) + cPrefabs.Length) % cPrefabs.Length;

        var chosenPrefab = cPrefabs[idx];
        if (chosenPrefab == null) return;

        Vector3 aPos = transform.position;
        Vector3 bPos = b.transform.position;

        Vector3 spawnPos = new Vector3(aPos.x, bPos.y, aPos.z);

        Vector3 dir = -(bPos - spawnPos);
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-6f) return;

        Vector3 right = -dir.normalized;
        Vector3 up = Vector3.up;
        Vector3 forward = Vector3.Cross(up, right).normalized;
        if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;

        Quaternion rot = Quaternion.LookRotation(forward, up);

        spawnedC = Instantiate(chosenPrefab, spawnPos, rot);
        spawnedC.Spawn(true);
    }
}
