using Unity.Netcode;
using UnityEngine;

public class NetworkSpawner : NetworkBehaviour
{
    [SerializeField] private GameObject objectToSpawn; // 拖入剛才做好的 Prefab

    public override void OnNetworkSpawn()
    {
        // 只有 Server (Host) 負責監聽玩家連入
        if (IsServer)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        // 當有玩家（包括 Host 自己）連線時
        GameObject spawnedObj = Instantiate(objectToSpawn);

        // 生成並把擁有權 (Ownership) 交給該 clientId
        var networkObj = spawnedObj.GetComponent<NetworkObject>();
        networkObj.SpawnWithOwnership(clientId);
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }
    }
}