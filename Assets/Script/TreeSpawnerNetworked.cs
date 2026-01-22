using UnityEngine;
using Unity.Netcode;
using Meta.XR.MRUtilityKit;
using System.Collections;

public class TreeSpawnerNetworked : NetworkBehaviour
{
    public static TreeSpawnerNetworked Instance { get; private set; }
    public enum TreeType { Wood, Fruit }

    [Header("Prefabs")]
    public GameObject woodTreePrefab;
    public GameObject fruitTreePrefab;

    [Header("Settings")]
    public int targetWoodTrees = 3;
    public float woodStartDelay = 2.0f; 
    public float woodSpawnInterval = 5.0f;

    public int targetFruitTrees = 3;
    public float fruitStartDelay = 4.0f;
    public float fruitSpawnInterval = 8.0f;

    [Header("Safety Area")]
    // 0.5f Half Extents = 1.0m Total Size
    public Vector3 safetyCheckSize = new Vector3(0.5f, 0.5f, 0.5f);
    public LayerMask collisionLayerMask; 

    private int _currentWoodCount = 0;
    private int _currentFruitCount = 0;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            if (MRUK.Instance && MRUK.Instance.GetCurrentRoom() != null) StartSpawningRoutines();
            else if (MRUK.Instance) MRUK.Instance.RegisterSceneLoadedCallback(StartSpawningRoutines);
        }
    }

    private void StartSpawningRoutines()
    {
        StartCoroutine(ManageWoodLifecycle());
        StartCoroutine(ManageFruitLifecycle());
    }

    // --- UPDATED LOGIC: Smart Retry ---
    private IEnumerator ManageWoodLifecycle()
    {
        yield return new WaitForSeconds(woodStartDelay);
        while (IsServer)
        {
            if (_currentWoodCount < targetWoodTrees)
            {
                // Try to spawn
                bool success = SpawnTree(TreeType.Wood);
                
                if (success) 
                {
                    // Success! Wait the full growth interval before checking again
                    yield return new WaitForSeconds(woodSpawnInterval); 
                }
                else 
                {
                    // Failed (No space found)? Wait briefly and retry.
                    // Don't wait 5 seconds, or it looks broken.
                    Debug.LogWarning("Failed to find space for Wood Tree. Retrying in 1s...");
                    yield return new WaitForSeconds(1.0f); 
                }
            }
            else 
            {
                yield return new WaitForSeconds(1.0f);
            }
        }
    }

    private IEnumerator ManageFruitLifecycle()
    {
        yield return new WaitForSeconds(fruitStartDelay);
        while (IsServer)
        {
            if (_currentFruitCount < targetFruitTrees)
            {
                bool success = SpawnTree(TreeType.Fruit);
                
                if (success) yield return new WaitForSeconds(fruitSpawnInterval);
                else 
                {
                    Debug.LogWarning("Failed to find space for Fruit Tree. Retrying in 1s...");
                    yield return new WaitForSeconds(1.0f);
                }
            }
            else yield return new WaitForSeconds(1.0f);
        }
    }

    // Changed return type to BOOL so we know if it worked
    private bool SpawnTree(TreeType type)
    {
        MRUKRoom room = MRUK.Instance.GetCurrentRoom();
        if (room == null) return false;

        MRUK.SurfaceType surfaceType = (type == TreeType.Wood) ? MRUK.SurfaceType.FACING_UP : MRUK.SurfaceType.FACING_DOWN;
        MRUKAnchor.SceneLabels labels = (type == TreeType.Wood) ? MRUKAnchor.SceneLabels.FLOOR : MRUKAnchor.SceneLabels.CEILING;
        LabelFilter filter = new LabelFilter(labels);

        // Increased attempts to 50 to help find empty spots
        int attempts = 0;
        while (attempts < 50)
        {
            attempts++;
            if (room.GenerateRandomPositionOnSurface(surfaceType, 0.1f, filter, out Vector3 pos, out Vector3 normal))
            {
                Quaternion rotation;
                if (type == TreeType.Wood)
                {
                    // Wood: Upright
                    rotation = Quaternion.FromToRotation(Vector3.up, normal);
                    rotation *= Quaternion.Euler(0, Random.Range(0, 360), 0);
                }
                else
                {
                    // Fruit: Hang Down (Inward)
                    // Align Tree Up to Ceiling Normal (Down)
                    rotation = Quaternion.FromToRotation(Vector3.up, normal);
                    rotation *= Quaternion.Euler(0, Random.Range(0, 360), 0);
                }

                if (IsSpaceEmpty(pos, rotation))
                {
                    PerformSpawn(type, pos, rotation);
                    return true; // SUCCESS
                }
            }
        }
        
        return false; // FAILED (No space found after 50 tries)
    }

    private bool IsSpaceEmpty(Vector3 center, Quaternion rotation)
    {
        Collider[] hits = Physics.OverlapBox(center, safetyCheckSize, rotation, collisionLayerMask);
        return hits.Length == 0;
    }

    private void PerformSpawn(TreeType type, Vector3 pos, Quaternion rot)
    {
        GameObject prefab = (type == TreeType.Wood) ? woodTreePrefab : fruitTreePrefab;
        GameObject newObj = Instantiate(prefab, pos, rot);
        newObj.GetComponent<NetworkObject>().Spawn();

        if (type == TreeType.Wood) _currentWoodCount++;
        else _currentFruitCount++;
    }

    public void NotifyTreeDestroyed(TreeType type)
    {
        if (type == TreeType.Wood) _currentWoodCount--;
        else _currentFruitCount--;
    }
}