using UnityEngine;
using Meta.XR.MRUtilityKit;

public class FruitShadowProjector : MonoBehaviour
{
    [Header("Raycast Settings")]
    public float rayStartOffset = 0.25f;
    public float maxRayDistance = 100f;

    [Header("Shadow Placement")]
    public float groundOffset = 0.02f;
    public float shadowAlpha = 0.5f;

    [Header("Shadow Size")]
    [Tooltip("投影大小倍數 (1.0 = 跟果實一樣大)")]
    public float sizeMultiplier = 1.0f;

    [Header("Debug")]
    public bool showDebugRay = true;

    private GameObject indicator;
    private Rigidbody rb;
    private bool isActive = true;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    /// <summary>
    /// 初始化投影,大小根據果實的 Renderer bounds 自動計算
    /// </summary>
    public void Initialize(GameObject prefab, Color color)
    {
        if (prefab == null)
        {
            Debug.LogError("[Shadow] Prefab is null!");
            return;
        }

        indicator = Instantiate(prefab);
        indicator.SetActive(true);

        // 根據果實大小設定投影大小
        float fruitSize = GetFruitSize();
        indicator.transform.localScale = Vector3.one * fruitSize * sizeMultiplier;

        // 設定材質和透明度
        if (indicator.TryGetComponent(out Renderer r))
        {
            Material mat = new Material(r.material);
            mat.color = new Color(color.r, color.g, color.b, shadowAlpha);
            r.material = mat;
        }

        Debug.Log($"[Shadow] 初始化完成: {gameObject.name}, 果實大小: {fruitSize}");
    }

    /// <summary>
    /// 獲取果實的實際大小 (使用 Renderer bounds)
    /// </summary>
    private float GetFruitSize()
    {
        Renderer fruitRenderer = GetComponent<Renderer>();
        if (fruitRenderer != null)
        {
            // 使用 bounds 的最大尺寸作為投影大小
            Bounds bounds = fruitRenderer.bounds;
            return Mathf.Max(bounds.size.x, bounds.size.z); // 取 XZ 平面較大值
        }

        // 備用方案:使用 transform.localScale
        return Mathf.Max(transform.localScale.x, transform.localScale.z);
    }

    private void LateUpdate()
    {
        if (!isActive || indicator == null)
            return;

        if (rb != null && !rb.isKinematic)
        {
            Destroy(indicator);
            isActive = false;
            return;
        }

        Vector3 origin = transform.position + Vector3.up * rayStartOffset;
        Vector3 direction = Vector3.down;

        if (showDebugRay)
        {
            Debug.DrawRay(origin, direction * maxRayDistance, Color.yellow);
        }

        // 🔥 使用 RaycastAll 取得所有碰撞
        RaycastHit[] hits = Physics.RaycastAll(origin,
                                                direction,
                                                maxRayDistance,
                                                ~0,
                                                QueryTriggerInteraction.Ignore);

        // 排序:由近到遠
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        // 找第一個不是自己的 hit
        RaycastHit validHit = default;
        bool foundValidHit = false;

        foreach (RaycastHit hit in hits)
        {
            // 跳過果實自己
            if (hit.collider.gameObject == gameObject)
                continue;

            validHit = hit;
            foundValidHit = true;
            break;
        }

        if (foundValidHit)
        {
            // MRUK 檢查
            bool isValidSurface = true;
            MRUKAnchor anchor = validHit.collider.GetComponent<MRUKAnchor>();

            if (anchor != null)
            {
                isValidSurface = anchor.Label.HasFlag(MRUKAnchor.SceneLabels.FLOOR);
            }

            if (isValidSurface)
            {
                PlaceIndicator(validHit);
            }
            else
            {
                indicator.SetActive(false);
            }
        }
        else
        {
            indicator.SetActive(false);
        }
    }

    private void PlaceIndicator(RaycastHit hit)
    {
        indicator.SetActive(true);
        indicator.transform.position = hit.point + hit.normal * groundOffset;

        // 計算朝向果實的方向
        Vector3 directionToFruit = transform.position - hit.point;
        directionToFruit.y = 0;

        if (directionToFruit != Vector3.zero)
        {
            Quaternion yRotation = Quaternion.LookRotation(directionToFruit);
            indicator.transform.rotation = yRotation * Quaternion.Euler(90, 0, 0);
        }
        else
        {
            indicator.transform.rotation = Quaternion.Euler(90, 0, 0);
        }

        // Debug
        if (showDebugRay)
        {
            Debug.DrawRay(hit.point, hit.normal * 0.3f, Color.blue, 0.1f);
        }
    }

    private void OnDestroy()
    {
        if (indicator)
            Destroy(indicator);
    }
}