using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class NetworkSpawner : NetworkBehaviour, IStageEnable
{
    [SerializeField] private GameObject objectToSpawn; // prefab 必須有 NetworkObject
    [SerializeField] private int maxPlayers = 2;        // ⭐ 最多兩個玩家

    private bool _stageEnabled = false;

    private readonly Dictionary<ulong, NetworkObject> _spawnedObjects
        = new Dictionary<ulong, NetworkObject>();

    // ⭐ 哪些 client 被視為「玩家」
    private readonly HashSet<ulong> _playerClients = new HashSet<ulong>();

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
        _playerClients.Clear();
    }

    public void SetStageEnabled(bool enabled)
    {
        if (!IsServer) return;
        if (_stageEnabled == enabled) return;

        _stageEnabled = enabled;
        Debug.Log($"[NetworkSpawner] StageEnabled = {_stageEnabled}");

        if (_stageEnabled)
        {
            // ⭐ 補 spawn 給已在線的 client（只挑前兩個）
            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                TryRegisterAndSpawnPlayer(client.ClientId);
            }
        }
        else
        {
            ForceDespawnAll();
            _playerClients.Clear(); // ⭐ 關掉時重置名額
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        if (!_stageEnabled) return;
        TryRegisterAndSpawnPlayer(clientId);
    }

    private void TryRegisterAndSpawnPlayer(ulong clientId)
    {
        if (_spawnedObjects.ContainsKey(clientId)) return;

        // ⭐ 已經是玩家就照常 spawn
        if (_playerClients.Contains(clientId))
        {
            TrySpawnForClient(clientId);
            return;
        }

        // ⭐ 玩家名額滿了：不 spawn（視為 Observer）
        if (_playerClients.Count >= maxPlayers)
        {
            Debug.Log($"[NetworkSpawner] Client {clientId} joined as OBSERVER (no spawn).");
            return;
        }

        // ⭐ 登記成玩家並 spawn
        _playerClients.Add(clientId);
        Debug.Log($"[NetworkSpawner] Client {clientId} registered as PLAYER ({_playerClients.Count}/{maxPlayers}).");

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
