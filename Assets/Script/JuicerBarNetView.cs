using Unity.Netcode;
using UnityEngine;

public class JuicerBarNetView : NetworkBehaviour
{
    [Header("Refs")]
    [SerializeField] private ColorFactoryNetDriver driver;
    [SerializeField] private ColorFactoryVisual visual;   // ⭐ 這個就是重點

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
        TryBindBars();

        if (!barB || !barC)
        {
            // ⭐ 等 visual ready
            visual.OnVisualReady += TryBindBars;
        }

        active = driver.IsActive.Value;
        targetOffset = ComputeOffset(driver.HandDistance.Value);

        driver.IsActive.OnValueChanged += (_, v) => active = v;
        driver.HandDistance.OnValueChanged += (_, v) => targetOffset = ComputeOffset(v);
    }

    private void TryBindBars()
    {
        barB = visual.CurrentBarB;
        barC = visual.CurrentBarC;

        if (!barB || !barC) return;

        B0 = barB.localPosition;
        C0 = barC.localPosition;

        visual.OnVisualReady -= TryBindBars; // ⭐ 解註冊
    }


    private void Update()
    {
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
