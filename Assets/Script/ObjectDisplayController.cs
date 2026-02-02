using UnityEngine;
using System;
using System.Collections.Generic;

// 定義房子狀態
public enum HouseState { Unbuilt, Built, Colored, Firing, Saved, Destroyed }

[Serializable]
public class StateConfig
{
    public HouseState state;
    public GameObject[] activeObjects; // 該狀態下要顯示的物件清單
}

public class ObjectDisplayController : MonoBehaviour
{
    [Header("State Settings")]
    [SerializeField] private List<StateConfig> stateMappings;

    [Header("Color Settings")]
    [SerializeField] private Renderer[] colorableRenderers; // 要變色的 Renderer
    [SerializeField] private Texture[] availableTextures;   // 0:紅, 1:藍, 2:綠 等貼圖

    // 根據 Enum 切換狀態
    public void ApplyState(HouseState newState, int colorIndex)
    {
        // 找出目前狀態對應的配置
        StateConfig config = stateMappings.Find(s => s.state == newState);

        // 先隱藏所有在 stateMappings 中出現過的物件（或乾脆隱藏所有相關子物件）
        foreach (var mapping in stateMappings)
        {
            foreach (var obj in mapping.activeObjects)
            {
                if (obj != null) obj.SetActive(false);
            }
        }

        // 啟動目前狀態指定的物件
        if (config != null)
        {
            if (config.state == HouseState.Colored) 
            {
                for (int i = 0; i < ColorTable.Count; i++)
                {
                    if (i == colorIndex && config.activeObjects[i] != null)
                    {
                        config.activeObjects[i].SetActive(true);
                    }
                }
                for (int i = ColorTable.Count; i < config.activeObjects.Length; i++)
                {
                    if (config.activeObjects[i] != null) config.activeObjects[i].SetActive(true);
                }
            }
            else
            {
                foreach (var obj in config.activeObjects)
                {
                    if (obj != null) obj.SetActive(true);
                }
            } 
        }
    }

    // 套用顏色邏輯
    public void ApplyColor(int colorIndex)
    {
        if (colorableRenderers == null || availableTextures == null || availableTextures.Length == 0) return;

        int safeIndex = colorIndex % availableTextures.Length;
        Texture targetTexture = availableTextures[safeIndex];

        foreach (var renderer in colorableRenderers)
        {
            if (renderer != null) renderer.material.mainTexture = targetTexture;
        }
    }
}