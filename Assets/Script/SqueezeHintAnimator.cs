using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 榨汁 UI 提示動畫：左右兩組「ghost 手 + 箭頭」反覆向中心推入，示範擠壓方向。
/// 節奏：停留 → 平滑推入 → 短暫消失 → 回到原位，循環播放。
/// 掛在 FactoryJuiceHint 的 hintRoot 上（顯示時機由 FactoryJuiceHint 控制，
/// 這裡只管動畫）。純本地視覺，不需要 networking。
/// </summary>
public class SqueezeHintAnimator : MonoBehaviour
{
    [Header("兩側提示組（ghost 手 + 箭頭放同一個父物件）")]
    [SerializeField] private Transform leftGroup;
    [SerializeField] private Transform rightGroup;

    [Header("動畫")]
    [Tooltip("每側向中心推入的距離（公尺），建議 ≈ squeezeRange 的一半")]
    [SerializeField] private float pushDistance = 0.25f;
    [Tooltip("一次完整循環的秒數")]
    [SerializeField] private float cycleSeconds = 1.8f;
    [Range(0f, 0.5f)]
    [Tooltip("循環開頭停留在原位的比例（讓使用者看清起始位置）")]
    [SerializeField] private float holdFraction = 0.2f;
    [Range(0f, 0.3f)]
    [Tooltip("循環結尾隱藏的比例（表示一次動作結束）")]
    [SerializeField] private float hideFraction = 0.12f;

    private Vector3 _leftRest, _rightRest;
    private Vector3 _leftDir, _rightDir;     // 各自朝中心的方向（local）
    private Renderer[] _leftRenderers, _rightRenderers;
    private float _t;

    private void OnEnable()
    {
        if (leftGroup == null || rightGroup == null) return;

        _leftRest = leftGroup.localPosition;
        _rightRest = rightGroup.localPosition;

        // 推入方向 = 朝向對側（在共同父空間計算，factory 旋轉時自動跟著轉）
        Vector3 line = _rightRest - _leftRest;
        _leftDir = line.sqrMagnitude > 1e-6f ? line.normalized : Vector3.right;
        _rightDir = -_leftDir;

        _leftRenderers = leftGroup.GetComponentsInChildren<Renderer>(true);
        _rightRenderers = rightGroup.GetComponentsInChildren<Renderer>(true);

        _t = 0f;
        SetVisible(true);
        Apply(0f);
    }

    private void OnDisable()
    {
        // 復位，下次顯示從頭播
        if (leftGroup != null) leftGroup.localPosition = _leftRest;
        if (rightGroup != null) rightGroup.localPosition = _rightRest;
    }

    private void Update()
    {
        if (leftGroup == null || rightGroup == null) return;

        _t = (_t + Time.deltaTime / Mathf.Max(0.1f, cycleSeconds)) % 1f;

        float hideStart = 1f - hideFraction;

        if (_t >= hideStart)
        {
            SetVisible(false);          // 短暫消失＝一次動作結束
            Apply(0f);                  // 位置先歸位，下一循環直接從原位出現
            return;
        }

        SetVisible(true);

        if (_t < holdFraction)
        {
            Apply(0f);                  // 開頭停留
            return;
        }

        // 停留結束 → 平滑推入（smoothstep 進出都柔和）
        float k = Mathf.InverseLerp(holdFraction, hideStart, _t);
        Apply(Mathf.SmoothStep(0f, 1f, k));
    }

    private void Apply(float push01)
    {
        leftGroup.localPosition = _leftRest + _leftDir * (pushDistance * push01);
        rightGroup.localPosition = _rightRest + _rightDir * (pushDistance * push01);
    }

    private void SetVisible(bool on)
    {
        if (_leftRenderers != null)
            foreach (var r in _leftRenderers) if (r != null && r.enabled != on) r.enabled = on;
        if (_rightRenderers != null)
            foreach (var r in _rightRenderers) if (r != null && r.enabled != on) r.enabled = on;
    }
}
