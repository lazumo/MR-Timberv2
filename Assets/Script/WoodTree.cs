using UnityEngine;
using Unity.Netcode;

public class WoodTree : NetworkBehaviour
{
    [Header("Cutting Settings")]
    public GameObject woodMaterialPrefab; 

    // MUST BE PUBLIC for SliceObject to call
    public void SpawnMaterials()
    {
        if (woodMaterialPrefab != null)
        {
            GameObject mat = Instantiate(woodMaterialPrefab, transform.position + Vector3.up * 0.5f, Quaternion.identity);
            var netObj = mat.GetComponent<NetworkObject>();
            if (netObj != null) netObj.Spawn();
        }
    }

    private void KillTree()
    {
        if (TreeSpawnerNetworked.Instance != null)
            TreeSpawnerNetworked.Instance.NotifyTreeDestroyed(TreeSpawnerNetworked.TreeType.Wood);
        GetComponent<NetworkObject>().Despawn();
    }
}