using UnityEngine;
using Unity.Netcode;
using Meta.XR.MRUtilityKit;
using System.Collections;
using System.Collections.Generic;

public class FireSpawnerSpreadNetworked : NetworkBehaviour
{
    [Header("Spawn Mode")]
    public FireSpawnMode spawnMode = FireSpawnMode.Auto;

    public enum FireSpawnMode
    {
        Auto,
        ForceSingle,
        ForceServer
    }

    [Header("Prefab (must have NetworkObject)")]
    public GameObject firePrefab;

    [Header("Spawn Settings")]
    public int maxFireCount = 100;
    public float startDelay = 1.0f;
    public float spreadInterval = 0.1f;        // 每個火焰的擴散間隔
    public float spreadDistance = 0.5f;
    public int spreadDirections = 8;

    [Header("Burn Settings")]
    public float burnTimeToDestroy = 3.0f; // 🔥 燒多久才毀
    // 記錄每棟建築燒了多久
    private Dictionary<GameObject, float> _burnTimers = new Dictionary<GameObject, float>();


    [Header("Surface Settings")]
    public float offsetFromSurface = 0.03f;
    public float maxRayDistance = 3.0f;        // 擴散時的最大檢測距離

    [Header("Vertical Spread (爬牆)")]
    public bool enableWallClimb = true;        // 啟用爬牆
    public float verticalSpreadChance = 0.6f;  // 向上擴散機率
    public float verticalSpreadDistance = 0.4f;// 垂直擴散距離
    public float horizontalSpreadChance = 0.8f;// 水平擴散機率

    [Header("Collision Check")]
    public float collisionCheckRadius = 0.2f;
    public LayerMask obstacleLayerMask;

    [Header("Burnable Buildings")]
    public string buildingTag = "Building";
    public GameObject burntBuildingPrefab;

    [Header("Initial Spawn")]
    public Transform initialSpawnPoint;

    [Header("Debug")]
    public bool enableDebug = true;

    private int _currentFireCount = 0;
    private bool _started = false;
    private HashSet<Vector3> _occupiedPositions = new HashSet<Vector3>();
    private List<Coroutine> _activeSpreadCoroutines = new List<Coroutine>();

    private void Start()
    {
        if (!NetworkManager.Singleton || !NetworkManager.Singleton.IsListening)
        {
            if (CanSpawnFire())
            {
                if (enableDebug)
                    Debug.Log("[FireSpawner] 無 Network，單機啟動生火");
                StartSpawningRoutine();
            }
        }
    }

    private bool CanSpawnFire()
    {
        if (spawnMode == FireSpawnMode.ForceSingle)
            return true;

        if (spawnMode == FireSpawnMode.ForceServer)
            return IsServer;

        if (Application.isEditor)
            return true;

        return IsServer;
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        StartCoroutine(TestMode());
    }

    private IEnumerator TestMode()
    {
        Debug.Log("[FireSpawner] 測試模式啟動");
        
        while (MRUK.Instance == null || MRUK.Instance.GetCurrentRoom() == null)
        {
            yield return new WaitForSeconds(0.1f);
        }
        
        Debug.Log("[FireSpawner] MRUK 已準備好");
        yield return new WaitForSeconds(2f);
        
        StartSpawningRoutine();
    }

    private IEnumerator WaitMrukThenHook()
    {
        while (MRUK.Instance == null) yield return null;
        
        if (MRUK.Instance.GetCurrentRoom() != null) 
            HookStageListener();
        else 
            MRUK.Instance.RegisterSceneLoadedCallback(HookStageListener);
    }

    private void HookStageListener()
    {
        if (_started) return;

        if (SceneController.Instance == null)
        {
            if (enableDebug)
                Debug.LogWarning("[FireSpawner] SceneController 尚未初始化,等待中...");
            
            StartCoroutine(WaitForSceneController());
            return;
        }

        SceneController.Instance.CurrentLevel.OnValueChanged += OnStageChanged;

        if (SceneController.Instance.GetCurrentStage() == 2)
            StartSpawningRoutine();
    }

    private IEnumerator WaitForSceneController()
    {
        float timeout = 10f;
        float elapsed = 0f;

        while (SceneController.Instance == null && elapsed < timeout)
        {
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }

        if (SceneController.Instance == null)
        {
            Debug.LogError("[FireSpawner] SceneController 初始化超時!");
            yield break;
        }

        if (enableDebug)
            Debug.Log("[FireSpawner] SceneController 已初始化");
        
        HookStageListener();
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        
        if (SceneController.Instance != null)
            SceneController.Instance.CurrentLevel.OnValueChanged -= OnStageChanged;
    }

