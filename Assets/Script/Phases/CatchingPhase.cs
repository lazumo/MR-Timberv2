using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Layer 2 — 接果子階段 (20s). Server-authoritative.
/// Spawns 3 fruit trees (different colours) on the ceiling; the existing fruit-drop /
/// catch / color-factory chain is reused. Color factories already auto-spawn under each
/// house when it becomes Built (ObjectNetworkSync → HouseColorFactoryPlacer).
/// Prop is locked to the box by ToolController (phase-driven).
/// EndPhase keeps existing trees/fruits/factories — they're needed by the Juicing phase —
/// and only stops spawning new trees.
/// </summary>
public class CatchingPhase : NetworkBehaviour, IPhase
{
    [Header("Dependencies")]
    [Tooltip("Optional — falls back to TreeSpawnerNetworked.Instance if left empty.")]
    [SerializeField] private TreeSpawnerNetworked treeSpawner;

    [Header("Config")]
    [SerializeField] private int fruitTreeCount = 3;

    public GamePhase Phase => GamePhase.Catching;

    public void StartPhase()
    {
        if (!IsServer) return;

        if (treeSpawner == null)
            treeSpawner = TreeSpawnerNetworked.Instance;

        if (treeSpawner != null)
        {
            treeSpawner.BeginFruitSpawning(fruitTreeCount);
            Debug.Log($"[CatchingPhase] StartPhase — spawning {fruitTreeCount} fruit trees.");
        }
        else
        {
            Debug.LogWarning("[CatchingPhase] No TreeSpawnerNetworked found — no fruit trees.");
        }
    }

    public void EndPhase()
    {
        if (!IsServer) return;

        // Keep fruit trees / fruits / factories for the Juicing phase; just stop new trees.
        if (treeSpawner != null)
            treeSpawner.StopFruitSpawning(despawnExisting: false);

        Debug.Log("[CatchingPhase] EndPhase — stopped spawning new fruit trees.");
    }
}
