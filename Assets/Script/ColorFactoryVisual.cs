using Unity.Netcode;
using UnityEngine;
using System;
[System.Serializable]
public class ColorVariantBinding
{
    [Tooltip("Same value as ColorFactoryData.color")]
    public int color;

    public GameObject root;
    public Transform barB;
    public Transform barC;

    // ⭐ 新增：handlers
    public Transform barHandlerB;
    public Transform barHandlerC;
}



public class ColorFactoryVisual : NetworkBehaviour
{
    public ColorVariantBinding[] variants;

    private ColorFactoryData data;

    public Transform CurrentBarB { get; private set; }
    public Transform CurrentBarC { get; private set; }

    // ⭐ 已經有這兩個，但現在要真正賦值
    public Transform CurrentBarHandlerB { get; private set; }
    public Transform CurrentBarHandlerC { get; private set; }

    public event Action OnVisualReady;

    void Awake()
    {
        data = GetComponent<ColorFactoryData>();
    }

    public override void OnNetworkSpawn()
    {
        data.color.OnValueChanged += OnColorChanged;

        ApplyColor(data.color.Value);
    }

    private void OnColorChanged(int oldValue, int newValue)
    {
        ApplyColor(newValue);
    }

    private void ApplyColor(int color)
    {
        if (color < 0 || color >= variants.Length)
        {
            Debug.LogError($"[ColorFactoryVisual] Invalid color index {color}");
            return;
        }

        // 關掉全部
        for (int i = 0; i < variants.Length; i++)
            variants[i].root.SetActive(false);

        // 開正確的 variant
        var v = variants[color];
        v.root.SetActive(true);

        // ⭐ 設定 bar
        CurrentBarB = v.barB;
        CurrentBarC = v.barC;

        // ⭐ 新增：設定 handler
        CurrentBarHandlerB = v.barHandlerB;
        CurrentBarHandlerC = v.barHandlerC;

        Debug.Log($"[ColorFactoryVisual] Applied color {color} -> {v.root.name}");

        OnVisualReady?.Invoke();
    }

    public override void OnNetworkDespawn()
    {
        if (data != null)
            data.color.OnValueChanged -= OnColorChanged;
    }
}
