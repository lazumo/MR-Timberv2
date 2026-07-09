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
        // 每個 peer 各自畫 3x3 邊界細線（純本地視覺，零接線）
        if (showRoomBoundary)
            RoomBoundaryLine.Spawn(bannerHalfSize);

        // 每個 peer 各自跑合體提示（滅火階段、未合體時每 15s 提示）
        ExtinguisherMergeHint.Spawn(
            mergeHintInterval, mergeHintDuration, mergeHintAnchorOffset, mergeHintUiPrefab);

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
        // 每個 peer 都讀自己的輸入；動作經由 ServerRpc 路由到主機（host/client 按都有效）
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
        {
            if (IsServer) DebugAdvanceStage();
            else          DebugAdvanceServerRpc();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void DebugAdvanceServerRpc() => DebugAdvanceStage();

    /// 測試後門：每按一次 = 自動完成「當前階段的目標」，走正規事件鏈進入下一階段。
    /// Logging→蓋好房子 / Catching→視為集滿果子 / Juicing→房子上色完成(觸發完整失火過場)
    /// / Firefighting→撲滅所有火(勝利)。單人即可預覽全流程。
    public void DebugAdvanceStage()
    {
        if (!IsServer || !_started) return;

        Debug.Log($"[GameFlow][DEBUG] Advance pressed — phase={CurrentPhase.Value}, requiredGoals={RequiredGoals}");

        switch (CurrentPhase.Value)
        {
            case GamePhase.Logging:
                Debug.Log("[GameFlow][DEBUG] Simulate: all trees felled → build all houses.");
                _felledTrees = RequiredGoals;            // 不再補生下一棵樹
                if (TreeSpawnerNetworked.Instance != null)
                    TreeSpawnerNetworked.Instance.StopWoodLogging(despawnExisting: true);   // 視為砍倒

                foreach (var h in FindObjectsByType<ObjectNetworkSync>(FindObjectsSortMode.None))
                {
                    if (h.CurrentState == HouseState.Unbuilt)
                        h.SetState(HouseState.Built);   // 觸發蓋房VFX+factory+NotifyHouseBuilt→門控→Catching
                }

                // Debug 保險：就算房子數不足讓門控不放行，也強制進下一階段
                if (CurrentPhase.Value == GamePhase.Logging)
                    EnterPhase(GamePhase.Catching);
                break;

            case GamePhase.Catching:
                Debug.Log("[GameFlow][DEBUG] Simulate: all factories filled.");
                EnterPhase(GamePhase.Juicing);           // factory 沒真的裝滿，直接跳過門控
                break;

            case GamePhase.Juicing:
                Debug.Log("[GameFlow][DEBUG] Simulate: all houses coloured → fire bridge.");
                foreach (var h in FindObjectsByType<ObjectNetworkSync>(FindObjectsSortMode.None))
                {
                    if (h.CurrentState != HouseState.Unbuilt && h.CurrentState != HouseState.Colored)
                        h.SetState(HouseState.Colored);  // 最後一棟觸發 NotifyHouseColored → 失火過場
                }

                // Debug 保險：門控沒放行（例如房子數不足）就強制走失火過場
                if (CurrentPhase.Value == GamePhase.Juicing && !_bridging)
                {
                    Vector3 pos = SpawnArea.Instance != null ? SpawnArea.Instance.GetCenter() : transform.position;
                    StartCoroutine(FireBridgeRoutine(pos));
                }
                break;

            case GamePhase.Firefighting:
                Debug.Log("[GameFlow][DEBUG] Simulate: all fires extinguished.");
                foreach (var fire in FindObjectsByType<FireGrowServerOnly>(FindObjectsSortMode.None))
                {
                    var no = fire.GetComponent<Unity.Netcode.NetworkObject>();
                    if (no != null && no.IsSpawned) no.Despawn(true);
                }
                // FireSpawner 的迴圈偵測到 0 → 自動走勝利（亮回+音效+小精靈舞）
                break;
        }
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
        _felledTrees = 0;
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

    // ============================================================
    // Pipeline ×N 門控：目標數 = HouseSpawner 的房子數（老師版 = 2）
    // 砍 N 棵樹 → N 棟房 → N 個 factory 都集滿 → N 棟房都上色完 → 失火
    // ============================================================

    private int _felledTrees;

    /// 目標數 = 設定值與「實際生出來的房子數」取小——第二棟房找不到空間時流程不會卡死。
    private int RequiredGoals
    {
        get
        {
            var hs = HouseSpawnerNetworked.Instance;
            if (hs == null) return 1;
            int cfg = Mathf.Max(1, hs.numberOfHouses);
            int spawned = hs.GetAllHouseData().Count;
            return spawned > 0 ? Mathf.Min(cfg, spawned) : cfg;
        }
    }

    private int CountHouses(System.Func<ObjectNetworkSync, bool> pred)
    {
        int n = 0;
        foreach (var h in FindObjectsByType<ObjectNetworkSync>(FindObjectsSortMode.None))
            if (pred(h)) n++;
        return n;
    }

    /// A logging tree was felled. If more trees are still needed, grow the next one;
    /// advancing to Catching waits for NotifyHouseBuilt (elf must finish building).
    public void NotifyWoodFelled()
    {
        if (!IsServer || !_started) return;
        if (CurrentPhase.Value != GamePhase.Logging) return;

        _felledTrees++;
        Debug.Log($"[GameFlow] Wood felled ({_felledTrees}/{RequiredGoals}).");

        if (_felledTrees < RequiredGoals)
            StartCoroutine(SpawnNextTreeAfter(2f));   // 留點空間給倒下的樹
    }

    private IEnumerator SpawnNextTreeAfter(float delay)
    {
        yield return new WaitForSeconds(delay);

        // 倒下的樹幹/木材可能暫時佔住空間 → 失敗就每秒重試，直到長出來或離開砍樹階段
        for (int i = 0; i < 20; i++)
        {
            if (CurrentPhase.Value != GamePhase.Logging || TreeSpawnerNetworked.Instance == null)
                yield break;

            if (TreeSpawnerNetworked.Instance.SpawnTree(TreeSpawnerNetworked.TreeType.Wood))
            {
                Debug.Log("[GameFlow] Next wood tree spawned.");
                yield break;
            }

            Debug.LogWarning("[GameFlow] No space for the next wood tree — retrying in 1s...");
            yield return new WaitForSeconds(1f);
        }
    }

    /// A house finished building → advance when ALL required houses are built.
    public void NotifyHouseBuilt()
    {
        if (!IsServer || !_started) return;
        if (CurrentPhase.Value != GamePhase.Logging) return;

        int built = CountHouses(h => h.CurrentState != HouseState.Unbuilt);
        Debug.Log($"[GameFlow] House built ({built}/{RequiredGoals}).");

        if (built >= RequiredGoals)
            EnterPhase(GamePhase.Catching);
    }

    /// A color factory reached ≥3 matching fruits → advance when ALL factories are ready.
    public void NotifyFruitsReady()
    {
        if (!IsServer || !_started) return;
        if (CurrentPhase.Value != GamePhase.Catching) return;

        int ready = 0;
        foreach (var bar in FindObjectsByType<BarShowWhenEnoughMatchingFruits>(FindObjectsSortMode.None))
            if (bar.IsRequirementMet()) ready++;

        Debug.Log($"[GameFlow] Factory ready ({ready}/{RequiredGoals}).");

        if (ready >= RequiredGoals)
            EnterPhase(GamePhase.Juicing);
    }

    /// A house finished colouring → fire bridge only when ALL houses are fully coloured.
    public void NotifyHouseColored(Transform houseAnchor)
    {
        if (!IsServer || !_started) return;
        if (_bridging) return;
        if (CurrentPhase.Value != GamePhase.Juicing && CurrentPhase.Value != GamePhase.Catching) return;

        int colored = CountHouses(h => h.CurrentState == HouseState.Colored);
        Debug.Log($"[GameFlow] House coloured ({colored}/{RequiredGoals}).");
        if (colored < RequiredGoals) return;

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

        // 3) 冒煙 + 房子同步開始「燒成灰燼」——焦黑漸變橫跨整個預告期，燒完剛好起火。
        PlayTelegraphClientRpc(housePos);
        if (HouseSpawnerNetworked.Instance != null)
            HouseSpawnerNetworked.Instance.FadeOutAllHouses(telegraphSeconds);
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

    [Header("Warning banner (fire transition)")]
    [Tooltip("警戒布條顯示秒數（建議 ≥ celebrate 後剩餘的過場長度 + 火起初期）")]
    [SerializeField] private float bannerLifetime = 12f;
    [SerializeField] private float bannerHeight = 1.4f;
    [SerializeField] private float bannerHalfSize = 1.5f;   // 3x3 房間 → 1.5

    [Header("Room boundary line (3x3 範圍地板細線)")]
    [SerializeField] private bool showRoomBoundary = true;

    [Header("合體提示（滅火階段每 N 秒：anchor+光束指向隊友，近紅遠黃）")]
    [SerializeField] private float mergeHintInterval = 15f;
    [SerializeField] private float mergeHintDuration = 6f;
    [Tooltip("anchor 相對滅火器手的 offset（老師說位置之後可再調）")]
    [SerializeField] private Vector3 mergeHintAnchorOffset = new Vector3(0f, 0.18f, 0f);
    [Tooltip("組員的動畫 UI prefab（之後拖進來；留空只顯示 anchor 球+光束）")]
    [SerializeField] private GameObject mergeHintUiPrefab;

    // 一明一滅的閃爍（加速）→ 定暗，配警報聲 + 環繞房間的警戒布條
    [ClientRpc]
    private void FlickerDarkenClientRpc(Vector3 pos)
    {
        SfxLib.Play2D("OminousAlarm", 1f);   // 警報用 2D，全場聽得到
        StartCoroutine(FlickerDarkenLocal());

        // 警戒布條：以房間中心為準（SpawnArea = 虛擬 3x3 房中心）
        Vector3 center = SpawnArea.Instance != null ? SpawnArea.Instance.transform.position : pos;
        center.y = 0f;
        RoomWarningBanner.Spawn(center, bannerHalfSize, bannerHeight, 0.35f, bannerLifetime);
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

        // 圓心 = 當下玩家位置中心（兩人滅火器的平均位置），server 算好、廣播給所有 client
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

        PlayElfDanceClientRpc(center);
    }

    // ⭐ 舞蹈改成「每台 client 本地播」：只廣播一次起始點，之後各自 60fps 動畫
    //    （之前 server 每幀搬 networked 小精靈 → client 只有網路頻率的內插，看起來卡卡的）
    [ClientRpc]
    private void PlayElfDanceClientRpc(Vector3 center)
    {
        StopLocalElfDance();
        _localDance = StartCoroutine(LocalElfDance(center));
    }

    [ClientRpc]
    private void StopElfDanceClientRpc() => StopLocalElfDance();

    private Coroutine _localDance;
    private readonly System.Collections.Generic.List<GameObject> _localElves = new();

    private void StopLocalElfDance()
    {
        if (_localDance != null) { StopCoroutine(_localDance); _localDance = null; }
        foreach (var go in _localElves)
            if (go != null) Destroy(go);
        _localElves.Clear();
    }

    private IEnumerator LocalElfDance(Vector3 center)
    {
        var mgr = ResourceManager.Instance;
        if (mgr == null || mgr.resourcePrefab == null) yield break;

        for (int i = 0; i < danceElfCount; i++)
        {
            float a = i * Mathf.PI * 2f / danceElfCount;
            Vector3 pos = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * danceRadius;
            var go = Instantiate(mgr.resourcePrefab, pos, Quaternion.identity);

            // 拆掉網路元件 → 純本地視覺（沒 spawn 的 NetworkObject 會干擾，全部移除）
            foreach (var nb in go.GetComponentsInChildren<NetworkBehaviour>(true))
                Destroy(nb);
            var netObj = go.GetComponent<NetworkObject>();
            if (netObj != null) Destroy(netObj);

            _localElves.Add(go);
        }

        // 一直跳到 Restart（StopElfDanceClientRpc / StopLocalElfDance 清掉）
        float t = 0f;
        while (true)
        {
            t += Time.deltaTime;
            float orbit = t * danceOrbitSpeed * Mathf.Deg2Rad;

            for (int i = 0; i < _localElves.Count; i++)
            {
                var go = _localElves[i];
                if (go == null) continue;

                float a = orbit + i * Mathf.PI * 2f / danceElfCount;
                Vector3 pos = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * danceRadius;
                pos.y += Mathf.Sin(t * 6f + i) * 0.15f;                       // 上下彈跳

                Vector3 tangent = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
                Quaternion rot = Quaternion.LookRotation(tangent, Vector3.up)
                               * Quaternion.Euler(Mathf.Sin(t * 8f + i) * 15f, 0f, Mathf.Sin(t * 7f + i * 2f) * 12f); // 手舞足蹈搖擺

                go.transform.SetPositionAndRotation(pos, rot);
            }
            yield return null;
        }
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
        StopLocalElfDance();          // 本地舞者（host 自己）
        StopElfDanceClientRpc();      // 通知所有 client 清掉本地舞者
        if (TreeSpawnerNetworked.Instance != null)
        {
            TreeSpawnerNetworked.Instance.StopWoodLogging(despawnExisting: true);
            TreeSpawnerNetworked.Instance.ClearAllFruits(keepTrees: false);
        }

        if (HouseSpawnerNetworked.Instance != null)
            HouseSpawnerNetworked.Instance.RespawnHouses();
    }
}
