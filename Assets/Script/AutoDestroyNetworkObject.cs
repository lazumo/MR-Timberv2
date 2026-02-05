using Unity.Netcode;
using UnityEngine;
using System.Collections;

public class AutoDestroyNetworkObject : NetworkBehaviour
{
    [Header("Destroy VFX by ColorIndex")]
    public GameObject[] destroyVFXByColor;

    private bool destroyScheduled = false;

    public void ScheduleDespawn(float delay)
    {
        if (!IsServer || destroyScheduled) return;
        destroyScheduled = true;
        StartCoroutine(DespawnAfterDelay(delay));
    }

    private IEnumerator DespawnAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        PerformDespawn();
    }

    private void PerformDespawn()
    {
        int colorIndex = ResolveColorIndex();

        // ⭐ 所有 Client 播放 VFX
        PlayDestroyVFXClientRpc(colorIndex, transform.position);

        // ⭐ Server Despawn
        if (NetworkObject != null && NetworkObject.IsSpawned)
            NetworkObject.Despawn(true);
    }

    private int ResolveColorIndex()
    {
        var fruitData = GetComponent<FruitData>();
        if (fruitData == null) return 0;
        return fruitData.colorIndex.Value;
    }

    [ClientRpc]
    private void PlayDestroyVFXClientRpc(int colorIndex, Vector3 pos)
    {
        if (destroyVFXByColor == null || destroyVFXByColor.Length == 0)
            return;

        if (colorIndex < 0 || colorIndex >= destroyVFXByColor.Length)
            colorIndex = 0;

        var prefab = destroyVFXByColor[colorIndex];
        if (prefab == null) return;

        Instantiate(prefab, pos, Quaternion.identity);
    }
}
