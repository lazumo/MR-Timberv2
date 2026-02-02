using Unity.Netcode;
using UnityEngine;

public class ColorFactoryData : NetworkBehaviour
{
    [Header("Design-time / Debug")]
    [Tooltip("Only used if server does not assign explicitly")]
    public int defaultColorIndex = 0;

    [Header("Runtime (Networked)")]
    public NetworkVariable<int> colorIndex =
        new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    private bool initialized;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // 如果 server 還沒指定，就用 Inspector 設的 default
        if (IsServer && !initialized)
        {
            colorIndex.Value = defaultColorIndex;
            initialized = true;
        }
    }

    /// <summary>
    /// Server-only: explicitly assign color index (preferred in production)
    /// </summary>
    public void ServerInitColorIndex(int idx)
    {
        if (!IsServer) return;
        if (initialized) return;

        colorIndex.Value = idx;
        initialized = true;
    }
}
