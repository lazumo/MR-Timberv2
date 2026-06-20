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
    public int targetWoodTrees = 1;
    public float woodStartDelay = 0.5f;
    public float woodSpawnInterval = 5.0f;

    public int targetFruitTrees = 1;
    public float fruitStartDelay = 1.0f;
    public float fruitSpawnInterval = 8.0f;
    
    [Header("Safety Area")]
    // X/Z = 0.5f (1m width). Y will be overwritten by column check.
    public Vector3 safetyCheckSize = new Vector3(0.5f, 0.5f, 0.5f);
    public LayerMask collisionLayerMask;
    public float posOffset = 1.5f;

    [Header("Phase Control")]
    [Tooltip("Legacy auto-start on spawn. Leave OFF — LoggingPhase / CatchingPhase now drive spawning.")]
    [SerializeField] private bool autoStartLegacy = false;

    private int _currentWoodCount = 0;
    private int _currentFruitCount = 0;
    private int _nextFruitColorIndex = 0;

    // ⭐ Phase-driven wood logging (Layer 2: LoggingPhase)
    private bool _woodLogging = false;
    private readonly List<NetworkObject> _woodTrees = new List<NetworkObject>();

    // ⭐ Phase-driven fruit spawning (Layer 2: CatchingPhase)
    private bool _fruitSpawning = false;
    private readonly List<NetworkObject> _fruitTrees = new List<NetworkObject>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer && autoStartLegacy)
        {
            if (MRUK.Instance && MRUK.Instance.GetCurrentRoom() != null) StartSpawningRoutines();
            else if (MRUK.Instance) MRUK.Instance.RegisterSceneLoadedCallback(StartSpawningRoutines);
        }
    }

    // =====================================================
    // Phase control — called by LoggingPhase (server only)
    // =====================================================

    public void BeginWoodLogging()
    {
        if (!IsServer) return;
        if (_woodLogging) return;
        _woodLogging = true;

        if (MRUK.Instance && MRUK.Instance.GetCurrentRoom() != null)
            StartCoroutine(EnsureInitialWoodTree());
        else if (MRUK.Instance)
            MRUK.Instance.RegisterSceneLoadedCallback(() => { if (_woodLogging) StartCoroutine(EnsureInitialWoodTree()); });
        else
            Debug.LogError("[TreeSpawner] BeginWoodLogging — MRUK.Instance is null; no tree will spawn.");
    }

    public void StopWoodLogging(bool despawnExisting)
    {
        if (!IsServer) return;
        _woodLogging = false;

        if (despawnExisting)
        {
            foreach (var t in _woodTrees)
                if (t != null && t.IsSpawned) t.Despawn(true);
        }

        _woodTrees.Clear();
        _currentWoodCount = 0;
    }

    // Keep at least targetWoodTrees standing at phase start. Respawn-on-chop is
    // driven by SliceObject (calls SpawnTree(Wood)), gated by _woodLogging.
    private IEnumerator EnsureInitialWoodTree()
    {
        int tries = 0;
        while (_woodLogging && _currentWoodCount < targetWoodTrees && tries < 50)
        {
            tries++;
            if (SpawnTree(TreeType.Wood)) yield break;
            yield return new WaitForSeconds(1.0f);
        }

        if (_woodLogging && _currentWoodCount < targetWoodTrees)
            Debug.LogWarning("[TreeSpawner] Could not find space for the initial wood tree.");
    }

    // =====================================================
    // Phase control — called by CatchingPhase (server only)
    // =====================================================

    // Spawn `treeCount` fruit trees on the ceiling, each a different colour.
    public void BeginFruitSpawning(int treeCount)
    {
        if (!IsServer) return;
        if (_fruitSpawning) return;
        _fruitSpawning = true;

        if (MRUK.Instance && MRUK.Instance.GetCurrentRoom() != null)
            StartCoroutine(SpawnFruitTreesRoutine(treeCount));
        else if (MRUK.Instance)
            MRUK.Instance.RegisterSceneLoadedCallback(() => { if (_fruitSpawning) StartCoroutine(SpawnFruitTreesRoutine(treeCount)); });
        else
            Debug.LogError("[TreeSpawner] BeginFruitSpawning — MRUK.Instance is null; no fruit tree will spawn.");
    }

    // Stops spawning new fruit trees. Existing trees/fruits stay (needed by the Juicing phase)
    // unless despawnExisting is requested (Firefighting clears everything).
    public void StopFruitSpawning(bool despawnExisting)
    {
        if (!IsServer) return;
        _fruitSpawning = false;

        if (despawnExisting)
        {
            foreach (var t in _fruitTrees)
                if (t != null && t.IsSpawned) t.Despawn(true);

            _fruitTrees.Clear();
            _currentFruitCount = 0;
        }
    }

    // Firefighting: remove all loose fruits (and optionally the fruit trees), stop dropping.
    public void ClearAllFruits(bool keepTrees)
    {
        if (!IsServer) return;
        _fruitSpawning = false;

        // Stop each tree's spawner and despawn the fruits it dropped.
        foreach (var t in _fruitTrees)
        {
            if (t == null) continue;
            var fsc = t.GetComponentInChildren<FruitSpawnController>();
            if (fsc != null) fsc.StopAndDespawnFruits();
        }

        // Catch-all: despawn any remaining fruit (e.g. fruits sitting inside factories).
        foreach (var fd in FindObjectsByType<FruitData>(FindObjectsSortMode.None))
        {
            var no = fd.GetComponent<NetworkObject>();
            if (no != null && no.IsSpawned) no.Despawn(true);
        }

        if (!keepTrees)
        {
            foreach (var t in _fruitTrees)
                if (t != null && t.IsSpawned) t.Despawn(true);

            _fruitTrees.Clear();
            _currentFruitCount = 0;
        }
    }

    private IEnumerator SpawnFruitTreesRoutine(int treeCount)
    {
        int spawned = 0;
        int guard = 0;
        int maxGuard = Mathf.Max(1, treeCount) * 50;

        while (_fruitSpawning && spawned < treeCount && guard < maxGuard)
        {
            guard++;
            int color = spawned % ColorTable.Count; // 不同顏色的果樹
            if (SpawnTree(TreeType.Fruit, color))
            {
                spawned++;
                yield return new WaitForSeconds(0.3f);
            }
            else
            {
                yield return new WaitForSeconds(0.5f);
            }
        }

        if (_fruitSpawning && spawned < treeCount)
            Debug.LogWarning($"[TreeSpawner] Only spawned {spawned}/{treeCount} fruit trees (no space).");
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

        while (IsServer && (HouseSpawnerNetworked.Instance == null || !HouseSpawnerNetworked.Instance.HasSpawnedHouse))
        {
            yield return new WaitForSeconds(0.2f);
        }

        while (IsServer)
        {
            // 進入滅火 stage 後不再生成新的果樹
            if (SceneController.Instance != null && SceneController.Instance.GetCurrentStage() > 1)
            {
                yield return new WaitForSeconds(1.0f);
                continue;
            }

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
    public bool SpawnTree(TreeType type, int forcedFruitColorIndex = -1)
    {
        // ⭐ Once a phase has ended, refuse respawns (e.g. SliceObject / FruitTree delayed respawn).
        if (type == TreeType.Wood && !_woodLogging) return false;
        if (type == TreeType.Fruit && !_fruitSpawning) return false;

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
                    PerformSpawn(type, pos, rotation, forcedFruitColorIndex);
                    return true;
                }
            }
        }
        return false;
    }

    // --- COLUMN CHECK LOGIC ---
    private bool IsSpaceEmpty(Vector3 center, Quaternion rotation)
    {
        // 0. SpawnArea radius check (XZ plane, centered on player initial position)
        if (SpawnArea.Instance != null && !SpawnArea.Instance.IsInside(center))
            return false;

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

    private void PerformSpawn(TreeType type, Vector3 pos, Quaternion rot, int forcedFruitColorIndex = -1)
    {
        GameObject prefab = (type == TreeType.Wood) ? woodTreePrefab : fruitTreePrefab;
        if (type == TreeType.Fruit)
        {
            pos.y += posOffset;
        }
        else
        {
            pos.y -= posOffset * 0.5f;
        }
        GameObject newObj = Instantiate(prefab, pos, rot);
        if (type == TreeType.Fruit)
        {
            FruitTree tree = newObj.GetComponent<FruitTree>();
            if (tree != null)
            {
                if (forcedFruitColorIndex >= 0)
                {
                    // CatchingPhase wants a specific colour per tree (3 different colours).
                    tree.selectedColorIndex = forcedFruitColorIndex % ColorTable.Count;
                }
                else if (HouseSpawnerNetworked.Instance != null && HouseSpawnerNetworked.Instance.HasSpawnedHouse)
                {
                    // 顏色與已生成的房子對齊；找不到時退回循環色
                    tree.selectedColorIndex = HouseSpawnerNetworked.Instance.SpawnedHouseColorIndex;
                }
                else
                {
                    tree.selectedColorIndex = _nextFruitColorIndex;
                    _nextFruitColorIndex = (_nextFruitColorIndex + 1) % ColorTable.Count;
                }
            }
        }
        NetworkObject netObj = newObj.GetComponent<NetworkObject>();
        netObj.Spawn();

        if (type == TreeType.Wood)
        {
            _currentWoodCount++;
            _woodTrees.Add(netObj); // track so LoggingPhase.EndPhase can clear standing trees
        }
        else
        {
            _currentFruitCount++;
            _fruitTrees.Add(netObj); // track so phases can clear fruit trees
        }
    }

    public void NotifyTreeDestroyed(TreeType type)
    {
        if (type == TreeType.Wood) _currentWoodCount--;
        else _currentFruitCount--;
    }
}