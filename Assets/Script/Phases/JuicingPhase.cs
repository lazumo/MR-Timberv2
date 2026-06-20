using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Layer 2 — 榨汁上色階段 (20s). Server-authoritative.
/// Most of this phase is already handled by existing systems:
///  - Prop is locked to the (new) juicer by ToolController (phase-driven).
///  - The squeeze → spray VFX (FruitDestroyVFX) → house re-colour (ObjectNetworkSync.AdvancePaintStage)
///    chain lives on the color factory and runs whenever enough matching fruits are collected.
///  - Per-factory UI hints (arrow + hand) are shown by FactoryJuiceHint, which watches
///    GameFlowController.CurrentPhase itself — no action needed here.
/// This manager is intentionally light; it's the place to add juicing-only setup later.
/// </summary>
public class JuicingPhase : NetworkBehaviour, IPhase
{
    public GamePhase Phase => GamePhase.Juicing;

    public void StartPhase()
    {
        if (!IsServer) return;
        Debug.Log("[JuicingPhase] StartPhase — juicing active (factory hints shown, house coloring enabled).");
    }

    public void EndPhase()
    {
        if (!IsServer) return;
        Debug.Log("[JuicingPhase] EndPhase.");
    }
}