    private void OnStageChanged(int prev, int cur)
    {
        if (!CanSpawnFire()) return;
        if (cur == 2 && !_started) StartSpawningRoutine();
    }

    private void StartSpawningRoutine()
    {
        _started = true;
        StartCoroutine(SpreadFireFromOrigin());
    }

    private IEnumerator SpreadFireFromOrigin()
    {
        yield return new WaitForSeconds(startDelay);

        Vector3 originPos = GetInitialSpawnPosition();
        if (originPos == Vector3.zero)
        {
            Debug.LogError("[FireSpawner] 無法找到初始生成點!");
            yield break;
        }

        // 🔥 生成第一個火焰並開始同步擴散
        if (SpawnFireAtPosition(originPos, out GameObject firstFire))
        {
            if (enableDebug)
                Debug.Log($"[FireSpawner] 初始火焰生成於: {originPos}");
            
            // 🔥 每個火焰生成後,立即開始它自己的擴散協程
            Coroutine spreadCoroutine = StartCoroutine(SpreadFromPosition(originPos));
            _activeSpreadCoroutines.Add(spreadCoroutine);
        }

        if (enableDebug)
            Debug.Log($"[FireSpawner] 火焰擴散系統啟動");
    }

    /// <summary>
    /// 🔥 從指定位置開始擴散 (倍數成長模式: 1→2→4→8)
    /// </summary>
    private IEnumerator SpreadFromPosition(Vector3 sourcePos)
    {
        yield return new WaitForSeconds(startDelay);

        // 🔥 地板一顆
        if (TryGetRandomFloorPosition(out Vector3 floorPos))
        {
            SpawnFireAtPosition(floorPos, out GameObject floorFire);
            StartCoroutine(SpreadFromPosition(floorPos));
        }

        // 🔥 牆壁一顆
        if (TryGetRandomWallPosition(out Vector3 wallPos))
        {
            SpawnFireAtPosition(wallPos, out GameObject wallFire);
            StartCoroutine(SpreadFromPosition(wallPos));
        }

        if (enableDebug)
            Debug.Log("[FireSpawner] 地板 + 牆壁 初始火焰已生成");


        // 🔥 每個火焰生成 1-2 個新火焰
        int targetNewFires = Random.Range(1, 3);
        int actualSpawned = 0;

        // 隨機化方向順序
        List<int> shuffledDirections = new List<int>();
        for (int i = 0; i < spreadDirections; i++)
            shuffledDirections.Add(i);
        
        for (int i = shuffledDirections.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (shuffledDirections[i], shuffledDirections[j]) = (shuffledDirections[j], shuffledDirections[i]);
        }

        // 🔥 優先嘗試水平擴散
        foreach (int dirIndex in shuffledDirections)
        {
            if (_currentFireCount >= maxFireCount) break;
            if (actualSpawned >= targetNewFires) break;

            if (Random.value < horizontalSpreadChance)
            {
                float angle = (360f / spreadDirections) * dirIndex;
                Vector3 direction = Quaternion.Euler(0, angle, 0) * Vector3.forward;
                
                if (TrySpreadInDirection(sourcePos, direction, false))
                {
                    actualSpawned++;
                }
            }
        }

        // 🔥 垂直擴散 (如果水平沒生成足夠,給垂直機會)
        if (enableWallClimb && actualSpawned < targetNewFires && Random.value < verticalSpreadChance)
        {
            if (TryClimbWall(sourcePos))
            {
                actualSpawned++;
            }
        }

        if (enableDebug && actualSpawned > 0)
        {
            Debug.Log($"[FireSpawner] 🔥 火焰在 {sourcePos} 分裂出 {actualSpawned} 個新火焰 (總數: {_currentFireCount})");
        }
    }

