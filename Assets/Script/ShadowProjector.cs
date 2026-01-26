using UnityEngine;

public class FruitShadowProjector : MonoBehaviour
{
    [Header("Shadow Prefab")]
    public GameObject shadowPrefab;

    [Header("Settings")]
    public float rayStartOffset = 0.25f;
    public float maxRayDistance = 50f;
    public float groundOffset = 0.02f;
    public float shadowAlpha = 0.5f;

    private GameObject shadowInstance;
    private Rigidbody rb;
    private bool initialized = false;

    private FruitDropState dropState;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        dropState = GetComponent<FruitDropState>();
    }

    // 由 Spawner 呼叫，傳入 fruit color
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
        Debug.Log($"[Shadow] Initialize called on {gameObject.name}");
        // ===== 設定大小（依 sphere 半徑）=====
        float radius = GetFruitRadius();
        float diameter = radius * 3f;
        shadowInstance.transform.localScale = new Vector3(diameter, diameter, diameter);

        // ===== 設定顏色 =====
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

        // 如果開始掉落 → 移除投影
        if (dropState != null && dropState.HasDropped.Value)
        {
            Destroy(shadowInstance);
            shadowInstance = null;
            return;
        }

        Vector3 origin = transform.position + Vector3.up * rayStartOffset;

        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, maxRayDistance);

        RaycastHit validHit = default;
        bool found = false;

        foreach (var h in hits)
        {
            if (h.collider.gameObject == gameObject)
                continue;

            validHit = h;
            found = true;
            break;
        }

        if (found)
        {
            shadowInstance.SetActive(true);
            shadowInstance.transform.position = validHit.point;
            shadowInstance.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }
        else
        {
            shadowInstance.SetActive(false);
        }
    }


    private float GetFruitRadius()
    {
        // 優先用 SphereCollider
        if (TryGetComponent(out SphereCollider sphere))
        {
            return sphere.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.y, transform.lossyScale.z);
        }
        return 0.25f; // fallback
    }

    private void OnDestroy()
    {
        if (shadowInstance != null)
            Destroy(shadowInstance);
    }
}
