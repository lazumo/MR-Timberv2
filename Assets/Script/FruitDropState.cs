using UnityEngine;
using Unity.Netcode;

public class FruitDropState : NetworkBehaviour
{
    public NetworkVariable<bool> HasDropped = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Server 呼叫
    public void MarkDropped()
    {
        if (!IsServer) return;
        HasDropped.Value = true;
    }
}
