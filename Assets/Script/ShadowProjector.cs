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
    public float blinkStartTime = 5f;
    public float minBlinkInterval = 0.18f;
    public float maxBlinkInterval = 0.6f;
    public float blinkExponent = 1.8f;

    [Header("Shadow Visual")]
    public float shadowAlpha = 0.5f;

    [Header("Layers")]
    public LayerMask surfaceMask;

    private GameObject shadowInstance;
    private FruitDropState dropState;

    private void Awake()
    {
        dropState = GetComponent<FruitDropState>();
    }

    // 這個 function 會被 FruitData 呼叫多次 (Spawn 一次, ValueChanged 一次)
    public void InitializeFromFruit()
    {
        FruitData data = GetComponent<FruitData>();
        if (data == null)
        {
            Debug.LogError("[FruitShadowProjector] Missing FruitData");
            return;
        }

        Color color = ColorTable.Get(data.colorIndex.Value);
        UpdateShadowColor(color);
    }

    public void UpdateShadowColor(Color fruitColor)
    {
        if (shadowInstance == null)
        {
            if (shadowPrefab == null) return;

            shadowInstance = Instantiate(shadowPrefab);

            float radius = GetFruitRadius();
            float diameter = radius * 3f;
            shadowInstance.transform.localScale = new Vector3(diameter, diameter, diameter);
        }

        if (shadowInstance.TryGetComponent(out Renderer r))
        {
            if (!r.material.name.Contains("(Instance)"))
            {
                r.material = new Material(r.material);
            }
            r.material.color = new Color(fruitColor.r, fruitColor.g, fruitColor.b, shadowAlpha);
        }
    }

    private void LateUpdate()
    {
        if (shadowInstance == null) return;

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

        // ===== 2️⃣ 地面偵測與位置更新 =====
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

        if (shouldShow)
        {
            shadowInstance.transform.position = hit.point;
            shadowInstance.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }
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
        if (dropState == null) return true;

        double dropTime = dropState.DropTime.Value;
        if (dropTime <= 0) return true;

        double now = NetworkManager.Singleton.LocalTime.Time;
        float remaining = (float)(dropTime - now);

        if (remaining > blinkStartTime) return true;

        float t = Mathf.Clamp01(1f - (remaining / blinkStartTime));
        float eased = Mathf.Pow(t, blinkExponent);
        float interval = Mathf.Lerp(maxBlinkInterval, minBlinkInterval, eased);

        return Mathf.FloorToInt(Time.time / interval) % 2 == 0;
    }
}