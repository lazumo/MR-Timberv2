using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class FruitSqueezeInContainer_Tag : NetworkBehaviour
{
    [Header("Visual Source")]
    [SerializeField] private ColorFactoryVisual visual;
    [SerializeField] private ColorFactory colorFactory;

    [Header("Fruit Tag")]
    [SerializeField] private string fruitTag = "Fruit";

    [Header("Squeeze Axis (bars move on local Z)")]
    [SerializeField] private float minSqueeze = 0.35f;
    [SerializeField] private float maxSqueeze = 1.0f;
    [SerializeField] private float lerpSpeed = 15f;

    [Header("Optional volume compensate")]
    [SerializeField] private bool volumeCompensate = true;
    [SerializeField] private float compensateStrength = 0.5f;

    // ⭐ bars 不再 inspector 指定
    private Transform barB;
    private Transform barC;

    private readonly HashSet<Transform> fruits = new();
    private readonly Dictionary<Transform, Vector3> initialScale = new();

    private float gap0;
    private bool barsReady;
    private bool hasDestroyed = false;
    private void OnEnable()
    {
        if (visual != null)
            visual.OnVisualReady += TryBindBars;

        TryBindBars(); // ⭐ 若 visual 已 ready
    }

    private void OnDisable()
    {
        if (visual != null)
            visual.OnVisualReady -= TryBindBars;
    }

    private void TryBindBars()
    {
        var newBarB = visual.CurrentBarB;
        var newBarC = visual.CurrentBarC;

        if (!newBarB || !newBarC) return;

        barB = newBarB;
        barC = newBarC;

        // ⭐ 重新計算基準 gap
        gap0 = GetGap();
        if (gap0 <= 0.0001f) gap0 = 0.0001f;

        barsReady = true;

        Debug.Log($"[FruitSqueeze] Bars re-binded: {barB.parent.name}");
    }

    private void Update()
    {
        if (!barsReady) return;
        if (fruits.Count == 0) return;

        float gap = GetGap();
        float t = gap / gap0;
        float squeeze = Mathf.Clamp(t, minSqueeze, maxSqueeze);
        Debug.Log(
            $"[FruitSqueeze] gap={gap:F3} (gap0={gap0:F3}) | " +
            $"t={t:F3} | squeeze={squeeze:F3} (min={minSqueeze:F2})"
        );
        // ===== 榨到極限：直接刪掉所有 fruit =====
        if (!hasDestroyed && squeeze <= minSqueeze + 0.001f)
        {
            DestroyAllFruits();
            hasDestroyed = true;
            return; // 這一幀不用再算 scale
        }
        else if (squeeze > minSqueeze + 0.01f) // 給一點 hysteresis
        {
            hasDestroyed = false;
        }
        // ===== 1. 建立 squeeze space（world space）=====
        Vector3 z = (barB.position - barC.position).normalized; // 擠壓方向

        if (z.sqrMagnitude < 1e-6f) return;

        Vector3 tmpUp = Mathf.Abs(Vector3.Dot(z, Vector3.up)) > 0.9f
            ? Vector3.right
            : Vector3.up;

        Vector3 x = Vector3.Normalize(Vector3.Cross(tmpUp, z));
        Vector3 y = Vector3.Cross(z, x);

        // squeeze space basis matrix (local -> world)
        Matrix4x4 B = new Matrix4x4();
        B.SetColumn(0, new Vector4(x.x, x.y, x.z, 0));
        B.SetColumn(1, new Vector4(y.x, y.y, y.z, 0));
        B.SetColumn(2, new Vector4(z.x, z.y, z.z, 0));
        B.SetColumn(3, new Vector4(0, 0, 0, 1));

        // ===== 2. squeeze space scale =====
        float axisScale = squeeze;
        float sideScale = volumeCompensate
            ? Mathf.Pow(1f / squeeze, compensateStrength)
            : 1f;

        Matrix4x4 S = Matrix4x4.Scale(new Vector3(
            sideScale,
            sideScale,
            axisScale
        ));

        // world-space equivalent scale matrix
        Matrix4x4 M = B * S * B.inverse;

        foreach (var fruit in fruits)
        {
            if (!fruit) continue;
            if (!initialScale.TryGetValue(fruit, out var s0)) continue;

            // ===== 3. 將 M 投影回 fruit 的 local scale =====
            // M 對 local XYZ 軸的拉伸量
            Vector3 sx = new Vector3(M.m00, M.m01, M.m02);
            Vector3 sy = new Vector3(M.m10, M.m11, M.m12);
            Vector3 sz = new Vector3(M.m20, M.m21, M.m22);

            Vector3 derivedScale = new Vector3(
                sx.magnitude,
                sy.magnitude,
                sz.magnitude
            );

            Vector3 targetScale = Vector3.Scale(s0, derivedScale);

            fruit.localScale = Vector3.Lerp(
                fruit.localScale,
                targetScale,
                Time.deltaTime * lerpSpeed
            );
        }
    }

    private float GetGap()
    {
        return Mathf.Abs(barB.localPosition.z - barC.localPosition.z);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;
        if (!other.CompareTag(fruitTag)) return;

        Transform fruit = other.transform;
        if (fruits.Add(fruit))
            initialScale[fruit] = fruit.localScale;
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsServer) return;
        if (!other.CompareTag(fruitTag)) return;

        Transform fruit = other.transform;
        fruits.Remove(fruit);
        initialScale.Remove(fruit);
    }
    private void DestroyAllFruits()
    {
        if (!IsServer) return;
        foreach (var fruit in fruits)
        {
            if (!fruit) continue;

            var netObj = fruit.GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsSpawned)
            {
                netObj.Despawn(true); // ✅ server authoritative
            }
            AdvanceHousePaintStage();
        }

        fruits.Clear();
        initialScale.Clear();

        Debug.Log("[FruitSqueeze] All fruits destroyed (fully squeezed).");
    }
    private void AdvanceHousePaintStage()
    {
        if (!IsServer) return;
        if (colorFactory == null) return;

        var houseSync = colorFactory.OwnerHouseSync;
        if (houseSync == null) return;
        houseSync.AdvancePaintStage();
    }
}
