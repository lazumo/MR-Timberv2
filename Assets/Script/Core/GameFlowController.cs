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
    [Tooltip("Jingle played when the LAST fire is put out (firefighting victory).")]
    [SerializeField] private AudioClip victorySfx;
    [SerializeField] private AudioSource audioSource;
    [Tooltip("Seconds before auto-spawned bridge VFX are destroyed.")]
    [SerializeField] private float bridgeVfxLifetime = 4f;

    [Header("Options")]
    [SerializeField] private bool autoStartOnSpawn = true;
    [Tooltip("Delay before the game starts, so every scene NetworkObject finishes spawning first.")]
    [SerializeField] private float startupDelay = 0.5f;
    [Tooltip("Optional editor/testing shortcut to restart. Leave as None to disable.")]
    [SerializeField] private KeyCode debugRestartKey = KeyCode.None;

    [Header("Debug 後門（測試用；demo 前設為 None 關閉）")]
    [Tooltip("按此鍵直接跳到滅火階段（Editor/Simulator 用）。")]
    [SerializeField] private KeyCode debugFireKey = KeyCode.F;
    [Tooltip("實體 controller 鈕跳到滅火階段（build 上測試用；None=關閉）。預設=右手搖桿按下（同舊後門）。")]
    [SerializeField] private OVRInput.Button debugFireButton = OVRInput.Button.SecondaryThumbstick;

    [Header("Victory elf dance (all fires out)")]
    [SerializeField] private int danceElfCount = 10;
    [SerializeField] private float danceRadius = 1f;
    [SerializeField] private float danceOrbitSpeed = 60f;   // deg/s around the circle (dance lasts until restart)
    [Tooltip("Optional dance-circle center; falls back to SpawnArea, then world origin.")]
    [SerializeField] private Transform danceCenter;

    private readonly System.Collections.Generic.List<NetworkObject> _danceElves = new();

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

    /// True after the last fire is put out (drives victory BGM); reset on restart.
    public NetworkVariable<bool> VictoryReached = new NetworkVariable<bool>(
        false,
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

        // Debug 後門：直接跳滅火（F 鍵 / 任一搖桿按下 / 舊式 JoystickButton8,9）
        bool stickPressed =
            (debugFireButton != OVRInput.Button.None && OVRInput.GetDown(debugFireButton)) ||
            OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick) ||
            Input.GetKeyDown(KeyCode.JoystickButton8) ||
            Input.GetKeyDown(KeyCode.JoystickButton9);

        if ((debugFireKey != KeyCode.None && Input.GetKeyDown(debugFireKey)) || stickPressed)
            DebugJumpToFirefighting();
    }

    /// 測試用：略過前面流程，直接進滅火（點火 + 換 prop + 變暗都會跟著發生）
    public void DebugJumpToFirefighting()
    {
        if (!IsServer || !_started) return;
        if (CurrentPhase.Value == GamePhase.Firefighting) return;

        Debug.Log("[GameFlow][DEBUG] Jump to Firefighting.");
        _bridging = false;

        ManagerFor(CurrentPhase.Value)?.EndPhase();
        CurrentPhase.Value = GamePhase.Firefighting;

        if (SceneController.Instance != null)
            SceneController.Instance.CurrentLevel.Value = 2;   // 點燃森林火

        firefightingPhase?.StartPhase();
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
        VictoryReached.Value = false;
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

    /// The one logging tree was felled. We do NOT advance yet — the elf still has to carry
    /// the wood over and build the house (takes a few seconds). We wait for NotifyHouseBuilt
    /// so Catching never starts before the house actually exists.
    public void NotifyWoodFelled()
    {
        if (!IsServer || !_started) return;
        if (CurrentPhase.Value != GamePhase.Logging) return;

        Debug.Log("[GameFlow] Wood felled — waiting for the elf to build the house before Catching.");
    }

    /// The elf finished delivering the wood and the house is built → move to Catching.
    public void NotifyHouseBuilt()
    {
        if (!IsServer || !_started) return;
        if (CurrentPhase.Value != GamePhase.Logging) return;

        Debug.Log("[GameFlow] House built → Catching.");
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

        // 1) Celebration beat — reward the players for finishing the house (過關音效).
        PlayCelebrateClientRpc(housePos);
        yield return new WaitForSeconds(celebrateSeconds);

        // 2) 一明一滅閃爍 + 不祥音效，然後暗下來…
        FlickerDarkenClientRpc(housePos);
        yield return new WaitForSeconds(1.2f);   // flicker length

        // 3) …才開始冒煙，預告失火。
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

    [ClientRpc]
    private void DarkenClientRpc()
    {
        if (PassthroughDarkener.Instance != null)
            PassthroughDarkener.Instance.Apply(true);
    }

    // 一明一滅的閃爍（加速）→ 定暗，配「有事情要發生」的音效
    [ClientRpc]
    private void FlickerDarkenClientRpc(Vector3 pos)
    {
        SfxLib.PlayAt("OminousAlarm", pos, 1f);
        StartCoroutine(FlickerDarkenLocal());
    }

    private IEnumerator FlickerDarkenLocal()
    {
        var pd = PassthroughDarkener.Instance;
        if (pd == null) yield break;

        // 暗-亮-暗-亮-暗，間隔越來越短
        float[] beats = { 0.25f, 0.2f, 0.18f, 0.14f, 0.1f };
        bool dark = true;
        foreach (var b in beats)
        {
            pd.Apply(dark, instant: true);
            dark = !dark;
            yield return new WaitForSeconds(b);
        }

        pd.Apply(true);   // 最後淡入定暗
    }

    /// Called by FireSpawnerIgnitionPointsNetworked when the last fire goes out.
    public void NotifyFiresExtinguished()
    {
        if (!IsServer || !_started) return;
        if (CurrentPhase.Value != GamePhase.Firefighting) return;

        Debug.Log("[GameFlow] All fires extinguished — victory!");
        VictoryReached.Value = true;   // PhaseBGM 切成勝利背景音樂
        PlayVictoryClientRpc();
        StartCoroutine(VictoryElfDance());
    }

    // 10 elves circle the room center (radius 1 m), bobbing and swaying, then vanish.
    private IEnumerator VictoryElfDance()
    {
        var mgr = ResourceManager.Instance;
        if (mgr == null || mgr.resourcePrefab == null) yield break;

        // 圓心 = 當下玩家位置中心（用兩人手上的滅火器平均位置），退而求其次才用固定點
        Vector3 center;
        var exts = FindObjectsByType<NetworkExtinguisherController>(FindObjectsSortMode.None);
        if (exts.Length > 0)
        {
            Vector3 sum = Vector3.zero;
            foreach (var e in exts) sum += e.transform.position;
            center = sum / exts.Length;
        }
        else
        {
            center = danceCenter != null ? danceCenter.position
                   : SpawnArea.Instance != null ? SpawnArea.Instance.transform.position
                   : Vector3.zero;
            center.y += 1.0f;
        }

        DespawnDanceElves();
        for (int i = 0; i < danceElfCount; i++)
        {
            float a = i * Mathf.PI * 2f / danceElfCount;
            Vector3 pos = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * danceRadius;
            var go = Instantiate(mgr.resourcePrefab, pos, Quaternion.identity);
            var no = go.GetComponent<NetworkObject>();
            if (no == null) { Destroy(go); yield break; }
            no.Spawn(true);
            _danceElves.Add(no);
        }

        // 一直跳到 Restart 為止（ResetWorld 會清掉舞者）
        float t = 0f;
        while (true)
        {
            t += Time.deltaTime;
            float orbit = t * danceOrbitSpeed * Mathf.Deg2Rad;

            for (int i = 0; i < _danceElves.Count; i++)
            {
                var no = _danceElves[i];
                if (no == null) continue;

                float a = orbit + i * Mathf.PI * 2f / danceElfCount;
                Vector3 pos = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * danceRadius;
                pos.y += Mathf.Sin(t * 6f + i) * 0.15f;                       // 上下彈跳

                Vector3 tangent = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
                Quaternion rot = Quaternion.LookRotation(tangent, Vector3.up)
                               * Quaternion.Euler(Mathf.Sin(t * 8f + i) * 15f, 0f, Mathf.Sin(t * 7f + i * 2f) * 12f); // 手舞足蹈搖擺

                no.transform.SetPositionAndRotation(pos, rot);
            }
            yield return null;
        }
        // unreachable — dancers are cleared by ResetWorld (StopAllCoroutines + DespawnDanceElves)
    }

    private void DespawnDanceElves()
    {
        foreach (var no in _danceElves)
            if (no != null && no.IsSpawned) no.Despawn(true);
        _danceElves.Clear();
    }

    [ClientRpc]
    private void PlayVictoryClientRpc()
    {
        if (audioSource != null && victorySfx != null)
            audioSource.PlayOneShot(victorySfx);

        // 過關特效：在玩家面前 1.5m 播慶祝粒子（若有指定）
        var cam = Camera.main;
        if (cam != null)
            SpawnLocalVfx(celebrateVfxPrefab, cam.transform.position + cam.transform.forward * 1.5f);
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
        DespawnDanceElves();   // victory dancers, if any
        if (TreeSpawnerNetworked.Instance != null)
        {
            TreeSpawnerNetworked.Instance.StopWoodLogging(despawnExisting: true);
            TreeSpawnerNetworked.Instance.ClearAllFruits(keepTrees: false);
        }

        if (HouseSpawnerNetworked.Instance != null)
            HouseSpawnerNetworked.Instance.RespawnHouses();
    }
}
