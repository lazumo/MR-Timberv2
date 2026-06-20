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
        if (Camera.main != null)
            _center = Camera.main.transform.position;
        else
            _center = transform.position;

        IsInitialized = true;
        Debug.Log($"[SpawnArea] Center locked at {_center} (radius={radius})");
    }

    public Vector3 GetCenter() => _center;

    public void SetCenter(Vector3 center)
    {
        _center = center;
        IsInitialized = true;
        Debug.Log($"[SpawnArea] Center overridden to {_center} (radius={radius})");
    }

    public bool IsInside(Vector3 worldPos)
    {
        if (!IsInitialized) return false;
        float dx = worldPos.x - _center.x;
        float dz = worldPos.z - _center.z;
        return (dx * dx + dz * dz) <= radius * radius;
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
