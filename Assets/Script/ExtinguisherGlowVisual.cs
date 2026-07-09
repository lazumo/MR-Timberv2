using UnityEngine;

/// <summary>
/// 滅火器充能特效（Buff）：分離狀態累積 extinguisherGlowAfter 秒後播放充能粒子
/// + 白光閃爍 + 提示音，提示玩家可以合體成水管。
/// 純視覺，每個 client 各自計時；合體狀態用同步的 IsMergedNet 重置。
/// </summary>
public class ExtinguisherChargeParticle : MonoBehaviour
{
    [Header("Auto-find ProximitySwitchManager")]
    [SerializeField] private ProximitySwitchManager manager;

    [Header("視覺開關（老師 feedback 2026-07-09：拿掉藍色 buff 視覺，改用合體提示光束）")]
    [Tooltip("false = 不播充能粒子與白光閃爍，只保留提示音+震動。")]
    [SerializeField] private bool showChargeVisual = false;

    [Header("充能粒子（留空會自動抓子物件的 ParticleSystem）")]
    [SerializeField] private ParticleSystem chargeVfx;

    [Header("閃光視覺（可選）")]
    [SerializeField] private Renderer targetRenderer;
    [SerializeField] private float flashDuration = 1.2f;      // 閃爍持續時間
    [SerializeField] private float flashIntensity = 2.5f;     // 最亮強度
    [SerializeField] private float flashSpeed = 12f;          // 閃爍頻率

    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private bool played = false;

    private bool flashing = false;
    private float flashTimer = 0f;

    private float _nextFindTime;

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
        // 滅火器是 runtime 生成的，manager 可能還沒就緒 — 每秒重試而不是永久放棄
        if (manager == null)
        {
            if (Time.time >= _nextFindTime)
            {
                _nextFindTime = Time.time + 1f;
                manager = FindObjectOfType<ProximitySwitchManager>();
            }
            if (manager == null) return;
        }

        if (chargeVfx == null) return;

        // 充能狀態直接讀 server 同步的 IsChargedNet（叮聲/震動/合體光束同一幀出現，
        // 且跟「實際可以合體」完全一致；合體或重新計時都會自動變 false）
        bool charged = manager.IsChargedNet.Value && !manager.IsMergedNet.Value;
        if (!charged)
        {
            if (played)
            {
                played = false;
                StopAndClear();
                StopFlash();
            }
            return;
        }

        // 充能完成：播放粒子 + 觸發閃光 + 提示音
        if (!played)
        {
            played = true;

            if (showChargeVisual)
            {
                // Play() 對 inactive 物件無效，先啟用
                if (!chargeVfx.gameObject.activeInHierarchy)
                    chargeVfx.gameObject.SetActive(true);

                chargeVfx.Play(true);
                StartFlash();
            }
            SfxLib.PlayAt("ChargeReady", transform.position, 0.9f);

            // 充能完成：自己的滅火器 → 握滅火器那隻手短震一下（Host=右手、Client=左手）
            var netObj = GetComponentInParent<Unity.Netcode.NetworkObject>();
            if (netObj != null && netObj.IsOwner)
                StartCoroutine(Haptics.Pulse(Haptics.ExtinguisherHand, 1f, 0.9f, 0.25f));
        }

        UpdateFlash();
    }

    private void StopAndClear()
    {
        if (chargeVfx != null)
            chargeVfx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    // ====== 閃光邏輯 ======

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

        // sin 波閃爍
        float blink = 0.5f + 0.5f * Mathf.Sin(Time.time * flashSpeed);

        // 隨時間慢慢淡出
        float fade = Mathf.Clamp01(flashTimer / flashDuration);

        float intensity = blink * flashIntensity * fade;

        targetRenderer.material.SetColor(EmissionColorId, Color.white * intensity);

        if (flashTimer <= 0f)
        {
            StopFlash();
        }
    }
}
