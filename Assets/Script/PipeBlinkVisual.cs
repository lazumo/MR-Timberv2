using UnityEngine;

public class PipeBlinkVisual : MonoBehaviour
{
    [SerializeField] private ProximitySwitchManager manager;
    [SerializeField] private Renderer targetRenderer;

    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private float _nextTickTime;   // 叮叮叮警示聲（跟閃爍同節奏）

    private void Awake()
    {
        if (targetRenderer == null) targetRenderer = GetComponentInChildren<Renderer>();
    }

    private void Start()
    {
        if (manager == null) manager = FindObjectOfType<ProximitySwitchManager>();
    }

    private void Update()
    {
        if (manager == null || targetRenderer == null) return;

        // �p�G�b�N�o�]�z�פW pipe �w despawn�A���H���U�@�^
        if (manager.CooldownRemain.Value > 0f)
        {
            SetEmission(0f);
            return;
        }

        float remain = manager.pipeForceBackAfter - manager.PipeAge.Value;

        if (remain > manager.warnBeforeForceBack)
        {
            SetEmission(0f);
            return;
        }

        float t = Mathf.InverseLerp(manager.warnBeforeForceBack, 0f, remain);
        float hz = Mathf.Lerp(manager.blinkHzSlow, manager.blinkHzFast, t);
        float blink = 0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.PI * 2f * hz);

        SetEmission(blink);

        // 叮叮叮：跟閃爍頻率一致、越接近變回越急促
        if (Time.time >= _nextTickTime)
        {
            _nextTickTime = Time.time + 1f / Mathf.Max(1f, hz);
            SfxLib.PlayAt("WarnTick", transform.position, 0.7f);
        }
    }

    private void SetEmission(float strength01)
    {
        Material m = targetRenderer.material;
        m.EnableKeyword("_EMISSION");
        m.SetColor(EmissionColorId, Color.white * strength01);
    }
}
