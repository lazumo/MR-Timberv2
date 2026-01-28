using Unity.Netcode;
using UnityEngine;

public class MiddlePointProvider : NetworkBehaviour
{
    public NetworkVariable<Vector3> MidPosition =
        new NetworkVariable<Vector3>(Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 平均 orientation（用 Slerp 0.5）
    public NetworkVariable<Quaternion> MidRotationAvg =
        new NetworkVariable<Quaternion>(Quaternion.identity, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Host orientation（給你的物件A用）
    public NetworkVariable<Quaternion> HostRotation =
        new NetworkVariable<Quaternion>(Quaternion.identity, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<float> Distance =
        new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public HandFollower HostHand { get; private set; }
    public HandFollower ClientHand { get; private set; }

    private void Update()
    {
        if (!IsServer) return;

        // 找兩個 HandFollower（場上通常就兩個）
        ResolveHands();
        if (HostHand == null || ClientHand == null) return;

        Vector3 p0 = HostHand.transform.position;
        Vector3 p1 = ClientHand.transform.position;

        MidPosition.Value = (p0 + p1) * 0.5f;
        Distance.Value = Vector3.Distance(p0, p1);

        Quaternion r0 = HostHand.transform.rotation;
        Quaternion r1 = ClientHand.transform.rotation;

        HostRotation.Value = r0;
        MidRotationAvg.Value = Quaternion.Slerp(r0, r1, 0.5f);
    }

    private void ResolveHands()
    {
        // 如果已經抓到就不重抓（除非物件被消失）
        if (HostHand != null && ClientHand != null) return;

        HostHand = null;
        ClientHand = null;

        var all = FindObjectsByType<HandFollower>(FindObjectsSortMode.None);
        ulong hostId = NetworkManager.Singleton.LocalClientId;

        foreach (var h in all)
        {
            if (h == null || h.NetworkObject == null || !h.NetworkObject.IsSpawned) continue;

            if (h.OwnerClientId == hostId) HostHand = h;
            else ClientHand = h;
        }
        if (HostHand != null && ClientHand != null)
            Debug.Log($"[MiddlePointProvider] HostHand={HostHand.OwnerClientId}, ClientHand={ClientHand.OwnerClientId}");
    }
}