    /// <summary>
    /// 🔥 嘗試往特定方向擴散 (水平方向)
    /// </summary>
    private bool TrySpreadInDirection(Vector3 from, Vector3 direction, bool isVertical)
    {
        Vector3 targetPos = from + direction * spreadDistance;

        // 🔥 修正:先檢查目標位置是否已佔用
        Vector3 gridPos = SnapToGrid(targetPos, 0.3f);
        if (_occupiedPositions.Contains(gridPos))
            return false;

        Vector3 probePos = from + direction * spreadDistance;

        // 從目標位置往下找地板 / 表面
        if (Physics.Raycast(
                probePos + Vector3.up * 0.5f,
                Vector3.down,
                out RaycastHit hit,
                maxRayDistance))
        {
        Vector3 spawnPos = hit.point + hit.normal * offsetFromSurface;

        // === 以下原本的檢查邏輯全部照舊 ===

        Vector3 spawnGridPos = SnapToGrid(spawnPos, 0.3f);
        if (_occupiedPositions.Contains(spawnGridPos))
            return false;

        Collider[] obstacles = Physics.OverlapSphere(
            spawnPos,
            collisionCheckRadius,
            obstacleLayerMask);

        foreach (Collider obstacle in obstacles)
        {
            if (obstacle.CompareTag(buildingTag))
            {
                ApplyBurn(obstacle.gameObject, spreadInterval);
                return false; // 火先不生成，持續燒
            }
        }
            

        if (obstacles.Length > 0)
            return false;

        if (SpawnFireAtPosition(spawnPos, out GameObject newFire))
        {
            _occupiedPositions.Add(gridPos);
            Coroutine spreadCoroutine = StartCoroutine(SpreadFromPosition(spawnPos));
            _activeSpreadCoroutines.Add(spreadCoroutine);

            if (enableDebug)
                Debug.Log($"[FireSpawner] ✅ 水平擴散成功: {from} → {spawnPos}");

            return true;
        }
    }
    else
    {
        if (enableDebug)
            Debug.Log("[FireSpawner] ❌ 水平擴散：往下找不到表面");
    }

    return false;
    }

    /// <summary>
    /// 🔥 嘗試爬牆 (垂直擴散)
    /// </summary>
    private bool TryClimbWall(Vector3 from)
    {
        // 🔥 方法 1: 直接往上生成 (適合平坦地面旁的牆)
        Vector3 upTarget = from + Vector3.up * verticalSpreadDistance;
        Vector3 gridPos = SnapToGrid(upTarget, 0.6f);
        
        if (_occupiedPositions.Contains(gridPos))
            return false;

        // 🔥 從目標位置往外找最近的牆面
        // 嘗試 4 個水平方向找牆
        Vector3[] wallCheckDirections = {
            Vector3.forward,
            Vector3.back,
            Vector3.right,
            Vector3.left
        };

        foreach (Vector3 dir in wallCheckDirections)
        {
            Vector3 checkOrigin = upTarget;
            
            // 往該方向 Raycast 找牆
            if (Physics.Raycast(checkOrigin, dir, out RaycastHit wallHit, 0.5f))
            {
                MRUKAnchor anchor = wallHit.collider.GetComponent<MRUKAnchor>();
                
                // 確認是牆面
                if (anchor != null && anchor.Label.HasFlag(MRUKAnchor.SceneLabels.WALL_FACE))
                {
                    Vector3 spawnPos = wallHit.point + wallHit.normal * offsetFromSurface;
                    
                    // 檢查障礙物
                    Collider[] obstacles = Physics.OverlapSphere(spawnPos, collisionCheckRadius, obstacleLayerMask);
                    if (obstacles.Length > 0)
                        continue;

                    // 生成火焰
                    if (SpawnFireAtPosition(spawnPos, out GameObject newFire))
                    {
                        _occupiedPositions.Add(gridPos);
                        Coroutine spreadCoroutine = StartCoroutine(SpreadFromPosition(spawnPos));
                        _activeSpreadCoroutines.Add(spreadCoroutine);
                        
                        if (enableDebug)
                            Debug.Log($"[FireSpawner] 🔥⬆️ 爬牆成功: {from.y:F2} → {spawnPos.y:F2} (高度差: {(spawnPos.y - from.y):F2}m)");
                        
                        return true;
                    }
                }
            }
        }

        // 🔥 方法 2: 如果附近沒牆,直接在空中生成 (模擬火焰往上竄)
        if (Physics.Raycast(upTarget, Vector3.down, out RaycastHit groundCheck, 0.3f))
        {
            // 正上方有東西,不生成
            return false;
        }

        // 空中生成 (用於火焰特效)
        if (SpawnFireAtPosition(upTarget, out GameObject airFire))
        {
            _occupiedPositions.Add(gridPos);
            Coroutine spreadCoroutine = StartCoroutine(SpreadFromPosition(upTarget));
            _activeSpreadCoroutines.Add(spreadCoroutine);
            
            if (enableDebug)
                Debug.Log($"[FireSpawner] 🔥⬆️ 火焰向上竄升: {from} → {upTarget}");
            
            return true;
        }

        return false;
    }

