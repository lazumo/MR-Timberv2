using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Layer 2 — 接果子階段. Server-authoritative.
/// Demo reflow: spawns 2 fruit trees of DIFFERENT colours, one of which matches the house
/// colour (so the player can collect 3 matching fruits to juice it). Fruit trees appear
/// AFTER the house is built, matching the narrative (chop → house appears → fruit trees).
/// The existing fruit-drop / catch / color-factory chain is reused. Prop is the box
/// (phase-driven by ToolController). EndPhase keeps trees/fruits/factories for Juicing.
/// </summary>
public class CatchingPhase : NetworkBehaviour, IPhase
{
    [Header("Dependencies")]
    [Tooltip("Optional — falls back to TreeSpawnerNetworked.Instance if left empty.")]
    [SerializeField] private TreeSpawnerNetworked treeSpawner;

    [Header("Config")]
    [SerializeField] private int fruitTreeCount = 2;
    [Tooltip("Max seconds to wait for the house to appear before spawning fruit trees anyway.")]
    [SerializeField] private float houseBuiltTimeout = 12f;

    public GamePhase Phase => GamePhase.Catching;

    private Coroutine _spawnRoutine;

    public void StartPhase()
    {
        if (!IsServer) return;

        if (treeSpawner == null)
            treeSpawner = TreeSpawnerNetworked.Instance;

        if (treeSpawner == null)
        {
            Debug.LogWarning("[CatchingPhase] No TreeSpawnerNetworked found — no fruit trees.");
            return;
        }

        _spawnRoutine = StartCoroutine(SpawnFruitTreesWhenHouseBuilt());
    }

    public void EndPhase()
    {
        if (!IsServer) return;

        if (_spawnRoutine != null) { StopCoroutine(_spawnRoutine); _spawnRoutine = null; }

        // Keep fruit trees / fruits / factories for the Juicing phase; just stop new trees.
        if (treeSpawner != null)
            treeSpawner.StopFruitSpawning(despawnExisting: false);

        Debug.Log("[CatchingPhase] EndPhase — stopped spawning new fruit trees.");
    }

    private IEnumerator SpawnFruitTreesWhenHouseBuilt()
    {
        // Wait for the house to actually appear (Built) so fruit trees come *after* the house.
        // Fall back after a timeout so the game never stalls.
        float t = 0f;
        while (t < houseBuiltTimeout && !AnyHouseBuilt())
        {
            t += 0.2f;
            yield return new WaitForSeconds(0.2f);
        }

        treeSpawner.BeginFruitSpawning(fruitTreeCount);
        Debug.Log($"[CatchingPhase] StartPhase — spawning {fruitTreeCount} fruit trees (one = house colour).");
        _spawnRoutine = null;
    }

    private bool AnyHouseBuilt()
    {
        foreach (var h in FindObjectsByType<ObjectNetworkSync>(FindObjectsSortMode.None))
            if (h.CurrentState != HouseState.Unbuilt) return true;
        return false;
    }
}
