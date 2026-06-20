using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Layer 2 — 滅火階段 (30s). Server-authoritative.
/// TODO (Step 5): fruits + houses disappear (only trees remain),
/// physical extinguisher Trigger drives virtual handle press animation.
/// </summary>
public class FirefightingPhase : NetworkBehaviour, IPhase
{
    public GamePhase Phase => GamePhase.Firefighting;

    public void StartPhase()
    {
        if (!IsServer) return;
        Debug.Log("[FirefightingPhase] StartPhase (TODO: clear fruits + houses, only trees remain)");
    }

    public void EndPhase()
    {
        if (!IsServer) return;
        Debug.Log("[FirefightingPhase] EndPhase (TODO: clean up)");
    }
}
