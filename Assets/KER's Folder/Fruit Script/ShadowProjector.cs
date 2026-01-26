using UnityEngine;
using Meta.XR.MRUtilityKit;

public class FruitShadowProjector : MonoBehaviour
{
    [Header("Raycast Settings")]
    public float rayStartOffset = 0.5f; // 加大偏移
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
    private float fruitSize;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        fruitSize = GetFruitSize();
    }

    public void Initialize(GameObject prefab, Color color)
    {
        if (prefab == null)
        {
            Debug.LogError("[Shadow] Prefab is null!");
            return;
        }

        indicator = Instantiate(prefab);
        indicator.SetActive(true);

        indicator.transform.localScale = Vector3.one * fruitSize * sizeMultiplier;

        if (indicator.TryGetComponent(out Renderer r))
        {
            Material mat = new Material(r.material);
            mat.color = new Color(color.r, color.g, color.b, shadowAlpha);
            r.material = mat;
        }

        Debug.Log($"[Shadow] 初始化完成: {gameObject.name}, 果實大小: {fruitSize}");
    }

    private float GetFruitSize()
    {
        Renderer fruitRenderer = GetComponent<Renderer>();
        if (fruitRenderer != null)
        {
            Bounds bounds = fruitRenderer.bounds;
            return Mathf.Max(bounds.size.x, bounds.size.z);
        }
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

        // 起點在果實上方,確保不會打到自己
        Vector3 origin = transform.position + Vector3.up * (fruitSize + rayStartOffset);
        Vector3 direction = Vector3.down;

        if (showDebugRay)
        {
            Debug.DrawRay(origin, direction * maxRayDistance, Color.yellow);
            Debug.DrawLine(transform.position, origin, Color.green); // 顯示起點位置
        }

        if (Physics.Raycast(origin,
                            direction,
                            out RaycastHit hit,
                            maxRayDistance,
                            ~0,
                            QueryTriggerInteraction.Ignore))
        {
            // 雙重保險:檢查是否打到自己
            if (hit.collider.transform == transform ||
                hit.collider.transform.IsChildOf(transform))
            {
                if (showDebugRay)
                    Debug.LogWarning("[Shadow] Raycast 打到果實自己!");

                indicator.SetActive(false);
                return;
            }

            bool isValidSurface = true;
            MRUKAnchor anchor = hit.collider.GetComponent<MRUKAnchor>();

            if (anchor != null)
            {
                isValidSurface = anchor.Label.HasFlag(MRUKAnchor.SceneLabels.FLOOR);
            }

            if (isValidSurface)
            {
                PlaceIndicator(hit);
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

        if (showDebugRay)
        {
            Debug.DrawRay(hit.point, hit.normal * 0.3f, Color.blue, 0.1f);
            Debug.DrawLine(transform.position, hit.point, Color.cyan, 0.1f);
        }
    }

    private void OnDestroy()
    {
        if (indicator)
            Destroy(indicator);
    }
}