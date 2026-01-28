using UnityEngine;
using Unity.Netcode;

public class FruitDropState : NetworkBehaviour
{
    public NetworkVariable<bool> HasDropped = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<bool> HasLanded = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Server 呼叫：開始掉落
    public void MarkDropped()
    {
        if (!IsServer) return;
        HasDropped.Value = true;
    }

    // Server 呼叫：真正落地 / 進箱
    public void MarkLanded()
    {
        if (!IsServer) return;
        HasLanded.Value = true;
    }
}
