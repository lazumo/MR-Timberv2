using Unity.Netcode;
using UnityEngine;

public class ColorFactoryNetDriver : NetworkBehaviour
{
    [Header("Target (A)")]
    [SerializeField] private Transform factoryTransform;

    [Header("Rotate (Yaw only)")]
    [SerializeField] private float rotateLerp = 20f;

    [Header("Networking")]
    public NetworkVariable<bool> IsActive = new(false);

    // serverLeft <-> clientRight distance
    public NetworkVariable<float> HandDistance = new(0f);      // current (m)
    public NetworkVariable<float> HandDistanceBase = new(0f);  // baseline at start (m)

    // ✅ Server 指定「哪個 client」的右手會被採用（第三人只能看）
    public NetworkVariable<ulong> TrackedClientId =
        new NetworkVariable<ulong>(
            ulong.MaxValue,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    // ✅ Client 右手位置（由 server 寫，大家可讀）
    private NetworkVariable<Vector3> clientRightPos =
        new NetworkVariable<Vector3>(
            Vector3.zero,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    // Host(Server) 左手 controller（server only）
    private Transform serverLeftController;

    // Remote client 右手 controller（client only，用來送）
    private Transform localRightController;

    // Meta Building Blocks rig
    private const string RigName = "[BuildingBlock] Camera Rig";
    private const string LeftPath = "TrackingSpace/LeftHandAnchor/LeftControllerAnchor";
    private const string RightPath = "TrackingSpace/RightHandAnchor/RightControllerAnchor";

    private void Reset()
    {
        factoryTransform = transform;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            TryBindServerLeft();
            PickTrackedClientIfNeeded();

            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }

        if (IsClient && !IsServer)
        {
            TryBindLocalRight();
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
        if (!IsServer) return;
        PickTrackedClientIfNeeded();
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (!IsServer) return;

        if (TrackedClientId.Value == clientId)
        {
            TrackedClientId.Value = ulong.MaxValue;
            clientRightPos.Value = Vector3.zero;
            PickTrackedClientIfNeeded();
        }
    }

    // ✅ 只選第一個「非 Server」client 當可影響的玩家；其他都只能看
    private void PickTrackedClientIfNeeded()
    {
        if (!IsServer) return;
        if (TrackedClientId.Value != ulong.MaxValue) return;

        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        ulong serverId = NetworkManager.ServerClientId;

        foreach (var id in nm.ConnectedClientsIds)
        {
            if (id != serverId)
            {
                TrackedClientId.Value = id;
                break;
            }
        }
    }

    private void TryBindServerLeft()
    {
        if (serverLeftController != null) return;

        var rig = GameObject.Find(RigName);
        if (rig == null) return;

        serverLeftController = rig.transform.Find(LeftPath);
    }

    private void TryBindLocalRight()
    {
        if (localRightController != null) return;

        var rig = GameObject.Find(RigName);
        if (rig == null) return;

        localRightController = rig.transform.Find(RightPath);
    }

    private void Update()
    {
        // ✅ 只有「被指定的那個 client」才送右手位置
        if (IsClient && !IsServer)
        {
            if (!IsActive.Value) return;

            // 不是被追蹤的 client（例如第三人）→ 直接不送
            if (NetworkManager.Singleton == null) return;
            if (NetworkManager.Singleton.LocalClientId != TrackedClientId.Value) return;

            TryBindLocalRight();
            if (localRightController == null) return;

            SubmitClientRightHandPosServerRpc(localRightController.position);
        }

        // ✅ 只有 Server 做旋轉與距離計算
        if (!IsServer) return;
        if (!IsActive.Value) return;

        TryBindServerLeft();
        if (serverLeftController == null || factoryTransform == null) return;

        Vector3 leftPos = serverLeftController.position;
        Vector3 rightPos = clientRightPos.Value;

        // distance
        float dist = Vector3.Distance(leftPos, rightPos);
        HandDistance.Value = dist;

        if (HandDistanceBase.Value <= 0.0001f)
            HandDistanceBase.Value = dist;

        // yaw rotation (left -> right)
        Vector3 dir = rightPos - leftPos;
        dir.y = 0f;

        if (dir.sqrMagnitude > 0.0001f)
        {
            Quaternion targetYaw = Quaternion.LookRotation(dir.normalized, Vector3.up);
            factoryTransform.rotation =
                Quaternion.Slerp(factoryTransform.rotation, targetYaw, Time.deltaTime * rotateLerp);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;
        if (!other.CompareTag("MiddlePoint")) return;

        IsActive.Value = true;
        HandDistance.Value = 0f;
        HandDistanceBase.Value = 0f;
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsServer) return;
        if (!other.CompareTag("MiddlePoint")) return;

        IsActive.Value = false;
        HandDistance.Value = 0f;
        HandDistanceBase.Value = 0f;
    }

    [ServerRpc(RequireOwnership = false)]
    private void SubmitClientRightHandPosServerRpc(Vector3 pos, ServerRpcParams rpcParams = default)
    {
        // ✅ 再保險一次：Server 只收被指定 client 的 RPC
        if (rpcParams.Receive.SenderClientId != TrackedClientId.Value) return;

        clientRightPos.Value = pos;
    }
}
