using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class NetworkSpawner : NetworkBehaviour, IStageEnable
{
    [SerializeField] private GameObject objectToSpawn; // prefab 必須有 NetworkObject

    private bool _stageEnabled = false;

    // ⭐ 記錄「每個 client 對應 spawn 的物件」
    private readonly Dictionary<ulong, NetworkObject> _spawnedObjects
        = new Dictionary<ulong, NetworkObject>();

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
    }

    public override void OnNetworkDespawn()
    {
        if (!IsServer) return;

        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;

        ForceDespawnAll();
    }

    // =========================
    // ⭐ 被 ToolStateStageActivator 呼叫
    // =========================
    public void SetStageEnabled(bool enabled)
    {
        if (!IsServer) return;

        if (_stageEnabled == enabled) return;
        _stageEnabled = enabled;

        Debug.Log($"[NetworkSpawner] StageEnabled = {_stageEnabled}");

        if (_stageEnabled)
        {
            // ⭐ 補 spawn 給已在線的 client
            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                TrySpawnForClient(client.ClientId);
            }
        }
        else
        {
            // ⭐ 關 stage 時，清掉所有 spawn
            ForceDespawnAll();
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        if (!_stageEnabled) return;

        TrySpawnForClient(clientId);
    }

    private void TrySpawnForClient(ulong clientId)
    {
        if (_spawnedObjects.ContainsKey(clientId)) return;

        var obj = Instantiate(objectToSpawn);
        var netObj = obj.GetComponent<NetworkObject>();

        if (netObj == null)
        {
            Debug.LogError("[NetworkSpawner] objectToSpawn missing NetworkObject");
            Destroy(obj);
            return;
        }

        netObj.SpawnWithOwnership(clientId);
        _spawnedObjects[clientId] = netObj;

        Debug.Log($"[NetworkSpawner] Spawned for client {clientId}");
    }

    private void ForceDespawnAll()
    {
        foreach (var kv in _spawnedObjects)
        {
            if (kv.Value != null && kv.Value.IsSpawned)
                kv.Value.Despawn(true);
        }

        _spawnedObjects.Clear();
    }
}
