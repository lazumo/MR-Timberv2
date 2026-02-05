using UnityEngine;
using Unity.Netcode;
using System.Linq;

public class SpectatorGateApproval : MonoBehaviour
{
    public int maxPlayers = 2;

    void Awake()
    {
        NetworkManager.Singleton.ConnectionApprovalCallback += Approval;
    }

    void Approval(NetworkManager.ConnectionApprovalRequest req,
                  NetworkManager.ConnectionApprovalResponse res)
    {
        var nm = NetworkManager.Singleton;

        // 已連線的 client 數（不含 server/host 自己）
        int connected = nm.ConnectedClientsIds.Count(id => id != nm.LocalClientId);

        // 這次加入後，會變成第幾個「非 server」client
        int afterJoin = connected + 1;

        bool spectator = afterJoin > maxPlayers;

        res.Approved = true;                  // 觀戰者也允許連線
        res.CreatePlayerObject = !spectator;  // 第3人以後不生成 Player
        res.Pending = false;
    }
}
