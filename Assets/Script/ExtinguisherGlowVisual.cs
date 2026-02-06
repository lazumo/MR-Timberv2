using UnityEngine;

public class ExtinguisherChargeParticle : MonoBehaviour
{
    [Header("Auto-find ProximitySwitchManager")]
    [SerializeField] private ProximitySwitchManager manager;

    [Header("充能粒子（拖子物件 ParticleSystem；不拖也會自動找）")]
    [SerializeField] private ParticleSystem chargeVfx;

    [Header("閃爍視覺（可選）")]
    [SerializeField] private Renderer targetRenderer;
    [SerializeField] private float flashDuration = 1.2f;      // 閃爍持續時間
    [SerializeField] private float flashIntensity = 2.5f;     // 最亮倍率
    [SerializeField] private float flashSpeed = 12f;          // 閃爍頻率

    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private float separatedTime = 0f;
    private bool played = false;

    private bool flashing = false;
    private float flashTimer = 0f;

    private void Awake()
    {
        if (chargeVfx == null)
            chargeVfx = GetComponentInChildren<ParticleSystem>(true);

        if (targetRenderer == null)
            targetRenderer = GetComponentInChildren<Renderer>();
    }

    private void Start()
    {
        if (manager == null)
            manager = FindObjectOfType<ProximitySwitchManager>();

        StopAndClear();
        StopFlash();
    }

    private void Update()
    {
        if (manager == null || chargeVfx == null) return;

        // 合體狀態：立刻重置
        if (manager.IsMerged)
        {
            separatedTime = 0f;
            played = false;
            StopAndClear();
            StopFlash();
            return;
        }

        // 分開狀態：累積時間
        separatedTime += Time.deltaTime;

        // 30 秒到：播放粒子 + 觸發閃爍
        if (!played && separatedTime >= manager.extinguisherGlowAfter)
        {
            played = true;

            chargeVfx.Play(true);
            StartFlash();
        }

        // 處理閃爍效果
        UpdateFlash();
    }

    private void StopAndClear()
    {
        chargeVfx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    // ====== 閃爍邏輯 ======

    private void StartFlash()
    {
        flashing = true;
        flashTimer = flashDuration;

        if (targetRenderer != null)
        {
            targetRenderer.material.EnableKeyword("_EMISSION");
        }
    }

    private void StopFlash()
    {
        flashing = false;
        flashTimer = 0f;

        if (targetRenderer != null)
        {
            targetRenderer.material.SetColor(EmissionColorId, Color.black);
        }
    }

    private void UpdateFlash()
    {
        if (!flashing || targetRenderer == null) return;

        flashTimer -= Time.deltaTime;

        // 用 sin 產生快速閃爍
        float blink = 0.5f + 0.5f * Mathf.Sin(Time.time * flashSpeed);

        // 隨著時間慢慢衰減
        float fade = Mathf.Clamp01(flashTimer / flashDuration);

        float intensity = blink * flashIntensity * fade;

        targetRenderer.material.SetColor(EmissionColorId, Color.white * intensity);

        if (flashTimer <= 0f)
        {
            StopFlash();
        }
    }
}
