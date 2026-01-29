using UnityEngine;
using Unity.Netcode;
using Meta.XR.MRUtilityKit;
using System.Collections;
using System.Collections.Generic;

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
    // X/Z = 0.5f (1m width). Y will be overwritten by column check.
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

    private IEnumerator ManageWoodLifecycle()
    {
        yield return new WaitForSeconds(woodStartDelay);
        while (IsServer)
        {
            if (_currentWoodCount < targetWoodTrees)
            {
                bool success = SpawnTree(TreeType.Wood);
                if (success) yield return new WaitForSeconds(woodSpawnInterval);
                else
                {
                    Debug.LogWarning("Failed to find space for Wood Tree. Retrying in 1s...");
                    yield return new WaitForSeconds(1.0f);
                }
            }
            else yield return new WaitForSeconds(1.0f);
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
    public bool SpawnTree(TreeType type)
    {
        MRUKRoom room = MRUK.Instance.GetCurrentRoom();
        if (room == null) return false;

        MRUK.SurfaceType surfaceType = (type == TreeType.Wood) ? MRUK.SurfaceType.FACING_UP : MRUK.SurfaceType.FACING_DOWN;
        MRUKAnchor.SceneLabels labels = (type == TreeType.Wood) ? MRUKAnchor.SceneLabels.FLOOR : MRUKAnchor.SceneLabels.CEILING;
        LabelFilter filter = new LabelFilter(labels);

        int attempts = 0;
        while (attempts < 50)
        {
            attempts++;
            if (room.GenerateRandomPositionOnSurface(surfaceType, 0.1f, filter, out Vector3 pos, out Vector3 normal))
            {
                Quaternion rotation = Quaternion.FromToRotation(Vector3.up, normal);
                rotation *= Quaternion.Euler(0, Random.Range(0, 360), 0);

                if (IsSpaceEmpty(pos, rotation))
                {
                    PerformSpawn(type, pos, rotation);
                    return true;
                }
            }
        }
        return false;
    }

    // --- COLUMN CHECK LOGIC ---
    private bool IsSpaceEmpty(Vector3 center, Quaternion rotation)
    {
        // 1. Physics Column Check (Tall Box)
        Vector3 columnSize = safetyCheckSize;
        columnSize.y = 10.0f; // 20m Tall box to hit Floor AND Ceiling
        Collider[] hits = Physics.OverlapBox(center, columnSize, Quaternion.identity, collisionLayerMask);
        if (hits.Length > 0)
        {
            Debug.Log($"[TreeSpawn] Blocked by {hits.Length} colliders:");
            foreach (var h in hits)
                Debug.Log($" - {h.name} | layer={LayerMask.LayerToName(h.gameObject.layer)}");

            return false;
        }
        //if (hits.Length > 0) return false;

        // 2. House Logic Column Check (Distance ignoring Y)
        if (HouseSpawnerNetworked.Instance != null)
        {
            var houses = HouseSpawnerNetworked.Instance.GetAllHouseData();
            foreach (var house in houses)
            {
                Vector3 treePosFlat = new Vector3(center.x, 0, center.z);
                Vector3 housePosFlat = new Vector3(house.Position.x, 0, house.Position.z);

                if (Vector3.Distance(treePosFlat, housePosFlat) < 1.0f) // 1m Radius
                {
                    return false;
                }
            }
        }

        return true;
    }

    private void PerformSpawn(TreeType type, Vector3 pos, Quaternion rot)
    {
        GameObject prefab = (type == TreeType.Wood) ? woodTreePrefab : fruitTreePrefab;
        GameObject newObj = Instantiate(prefab, pos, rot);
        if (type == TreeType.Fruit)
        {
            FruitTree tree = newObj.GetComponent<FruitTree>();
            if (tree != null)
            {
                tree.selectedColorIndex = Random.Range(0, tree.treeFruitColors.Length);
            }
        }
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