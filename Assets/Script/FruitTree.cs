using UnityEngine;
using Unity.Netcode;
using System.Collections;

public class FruitTree : NetworkBehaviour
{
    [Header("Lifecycle")]
    [Tooltip("Time in seconds before fruits fall and tree disappears")]
    public float fruitLifetime = 10.0f;
    
    [Header("Fruit Drop")]
    public GameObject fruitItemPrefab; // The apple/fruit item to spawn

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            StartCoroutine(LifecycleRoutine());
        }
    }

    private IEnumerator LifecycleRoutine()
    {
        // Wait for the tree to "grow" and fruits to ripen
        yield return new WaitForSeconds(fruitLifetime);

        // Time's up! Drop fruits.
        DropFruits();

        // Destroy tree
        KillTree();
    }

    private void DropFruits()
    {
        // Because the tree is on the ceiling, we look DOWN (relative to World) to find the floor.
        // We cast a ray from the tree position downwards.
        if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, 10f))
        {
            Debug.Log($"Dropping fruit at floor position: {hit.point}");
            
            if (fruitItemPrefab != null)
            {
                // Spawn fruit on the floor point we hit
                GameObject fruit = Instantiate(fruitItemPrefab, hit.point, Quaternion.identity);
                fruit.GetComponent<NetworkObject>().Spawn();
            }
        }
    }

    private void KillTree()
    {
        // 1. Tell Manager we died
        if (TreeSpawnerNetworked.Instance != null)
        {
            TreeSpawnerNetworked.Instance.NotifyTreeDestroyed(TreeSpawnerNetworked.TreeType.Fruit);
        }

        // 2. Despawn self
        GetComponent<NetworkObject>().Despawn();
    }
}