    /// <summary>
    /// 取得初始生成點 (房間地板中心)
    /// </summary>
    private Vector3 GetInitialSpawnPosition()
    {
        if (initialSpawnPoint != null)
        {
            Vector3 pos = initialSpawnPoint.position;
            if (FindGroundPosition(pos, out Vector3 groundPos))
            {
                if (enableDebug)
                    Debug.Log($"[FireSpawner] 使用手動設定的起點: {groundPos}");
                return groundPos;
            }
        }

        MRUKRoom room = MRUK.Instance?.GetCurrentRoom();
        if (room != null)
        {
            Bounds roomBounds = room.GetRoomBounds();
            Vector3 roomCenter = roomBounds.center;

            if (enableDebug)
                Debug.Log($"[FireSpawner] 房間邊界: {roomBounds}, 中心: {roomCenter}");

            Vector3 rayStart = new Vector3(roomCenter.x, roomBounds.max.y, roomCenter.z);
            
            if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 100f))
            {
                MRUKAnchor anchor = hit.collider.GetComponent<MRUKAnchor>();
                
                if (anchor != null && anchor.Label.HasFlag(MRUKAnchor.SceneLabels.FLOOR))
                {
                    Vector3 groundPos = hit.point + hit.normal * offsetFromSurface;
                    if (enableDebug)
                        Debug.Log($"[FireSpawner] 房間中心地板位置: {groundPos}");
                    return groundPos;
                }
                else if (anchor == null)
                {
                    Vector3 groundPos = hit.point + Vector3.up * offsetFromSurface;
                    if (enableDebug)
                        Debug.Log($"[FireSpawner] 找到地板 (無 MRUK): {groundPos}");
                    return groundPos;
                }
            }

