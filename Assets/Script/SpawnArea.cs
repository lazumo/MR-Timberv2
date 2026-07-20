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
        // 只做「第一次」的開機保底。這個方法掛在 MRUK 的 SceneLoaded callback 上，
        // 每次場景載入（真實房、虛擬房、裝置端場景更新）都會再被呼叫——沒有這個閂鎖，
        // 房間中心會一次次跳到 host 當下的頭盔位置，還會蓋掉 loader 算好的權威 pose。
        if (IsInitialized) return;

        if (Camera.main != null)
            _center = Camera.main.transform.position;
        else
            _center = transform.position;

        IsInitialized = true;
        Debug.Log($"[SpawnArea] Center locked at {_center} (radius={radius})");
    }

    public Vector3 GetCenter() => _center;
    public Quaternion GetRotation() => _yaw;

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
    }

    /// host 廣播的權威房間 pose（client 用這個，並鎖定不被本機 loader 覆寫）。
    public void SetPoseFromNetwork(Vector3 center, float yawDegrees)
    {
        _lockedByNetwork = true;
        _yaw = Quaternion.Euler(0f, yawDegrees, 0f);
        _center = center;
        IsInitialized = true;
        Debug.Log($"[SpawnArea] Pose locked from network: {center}, yaw={yawDegrees}");
    }

    public bool IsInside(Vector3 worldPos)
    {
        if (!IsInitialized) return false;
        float dx = worldPos.x - _center.x;
        float dz = worldPos.z - _center.z;
        return (dx * dx + dz * dz) <= radius * radius;
    }

    /// 方形判定（半邊長 = radius，隨房間 yaw 旋轉）。火/房子用這個。
    /// margin：容差——牆面剛好在邊界上（距離=radius），浮點誤差會誤判成外面，
    /// 牆掛物件（房子）請帶一點 margin。
    public bool IsInsideBox(Vector3 worldPos, float margin = 0f)
    {
        if (!IsInitialized) return false;
        Vector3 local = Quaternion.Inverse(_yaw) * (worldPos - _center);
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
