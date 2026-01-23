using Unity.Netcode;
using UnityEngine;

public class ExtinguisherManager : NetworkBehaviour
{
    public NetworkObject serverExtinguisher;
    public NetworkObject clientExtinguisher;

    public override void OnNetworkSpawn()
    {
        // 只有 Server (Host) 有權力更改擁有權
        if (!IsServer) return;

        // 1. 將 Server 滅火器的擁有權給自己 (ServerID 永遠是 0)
        serverExtinguisher.ChangeOwnership(NetworkManager.ServerClientId);

        // 2. 監聽 Client 連線事件
        NetworkManager.OnClientConnectedCallback += (clientId) =>
        {
            if (clientId != NetworkManager.ServerClientId)
            {
                // 當 Client 加入時，把 Client 滅火器的擁有權給他
                clientExtinguisher.ChangeOwnership(clientId);
                Debug.Log($"已將左手滅火器分配給 Client: {clientId}");
            }
        };
    }
}