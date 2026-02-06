using UnityEngine;
using System.Collections;

public class PassthroughDarkener : MonoBehaviour
{
    public static PassthroughDarkener Instance { get; private set; }

    public OVRPassthroughLayer passthroughLayer;

    [Header("Transition Settings")]
    public float transitionSeconds = 1.5f;

    [Header("Fire Scene Look")]
    public float stage2Brightness = -0.45f;
    public float stage2Contrast = 1.25f;
    public float stage2Saturation = 0.85f;

    [Header("Default Look")]
    public float defaultBrightness = 0f;
    public float defaultContrast = 1f;
    public float defaultSaturation = 1f;

    // ✅ 記錄「目前值」
    float curB, curC, curS;

    Coroutine fadeRoutine;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        // ✅ 初始化目前值 = default
        curB = defaultBrightness;
        curC = defaultContrast;
        curS = defaultSaturation;

        if (SceneController.Instance != null)
            SceneController.Instance.CurrentLevel.OnValueChanged += OnStageChanged;

        bool isStage2 = SceneController.Instance != null &&
                        SceneController.Instance.GetCurrentStage() == 2;

        Apply(isStage2, true);
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

    public void Apply(bool fireScene, bool instant = false)
    {
        if (passthroughLayer == null) return;

        float targetB = fireScene ? stage2Brightness : defaultBrightness;
        float targetC = fireScene ? stage2Contrast : defaultContrast;
        float targetS = fireScene ? stage2Saturation : defaultSaturation;

        if (instant)
        {
            SetNow(targetB, targetC, targetS);
            return;
        }

        if (fadeRoutine != null)
            StopCoroutine(fadeRoutine);

        fadeRoutine = StartCoroutine(FadeTo(targetB, targetC, targetS));
    }

    void SetNow(float b, float c, float s)
    {
        curB = b; curC = c; curS = s;
        passthroughLayer.SetBrightnessContrastSaturation(curB, curC, curS);
    }

    IEnumerator FadeTo(float tb, float tc, float ts)
    {
        float sb = curB, sc = curC, ss = curS; // ✅ 起點 = 目前值
        float t = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.0001f, transitionSeconds);

            curB = Mathf.Lerp(sb, tb, t);
            curC = Mathf.Lerp(sc, tc, t);
            curS = Mathf.Lerp(ss, ts, t);

            passthroughLayer.SetBrightnessContrastSaturation(curB, curC, curS);
            yield return null;
        }

        SetNow(tb, tc, ts);
        fadeRoutine = null;
    }
}
