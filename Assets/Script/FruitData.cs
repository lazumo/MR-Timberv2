using Unity.Netcode;
using UnityEngine;

public class FruitData : NetworkBehaviour
{
    // 建議：給予初始值，雖然 int 預設是 0
    public NetworkVariable<int> colorIndex = new NetworkVariable<int>(0);

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // 1. 訂閱數值變更事件 (當 Server 改數值時，Client 會收到通知)
        colorIndex.OnValueChanged += OnColorChanged;

        // 2. 初始執行一次 (處理 Late Join 或者剛好數值就是 0 的情況)
        UpdateFruitVisuals(colorIndex.Value);
    }

    public override void OnNetworkDespawn()
    {
        // 記得取消訂閱，避免 Memory Leak
        colorIndex.OnValueChanged -= OnColorChanged;
        base.OnNetworkDespawn();
    }

    // 當數值改變時觸發
    private void OnColorChanged(int previousValue, int newValue)
    {
        UpdateFruitVisuals(newValue);
    }

    // 統一處理視覺更新邏輯
    private void UpdateFruitVisuals(int index)
    {
        var projector = GetComponent<FruitShadowProjector>();
        if (projector != null)
        {
            projector.InitializeFromFruit();
        }
    }
}