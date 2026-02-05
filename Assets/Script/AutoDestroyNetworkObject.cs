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
        // ⭐ 根據 FruitData.colorIndex 選 VFX
        GameObject vfxPrefab = ResolveDestroyVFX();

        if (vfxPrefab != null)
        {
            Instantiate(vfxPrefab, transform.position, Quaternion.identity);
        }

        // ⭐ Network Despawn（Server only）
        if (NetworkObject != null && NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn(true);
        }
    }


    private GameObject ResolveDestroyVFX()
    {
        var fruitData = GetComponent<FruitData>();
        if (fruitData == null) return null;

        int index = fruitData.colorIndex.Value;

        if (destroyVFXByColor == null || destroyVFXByColor.Length == 0)
            return null;

        if (index < 0 || index >= destroyVFXByColor.Length)
            index = 0;

        return destroyVFXByColor[index];
    }
}
