using UnityEngine;

public class ExtinguisherChargeParticle : MonoBehaviour
{
    [Header("Auto-find ProximitySwitchManager")]
    [SerializeField] private ProximitySwitchManager manager;

    [Header("�R��ɤl�]��l���� ParticleSystem�F����]�|�۰ʧ�^")]
    [SerializeField] private ParticleSystem chargeVfx;

    [Header("�{�{��ı�]�i��^")]
    [SerializeField] private Renderer targetRenderer;
    [SerializeField] private float flashDuration = 1.2f;      // �{�{����ɶ�
    [SerializeField] private float flashIntensity = 2.5f;     // �̫G���v
    [SerializeField] private float flashSpeed = 12f;          // �{�{�W�v

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

    private float _nextFindTime;

    private void Start()
    {
        if (manager == null)
            manager = FindObjectOfType<ProximitySwitchManager>();

        StopAndClear();
        StopFlash();
    }

    private void Update()
    {
        // Late-bind: the extinguisher is runtime-spawned and may appear before the
        // manager exists/activates — keep retrying instead of giving up forever.
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

        // �X�骬�A�G�ߨ譫�m
        if (manager.IsMerged)
        {
            separatedTime = 0f;
            played = false;
            StopAndClear();
            StopFlash();
            return;
        }

        // ���}���A�G�ֿn�ɶ�
        separatedTime += Time.deltaTime;

        // 30 ����G����ɤl + Ĳ�o�{�{
        if (!played && separatedTime >= manager.extinguisherGlowAfter)
        {
            played = true;

            // Play() on an inactive GameObject silently does nothing — activate first.
            if (!chargeVfx.gameObject.activeInHierarchy)
                chargeVfx.gameObject.SetActive(true);

            chargeVfx.Play(true);
            StartFlash();
            SfxLib.PlayAt("ChargeReady", transform.position, 0.9f);   // 充能完成提示音
        }

        // �B�z�{�{�ĪG
        UpdateFlash();
    }

    private void StopAndClear()
    {
        if (chargeVfx != null)
            chargeVfx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    // ====== �{�{�޿� ======

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

        // �� sin ���ֳͧt�{�{
        float blink = 0.5f + 0.5f * Mathf.Sin(Time.time * flashSpeed);

        // �H�ۮɶ��C�C�I��
        float fade = Mathf.Clamp01(flashTimer / flashDuration);

        float intensity = blink * flashIntensity * fade;

        targetRenderer.material.SetColor(EmissionColorId, Color.white * intensity);

        if (flashTimer <= 0f)
        {
            StopFlash();
        }
    }
}
