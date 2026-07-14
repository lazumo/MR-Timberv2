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

    private readonly HashSet<ulong> inside = new();
    public IReadOnlyCollection<ulong> InsideFruitIds => inside;

    // Latch so we only advance the game flow once (box disappears once).
    private bool _notifiedReady;
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

    private void ApplyBarsVisualFromState()
    {
        ApplyBarsVisual(shouldShowBars.Value);
    }

    private void ApplyBarsVisual(bool show)
    {
        if (visual == null) return;

        var b = visual.CurrentBarB;
        var c = visual.CurrentBarC;

        // ✅ 你新增的 handler
        var b_handler = visual.CurrentBarHandlerB;
        var c_handler = visual.CurrentBarHandlerC; // ✅ 修正：C handler

        if (b) b.gameObject.SetActive(show);
        if (c) c.gameObject.SetActive(show);

        // ✅ handler 也一起顯示/隱藏
        if (b_handler) b_handler.gameObject.SetActive(show);
        if (c_handler) c_handler.gameObject.SetActive(show);

        // ✅ 其他左右手把零件（免重新分組，動畫路徑不變）
        var parts = visual.CurrentHandleParts;
        if (parts != null)
            foreach (var p in parts)
                if (p != null && p.activeSelf != show)
                    p.SetActive(show);
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

        bool met = (match + consumedMatch.Value >= requiredCount);
        shouldShowBars.Value = met;

        // 集滿 → 通知 director 檢查「所有 factory 是否同時滿」。
        // 閂可重置：滿→不滿→再滿 會再次通知（一次性閂曾造成死鎖：
        // A 滿時 B 未滿、B 滿時 A 剛好掉到 2/3，之後 A 補滿卻再也不通知）。
        if (met && !_notifiedReady)
        {
            _notifiedReady = true;
            if (GameFlowController.Instance != null)
                GameFlowController.Instance.NotifyFruitsReady();
        }
        else if (!met)
        {
            _notifiedReady = false;
        }
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

    public bool IsRequirementMet()
    {
        return shouldShowBars.Value;
    }
}
