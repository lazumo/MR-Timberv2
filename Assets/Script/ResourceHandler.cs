using UnityEngine;
using Unity.Netcode;

public class ResourceHandlerNetworked : NetworkBehaviour
{
    [Header("Settings")]
    public GameObject resourcePrefab;
    public float moveSpeed = 1.5f;

    private NetworkObject _currentResource;
    private Vector3 _targetPosition;
    private bool _isMoving = false;

    // =============================
    // Server: Spawn resource
    // =============================
    [ServerRpc(RequireOwnership = false)]
    public void SpawnResourceServerRpc()
    {
        SpawnResourceInternal();
    }

    private void SpawnResourceInternal()
    {
        if (!IsServer) return;

        // 1. Spawn resource
        GameObject obj = Instantiate(resourcePrefab, transform.position, Quaternion.identity);
        var netObj = obj.GetComponent<NetworkObject>();
        netObj.Spawn();

        _currentResource = netObj;

        // 2. Query next house
        if (HouseSpawnerNetworked.Instance.TryGetNextHouse(out var house))
        {
            _targetPosition = house.Position;
            _isMoving = true;
            Debug.Log($"[ResourceHandler][Server] Assign resource {netObj.NetworkObjectId} → House ID {house.Id} at {house.Position}");
            // Sync target to clients
            SetTargetClientRpc(_targetPosition, _currentResource.NetworkObjectId);
        }
        else
        {
            Debug.LogWarning("No more houses available.");
        }
    }

    // =============================
    // Client sync
    // =============================
    [ClientRpc]
    private void SetTargetClientRpc(Vector3 target, ulong resourceNetId)
    {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(resourceNetId, out var netObj))
        {
            _currentResource = netObj;
            _targetPosition = target;
            _isMoving = true;
        }
    }

    // =============================
    // Movement (runs on all)
    // =============================
    private void Update()
    {
        if (!_isMoving || _currentResource == null) return;

        Transform t = _currentResource.transform;

        t.position = Vector3.MoveTowards(
            t.position,
            _targetPosition,
            moveSpeed * Time.deltaTime
        );

        if (Vector3.Distance(t.position, _targetPosition) < 0.02f)
        {
            t.position = _targetPosition;
            _isMoving = false;
        }
    }
}
