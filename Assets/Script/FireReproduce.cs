using Unity.Netcode;
using UnityEngine;

public class FireGrowServerOnly : NetworkBehaviour
{
    [Header("Network Prefab (self)")]
    [SerializeField] private NetworkObject firePrefab;

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
        Vector3 outwardN = -transform.forward.normalized;

        Vector3 tangent = Vector3.Cross(outwardN, Vector3.up);
        if (tangent.sqrMagnitude < 1e-4f)
            tangent = Vector3.Cross(outwardN, Vector3.right);
        tangent.Normalize();
        Vector3 bitangent = Vector3.Cross(outwardN, tangent).normalized;

        for (int a = 0; a < attempts; a++)
        {
            Vector3 dir = (tangent * Random.Range(-1f, 1f) +
                           bitangent * Random.Range(-1f, 1f)).normalized;

            Vector3 candidate = transform.position
                                + dir * step
                                + tangent * Random.Range(-jitter, jitter)
                                + bitangent * Random.Range(-jitter, jitter);

            if (Physics.Raycast(candidate + outwardN * 0.15f, -outwardN,
                    out RaycastHit hit, 0.6f))
            {
                Vector3 n = hit.normal.normalized;
                Vector3 pos = hit.point + n * offsetFromSurface;

                if (avoidFireOverlap && IsNearOtherFire(pos))
                    continue;

                Quaternion rot = Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.up);

                var child = Instantiate(firePrefab, pos, rot);
                child.Spawn(true);
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
}
