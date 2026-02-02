using Unity.Netcode;
using UnityEngine;

public class SpawnCWhenRequirementMet : NetworkBehaviour
{
    [Header("Refs on A")]
    [SerializeField] private ColorFactory factory; // A
    [SerializeField] private BarShowWhenEnoughMatchingFruits requirement;

    [Header("C Prefab (must have NetworkObject)")]
    [SerializeField] private NetworkObject cPrefab;

    private NetworkObject spawnedC;

    private void Reset()
    {
        factory = GetComponent<ColorFactory>();
        requirement = GetComponent<BarShowWhenEnoughMatchingFruits>();
    }

    private void Update()
    {
        if (!IsServer) return;
        if (spawnedC != null) return;

        if (factory == null || requirement == null || cPrefab == null) return;
        if (!requirement.IsRequirementMet()) return;

        var b = factory.ownerHouse; // B
        if (b == null) return;

        Vector3 aPos = transform.position;
        Vector3 bPos = b.transform.position;

        // C 位於 A/B 的 y 軸交會處：x,z 用 A；y 用 B
        Vector3 spawnPos = new Vector3(aPos.x, bPos.y, aPos.z);

        // 讓 C 的 -X 軸朝向 B（通常只在水平面轉向）
        Vector3 dir = -(bPos - spawnPos);
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-6f) return;

        Vector3 right = -dir.normalized;          // C.right 指向 -dir => C.-right(= -X) 指向 dir
        Vector3 up = Vector3.up;
        Vector3 forward = Vector3.Cross(up, right).normalized;
        if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;

        Quaternion rot = Quaternion.LookRotation(forward, up);

        spawnedC = Instantiate(cPrefab, spawnPos, rot);
        spawnedC.Spawn(true);
    }
}
