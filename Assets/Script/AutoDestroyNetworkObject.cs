using Unity.Netcode;
using UnityEngine;
using System.Collections;

public class AutoDestroyNetworkObject : NetworkBehaviour
{
    public float lifetime = 240f; // 4 minutes

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            StartCoroutine(DestroyAfterTime());
        }
    }

    private IEnumerator DestroyAfterTime()
    {
        yield return new WaitForSeconds(lifetime);

        if (IsServer)
        {
            NetworkObject.Despawn(true);  // true = also destroy GameObject
        }
    }
}
