using UnityEngine;
using Unity.Netcode;
using Meta.XR.MRUtilityKit;
using System.Collections;
using System.Collections.Generic;

public class HouseSpawnerNetworked : NetworkBehaviour
{
    public static HouseSpawnerNetworked Instance { get; private set; }

    // --- 1. Define the Data Structure ---
    public struct HouseData : INetworkSerializable, System.IEquatable<HouseData>
    {
        public int Id;
        public Vector3 Position;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Id);
            serializer.SerializeValue(ref Position);
        }

        public bool Equals(HouseData other)
        {
            return Id == other.Id && Position == other.Position;
        }
    }

    [Header("Settings")]
    public GameObject housePrefab;
    public int numberOfHouses = 1;

    public int SpawnedHouseColorIndex { get; private set; } = -1;
    public bool HasSpawnedHouse => SpawnedHouseColorIndex >= 0;
    [SerializeField] private float minWallHeight = 1.0f;  // 離地 50 cm
    [SerializeField] private float maxWallHeight = 2.5f;  // 離地 180 cm
    [Header("Placement Rules")]
    public LayerMask obstacleLayerMask;
    // TUNED: Set to 0.5f. (0.5 + 0.5 = 1.0 meter total size).
    // 0.1f is often too small to stop overlaps.
    public Vector3 collisionCheckSize = new Vector3(0.5f, 0.5f, 0.5f);
    static readonly float[] NormalAxisRotations = { 0f, 90f, 180f, 270f };

    // --- 2. The Networked List ---
    private NetworkList<HouseData> _spawnedHouseData;
    private int _nextQueryID;
    private Dictionary<int, GameObject> _houseObjectMap = new();

    // ⭐ First-run layout, replayed on restart so houses reappear in the SAME spots/colours.
    private struct HouseLayout { public Vector3 pos; public Quaternion rot; public int color; }
    private readonly List<HouseLayout> _initialLayout = new();
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        _spawnedHouseData = new NetworkList<HouseData>(
            null, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server
        );
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            if (MRUK.Instance && MRUK.Instance.GetCurrentRoom() != null)
                SpawnHousesLogic();
            else if (MRUK.Instance)
                MRUK.Instance.RegisterSceneLoadedCallback(OnSceneLoaded);
        }
    }

    private void OnSceneLoaded()
    {
        if (IsServer) SpawnHousesLogic();
    }

    private void SpawnHousesLogic()
    {
        MRUKRoom room = MRUK.Instance.GetCurrentRoom();
        if (room == null) return;

        _spawnedHouseData.Clear();
        _initialLayout.Clear();   // fresh random run re-captures the layout
        int successfulSpawns = 0;
        int attempts = 0;
        _nextQueryID = 0;

        // 1. 設定允許生成的表面 (原本的邏輯)
        MRUK.SurfaceType allowedSurfaces = MRUK.SurfaceType.VERTICAL;
        MRUKAnchor.SceneLabels allowedLabels = MRUKAnchor.SceneLabels.FLOOR | MRUKAnchor.SceneLabels.WALL_FACE | MRUKAnchor.SceneLabels.CEILING;
        LabelFilter filter = new LabelFilter(allowedLabels);

        // 2. ⭐ 新增：找出所有我不想要生成的 "Other" Anchor
        // 這裡我們把所有標記為 OTHER 的物件找出來當作「禁區」
        List<MRUKAnchor> forbiddenAnchors = new List<MRUKAnchor>();
        foreach (var anchor in room.Anchors)
        {
            if (anchor.Label == MRUKAnchor.SceneLabels.OTHER)
            {
                forbiddenAnchors.Add(anchor);
            }
        }

        while (successfulSpawns < numberOfHouses && attempts < 1000)
        {
            attempts++;

            if (room.GenerateRandomPositionOnSurface(allowedSurfaces, 0.1f, filter, out Vector3 pos, out Vector3 normal))
            {
                if (pos.y < minWallHeight || pos.y > maxWallHeight)
                    continue;
                if (SpawnArea.Instance != null && !SpawnArea.Instance.IsInside(pos))
                    continue;
                // --- 以下是原本的旋轉與生成邏輯 (保持不變) ---
                Quaternion rot = Quaternion.FromToRotation(Vector3.up, normal);
                int rotIndex = Random.Range(0, NormalAxisRotations.Length);
                float angle = NormalAxisRotations[rotIndex];
                rot = Quaternion.AngleAxis(angle, normal) * rot;

                if (IsSpaceEmpty(pos, rot))
                {
                    int randomColor = Random.Range(0, ColorTable.Count);
                    SpawnHouseAt(pos, rot, randomColor, successfulSpawns);

                    // remember first-run layout for restart replay
                    _initialLayout.Add(new HouseLayout { pos = pos, rot = rot, color = randomColor });
                    successfulSpawns++;
                }
            }
        }
    }

    private void SpawnHouseAt(Vector3 pos, Quaternion rot, int color, int id)
    {
        GameObject houseObj = Instantiate(housePrefab, pos, rot);
        houseObj.GetComponent<NetworkObject>().Spawn();

        var houseSync = houseObj.GetComponent<ObjectNetworkSync>();
        if (houseSync != null)
            houseSync.InitializeColorIndex(color);

        if (id == 0)
            SpawnedHouseColorIndex = color;

        _houseObjectMap[id] = houseObj;
        _spawnedHouseData.Add(new HouseData { Id = id, Position = pos });
    }
    public bool TryGetHouseObject(int id, out GameObject houseObj)
    {
        return _houseObjectMap.TryGetValue(id, out houseObj);
    }

    // --- TUNED: COLUMN COLLISION CHECK ---
    private bool IsSpaceEmpty(Vector3 center, Quaternion rotation)
    {
        // 1. Create a "Column" shape instead of a small box.
        // We set Y to 10.0f (Total 20 meters tall).
        // This ensures a House on the Floor detects a Tree on the Ceiling above it.
        Vector3 columnSize = collisionCheckSize;
        columnSize.y = 10.0f;

        // 2. Use Quaternion.identity (No Rotation) for the check box.
        // Rotating a tall column sideways usually causes it to hit walls incorrectly.
        // We just want to know "Is this vertical space occupied?".
        Collider[] hits = Physics.OverlapBox(center, columnSize, Quaternion.identity, obstacleLayerMask);

        return hits.Length == 0;
    }

    // --- 3. PUBLIC API for Other Scripts ---
    public bool TryGetNextHouse(out HouseData data)
    {
        if (!IsServer)
        {
            data = default;
            return false;
        }
        if (_nextQueryID >= _spawnedHouseData.Count)
        {
            data = default;
            return false;
        }

        data = _spawnedHouseData[_nextQueryID];
        _nextQueryID++;
        return true;
    }

    public List<HouseData> GetAllHouseData()
    {
        List<HouseData> list = new List<HouseData>();
        foreach (var h in _spawnedHouseData) list.Add(h);
        return list;
    }

    // Firefighting: remove all houses and their bound color factories.
    public void DespawnAllHouses()
    {
        if (!IsServer) return;

        // Color factories are separate NetworkObjects bound to houses — despawn them first.
        foreach (var f in FindObjectsByType<ColorFactory>(FindObjectsSortMode.None))
        {
            var no = f.GetComponent<NetworkObject>();
            if (no != null && no.IsSpawned) no.Despawn(true);
        }

        foreach (var kv in _houseObjectMap)
        {
            var houseObj = kv.Value;
            if (houseObj == null) continue;
            var no = houseObj.GetComponent<NetworkObject>();
            if (no != null && no.IsSpawned) no.Despawn(true);
        }

        _houseObjectMap.Clear();
        _spawnedHouseData.Clear();
    }

    // Firefighting bridge: fade houses out (scale → 0) then despawn, plus their color factories.
    // Mirrors the existing factory scale-fade (ObjectNetworkSync.DespawnFactoryWithScale).
    public void FadeOutAllHouses(float duration)
    {
        if (!IsServer) return;

        foreach (var f in FindObjectsByType<ColorFactory>(FindObjectsSortMode.None))
        {
            var no = f.GetComponent<NetworkObject>();
            if (no != null && no.IsSpawned) StartCoroutine(FadeAndDespawn(no, duration));
        }

        foreach (var kv in _houseObjectMap)
        {
            var houseObj = kv.Value;
            if (houseObj == null) continue;
            var no = houseObj.GetComponent<NetworkObject>();
            if (no != null && no.IsSpawned) StartCoroutine(FadeAndDespawn(no, duration));
        }

        _houseObjectMap.Clear();
        _spawnedHouseData.Clear();
    }

    private IEnumerator FadeAndDespawn(NetworkObject no, float duration)
    {
        Transform t = no.transform;
        Vector3 start = t.localScale;
        float time = 0f;

        while (time < duration && no != null && t != null)
        {
            time += Time.deltaTime;
            t.localScale = Vector3.Lerp(start, Vector3.zero, duration <= 0f ? 1f : time / duration);
            yield return null;
        }

        if (no != null && no.IsSpawned) no.Despawn(true);
    }

    // Restart: clear existing houses and spawn fresh Unbuilt placeholders.
    public void RespawnHouses()
    {
        if (!IsServer) return;

        StopAllCoroutines();   // cancel any in-progress fade-outs
        DespawnAllHouses();

        // Catch houses whose fade was interrupted (already removed from the map).
        foreach (var h in FindObjectsByType<ObjectNetworkSync>(FindObjectsSortMode.None))
        {
            var no = h.GetComponent<NetworkObject>();
            if (no != null && no.IsSpawned) no.Despawn(true);
        }

        SpawnedHouseColorIndex = -1;

        // ⭐ Replay the first-run layout → same positions & colours every restart.
        if (_initialLayout.Count > 0)
        {
            _spawnedHouseData.Clear();
            _nextQueryID = 0;

            for (int i = 0; i < _initialLayout.Count; i++)
                SpawnHouseAt(_initialLayout[i].pos, _initialLayout[i].rot, _initialLayout[i].color, i);
            return;
        }

        if (MRUK.Instance != null && MRUK.Instance.GetCurrentRoom() != null)
            SpawnHousesLogic();
    }
}