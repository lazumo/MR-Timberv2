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

    public GamePhase Phase => GamePhase.Firefighting;

    public void StartPhase()
    {
        if (!IsServer) return;

        // 水果消失（停止掉落 + 清掉現有果子；保留果樹）
        if (TreeSpawnerNetworked.Instance != null)
            TreeSpawnerNetworked.Instance.ClearAllFruits(keepTrees: keepFruitTrees);

        // 房子 + color factory 消失
        if (HouseSpawnerNetworked.Instance != null)
            HouseSpawnerNetworked.Instance.DespawnAllHouses();

        Debug.Log("[FirefightingPhase] StartPhase — cleared fruits + houses (scene-darken via PassthroughDarkener).");
    }

    public void EndPhase()
    {
        if (!IsServer) return;
        Debug.Log("[FirefightingPhase] EndPhase.");
    }
}
