using UnityEngine;
using Meta.XR.MRUtilityKit;

public class FruitShadowProjector : MonoBehaviour
{
    private GameObject indicator;
    private Rigidbody rb;
    private Renderer fruitRenderer;

    private bool isActive = true;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        fruitRenderer = GetComponentInChildren<Renderer>();
    }

    public void Initialize(GameObject prefab, float size, Color color)
    {
        indicator = Instantiate(prefab);
        indicator.transform.localScale = Vector3.one * size;

        if (indicator.TryGetComponent(out Renderer r))
        {
            Material mat = new Material(r.material);
            mat.color = new Color(color.r, color.g, color.b, 0.5f);
            r.material = mat;
        }
    }

    private void LateUpdate()
    {
        if (!isActive || indicator == null || fruitRenderer == null)
            return;

        // 掉落後立刻關掉投影
        if (rb != null && !rb.isKinematic)
        {
            Destroy(indicator);
            isActive = false;
            return;
        }

        Vector3 origin = fruitRenderer.bounds.center + Vector3.up * 0.3f;

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 50f))
        {
            // ========= 優先：MRUK =========
            MRUKAnchor anchor = hit.collider.GetComponent<MRUKAnchor>();
            if (anchor != null &&
                anchor.Label.HasFlag(MRUKAnchor.SceneLabels.FLOOR))
            {
                PlaceIndicator(hit.point);
                return;
            }

            // ========= Fallback：Tag =========
            if (hit.collider.CompareTag("Ground"))
            {
                PlaceIndicator(hit.point);
                return;
            }
        }

        // 沒命中合法地面就隱藏
        indicator.SetActive(false);
    }

    private void PlaceIndicator(Vector3 hitPoint)
    {
        indicator.SetActive(true);
        indicator.transform.position = hitPoint + Vector3.up * 0.02f;
    }

    private void OnDestroy()
    {
        if (indicator)
            Destroy(indicator);
    }
}

