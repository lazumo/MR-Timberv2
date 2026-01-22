using UnityEngine;
using Unity.Netcode;

public class WoodTree : NetworkBehaviour
{
    [Header("Loot Settings")]
    public GameObject woodMaterialPrefab; // Drag your Wood Item Prefab here
    public int dropCount = 1;

    // --- OLD HIT LOGIC (Optional) ---
    // If you want the saw to cut INSTANTLY using velocity, you don't need 'hitsToCut'.
    // If you want to require 3 hits, we would need to change SliceObject to call this instead.
    // For now, we assume SliceObject handles the cut immediately.
    
    /// <summary>
    /// Called by SliceObject.cs (ServerRpc) right before the tree is destroyed.
    /// </summary>
    public void SpawnMaterials()
    {
        // Safety Checks
        if (!IsServer || woodMaterialPrefab == null) return;

        for (int i = 0; i < dropCount; i++)
        {
            // 1. Calculate Position
            // Spawn slightly above and randomized so they don't stack perfectly
            Vector3 spawnPos = transform.position + Vector3.up * 0.5f;
            spawnPos += new Vector3(Random.Range(-0.2f, 0.2f), 0, Random.Range(-0.2f, 0.2f));

            // 2. Instantiate
            GameObject mat = Instantiate(woodMaterialPrefab, spawnPos, Quaternion.identity);

            // 3. Spawn on Network (CRITICAL for clients to see it)
            var netObj = mat.GetComponent<NetworkObject>();
            if (netObj != null)
            {
                netObj.Spawn();
            }
        }
    }
}