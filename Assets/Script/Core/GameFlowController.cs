using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Layer 1 — the "director". Server-authoritative.
/// EVENT-DRIVEN single loop (SIGGRAPH demo reflow, 2026-06-29):
///   Logging  —(player fells the one tree)→  Catching
///   Catching —(factory has ≥3 matching fruits)→  Juicing
///   Juicing  —(house fully coloured = goal)→  [fire bridge] → Firefighting
/// No phase timers anymore — each transition is triggered by a gameplay event.
/// The Juicing→Firefighting hop plays a short bridge (celebrate → smoke telegraph →
/// ignite the forest) so it doesn't hard-cut.
/// </summary>
public class GameFlowController : NetworkBehaviour
{
    [Header("Phase Managers (assign the 4 Layer-2 managers)")]
    [SerializeField] private LoggingPhase loggingPhase;
    [SerializeField] private CatchingPhase catchingPhase;
    [SerializeField] private JuicingPhase juicingPhase;
    [SerializeField] private FirefightingPhase firefightingPhase;

    [Header("Fire bridge (Juicing → Firefighting, so it isn't an abrupt cut)")]
    [Tooltip("Celebration beat after the house is coloured, before anything bad happens.")]
    [SerializeField] private float celebrateSeconds = 1.5f;
    [Tooltip("Smoke 'telegraph' beat warning the fire is about to start.")]
    [SerializeField] private float telegraphSeconds = 1.5f;
    [Tooltip("Sparkle/celebration VFX spawned at the finished house (plain particle prefab).")]
    [SerializeField] private GameObject celebrateVfxPrefab;
    [Tooltip("Rising-smoke VFX spawned as the fire warning (plain particle prefab).")]
    [SerializeField] private GameObject telegraphSmokePrefab;
    [Tooltip("Chime played when the house is finished.")]
    [SerializeField] private AudioClip celebrateSfx;
    [SerializeField] private AudioSource audioSource;
    [Tooltip("Seconds before auto-spawned bridge VFX are destroyed.")]
    [SerializeField] private float bridgeVfxLifetime = 4f;

    [Header("Options")]
    [SerializeField] private bool autoStartOnSpawn = true;
    [Tooltip("Delay before the game starts, so every scene NetworkObject finishes spawning first.")]
    [SerializeField] private float startupDelay = 0.5f;
    [Tooltip("Optional editor/testing shortcut to restart. Leave as None to disable.")]
    [SerializeField] private KeyCode debugRestartKey = KeyCode.None;

    [Header("Restart Input (physical controller)")]
    [SerializeField] private OVRInput.Button restartButton = OVRInput.Button.Two; // B / Y
    [SerializeField] private OVRInput.Controller restartController = OVRInput.Controller.Active;

    /// So other systems (ToolController, FactoryJuiceHint, PassthroughDarkener) can read the phase.
    public static GameFlowController Instance { get; private set; }

