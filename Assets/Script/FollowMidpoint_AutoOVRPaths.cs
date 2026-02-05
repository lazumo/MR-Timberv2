using UnityEngine;
using Unity.Netcode;

public class CrossDeviceSawFollower_ServerAuthoritative : NetworkBehaviour
{
    [Header("Assign in scene/prefab (per device)")]
    public Transform serverLeftControllerAnchor;   // Server 端要有（左手）
    public Transform clientRightControllerAnchor;  // Client 端要有（右手）

    private Transform toolsRoot;

    private NetworkVariable<Vector3> clientRightPos =
        new NetworkVariable<Vector3>(
            Vector3.zero,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    public override void OnNetworkSpawn()
    {
        toolsRoot = transform.parent;
    }

    void Update()
    {
        // Client 只負責送右手位置（非 Server 的那台）
        if (IsClient && !IsServer && clientRightControllerAnchor != null)
        {
            SubmitClientRightHandPosServerRpc(clientRightControllerAnchor.position);
        }

        // Server 也可以額外保險：如果 serverLeft 沒設到就不做
        // （不在這裡動 saw，saw 在 LateUpdate 才動）
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
    void SubmitClientRightHandPosServerRpc(Vector3 pos)
    {
        clientRightPos.Value = pos;
    }
}
