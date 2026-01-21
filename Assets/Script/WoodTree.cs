using UnityEngine;
using Unity.Netcode;

public class WoodTree : NetworkBehaviour
{
    [Header("Cutting Settings")]
    public int hitsToCut = 3;
    public GameObject woodMaterialPrefab; // The item to spawn when cut
    
    private int _currentHits = 0;

    // Call this function from your Saw/Axe script when it triggers the collider
    public void TakeSawDamage()
    {
        // Only the Server manages damage and death
        if (!IsServer) return;

        _currentHits++;
        if (_currentHits >= hitsToCut)
        {
            SpawnMaterials();
            KillTree();
        }
    }

    private void SpawnMaterials()
    {
        if (woodMaterialPrefab != null)
        {
            // Spawn logic for the wood item
            GameObject mat = Instantiate(woodMaterialPrefab, transform.position + Vector3.up * 0.5f, Quaternion.identity);
            mat.GetComponent<NetworkObject>().Spawn();
        }
    }

    private void KillTree()
    {
        // 1. Tell Manager we died
        if (TreeSpawnerNetworked.Instance != null)
        {
            TreeSpawnerNetworked.Instance.NotifyTreeDestroyed(TreeSpawnerNetworked.TreeType.Wood);
        }

        // 2. Despawn self
        GetComponent<NetworkObject>().Despawn();
    }
}