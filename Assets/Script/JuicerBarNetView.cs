using Unity.Netcode;
using UnityEngine;

public class JuicerBarNetView : NetworkBehaviour
{
    [Header("Refs")]
    [SerializeField] private ColorFactoryNetDriver driver;
    [SerializeField] private ColorFactoryVisual visual;

    [Header("Mapping: ΔD -> offset")]
    [SerializeField] private float offsetScale = 1.0f;
    [SerializeField] private float minOffset = -0.30f;
    [SerializeField] private float maxOffset = +0.30f;
    [SerializeField] private float lerpSpeed = 25f;

    private Transform barB;
    private Transform barC;

    private Vector3 B0, C0;
    private bool active;
    private float targetOffset;

    public override void OnNetworkSpawn()
    {
        // 1. 訂閱 Visual 變化 (不管現在有沒有 Ready，都要訂閱，以防後續換顏色)
        visual.OnVisualReady += TryBindBars;

        // 2. 嘗試立即綁定一次 (如果 Visual 已經好了)
        TryBindBars();

        active = driver.IsActive.Value;
        targetOffset = ComputeOffset(driver.HandDistance.Value);

        driver.IsActive.OnValueChanged += (_, v) => active = v;
        driver.HandDistance.OnValueChanged += (_, v) => targetOffset = ComputeOffset(v);
    }

    public override void OnNetworkDespawn()
    {
        // 記得在物件消失時取消訂閱，避免 Memory Leak
        if (visual != null)
            visual.OnVisualReady -= TryBindBars;
    }

    private void TryBindBars()
    {
        // 拿到當前最新的 Bar
        var newBarB = visual.CurrentBarB;
        var newBarC = visual.CurrentBarC;

        if (!newBarB || !newBarC) return;

        // 更新引用
        barB = newBarB;
        barC = newBarC;

        // 重置基準點 (因為不同顏色的模型，初始位置可能微幅不同，或是為了確保正確)
        B0 = barB.localPosition;
        C0 = barC.localPosition;

        Debug.Log($"[JuicerBarNetView] Re-binded bars to: {barB.parent.name}");

        // ⭐ 重點修正：這裡絕對不能解除訂閱 (visual.OnVisualReady -= TryBindBars)
        // 因為動態 Spawn 後，顏色可能會馬上改變，我們需要再次觸發這個函式。
    }

    private void Update()
    {
        // 安全檢查：如果還沒綁定好，就跳過
        if (barB == null || barC == null) return;

        float off = active ? targetOffset : 0f;

        Vector3 bTarget = B0 + new Vector3(0f, 0f, +off);
        Vector3 cTarget = C0 + new Vector3(0f, 0f, -off);

        barB.localPosition = Vector3.Lerp(barB.localPosition, bTarget, Time.deltaTime * lerpSpeed);
        barC.localPosition = Vector3.Lerp(barC.localPosition, cTarget, Time.deltaTime * lerpSpeed);
    }

    private float ComputeOffset(float handDistance)
    {
        float delta = handDistance - driver.HandDistanceBase.Value;
        float off = delta * offsetScale;
        return Mathf.Clamp(off, minOffset, maxOffset);
    }
}