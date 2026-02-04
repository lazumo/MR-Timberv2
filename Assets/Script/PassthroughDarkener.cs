using UnityEngine;
using Meta.XR.MRUtilityKit; // 不一定需要，留著也行

public class PassthroughDarkener : MonoBehaviour
{
    [Header("Drag the object that has OVRPassthroughLayer")]
    public OVRPassthroughLayer passthroughLayer;

    [Header("Stage 2 (Fire scene) look")]
    public float stage2Brightness = -0.45f;
    public float stage2Contrast = 1.25f;
    public float stage2Saturation = 0.85f;

    [Header("Default look")]
    public float defaultBrightness = 0.0f;
    public float defaultContrast = 1.0f;
    public float defaultSaturation = 1.0f;

    private void Start()
    {
        if (SceneController.Instance != null)
            SceneController.Instance.CurrentLevel.OnValueChanged += OnStageChanged;

        Apply(SceneController.Instance != null && SceneController.Instance.GetCurrentStage() == 2);
    }

    private void OnDestroy()
    {
        if (SceneController.Instance != null)
            SceneController.Instance.CurrentLevel.OnValueChanged -= OnStageChanged;
    }

    private void OnStageChanged(int prev, int cur)
    {
        Apply(cur == 2);
    }

    private void Apply(bool fireScene)
    {
        if (passthroughLayer == null) return;

        // 這個 API/欄位在不同版本名字可能略不同；
        // 若你用的是 Color Adjustment/Controls 模式，會有效果。
        float b = fireScene ? stage2Brightness : defaultBrightness;
        float c = fireScene ? stage2Contrast : defaultContrast;
        float s = fireScene ? stage2Saturation : defaultSaturation;

        passthroughLayer.SetBrightnessContrastSaturation(b, c, s);
    }
}
