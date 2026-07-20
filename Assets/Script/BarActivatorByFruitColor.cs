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

    // 擠壓 UI 的全域門檻：要「所有」factory 都集滿，兩邊的 bars/handler 才一起亮
    // （單一 factory 集滿只默默記著，避免一邊先玩起來、另一邊還在接果子）。
    private static readonly List<BarShowWhenEnoughMatchingFruits> All = new();
    private bool _selfMet;   // server-only：這個 factory 自己滿了沒

    // 閂鎖：擠壓 UI 一旦全域亮起就不再縮回。一邊擠壓成功時果子被消耗/despawn，
    // 該 factory 的計數會瞬間掉回未滿，沒有閂鎖的話另一邊的手把會跟著被收掉。
    private static bool _revealed;
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

        if (IsServer && !All.Contains(this)) All.Add(this);

        shouldShowBars.OnValueChanged += OnShouldShowBarsChanged;

        ApplyBarsVisual(shouldShowBars.Value);
    }

    public override void OnNetworkDespawn()
    {
        All.Remove(this);
        if (All.Count == 0) _revealed = false;   // restart：全部收掉 → 閂鎖歸零
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
        _selfMet = met;
        UpdateBarsAllFactories();   // 全部 factory 都滿 → 兩邊的擠壓 UI 才一起亮

        // First time we have enough matching fruits → advance the flow (box prop disappears,
        // juice UI shows). Latched so it only fires once.
        if (met && !_notifiedReady)
        {
            _notifiedReady = true;
            if (GameFlowController.Instance != null)
                GameFlowController.Instance.NotifyFruitsReady();
        }
    }

    private static void UpdateBarsAllFactories()
    {
        bool allMet = All.Count > 0;
        foreach (var f in All)
            if (f == null || !f._selfMet) { allMet = false; break; }

        bool firstReveal = allMet && !_revealed;
        if (allMet) _revealed = true;
        bool show = _revealed;   // 亮過就維持亮（直到 restart 全部 despawn）

        foreach (var f in All)
            if (f != null && f.shouldShowBars.Value != show)
                f.shouldShowBars.Value = show;

        // 推進階段跟 UI 亮起綁同一瞬間。各 factory 的一次性閂鎖有順序漏洞：
        // A 滿→閂鎖用掉→果子掉了又補滿（不再通知）→ B 早已滿（閂鎖也用掉）
        // → UI 亮了但 NotifyFruitsReady 永遠沒人叫 → 卡在 Catching、box 收不掉。
        // 這裡在「全部集滿」的第一刻直接通知，跟閂鎖誰先誰後無關。
        if (firstReveal && GameFlowController.Instance != null)
        {
            Debug.Log("[BarShow] All factories full — revealing squeeze UI + notifying GameFlow.");
            GameFlowController.Instance.NotifyFruitsReady();
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

    /// 需要收集的果子數（擠壓端按這個數字等比推進房子上色進度）
    public int RequiredCount => requiredCount;
}
