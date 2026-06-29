using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Layer 2 — 滅火階段 (30s). Server-authoritative.
/// Scene transition on start: clear all fruits + houses (and their color factories),
/// leaving the trees. Scene-darken is handled by PassthroughDarkener watching CurrentPhase.
/// TODO: physical extinguisher Trigger → virtual handle press animation.
/// </summary>
public class FirefightingPhase : NetworkBehaviour, IPhase
{
    [Header("Config")]
    [Tooltip("Keep the fruit trees standing (just remove their fruits). Off = remove trees too.")]
    [SerializeField] private bool keepFruitTrees = true;
    [Tooltip("Seconds for the house + factory to fade out (scale → 0) instead of popping away.")]
    [SerializeField] private float houseFadeDuration = 1.5f;

    public GamePhase Phase => GamePhase.Firefighting;

    public void StartPhase()
    {
        if (!IsServer) return;

        // 水果消失（停止掉落 + 清掉現有果子；保留果樹，讓火燒在森林裡）
        if (TreeSpawnerNetworked.Instance != null)
            TreeSpawnerNetworked.Instance.ClearAllFruits(keepTrees: keepFruitTrees);

        // 房子 + color factory 淡出（非瞬間消失）
        if (HouseSpawnerNetworked.Instance != null)
            HouseSpawnerNetworked.Instance.FadeOutAllHouses(houseFadeDuration);

        Debug.Log("[FirefightingPhase] StartPhase — faded houses, cleared fruits (forest fire lit via SceneController stage 2; darken via PassthroughDarkener).");
    }

    public void EndPhase()
    {
        if (!IsServer) return;
        Debug.Log("[FirefightingPhase] EndPhase.");
    }
}
