using Unity.Netcode;
using UnityEngine;

public class JuicerBarNetView : NetworkBehaviour
{
    [Header("Refs")]
    [SerializeField] private ColorFactoryNetDriver driver;
    [SerializeField] private Transform barB;
    [SerializeField] private Transform barC;

    [Header("Mapping: £GD -> offset")]
    [SerializeField] private float offsetScale = 1.0f; // 1:1
    [SerializeField] private float minOffset = -0.30f; // meters
    [SerializeField] private float maxOffset = +0.30f; // meters
    [SerializeField] private float lerpSpeed = 25f;

    private Vector3 B0, C0;
    private bool active;
    private float targetOffset;

    private void Awake()
    {
        B0 = barB.localPosition;
        C0 = barC.localPosition;
    }

    public override void OnNetworkSpawn()
    {
        active = driver.IsActive.Value;
        targetOffset = ComputeOffset(driver.HandDistance.Value);

        driver.IsActive.OnValueChanged += (_, v) => active = v;
        driver.HandDistance.OnValueChanged += (_, v) => targetOffset = ComputeOffset(v);
    }

    private void Update()
    {
        float off = active ? targetOffset : 0f;

        Vector3 bTarget = B0 + new Vector3( 0f, 0f, +off);
        Vector3 cTarget = C0 + new Vector3( 0f, 0f, -off);

        barB.localPosition = Vector3.Lerp(barB.localPosition, bTarget, Time.deltaTime * lerpSpeed);
        barC.localPosition = Vector3.Lerp(barC.localPosition, cTarget, Time.deltaTime * lerpSpeed);
    }

    private float ComputeOffset(float handDistance)
    {
        float delta = handDistance - driver.HandDistanceBase.Value; // £GD
        float off = delta * offsetScale;
        return Mathf.Clamp(off, minOffset, maxOffset);
    }
}
