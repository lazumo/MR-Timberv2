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
}
public class ColorFactoryVisual : NetworkBehaviour
{
    public ColorVariantBinding[] variants;

    private ColorFactoryData data;

    public Transform CurrentBarB { get; private set; }
    public Transform CurrentBarC { get; private set; }

    public event Action OnVisualReady;

    void Awake()
    {
        data = GetComponent<ColorFactoryData>();
    }

    public override void OnNetworkSpawn()
    {
        // ⭐ 關鍵：訂閱 color 變化
        data.color.OnValueChanged += OnColorChanged;

        // ⭐ 立即用「目前值」跑一次（server / late join 都安全）
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

        // 🔒 關掉全部（保險，避免多個 active）
        for (int i = 0; i < variants.Length; i++)
            variants[i].root.SetActive(false);

        // ⭐ 開正確的那個
        variants[color].root.SetActive(true);

        CurrentBarB = variants[color].barB;
        CurrentBarC = variants[color].barC;

        OnVisualReady?.Invoke();
    }

    public override void OnNetworkDespawn()
    {
        if (data != null)
            data.color.OnValueChanged -= OnColorChanged;
    }
}
