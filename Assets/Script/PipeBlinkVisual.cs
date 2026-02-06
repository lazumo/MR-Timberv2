using UnityEngine;

public class PipeBlinkVisual : MonoBehaviour
{
    [SerializeField] private ProximitySwitchManager manager;
    [SerializeField] private Renderer targetRenderer;

    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

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

        // 如果在冷卻（理論上 pipe 已 despawn，但以防萬一）
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
    }

    private void SetEmission(float strength01)
    {
        Material m = targetRenderer.material;
        m.EnableKeyword("_EMISSION");
        m.SetColor(EmissionColorId, Color.white * strength01);
    }
}
