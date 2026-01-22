using UnityEngine;
using Unity.Netcode;
using EzySlice;

public class SliceObject : NetworkBehaviour
{
    [Header("Knife Points")]
    public Transform startPoint;
    public Transform endPoint; 
    public Transform frontEnd;
    public Transform backEnd;

    [Header("Slice")]
    public LayerMask sliceLayer;
    public Material crossSectionMaterial;
    public float energyThreshold = 5f;
    public float velocityWeight = 1f;

    [Header("Particles")]
    public ParticleSystem woodChips;
    public GameObject vfxPrefab;

    [Header("Cut Physics")]
    public float cutForce = 1500f;

    [Header("Lower Hull Shrink")]
    public float lowerDelay = 1.5f;
    public float lowerDisappearTime = 2.0f;

    private float energy = 0f;
    private Vector3 lastPos;

    private NetworkVariable<bool> isSawingNet = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    void Start()
    {
        lastPos = transform.position;
    }

    void Update()
    {
        if (!woodChips) return;

        if (isSawingNet.Value && !woodChips.isPlaying)
            woodChips.Play();
        else if (!isSawingNet.Value && woodChips.isPlaying)
            woodChips.Stop();
    }

    void FixedUpdate()
    {
        if (!IsOwner) return;

        bool sawing = false;

        if (Physics.Linecast(startPoint.position, endPoint.position, out RaycastHit hit, sliceLayer))
        {
            sawing = true;

            float speed = ((transform.position - lastPos) / Time.fixedDeltaTime).magnitude;
            energy += speed * velocityWeight * Time.fixedDeltaTime;
            Debug.Log(
                $"[Saw Energy] Hit={hit.collider.name} | " +
                $"Speed={speed:F2} | " +
                $"Energy={energy:F2}/{energyThreshold}"
            );
            if (energy >= energyThreshold)
            {
                Vector3 bladeDir = (endPoint.position - startPoint.position).normalized;
                Vector3 sawForward = (frontEnd.position - backEnd.position).normalized;

                Vector3 normal = Vector3.Cross(bladeDir, sawForward).normalized;

                var netObj = hit.transform.GetComponentInParent<NetworkObject>();
                if (netObj)
                {
                    RequestSliceServerRpc(netObj.NetworkObjectId, hit.point, normal);
                }

                energy = 0f;
            }
        }
        else
        {
            energy = Mathf.Max(0f, energy - Time.fixedDeltaTime);
        }

        UpdateSawingState(sawing);
        lastPos = transform.position;
    }

    void UpdateSawingState(bool state)
    {
        if (isSawingNet.Value != state)
            isSawingNet.Value = state;
    }

    [ServerRpc]
    void RequestSliceServerRpc(ulong objId, Vector3 point, Vector3 normal)
    {
        PerformSliceClientRpc(objId, point, normal);
    }

    [ClientRpc]
    void PerformSliceClientRpc(ulong objId, Vector3 point, Vector3 normal)
    {
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(objId, out var netObj))
            return;

        GameObject target = netObj.gameObject;

        var hull = target.Slice(point, normal, crossSectionMaterial);
        if (hull == null) return;

        Transform parent = target.transform.parent;
        Vector3 originalPos = target.transform.position;
        Quaternion originalRot = target.transform.rotation;
        Vector3 originalScale = target.transform.localScale;

        // ---------- Upper Hull ----------
        GameObject upper = hull.CreateUpperHull(target, crossSectionMaterial);
        if (upper != null)
        {
            upper.transform.SetParent(parent, worldPositionStays: false);
            upper.transform.position = originalPos;
            upper.transform.rotation = originalRot;
            upper.transform.localScale = originalScale;

            var mc = upper.AddComponent<MeshCollider>();
            mc.convex = true;

            Rigidbody rb = upper.AddComponent<Rigidbody>();
            rb.mass = 1f;
            rb.angularDrag = 4f;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            // 推倒方向（用鋸子方向）
            Vector3 sawDir = (endPoint.position - startPoint.position).normalized;
            Vector3 fallDir = (sawDir + Vector3.down * 0.4f).normalized;

            rb.AddForce(fallDir * cutForce * 0.002f, ForceMode.Impulse);
            HullDisappear hd = upper.AddComponent<HullDisappear>();
            hd.SetDisappearTime(3f);
            hd.vfxPrefab = vfxPrefab;
        }

        // ---------- Lower Hull ----------
        GameObject lower = hull.CreateLowerHull(target, crossSectionMaterial);
        if (lower != null)
        {
            lower.transform.SetParent(parent, worldPositionStays: false);
            lower.transform.position = originalPos;
            lower.transform.rotation = originalRot;
            lower.transform.localScale = originalScale;

            var mc = lower.AddComponent<MeshCollider>();
            mc.convex = true;

            Rigidbody rb = lower.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.isKinematic = true;

            // 加縮小消失效果
            LowerHullShrinkDisappear shrink = lower.AddComponent<LowerHullShrinkDisappear>();
            shrink.vfxPrefab = vfxPrefab;
            shrink.delayBeforeShrink = lowerDelay;
            shrink.disappearTime = lowerDisappearTime;
            shrink.StartShrinkDisappear();
        }

        // 刪除原始樹
        Destroy(target);

    }
}
