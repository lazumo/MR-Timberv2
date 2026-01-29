using Unity.Netcode;
using UnityEngine;

public class FruitData : NetworkBehaviour
{
    [HideInInspector]
    public int colorIndex;

    [HideInInspector]
    public Color color;

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
