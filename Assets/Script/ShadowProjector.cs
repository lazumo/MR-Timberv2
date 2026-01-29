using UnityEngine;

public class FruitShadowProjector : MonoBehaviour
{
    [Header("Shadow Prefab")]
    public GameObject shadowPrefab;

    [Header("Raycast Settings")]
    public float rayStartOffset = 0.25f;
    public float maxRayDistance = 50f;

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

        Initialize(data.color);
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

        // ===== 2️⃣ 投影到最近實體表面 (Box / Ground) =====
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit,
                             maxRayDistance, surfaceMask,
                             QueryTriggerInteraction.Ignore))
        {
            shadowInstance.SetActive(true);
            shadowInstance.transform.position = hit.point;
            shadowInstance.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }
        else
        {
            shadowInstance.SetActive(false);
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
}
