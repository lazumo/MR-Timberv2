using UnityEngine;
using Meta.XR.MRUtilityKit;

// 限制 TreeSpawner / HouseSpawner 只在玩家初始站位附近的圓形範圍（XZ 平面）內生成物件。
// MRUK 會把實體上缺少的牆面補完、或把真牆延伸太長，導致生成點落在玩家構不到的位置。
// 解法：遊戲開始（MRUK 掃完房間）時抓一次玩家頭盔位置當圓心，半徑由 Inspector 設定。
// 之後玩家可自由移動，圓心不會跟著走。
public class SpawnArea : MonoBehaviour
{
    public static SpawnArea Instance { get; private set; }

    [Tooltip("生成範圍半徑（米）。樹和房子只能生成在此半徑內。")]
    public float radius = 1.5f;

    [Tooltip("是否在 Scene View 中畫出可生成範圍。")]
    public bool drawGizmo = true;

    private Vector3 _center;
    private Quaternion _yaw = Quaternion.identity;   // 虛擬房間的朝向（方形判定用）
    public bool IsInitialized { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        if (MRUK.Instance != null && MRUK.Instance.GetCurrentRoom() != null)
            CaptureCenter();
        else if (MRUK.Instance != null)
            MRUK.Instance.RegisterSceneLoadedCallback(CaptureCenter);
        else
            CaptureCenter();
    }

    private void CaptureCenter()
    {
        // 閂鎖：只做「第一次」的保底定位。這個方法掛在 MRUK 的 SceneLoaded callback 上，
        // 玩家走出掃描範圍→re-localization→場景重載時會再被觸發，沒有閂鎖的話
        // 房間會重新以玩家當下位置為中心（永久搬家），並蓋掉 loader 的權威 pose。
        if (IsInitialized) return;

        if (Camera.main != null)
            _center = Camera.main.transform.position;
        else
            _center = transform.position;

        IsInitialized = true;
        Debug.Log($"[SpawnArea] Center locked at {_center} (radius={radius})");
    }

    // ===== 房間 pose 以 colocation anchor 為基準 =====
    // anchor 是物理鎖定的：玩家走出邊界→re-localization→世界座標系移動時，
    // anchor 的 transform 會被追蹤系統自動貼回正確的物理位置。
    // 房間 pose 若存成死的世界座標，世界一動房間就「壞掉」；
    // 改存「相對 anchor」的偏移、讀取時用 anchor 當下位置換算回來 → anchor 對，房間就永遠對。
    private Transform _anchorRef;
    private Vector3 _localCenter;     // 房間中心（anchor 座標系、只算 yaw）
    private float _localYawOffset;    // 房間 yaw 相對 anchor yaw
    private bool _anchorRelative;

    private void TryBindAnchor()
    {
        if (_anchorRef != null) return;
        // 只綁 colocation 圖釘 — 排除手動擺放的「中心圖釘」（兩台要綁同一支才一致）
        // 統一走 ColocationHostAlignment 的 finder:排除中心圖釘,
        // guest 只認 host 廣播的 UUID(兩台綁到不同 anchor = 修不掉的固定位移)。
        var a = ColocationHostAlignment.FindSharedAlignmentAnchor();
        if (a != null && a.Created)
            _anchorRef = a.transform;
    }

    // 已有世界 pose、之後才找到 anchor → 補做一次轉換（lazy）
    private void TryConvertToAnchorRelative()
    {
        if (_anchorRelative || !IsInitialized) return;
        TryBindAnchor();
        if (_anchorRef == null) return;

        float ay = _anchorRef.eulerAngles.y;
        _localCenter = Quaternion.Euler(0f, -ay, 0f) * (_center - _anchorRef.position);
        _localYawOffset = _yaw.eulerAngles.y - ay;
        _anchorRelative = true;
        Debug.Log($"[SpawnArea] Room pose now anchor-relative (localCenter={_localCenter}, yawOffset={_localYawOffset:F1}).");
    }

    public Vector3 GetCenter()
    {
        TryConvertToAnchorRelative();
        if (_anchorRelative && _anchorRef != null)
            return _anchorRef.position + Quaternion.Euler(0f, _anchorRef.eulerAngles.y, 0f) * _localCenter;
        return _center;
    }

    public Quaternion GetRotation()
    {
        TryConvertToAnchorRelative();
        if (_anchorRelative && _anchorRef != null)
            return Quaternion.Euler(0f, _anchorRef.eulerAngles.y + _localYawOffset, 0f);
        return _yaw;
    }

    public void SetCenter(Vector3 center)
    {
        _center = center;
        IsInitialized = true;
        Debug.Log($"[SpawnArea] Center overridden to {_center} (radius={radius})");
    }

    // client 收到 host 廣播的房間 pose 後鎖定，本機 loader 之後的 SetPose 不再覆寫
    // （colocation 對齊後兩台共用世界座標，以 host 的房間 pose 為準才會兩台一致）。
    private bool _lockedByNetwork;

    /// 虛擬房間載入時一併記下朝向，讓方形判定/邊界線跟房間的牆對齊。
    public void SetPose(Vector3 center, float yawDegrees)
    {
        if (_lockedByNetwork) return;
        _yaw = Quaternion.Euler(0f, yawDegrees, 0f);
        SetCenter(center);
        _anchorRelative = false;              // 用新值重新換算 anchor 相對 pose
        TryConvertToAnchorRelative();
    }

    /// host 廣播的權威房間 pose（client 用這個，並鎖定不被本機 loader 覆寫）。
    public void SetPoseFromNetwork(Vector3 center, float yawDegrees)
    {
        _lockedByNetwork = true;
        _yaw = Quaternion.Euler(0f, yawDegrees, 0f);
        _center = center;
        IsInitialized = true;
        _anchorRelative = false;              // 用新值重新換算 anchor 相對 pose
        TryConvertToAnchorRelative();
        Debug.Log($"[SpawnArea] Pose locked from network: {center}, yaw={yawDegrees}");
    }

    public bool IsInside(Vector3 worldPos)
    {
        if (!IsInitialized) return false;
        Vector3 c = GetCenter();   // 透過 anchor 讀當下位置（世界座標移動也不會偏）
        float dx = worldPos.x - c.x;
        float dz = worldPos.z - c.z;
        return (dx * dx + dz * dz) <= radius * radius;
    }

    /// 方形判定（半邊長 = radius，隨房間 yaw 旋轉）。火/房子用這個。
    /// margin：容差——牆面剛好在邊界上（距離=radius），浮點誤差會誤判成外面，
    /// 牆掛物件（房子）請帶一點 margin。
    public bool IsInsideBox(Vector3 worldPos, float margin = 0f)
    {
        if (!IsInitialized) return false;
        Vector3 local = Quaternion.Inverse(GetRotation()) * (worldPos - GetCenter());
        float limit = radius + margin;
        return Mathf.Abs(local.x) <= limit && Mathf.Abs(local.z) <= limit;
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmo) return;
        Vector3 c = IsInitialized ? _center : transform.position;
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.7f);
        const int seg = 48;
        Vector3 prev = c + new Vector3(radius, 0, 0);
        for (int i = 1; i <= seg; i++)
        {
            float ang = (i / (float)seg) * Mathf.PI * 2f;
            Vector3 next = c + new Vector3(Mathf.Cos(ang) * radius, 0, Mathf.Sin(ang) * radius);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }
}
