using UnityEngine;
using System;
using System.Collections.Generic;

// =======================
// Enums / Data
// =======================

public enum HouseState { Unbuilt, Built, Coloring, Colored, Firing, Saved, Destroyed }
public enum HouseColor { Red, Green, Blue }

public enum PaintStage { None, One, Two, Full }

[Serializable]
public class ColorPaintSet
{
    public HouseColor color;
    public GameObject paint1; // 1/3
    public GameObject paint2; // 2/3
    public GameObject paint3; // 3/3
}

[Serializable]
public class StateConfig
{
    public HouseState state;
    public GameObject[] activeObjects; // 非上色狀態用
}

// =======================
// Controller
// =======================

public class ObjectDisplayController : MonoBehaviour
{
    [Header("State Objects (Non-coloring)")]
    [SerializeField] private List<StateConfig> stateMappings;

    [Header("Color Paint Sets")]
    [SerializeField] private List<ColorPaintSet> colorPaintSets;

    // 快取
    private Dictionary<HouseState, StateConfig> _stateMap;
    private Dictionary<HouseColor, ColorPaintSet> _paintMap;

    private void Awake()
    {
        _stateMap = new Dictionary<HouseState, StateConfig>();
        foreach (var s in stateMappings)
        {
            if (!_stateMap.ContainsKey(s.state))
                _stateMap.Add(s.state, s);
        }

        _paintMap = new Dictionary<HouseColor, ColorPaintSet>();
        foreach (var set in colorPaintSets)
        {
            if (!_paintMap.ContainsKey(set.color))
                _paintMap.Add(set.color, set);
        }
    }

    // =======================
    // Public API（唯一入口）
    // =======================

    public void ApplyVisual(
        HouseState state,
        int colorIndex,
        PaintStage stage
    )
    {
        HideAllStateObjects();
        HideAllPaintObjects();

        // 1️⃣ 顯示「非上色」狀態物件
        if (_stateMap.TryGetValue(state, out var stateConfig))
        {
            foreach (var obj in stateConfig.activeObjects)
            {
                if (obj != null)
                    obj.SetActive(true);
            }
        }

        // 2️⃣ 上色只在 Coloring / Colored
        if (state != HouseState.Coloring && state != HouseState.Colored)
            return;

        HouseColor color = GetHouseColorFromIndex(colorIndex);

        if (!_paintMap.TryGetValue(color, out var paintSet))
            return;

        ApplyPaintStage(paintSet, stage);
    }

    // =======================
    // Internal helpers
    // =======================

    private void ApplyPaintStage(ColorPaintSet set, PaintStage stage)
    {
        switch (stage)
        {
            case PaintStage.One:
                set.paint1?.SetActive(true);
                break;

            case PaintStage.Two:
                set.paint2?.SetActive(true);
                break;

            case PaintStage.Full:
                set.paint3?.SetActive(true);
                break;
        }
    }

    private void HideAllStateObjects()
    {
        foreach (var mapping in stateMappings)
        {
            foreach (var obj in mapping.activeObjects)
            {
                if (obj != null)
                    obj.SetActive(false);
            }
        }
    }

    private void HideAllPaintObjects()
    {
        foreach (var set in colorPaintSets)
        {
            set.paint1?.SetActive(false);
            set.paint2?.SetActive(false);
            set.paint3?.SetActive(false);
        }
    }

    private HouseColor GetHouseColorFromIndex(int index)
    {
        int max = Enum.GetValues(typeof(HouseColor)).Length;
        return (HouseColor)(index % max);
    }
}
