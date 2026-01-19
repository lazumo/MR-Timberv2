using UnityEngine;
using Unity.Netcode;
using Meta.XR.MRUtilityKit;
using System.Collections.Generic;

public class HouseSpawnerNetworked : NetworkBehaviour
{
    public static HouseSpawnerNetworked Instance { get; private set; }

    // --- 1. Define the Data Structure ---
    // This struct holds the ID and Position together.
    // INetworkSerializable allows Netcode to sync it automatically.
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
    public GameObject housePrefab; // Needs NetworkObject component
    public int numberOfHouses = 5;
    
    [Header("Placement Rules")]
    public LayerMask obstacleLayerMask;
    public Vector3 collisionCheckSize = new Vector3(0.1f, 0.1f, 0.1f);

    // --- 2. The Networked List ---
    // This list syncs the HouseData (ID + Position) to all clients.
    private NetworkList<HouseData> _spawnedHouseData;

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

        // Setup Filters (MRUK v81+)
        MRUK.SurfaceType allowedSurfaces = MRUK.SurfaceType.FACING_UP | MRUK.SurfaceType.VERTICAL;
        MRUKAnchor.SceneLabels allowedLabels = MRUKAnchor.SceneLabels.FLOOR | MRUKAnchor.SceneLabels.WALL_FACE | MRUKAnchor.SceneLabels.CEILING;
        LabelFilter filter = new LabelFilter(allowedLabels);

        while (successfulSpawns < numberOfHouses && attempts < 100)
        {
            attempts++;

            if (room.GenerateRandomPositionOnSurface(allowedSurfaces, 0.1f, filter, out Vector3 pos, out Vector3 normal))
            {
                // Simple rotation logic
                Quaternion rot = (Mathf.Abs(Vector3.Dot(normal, Vector3.up)) < 0.1f) 
                    ? Quaternion.LookRotation(normal, Vector3.up) 
                    : Quaternion.Euler(0, Random.Range(0, 360), 0);

                if (IsSpaceEmpty(pos, rot))
                {
                    // 1. Spawn the physical object
                    GameObject houseObj = Instantiate(housePrefab, pos, rot);
                    houseObj.GetComponent<NetworkObject>().Spawn();

                    // 2. Create the Data Entry (ID + Position)
                    successfulSpawns++; // ID starts at 1
                    HouseData data = new HouseData
                    {
                        Id = successfulSpawns,
                        Position = pos
                    };

                    // 3. Add to NetworkList (Syncs to everyone)
                    _spawnedHouseData.Add(data);
                }
            }
        }
    }

    private bool IsSpaceEmpty(Vector3 center, Quaternion rotation)
    {
        Collider[] hits = Physics.OverlapBox(center, collisionCheckSize, rotation, obstacleLayerMask);
        return hits.Length == 0;
    }

    // --- 3. PUBLIC API for Other Scripts ---
    
    /// <summary>
    /// Returns the position of a specific house ID (e.g., GetPositionForHouse(3)).
    /// Returns Vector3.zero if not found.
    /// </summary>
    public Vector3 GetPositionForHouse(int houseId)
    {
        foreach (var house in _spawnedHouseData)
        {
            if (house.Id == houseId)
                return house.Position;
        }
        Debug.LogWarning($"House ID {houseId} not found!");
        return Vector3.zero;
    }

    /// <summary>
    /// Returns all house data (IDs and Positions).
    /// </summary>
    public List<HouseData> GetAllHouseData()
    {
        List<HouseData> list = new List<HouseData>();
        foreach (var h in _spawnedHouseData) list.Add(h);
        return list;
    }
}