            if (room.FloorAnchor != null)
            {
                Vector3 floorPos = room.FloorAnchor.transform.position;
                if (enableDebug)
                    Debug.Log($"[FireSpawner] 使用 FloorAnchor 位置: {floorPos}");
                return floorPos + Vector3.up * offsetFromSurface;
            }
        }

        Debug.LogError("[FireSpawner] 無法找到初始生成點!");
        return Vector3.zero;
    }



    private bool TryGetRandomFloorPosition(out Vector3 pos)
    {
        pos = Vector3.zero;

        MRUKRoom room = MRUK.Instance?.GetCurrentRoom();
        if (room == null) return false;

        // 隨機房間內一點
        Bounds b = room.GetRoomBounds();
        Vector3 randomXZ = new Vector3(
            Random.Range(b.min.x, b.max.x),
            b.max.y + 0.5f,
            Random.Range(b.min.z, b.max.z)
        );

        // 往下打找地板
        if (Physics.Raycast(randomXZ, Vector3.down, out RaycastHit hit, b.size.y + 1f))
        {
            MRUKAnchor anchor = hit.collider.GetComponent<MRUKAnchor>();
            if (anchor != null && anchor.Label.HasFlag(MRUKAnchor.SceneLabels.FLOOR))
            {
                pos = hit.point + hit.normal * offsetFromSurface;
                return true;
            }
        }

        return false;
    }


    private bool TryGetRandomWallPosition(out Vector3 pos)
    {
        pos = Vector3.zero;

        MRUKRoom room = MRUK.Instance?.GetCurrentRoom();
        if (room == null) return false;

        Bounds b = room.GetRoomBounds();

        // 隨機高度（牆的高度）
        float y = Random.Range(b.min.y + 0.3f, b.max.y - 0.3f);

        // 從房間中心往四周隨機射
        Vector3 center = b.center;
        Vector3 dir = Random.onUnitSphere;
        dir.y = 0;
        dir.Normalize();

        Vector3 rayStart = new Vector3(center.x, y, center.z);

        if (Physics.Raycast(rayStart, dir, out RaycastHit hit, b.extents.magnitude))
        {
            MRUKAnchor anchor = hit.collider.GetComponent<MRUKAnchor>();
            if (anchor != null && anchor.Label.HasFlag(MRUKAnchor.SceneLabels.WALL_FACE))
            {
                pos = hit.point + hit.normal * offsetFromSurface;
                return true;
            }
        }

        return false;
    }




    /// <summary>
    /// 🔥 生成火焰 (返回生成的物件)
    /// </summary>
    private bool SpawnFireAtPosition(Vector3 position, out GameObject fireObject)
    {
        fireObject = null;

        if (_currentFireCount >= maxFireCount) 
            return false;

        GameObject obj = Instantiate(firePrefab, position, Quaternion.identity);
        
        NetworkObject netObj = obj.GetComponent<NetworkObject>();
        if (netObj != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            netObj.Spawn();
        }

        _currentFireCount++;
        fireObject = obj;

        if (enableDebug)
            Debug.Log($"[FireSpawner] 火焰生成於: {position} ({_currentFireCount}/{maxFireCount})");
        
        return true;
    }

    private bool FindGroundPosition(Vector3 fromPos, out Vector3 groundPos)
    {
        groundPos = Vector3.zero;
        Vector3 rayStart = fromPos + Vector3.up * 0.5f;
        
        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, maxRayDistance))
        {
            MRUKAnchor anchor = hit.collider.GetComponent<MRUKAnchor>();
            if (anchor != null && anchor.Label.HasFlag(MRUKAnchor.SceneLabels.FLOOR))
            {
                groundPos = hit.point + hit.normal * offsetFromSurface;
                return true;
            }
            else if (anchor == null)
            {
                groundPos = hit.point + Vector3.up * offsetFromSurface;
                return true;
            }
        }

        return false;
    }

    private void BurnBuilding(GameObject building)
    {
        if (burntBuildingPrefab == null)
        {
            Debug.LogWarning("[FireSpawner] burntBuildingPrefab 未設定!");
            Destroy(building);
            return;
        }

        Vector3 pos = building.transform.position;
        Quaternion rot = building.transform.rotation;
        Vector3 scale = building.transform.localScale;

        NetworkObject netObj = building.GetComponent<NetworkObject>();
        if (netObj != null && netObj.IsSpawned)
        {
            netObj.Despawn();
        }
        else
        {
            Destroy(building);
        }

        GameObject burntBuilding = Instantiate(burntBuildingPrefab, pos, rot);
        burntBuilding.transform.localScale = scale;

        NetworkObject burntNetObj = burntBuilding.GetComponent<NetworkObject>();
        if (burntNetObj != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            burntNetObj.Spawn();
        }

        if (enableDebug)
            Debug.Log($"[FireSpawner] 建築被燒毀: {building.name}");
    }

    private void ApplyBurn(GameObject building, float deltaTime)
    {
        if (!_burnTimers.ContainsKey(building))
            _burnTimers[building] = 0f;

        _burnTimers[building] += deltaTime;

        if (enableDebug)
            Debug.Log($"[FireSpawner] {building.name} 燒了 {_burnTimers[building]:F2}s");

        if (_burnTimers[building] >= burnTimeToDestroy)
        {
            _burnTimers.Remove(building);
            BurnBuilding(building);
        }
    }


    private Vector3 SnapToGrid(Vector3 pos, float gridSize)
    {
        return new Vector3(
            Mathf.Round(pos.x / gridSize) * gridSize,
            Mathf.Round(pos.y / gridSize) * gridSize,
            Mathf.Round(pos.z / gridSize) * gridSize
        );
    }

    private void OnDrawGizmos()
    {
        if (!Application.isPlaying || !enableDebug) return;

        MRUKRoom room = MRUK.Instance?.GetCurrentRoom();
        if (room != null)
        {
            Bounds bounds = room.GetRoomBounds();
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(bounds.center, bounds.size);
            
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(bounds.center, 0.2f);
        }

        Gizmos.color = Color.red;
        foreach (Vector3 pos in _occupiedPositions)
        {
            Gizmos.DrawWireSphere(pos, 0.1f);
        }

        // 顯示活躍的擴散協程數量
        if (enableDebug && _activeSpreadCoroutines.Count > 0)
        {
            Gizmos.color = Color.yellow;
            Vector3 textPos = Camera.main ? Camera.main.transform.position + Camera.main.transform.forward * 2 : Vector3.zero;
            // (在 Scene 視圖顯示文字需要用 Handles,這裡用 Gizmos 簡化)
        }
    }
}