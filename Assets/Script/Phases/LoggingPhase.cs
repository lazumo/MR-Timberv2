using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Layer 2 — 砍樹階段 (20s). Server-authoritative.
/// Drives the existing wood-logging engine (TreeSpawnerNetworked): one conifer at a time,
/// chopping it respawns the next (via SliceObject). Wall house placeholders + the
/// chop → wood drop → elf carries → wooden house chain are all reused as-is.
/// EndPhase clears any remaining standing conifers (scene transition into Catching).
/// </summary>
public class LoggingPhase : NetworkBehaviour, IPhase
{
    [Header("Dependencies")]
    [Tooltip("Optional — falls back to TreeSpawnerNetworked.Instance if left empty.")]
    [SerializeField] private TreeSpawnerNetworked treeSpawner;

    public GamePhase Phase => GamePhase.Logging;

    public void StartPhase()
    {
        if (!IsServer) return;

        if (treeSpawner == null)
            treeSpawner = TreeSpawnerNetworked.Instance;

        if (treeSpawner != null)
        {
            treeSpawner.BeginWoodLogging();
            Debug.Log("[LoggingPhase] StartPhase — wood logging begun.");
        }
        else
        {
            Debug.LogWarning("[LoggingPhase] No TreeSpawnerNetworked found — nothing to log.");
        }
        // Prop lock (saw) is handled centrally by ToolController watching GameFlowController.CurrentPhase.
    }

    public void EndPhase()
    {
        if (!IsServer) return;

        if (treeSpawner != null)
            treeSpawner.StopWoodLogging(despawnExisting: true);

        Debug.Log("[LoggingPhase] EndPhase — conifers cleared.");
    }
}
