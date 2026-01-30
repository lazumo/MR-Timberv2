using UnityEngine;
using Unity.Netcode;
using Meta.XR.MRUtilityKit;
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
    public int numberOfHouses = 5;

    [Header("Placement Rules")]
    public LayerMask obstacleLayerMask;

    // TUNED: Set to 0.5f. (0.5 + 0.5 = 1.0 meter total size). 
    // 0.1f is often too small to stop overlaps.
    public Vector3 collisionCheckSize = new Vector3(0.5f, 0.5f, 0.5f);

    // --- 2. The Networked List ---
    private NetworkList<HouseData> _spawnedHouseData;
    private int _nextQueryID;
    private Dictionary<int, GameObject> _houseObjectMap = new();
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
        int successfulSpawns = 0;
        int attempts = 0;
        _nextQueryID = 0;

        // Setup Filters (MRUK v81+)
        MRUK.SurfaceType allowedSurfaces = MRUK.SurfaceType.FACING_UP | MRUK.SurfaceType.VERTICAL;
        MRUKAnchor.SceneLabels allowedLabels = MRUKAnchor.SceneLabels.FLOOR | MRUKAnchor.SceneLabels.WALL_FACE | MRUKAnchor.SceneLabels.CEILING;
        LabelFilter filter = new LabelFilter(allowedLabels);

        while (successfulSpawns < numberOfHouses && attempts < 100)
        {
            attempts++;

            if (room.GenerateRandomPositionOnSurface(allowedSurfaces, 0.1f, filter, out Vector3 pos, out Vector3 normal))
            {
                // --- ROTATION LOGIC ---
                // Goal: Top of house (Y-Axis) faces inward (Normal).
                // Floor: Top faces Up. Wall: Top faces In.
                Quaternion rot = Quaternion.FromToRotation(Vector3.up, normal);

                // Add random spin around the "Normal" axis (the pole sticking out of the wall)
                rot *= Quaternion.Euler(0, Random.Range(0, 360), 0);

                if (IsSpaceEmpty(pos, rot))
                {
                    // 1. Spawn the physical object
                    GameObject houseObj = Instantiate(housePrefab, pos, rot);
                    var houseSync = houseObj.GetComponent<ObjectNetworkSync>();
                    if (houseSync != null)
                    {
                        int randomColor = Random.Range(0, 3);
                        houseSync.InitializeColorIndex(randomColor);
                    }
                    houseObj.GetComponent<NetworkObject>().Spawn();

                    // 2. Create the Data Entry (ID + Position)
                    HouseData data = new HouseData
                    {
                        Id = successfulSpawns,
                        Position = pos
                    };
                    _houseObjectMap[data.Id] = houseObj;
                    successfulSpawns++;

                    // 3. Add to NetworkList (Syncs to everyone)
                    _spawnedHouseData.Add(data);
                }
            }
        }
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
}