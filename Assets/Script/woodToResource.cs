using UnityEngine;
using Unity.Netcode;
using System.Collections;

public class WoodDropToResource : NetworkBehaviour
{
    [SerializeField] private float delay = 3f;

    public override void OnNetworkSpawn()
    {
        if (IsServer)
            StartCoroutine(SpawnFinalResourceAfterDelay());
    }

    private IEnumerator SpawnFinalResourceAfterDelay()
    {
        yield return new WaitForSeconds(delay);

        // ⭐ 用「當下位置」
        Vector3 finalPos = transform.position;

        if (ResourceManager.Instance != null)
        {
            ResourceManager.Instance.SpawnResource(finalPos);
        }

        // 自己消失（素材結束）
        NetworkObject.Despawn();
    }
}
