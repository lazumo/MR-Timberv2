/// <summary>
/// Layer 2 — a single gameplay phase manager.
/// The GameFlowController (Layer 1) calls StartPhase()/EndPhase() on the server.
/// Each implementation owns its own scene transitions and object spawn/despawn.
/// </summary>
public interface IPhase
{
    /// Which phase this manager represents.
    GamePhase Phase { get; }

    /// Called by GameFlowController (server-only) when this phase begins.
    void StartPhase();

    /// Called by GameFlowController (server-only) when this phase ends.
    /// Clean up everything this phase spawned.
    void EndPhase();
}
