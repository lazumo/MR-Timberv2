using UnityEngine;
using Unity.Netcode;
using Meta.XR.MRUtilityKit;
using System.Collections.Generic;

public class HouseSpawnerNetworked : NetworkBehaviour
{
    public static HouseSpawnerNetworked Instance { get; private set; }

    public struct HouseData : INetworkSerializable, System.IEquatable<HouseData>
    {
        public int Id;
        public Vector3 Position;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Id);
            serializer.SerializeValue(ref Position);
        }

        public bool Equals(HouseData other) { return Id == other.Id && Position == other.Position; }
    }

    [Header("Settings")]
    public GameObject housePrefab;
    public int numberOfHouses = 5;

    [Header("Safety Area (1m x 1m)")]
    // 0.5f Half-Extents = 1.0m Total Size
    public Vector3 collisionCheckSize = new Vector3(0.5f, 0.5f, 0.5f);
    public LayerMask obstacleLayerMask; // Select Default, House, Tree

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

        // 1. Setup Filters (Floor + Walls + Ceiling)
        MRUK.SurfaceType allowedSurfaces = MRUK.SurfaceType.FACING_UP | MRUK.SurfaceType.VERTICAL | MRUK.SurfaceType.FACING_DOWN;
        MRUKAnchor.SceneLabels allowedLabels = MRUKAnchor.SceneLabels.FLOOR | MRUKAnchor.SceneLabels.WALL_FACE | MRUKAnchor.SceneLabels.CEILING;
        LabelFilter filter = new LabelFilter(allowedLabels);

        while (successfulSpawns < numberOfHouses && attempts < 100)
        {
            attempts++;

            if (room.GenerateRandomPositionOnSurface(allowedSurfaces, 0.1f, filter, out Vector3 pos, out Vector3 normal))
            {
                // --- FIXED ROTATION LOGIC ---
                // "House Top (Y) should face Out (Normal)"
                // This works for Floor (Up), Ceiling (Down), and Walls (Out).
                
                Quaternion rot = Quaternion.FromToRotation(Vector3.up, normal);

                // Add a random spin around the "pole" (the normal axis)
                // This ensures the house isn't always rotated the same way relative to its base
                rot *= Quaternion.Euler(0, Random.Range(0, 360), 0);

                // 3. Collision Check (1m x 1m)
                if (IsSpaceEmpty(pos, rot))
                {
                    GameObject houseObj = Instantiate(housePrefab, pos, rot);
                    houseObj.GetComponent<NetworkObject>().Spawn();

                    successfulSpawns++;
                    HouseData data = new HouseData { Id = successfulSpawns, Position = pos };
                    _spawnedHouseData.Add(data);
                }
            }
        }
        Debug.Log($"HouseSpawner: Generated {successfulSpawns} houses.");
    }

    private bool IsSpaceEmpty(Vector3 center, Quaternion rotation)
    {
        Collider[] hits = Physics.OverlapBox(center, collisionCheckSize, rotation, obstacleLayerMask);
        return hits.Length == 0;
    }

    public Vector3 GetPositionForHouse(int houseId)
    {
        foreach (var house in _spawnedHouseData)
        {
            if (house.Id == houseId) return house.Position;
        }
        return Vector3.zero;
    }
}