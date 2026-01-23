using UnityEngine;

public class ColorController : MonoBehaviour
{
    public int colorIndex;

    [Header("材質設定")]
    [SerializeField] private Material[] materialList; // 在 Inspector 中拖入各種顏色的 Material

    public MeshRenderer meshRenderer;

    private void Awake()
    {
        // 預先取得 Renderer 組件以增進效能

        if (meshRenderer == null)
        {
            // 如果腳本掛在父物件，但模型在子物件，請改用 GetComponentInChildren
            meshRenderer = GetComponentInChildren<MeshRenderer>();
        }
    }

    // 提供一個方法讓父物件 A 在 NetworkSpawn 時呼叫
    public void InitializeColor(int newIndex)
    {
        colorIndex = newIndex;

        if (materialList == null || materialList.Length == 0)
        {
            Debug.LogWarning("ColorController: materialList 為空，請在 Inspector 中設定材質。");
            return;
        }

        // 確保 Index 不會超出陣列範圍 (防止隨機數太大或太小)
        int safeIndex = Mathf.Clamp(colorIndex, 0, materialList.Length - 1);

        ApplyMaterial(safeIndex);

        Debug.Log($"子物件 B 已初始化，套用材質索引：{safeIndex}");
    }

    private void ApplyMaterial(int index)
    {
        if (meshRenderer != null)
        {
            meshRenderer.material = materialList[index];
        }
    }
}