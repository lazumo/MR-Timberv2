using Unity.Netcode;
using UnityEngine;

public class FruitShadowProjector : MonoBehaviour
{
    [Header("Shadow Prefab")]
    public GameObject shadowPrefab;

    [Header("Raycast Settings")]
    public float rayStartOffset = 0.25f;
    public float maxRayDistance = 50f;

    [Header("Blink Curve")]
    public float blinkStartTime = 5f;      // 幾秒前開始閃
    public float minBlinkInterval = 0.18f; // 最快閃爍（快掉時）
    public float maxBlinkInterval = 0.6f;  // 剛開始警告時
    public float blinkExponent = 1.8f;     // 越大 → 後段加速越猛

    [Header("Shadow Visual")]
    public float shadowAlpha = 0.5f;

    [Header("Layers")]
    [Tooltip("Box + Ground layers")]
    public LayerMask surfaceMask;

    private GameObject shadowInstance;
    private bool initialized = false;

    private FruitDropState dropState;

    private void Awake()
    {
        dropState = GetComponent<FruitDropState>();
    }
    public void InitializeFromFruit()
    {
        if (initialized) return;

        FruitData data = GetComponent<FruitData>();
        if (data == null)
        {
            Debug.LogError("[FruitShadowProjector] Missing FruitData");
            return;
        }
        Color color = ColorTable.Get(data.colorIndex.Value);
        Initialize(color);
    }

    public void Initialize(Color fruitColor)
    {
        if (initialized) return;
        initialized = true;

        if (shadowPrefab == null)
        {
            Debug.LogError("[FruitShadowProjector] shadowPrefab not assigned!");
            return;
        }

        shadowInstance = Instantiate(shadowPrefab);

        float radius = GetFruitRadius();
        float diameter = radius * 3f;
        shadowInstance.transform.localScale = new Vector3(diameter, diameter, diameter);

        if (shadowInstance.TryGetComponent(out Renderer r))
        {
            Material mat = new Material(r.material);
            mat.color = new Color(fruitColor.r, fruitColor.g, fruitColor.b, shadowAlpha);
            r.material = mat;
        }
    }

    private void LateUpdate()
    {
        if (shadowInstance == null)
            return;

        // 真正落地後移除 shadow
        if (dropState != null && dropState.HasLanded.Value)
        {
            Destroy(shadowInstance);
            shadowInstance = null;
            return;
        }
        bool blinkVisible = ShouldBlinkVisible();
        Vector3 origin = transform.position + Vector3.up * rayStartOffset;

        // ===== 1️⃣ 偵測 ShadowDetector (trigger) =====
        RaycastHit[] triggerHits = Physics.RaycastAll(origin, Vector3.down, maxRayDistance);

        foreach (var h in triggerHits)
        {
            var receiver = h.collider.GetComponent<ToolShadowReceiver>();
            if (receiver != null)
            {
                receiver.RegisterShadowHit();
                break;
            }
        }

        bool hasSurface = Physics.Raycast(
            origin,
            Vector3.down,
            out RaycastHit hit,
            maxRayDistance,
            surfaceMask,
            QueryTriggerInteraction.Ignore
        );
        bool shouldShow = hasSurface && blinkVisible;
        shadowInstance.SetActive(shouldShow);

        if (!shouldShow)
            return;

        // ===== 更新位置 =====
        shadowInstance.transform.position = hit.point;
        shadowInstance.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }

    private float GetFruitRadius()
    {
        if (TryGetComponent(out SphereCollider sphere))
        {
            return sphere.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.y, transform.lossyScale.z);
        }
        return 0.25f;
    }

    private void OnDestroy()
    {
        if (shadowInstance != null)
            Destroy(shadowInstance);
    }
    private bool ShouldBlinkVisible()
    {
        if (dropState == null)
            return true;

        double dropTime = dropState.DropTime.Value;
        if (dropTime <= 0)
            return true;

        double now = NetworkManager.Singleton.LocalTime.Time;
        float remaining = (float)(dropTime - now);

        // 還沒進入警告區間 → 穩定顯示
        if (remaining > blinkStartTime)
            return true;

        // 進入警告區間後，remaining 可能是正或負，都 OK
        float t = Mathf.Clamp01(1f - (remaining / blinkStartTime));
        // t = 0 → 剛開始警告
        // t = 1 → 掉落瞬間（甚至之後）

        // Exponential 加速
        float eased = Mathf.Pow(t, blinkExponent);

        float interval = Mathf.Lerp(
            maxBlinkInterval,
            minBlinkInterval,
            eased
        );
        Debug.Log($"{interval}");
        return Mathf.FloorToInt(Time.time / interval) % 2 == 0;
    }
}
