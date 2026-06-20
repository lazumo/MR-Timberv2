using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Layer 1 — the "director". Server-authoritative.
/// Only does timing + phase switching; knows nothing about what each phase actually does.
/// Runs a coroutine that walks the pipeline:
///   Logging (20s) → Catching (20s) → Juicing (20s) → Firefighting (30s).
/// Tune all durations here in one place.
/// </summary>
public class GameFlowController : NetworkBehaviour
{
    [Header("Phase Managers (assign the 4 Layer-2 managers; pipeline runs in this order)")]
    [SerializeField] private LoggingPhase loggingPhase;
    [SerializeField] private CatchingPhase catchingPhase;
    [SerializeField] private JuicingPhase juicingPhase;
    [SerializeField] private FirefightingPhase firefightingPhase;

    [Header("Durations (seconds)")]
    [SerializeField] private float loggingDuration = 20f;
    [SerializeField] private float catchingDuration = 20f;
    [SerializeField] private float juicingDuration = 20f;
    [SerializeField] private float firefightingDuration = 30f;

    [Header("Options")]
    [SerializeField] private bool autoStartOnSpawn = true;
    [Tooltip("Delay before the pipeline starts, so every scene NetworkObject (treeSpawner, houseSpawner, ...) finishes spawning first.")]
    [SerializeField] private float startupDelay = 0.5f;

    /// So other systems (e.g. ToolController) can read the current phase.
    public static GameFlowController Instance { get; private set; }

    /// Current phase, synced to all clients (server writes).
    public NetworkVariable<GamePhase> CurrentPhase = new NetworkVariable<GamePhase>(
        GamePhase.Logging,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private IPhase[] _phases;
    private float[] _durations;
    private Coroutine _pipeline;
    private int _activeIndex = -1;

    private void Awake()
    {
        Instance = this;
        _phases    = new IPhase[] { loggingPhase, catchingPhase, juicingPhase, firefightingPhase };
        _durations = new float[]  { loggingDuration, catchingDuration, juicingDuration, firefightingDuration };
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer && autoStartOnSpawn)
            StartCoroutine(StartPipelineDelayed());
    }

    // Wait so all other scene NetworkObjects finish their OnNetworkSpawn (IsSpawned=true)
    // before phase managers start touching them.
    private IEnumerator StartPipelineDelayed()
    {
        if (startupDelay > 0f)
            yield return new WaitForSeconds(startupDelay);
        else
            yield return null; // at least one frame

        StartPipeline();
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
            StopPipelineInternal();
    }

    // =========================
    // Server pipeline control
    // =========================

    private void StartPipeline()
    {
        if (!IsServer) return;

        StopPipelineInternal();
        _pipeline = StartCoroutine(RunPipeline());
    }

    private void StopPipelineInternal()
    {
        if (_pipeline != null)
        {
            StopCoroutine(_pipeline);
            _pipeline = null;
        }

        // End whichever phase is currently active so it can clean up.
        if (_activeIndex >= 0 && _activeIndex < _phases.Length)
            _phases[_activeIndex]?.EndPhase();

        _activeIndex = -1;
    }

    private IEnumerator RunPipeline()
    {
        for (int i = 0; i < _phases.Length; i++)
        {
            // Cut the previous phase before starting the next.
            if (_activeIndex >= 0 && _activeIndex < _phases.Length)
                _phases[_activeIndex]?.EndPhase();

            _activeIndex = i;
            IPhase phase = _phases[i];

            if (phase != null)
            {
                CurrentPhase.Value = phase.Phase;
                Debug.Log($"[GameFlow] >>> START {phase.Phase} ({_durations[i]}s)");
                phase.StartPhase();
            }
            else
            {
                Debug.LogWarning($"[GameFlow] Phase index {i} is not assigned — skipping.");
            }

            yield return new WaitForSeconds(_durations[i]);
        }

        // Cut the final phase.
        if (_activeIndex >= 0 && _activeIndex < _phases.Length)
            _phases[_activeIndex]?.EndPhase();
        _activeIndex = -1;

        Debug.Log("[GameFlow] Pipeline finished.");
    }

    // =========================
    // Restart (one-button)
    // =========================

    /// Hook a UI Button's OnClick to this. Works whether pressed on host or client.
    public void Restart()
    {
        if (IsServer) StartPipeline();
        else          RestartServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    public void RestartServerRpc()
    {
        Debug.Log("[GameFlow] Restart requested.");
        StartPipeline();
    }
}
