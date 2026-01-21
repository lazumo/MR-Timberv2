using Unity.Netcode;
using UnityEngine;

public class ColorInitializer : NetworkBehaviour // 必須繼承 NetworkBehaviour
{
    [SerializeField] private Material[] colorMaterials;

    // 使用 OnNetworkSpawn 確保網路物件已完全在場景中就緒
    public override void OnNetworkSpawn()
    {
        ObjectNetworkSync rootSync = GetComponentInParent<ObjectNetworkSync>();

        if (rootSync != null)
        {
            int index = rootSync.GetColorValue(); // 直接讀取 NetworkVariable
            ApplyMaterial(index);
            Debug.Log($"[成功] 在 OnNetworkSpawn 抓到父物件，ColorIndex 為: {index}");
        }
        else
        {
            // 如果還是抓不到，印出完整路徑來除錯
            Debug.LogError($"[失敗] 依然找不到 ObjectNetworkSync。物件目前路徑為: {GetPath(transform)}");
        }
    }

    private string GetPath(Transform t)
    {
        return t.parent == null ? t.name : GetPath(t.parent) + "/" + t.name;
    }

    private void ApplyMaterial(int index)
    {
        MeshRenderer renderer = GetComponent<MeshRenderer>();
        if (renderer != null && index >= 0 && index < colorMaterials.Length)
        {
            renderer.material = colorMaterials[index];
        }
    }
}