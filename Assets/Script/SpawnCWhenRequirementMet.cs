using Unity.Netcode;
using UnityEngine;

public class SpawnCWhenActive : NetworkBehaviour
{
    [Header("Refs")]
    [SerializeField] private ColorFactory factory;
    [SerializeField] private ColorFactoryData factoryData;
    [SerializeField] private BarShowWhenEnoughMatchingFruits requirement;
    [SerializeField] private ColorFactoryNetDriver netDriver;

    [Header("C Prefabs (must have NetworkObject)")]
    [SerializeField] private NetworkObject[] cPrefabs;

    private NetworkObject spawnedC;

    // house state
    private ObjectNetworkSync houseNet;
    private bool subscribed;

    private void Reset()
    {
        factory = GetComponent<ColorFactory>();
        factoryData = GetComponent<ColorFactoryData>();
        requirement = GetComponent<BarShowWhenEnoughMatchingFruits>();
        netDriver = GetComponent<ColorFactoryNetDriver>();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        TryBindHouse();
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
            DespawnC();

        UnbindHouse();
    }

    private void Update()
    {
        if (!IsServer) return;

        // late bind
        if (houseNet == null)
            TryBindHouse();

        // safety
        if (factory == null || factoryData == null)
            return;

        // house finished → force despawn
        if (houseNet != null && houseNet.CurrentState == HouseState.Colored)
        {
            DespawnC();
            return;
        }

        if (requirement == null || !requirement.IsRequirementMet())
        {
            DespawnC();
            return;
        }

        // already spawned
        if (spawnedC != null)
            return;

        SpawnC();
    }

    // =============================
    // Spawn / Despawn
    // =============================

    private void SpawnC()
    {
        if (cPrefabs == null || cPrefabs.Length == 0)
            return;

        var house = factory.ownerHouse;
        if (house == null)
            return;

        int idx = Mathf.Clamp(factoryData.color.Value, 0, cPrefabs.Length - 1);
        var prefab = cPrefabs[idx];
        if (prefab == null)
            return;

        Vector3 aPos = transform.position;
        Vector3 bPos = house.transform.position;

        Vector3 spawnPos = new Vector3(aPos.x, bPos.y, aPos.z);

        Vector3 dir = -(bPos - spawnPos);
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-6f)
            return;

        Vector3 right = -dir.normalized;
        Vector3 forward = Vector3.Cross(Vector3.up, right).normalized;
        if (forward.sqrMagnitude < 1e-6f)
            forward = Vector3.forward;

        Quaternion rot = Quaternion.LookRotation(forward, Vector3.up);

        spawnedC = Instantiate(prefab, spawnPos, rot);
        var effects = spawnedC.GetComponent<ElfPlayEffects>();
        var netObj = spawnedC.GetComponent<NetworkObject>();

        // 1. 先 Spawn (必須先 Spawn 才能改 NetworkVariable)
        netObj.Spawn(true);

        // 2. 設定要綁定哪個 Driver
        if (effects != null && netDriver != null) // netDriver 是你原本持有的那個
        {
            // ⭐ 把你的 netDriver 轉成 NetworkObject 塞進去
            // 這一行執行後，所有 Client 都會收到通知，並執行上面的 OnTargetChanged -> BindDriver
            effects.TargetDriverRef.Value = netDriver.GetComponent<NetworkObject>();
        }

        Debug.Log("[SpawnC] Spawned C");
    }

    private void DespawnC()
    {
        if (spawnedC != null && spawnedC.IsSpawned)
        {
            spawnedC.Despawn(true);
            Debug.Log("[SpawnC] Despawn C");
        }
        spawnedC = null;
    }

    // =============================
    // House binding
    // =============================

    private void TryBindHouse()
    {
        if (factory == null || factory.ownerHouse == null)
            return;

        var net = factory.ownerHouse.GetComponent<ObjectNetworkSync>();
        if (net == null)
            return;

        if (houseNet == net && subscribed)
            return;

        UnbindHouse();
        houseNet = net;
        houseNet.OnHouseStateChanged += OnHouseStateChanged;
        subscribed = true;
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

    private void OnHouseStateChanged(HouseState state)
    {
        if (!IsServer) return;

        if (state == HouseState.Colored)
            DespawnC();
    }
}
