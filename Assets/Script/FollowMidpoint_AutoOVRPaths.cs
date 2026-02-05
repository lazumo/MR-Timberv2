using UnityEngine;
using Unity.Netcode;

public class CrossDeviceSawFollower_ServerAuthoritative : NetworkBehaviour
{
    [Header("Assign in scene/prefab (per device)")]
    public Transform serverLeftControllerAnchor;   // Server 端左手
    public Transform clientRightControllerAnchor;  // Client 端右手（非 Server 的那台）

    private Transform toolsRoot;

    private NetworkVariable<Vector3> clientRightPos =
        new NetworkVariable<Vector3>(
            Vector3.zero,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    // 只追蹤這個 client 的右手（避免第三人覆蓋）
    private ulong trackedClientId = ulong.MaxValue;

    public override void OnNetworkSpawn()
    {
        toolsRoot = transform.parent;

        if (IsServer)
        {
            PickTrackedClientIfNeeded();

            // 斷線/重連時保持穩定
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        // 如果還沒選到玩家，就選一個
        PickTrackedClientIfNeeded();
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (clientId == trackedClientId)
        {
            trackedClientId = ulong.MaxValue;
            PickTrackedClientIfNeeded();
        }
    }

    private void PickTrackedClientIfNeeded()
    {
        if (trackedClientId != ulong.MaxValue) return;

        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        ulong serverId = NetworkManager.ServerClientId;


        foreach (var id in nm.ConnectedClientsIds)
        {
            if (id != serverId)
            {
                trackedClientId = id; // 只追蹤第一個非Server client
                break;
            }
        }
    }

    void Update()
    {
        // 任何非 Server 的 client 都可能跑到這段（含 Observer）
        // 沒關係，Server 端會用 SenderClientId 過濾掉不該進來的
        if (IsClient && !IsServer && clientRightControllerAnchor != null)
        {
            SubmitClientRightHandPosServerRpc(clientRightControllerAnchor.position);
        }
    }

    void LateUpdate()
    {
        // 鋸子只讓 Server 動
        if (!IsServer) return;
        if (toolsRoot == null || serverLeftControllerAnchor == null) return;

        Vector3 leftPos = serverLeftControllerAnchor.position;
        Vector3 rightPos = clientRightPos.Value;

        Vector3 midpoint = (leftPos + rightPos) * 0.5f;

        // rotation 以 server 左手 rotation 為準
        Quaternion rot = serverLeftControllerAnchor.rotation;

        toolsRoot.SetPositionAndRotation(midpoint, rot);
    }

    [ServerRpc(RequireOwnership = false)]
    void SubmitClientRightHandPosServerRpc(Vector3 pos, ServerRpcParams rpcParams = default)
    {
        // ✅ 關鍵：只收被追蹤的那個 client
        if (rpcParams.Receive.SenderClientId != trackedClientId) return;

        clientRightPos.Value = pos;
    }
}
