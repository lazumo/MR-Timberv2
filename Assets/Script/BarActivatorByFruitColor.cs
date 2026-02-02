using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class BarShowWhenEnoughMatchingFruits : NetworkBehaviour
{
    [Header("Refs")]
    [SerializeField] private ColorFactoryData factoryData;
    [SerializeField] private ColorFactoryVisual visual;

    [Header("Rule")]
    [SerializeField] private int requiredCount = 3;

    // ✅ 同步：是否應該顯示 bar（所有 client 一致）
    private NetworkVariable<bool> shouldShowBars =
        new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );
    private NetworkVariable<int> consumedMatch =
    new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Server：記錄 trigger 內有哪些水果（用 NetworkObjectId 比 Transform 穩）
    private readonly HashSet<ulong> inside = new();

    private void OnEnable()
    {
        if (visual != null)
            visual.OnVisualReady += ApplyBarsVisualFromState;
    }

    private void OnDisable()
    {
        if (visual != null)
            visual.OnVisualReady -= ApplyBarsVisualFromState;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        shouldShowBars.OnValueChanged += OnShouldShowBarsChanged;

        // 初始套用一次（late join / 剛 spawn）
        ApplyBarsVisual(shouldShowBars.Value);
    }

    public override void OnNetworkDespawn()
    {
        shouldShowBars.OnValueChanged -= OnShouldShowBarsChanged;
        base.OnNetworkDespawn();
    }

    private void OnShouldShowBarsChanged(bool prev, bool next)
    {
        ApplyBarsVisual(next);
    }

    // OnVisualReady 會叫到這裡：確保 bar 換 variant 也能套用正確顯示狀態
    private void ApplyBarsVisualFromState()
    {
        ApplyBarsVisual(shouldShowBars.Value);
    }

    private void ApplyBarsVisual(bool show)
    {
        if (visual == null) return;

        var b = visual.CurrentBarB;
        var c = visual.CurrentBarC;

        // 有可能 root 尚未 active 或剛切換，做 null 保護
        if (b) b.gameObject.SetActive(show);
        if (c) c.gameObject.SetActive(show);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;

        var netObj = other.GetComponentInParent<NetworkObject>();
        if (netObj == null || !netObj.IsSpawned) return;

        var fruit = netObj.GetComponent<FruitData>();
        if (fruit == null) return;

        inside.Add(netObj.NetworkObjectId);
        RecountAndUpdate();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsServer) return;

        var netObj = other.GetComponentInParent<NetworkObject>();
        if (netObj == null) return;

        if (inside.Remove(netObj.NetworkObjectId))
            RecountAndUpdate();
    }

    private void RecountAndUpdate()
    {
        if (!IsServer) return;
        if (factoryData == null) return;

        int targetColor = factoryData.color.Value;

        int match = 0;
        var nm = NetworkManager.Singleton;

        foreach (var id in inside)
        {
            if (!nm.SpawnManager.SpawnedObjects.TryGetValue(id, out var obj)) continue;

            var fruit = obj.GetComponent<FruitData>();
            if (fruit == null) continue;

            if (fruit.colorIndex.Value == targetColor)
                match++;
        }

        shouldShowBars.Value = (match + consumedMatch.Value >= requiredCount);
    }
    public void NotifyFruitConsumed(int fruitColorIndex)
    {
        if (!IsServer) return;
        if (factoryData == null) return;

        int targetColor = factoryData.color.Value;
        if (fruitColorIndex == targetColor)
        {
            consumedMatch.Value += 1;
            RecountAndUpdate();
        }
    }
}
