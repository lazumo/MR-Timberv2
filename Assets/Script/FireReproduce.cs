using Unity.Netcode;
using UnityEngine;

public class FireGrowServerOnly : NetworkBehaviour
{
    [Header("Network Prefab (self)")]
    [SerializeField] private NetworkObject firePrefab;
    [SerializeField] private LayerMask houseLayerMask;
    [SerializeField] private LayerMask surfaceLayerMask;
    [Header("Growth")]
    [SerializeField] private float spawnInterval = 0.35f;
    [SerializeField] private float step = 0.22f;
    [SerializeField] private float jitter = 0.08f;
    [SerializeField] private float offsetFromSurface = 0.02f;
    [SerializeField] private int attempts = 6;

    [Header("Avoid Fire Overlap")]
    public bool avoidFireOverlap = true;
    public float fireMinDistance = 0.25f;
    public LayerMask fireLayerMask;

    [Header("Limits")]
    [SerializeField] private int maxTotalFires = 180;
    private Vector3 currentSurfaceNormal = Vector3.up;
    static int totalFires;
    public static int TotalFires => totalFires;

    float t;

    public override void OnNetworkSpawn()
    {
        if (IsServer) totalFires++;
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer) totalFires = Mathf.Max(0, totalFires - 1);
    }
    public void InitializeNormal(Vector3 normal)
    {
        currentSurfaceNormal = normal.normalized;
    }
    void Update()
    {
        if (!IsServer) return;
        if (totalFires >= maxTotalFires) return;

        t += Time.deltaTime;
        if (t < spawnInterval) return;
        t = 0f;

        TrySpawnChild();
    }

    void TrySpawnChild()
    {
        Vector3 outwardN = currentSurfaceNormal;
        bool upward = false;
        Vector3 tangent;
        float d = Vector3.Dot(outwardN, Vector3.up);
        if (Mathf.Abs(d) > 0.7f)
        {
            tangent = Vector3.Cross(outwardN, Vector3.right);
            upward = true;
        }
        else
        {
            tangent = Vector3.Cross(outwardN, Vector3.up);
        }
        tangent.Normalize();
        Vector3 bitangent = Vector3.Cross(outwardN, tangent).normalized;
        
        for (int a = 0; a < attempts; a++)
        {
            Vector3 dir = (tangent * Random.Range(-1f, 1f) +
                           bitangent * Random.Range(-1f, 1f)).normalized;
            if (upward)
            {
                dir *= 1.12f;
            }
            else
            {
                dir *= 1.01f;
            }
            Vector3 candidate = transform.position
                                + dir * step
                                + tangent * Random.Range(-jitter, jitter)
                                + bitangent * Random.Range(-jitter, jitter);
            if (Physics.Raycast(candidate + outwardN * 0.3f, -outwardN,
                out RaycastHit hit, 1.0f, surfaceLayerMask))
            {
                Vector3 n = hit.normal.normalized;
                Vector3 pos = hit.point + n * offsetFromSurface;
                if (TryIgniteHouseAt(pos))
                {
                    return;
                }
                
                if (avoidFireOverlap && IsNearOtherFire(pos))
                    continue;
                Quaternion rot = Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.up);

                var childNetObj = Instantiate(firePrefab, pos, rot);

                // 關鍵傳遞：告訴子火它現在貼在什麼樣的法線上
                if (childNetObj.TryGetComponent<FireGrowServerOnly>(out var childScript))
                {
                    childScript.InitializeNormal(n);
                }

                childNetObj.Spawn(true);
                return;
            }
        }
    }

    bool IsNearOtherFire(Vector3 pos)
    {
        Collider[] hits = Physics.OverlapSphere(
            pos,
            fireMinDistance,
            fireLayerMask,
            QueryTriggerInteraction.Collide);

        return hits.Length > 0;
    }
    bool TryIgniteHouseAt(Vector3 pos)
    {
        Collider[] hits = Physics.OverlapSphere(
            pos,
            0.03f, // 小一點避免誤判，可調
            houseLayerMask,
            QueryTriggerInteraction.Collide
        );

        foreach (var hit in hits)
        {
            var houseFire = hit.GetComponentInParent<HouseFireController>();
            if (houseFire != null)
            {
                houseFire.Ignite();
                return true;
            }
        }

        return false;
    }
}
