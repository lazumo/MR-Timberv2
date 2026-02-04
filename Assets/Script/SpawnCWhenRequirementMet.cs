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

    private ObjectNetworkSync houseNet;
    private bool subscribed;

    private void Reset()
    {
        factory = GetComponent<ColorFactory>();
        requirement = GetComponent<BarShowWhenEnoughMatchingFruits>();
        factoryData = GetComponent<ColorFactoryData>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer) TryBindHouse();
    }

    public override void OnNetworkDespawn()
    {
        // ✅ 保底：就算 factory 要被 despawn，也先把 C despawn 掉
        if (IsServer)
            DespawnC();

        UnbindHouse();
        base.OnNetworkDespawn();
    }

    private void TryBindHouse()
    {
        if (factory == null) return;
        if (factory.ownerHouse == null) return;

        var net = factory.ownerHouse.GetComponent<ObjectNetworkSync>();
        if (net == null) return;

        if (houseNet == net && subscribed) return;

        UnbindHouse();
        houseNet = net;

        houseNet.OnHouseStateChanged += OnHouseStateChanged;
        subscribed = true;

        // 立刻檢查：如果已經 Colored，直接清掉
        if (houseNet.CurrentState == HouseState.Colored)
            DespawnC();
    }

    private void UnbindHouse()
    {
        if (houseNet != null && subscribed)
        {
            houseNet.OnHouseStateChanged -= OnHouseStateChanged;
            subscribed = false;
        }
        houseNet = null;
    }

    private void OnHouseStateChanged(HouseState s)
    {
        if (!IsServer) return;

        if (s == HouseState.Colored)
            DespawnC();
    }

    private void DespawnC()
    {
        if (spawnedC != null && spawnedC.IsSpawned)
        {
            Debug.Log("[SpawnC] Despawn C");
            spawnedC.Despawn(true);
        }
        spawnedC = null;
    }

    private void Update()
    {
        if (!IsServer) return;

        // ownerHouse 可能晚一點才有，補綁定
        if (houseNet == null)
            TryBindHouse();

        // house 已 Colored：不要 spawn
        if (houseNet != null && houseNet.CurrentState == HouseState.Colored)
            return;

        if (spawnedC != null)
            return;

        if (factory == null || requirement == null || factoryData == null)
            return;

        if (cPrefabs == null || cPrefabs.Length == 0)
            return;

        if (!requirement.IsRequirementMet())
            return;

        var b = factory.ownerHouse;
        if (b == null)
            return;

        int idx = Mathf.Clamp(factoryData.color.Value, 0, cPrefabs.Length - 1);
        var chosenPrefab = cPrefabs[idx];
        if (chosenPrefab == null)
            return;

        Vector3 aPos = transform.position;
        Vector3 bPos = b.transform.position;

        Vector3 spawnPos = new Vector3(aPos.x, bPos.y, aPos.z);

        Vector3 dir = -(bPos - spawnPos);
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-6f)
            return;

        Vector3 right = -dir.normalized;
        Vector3 up = Vector3.up;
        Vector3 forward = Vector3.Cross(up, right).normalized;
        if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;

        Quaternion rot = Quaternion.LookRotation(forward, up);

        spawnedC = Instantiate(chosenPrefab, spawnPos, rot);
        spawnedC.Spawn(true);

        Debug.Log($"[SpawnC] Spawned C: {spawnedC.name}");
    }
}
