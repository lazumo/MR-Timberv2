using Unity.Netcode;
using UnityEngine;

public class ColorFactoryData : NetworkBehaviour
{
    public NetworkVariable<int> color =
        new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    private bool initialized;

    public void ServerInit(int c)
    {
        if (!IsServer || initialized) return;

        color.Value = c;
        initialized = true;
    }
}
