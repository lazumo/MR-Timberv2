using Unity.Netcode;
using UnityEngine;

public class FruitData : NetworkBehaviour
{
    public NetworkVariable<int> colorIndex = new NetworkVariable<int>();

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        var projector = GetComponent<FruitShadowProjector>();
        if (projector != null)
        {
            projector.InitializeFromFruit();
        }
    }

}