    /// Current phase, synced to all clients (server writes).
    public NetworkVariable<GamePhase> CurrentPhase = new NetworkVariable<GamePhase>(
        GamePhase.Logging,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private bool _started;     // game has begun
    private bool _bridging;    // fire bridge in progress (guards against double-trigger)

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer && autoStartOnSpawn)
            StartCoroutine(StartGameDelayed());
    }

    private IEnumerator StartGameDelayed()
    {
        if (startupDelay > 0f) yield return new WaitForSeconds(startupDelay);
        else                   yield return null;
        StartGame();
    }

    private void Update()
    {
        if (!IsServer) return;

        if (debugRestartKey != KeyCode.None && Input.GetKeyDown(debugRestartKey))
            Restart();

        if (restartButton != OVRInput.Button.None && OVRInput.GetDown(restartButton, restartController))
            Restart();
    }

    // =========================
    // Phase lookup
    // =========================

    private IPhase ManagerFor(GamePhase phase)
    {
        switch (phase)
        {
            case GamePhase.Logging:      return loggingPhase;
            case GamePhase.Catching:     return catchingPhase;
            case GamePhase.Juicing:      return juicingPhase;
            case GamePhase.Firefighting: return firefightingPhase;
            default:                     return null;
        }
    }

    // =========================
    // Server flow control
    // =========================

    private void StartGame()
    {
        if (!IsServer) return;

        _started = true;
        _bridging = false;
        EnterPhase(GamePhase.Logging);
    }

    // End the current phase, switch, start the next. (No timer — caller decides when.)
    private void EnterPhase(GamePhase next)
    {
        if (!IsServer) return;

        ManagerFor(CurrentPhase.Value)?.EndPhase();
        CurrentPhase.Value = next;
        Debug.Log($"[GameFlow] >>> ENTER {next}");
        ManagerFor(next)?.StartPhase();
    }

    // =========================
    // Gameplay event hooks (called by Layer-3 systems on the server)
    // =========================

    /// The one logging tree was felled → move to catching.
    public void NotifyWoodFelled()
    {
        if (!IsServer || !_started) return;
        if (CurrentPhase.Value != GamePhase.Logging) return;

        Debug.Log("[GameFlow] Wood felled → Catching.");
        EnterPhase(GamePhase.Catching);
    }

    /// A color factory now holds ≥3 matching fruits → move to juicing (box prop disappears).
    public void NotifyFruitsReady()
    {
        if (!IsServer || !_started) return;
        if (CurrentPhase.Value != GamePhase.Catching) return;

        Debug.Log("[GameFlow] Enough fruits → Juicing.");
        EnterPhase(GamePhase.Juicing);
    }

    /// The house finished colouring (goal). Play the bridge, then start firefighting.
    public void NotifyHouseColored(Transform houseAnchor)
    {
        if (!IsServer || !_started) return;
        if (_bridging) return;
        if (CurrentPhase.Value != GamePhase.Juicing && CurrentPhase.Value != GamePhase.Catching) return;

        StartCoroutine(FireBridgeRoutine(houseAnchor != null ? houseAnchor.position : transform.position));
    }

    private IEnumerator FireBridgeRoutine(Vector3 housePos)
    {
        _bridging = true;
        Debug.Log("[GameFlow] House coloured → fire bridge.");

        // 1) Celebration beat — reward the players for finishing the house.
        PlayCelebrateClientRpc(housePos);
        yield return new WaitForSeconds(celebrateSeconds);

        // 2) Telegraph beat — smoke rises as a warning before the fire.
        PlayTelegraphClientRpc(housePos);
        yield return new WaitForSeconds(telegraphSeconds);

        // 3) Enter firefighting: phase flip drives prop→extinguisher + passthrough darken.
        ManagerFor(CurrentPhase.Value)?.EndPhase();
        CurrentPhase.Value = GamePhase.Firefighting;

        // Light the forest on fire + stop fruit via the legacy stage-2 wiring
        // (FireSpawnerIgnitionPointsNetworked + FruitTree watch SceneController stage).
        if (SceneController.Instance != null)
            SceneController.Instance.CurrentLevel.Value = 2;

        // Fade out house + fruits, keep the trees standing among the fire.
        firefightingPhase?.StartPhase();

        Debug.Log("[GameFlow] Firefighting started (forest ignited).");
    }

    // =========================
    // Bridge VFX (shown on every client)
    // =========================

    [ClientRpc]
    private void PlayCelebrateClientRpc(Vector3 pos)
    {
        SpawnLocalVfx(celebrateVfxPrefab, pos);
        if (audioSource != null && celebrateSfx != null)
            audioSource.PlayOneShot(celebrateSfx);
    }

    [ClientRpc]
    private void PlayTelegraphClientRpc(Vector3 pos)
    {
        SpawnLocalVfx(telegraphSmokePrefab, pos);
    }

    private void SpawnLocalVfx(GameObject prefab, Vector3 pos)
    {
        if (prefab == null) return;
        var go = Instantiate(prefab, pos, Quaternion.identity);
        if (bridgeVfxLifetime > 0f) Destroy(go, bridgeVfxLifetime);
    }

    // =========================
    // Restart (one-button)
    // =========================

    public void Restart()
    {
        if (IsServer) RestartInternal();
        else          RestartServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    public void RestartServerRpc() => RestartInternal();

    private void RestartInternal()
    {
        if (!IsServer) return;

        Debug.Log("[GameFlow] Restart — resetting world, back to Logging.");
        StopAllCoroutines();
        _bridging = false;

        ResetWorld();
        if (SceneController.Instance != null)
            SceneController.Instance.CurrentLevel.Value = 1;

        StartGame();
    }

    // Clear leftovers so Logging starts fresh. Prop + passthrough reset via CurrentPhase.
    private void ResetWorld()
    {
        if (TreeSpawnerNetworked.Instance != null)
        {
            TreeSpawnerNetworked.Instance.StopWoodLogging(despawnExisting: true);
            TreeSpawnerNetworked.Instance.ClearAllFruits(keepTrees: false);
        }

        if (HouseSpawnerNetworked.Instance != null)
            HouseSpawnerNetworked.Instance.RespawnHouses();
    }
}
