using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 世界座標系 ↔ colocation alignment anchor 的「聰明對錶」。host 與 guest 都用這一套
/// （class 名沿用 Host 舊名以免改動呼叫端；實際上兩端都跑）。
///
/// 歷史（為什麼是現在這個設計，之後別再改回任一極端）：
///  - v1 (7/14, 968fb88)：host 每幀對齊 → anchor 估計噪音帶著整個世界連續滑動，
///    實測樹葉範圍一直漂。
///  - v2 (7/20, e69a9d2)：改成開場對齊一次 → 噪音沒了，但 relocalization／recenter／
///    休眠喚醒的「真實修正」永遠不被吸收；黑幕展場實測累積 30cm+，玩家被誤判出房間
///    （灰畫面）。
///  - v3（本版）：deadband 濾波。平常誤差 &lt; 3cm/1.5° 完全不動（吃掉 v1 的噪音）；
///    誤差連續超標 0.5 秒（= anchor 真的跳了）才在 ~1 秒內平滑跟上（吸收 v2 吃不掉
///    的大跳）。另外：左手 Start 鍵長按 1 秒手動校正、recenter／tracking 恢復／HMD
///    重戴立即校正、遊戲階段切換順手校正。
///
/// Guest 端：Meta colocation building block 的 AlignCameraToAnchor 是「每幀貼死」版
/// （噪音照吃），與本策略衝突 → 找到就 disable、由本元件接管（同一套數學，行為相容）。
/// 對齊數學照抄 Meta 的 AlignCameraToAnchor（該 class 是 internal，無法直接使用）。
/// 零接線：GameFlowController.OnNetworkSpawn 呼叫 Ensure() 動態生成（host/guest 都會）。
/// </summary>
[DefaultExecutionOrder(10)]
public class ColocationHostAlignment : MonoBehaviour
{
    // ── 濾波參數（展場實測後可調）──────────────────────────────
    private const float PosDeadband = 0.03f;      // 位置誤差 < 3cm 視為噪音 → 不動
    private const float YawDeadbandDeg = 1.5f;    // 角度誤差 < 1.5° 視為噪音
    private const float PersistSeconds = 0.5f;    // 誤差連續超標這麼久才判定「真跳」
    private const float CorrectHalfLife = 0.25f;  // 平滑修正半衰期（約 1 秒收斂九成五）
    private const float CorrectTimeout = 5f;      // 修正最多追這麼久（anchor 太抖就先放手）
    private const float SettlePosEps = 0.005f;    // 收斂到 5mm / 0.2° 內就算完成
    private const float SettleYawEps = 0.2f;
    private const float CooldownSeconds = 3f;     // 兩次自動校正之間的冷卻
    private const float ManualHoldSeconds = 1f;   // 左手 Start 長按秒數

    /// 本機是否已完成「開場第一次對齊」（RoomCenterSetup / loader 等這個才讀中心圖釘，
    /// 避免把房間蓋在還沒對齊的座標系上）
    public static bool AlignedOnce { get; private set; }

    private OVRSpatialAnchor _anchor;
    private Transform _rig;
    private float _nextFind;
    private bool _alignedOnce;        // 開場第一次對齊（立即 snap，不平滑）

    private float _errorSince = -1f;  // 誤差開始超標的時刻；-1 = 目前在 deadband 內
    private bool _correcting;
    private float _correctStarted;
    private float _cooldownUntil;

    private float _startHeld;         // 左手 Start 鍵已按住秒數
    private bool _manualFired;        // 這次按住已觸發過（放開才重新武裝）

    private float _nextMetaScan;      // guest：定期掃描並關閉 Meta 的每幀對齊元件
    private bool _ovrHooked;          // recenter / tracking 事件已訂閱
    private GameFlowController _flowHooked;   // 已訂閱階段變化的 GameFlowController

    // ── 近距離漂移基準（Meta 3 公尺規則）────────────────────────
    // colocation 圖釘 = host 開機位置，可能離遊戲區超過 3 公尺 → 圖釘本身開始漂，
    // client 靠分享資料認它更不穩。改成：每台在「房間中心」建立/選用一支近距離基準
    // 圖釘（3x3 內永遠 ≤2.1m），漂移量測與校正都對著它；colocation 圖釘只當 fallback。
    private OVRSpatialAnchor _refAnchor;      // 房間中心基準（host=中心圖釘；client=自建）
    private Vector3 _refExpectedPos;          // 基準「應該在」的世界位置（host 廣播 pose）
    private float _refExpectedYaw;
    private bool _refIsLocallyCreated;        // client 自建的 → re-place 時銷毀重建
    private float _nextRefTry;
    private float _refCreatedAt;              // 自建圖釘的建立時刻（15 秒沒 Created 就重建）
    private int _timeoutStreak;               // 連續修正收斂失敗次數（遠圖釘太晃的訊號）

    /// 零接線生成（GameFlowController.OnNetworkSpawn 呼叫；host/guest 都需要）
    public static void Ensure()
    {
        if (FindAnyObjectByType<ColocationHostAlignment>() != null) return;
        new GameObject("ColocationHostAlignment").AddComponent<ColocationHostAlignment>();
    }

    private void OnDestroy()
    {
        if (_ovrHooked)
        {
            if (OVRManager.display != null) OVRManager.display.RecenteredPose -= OnPoseEvent;
            OVRManager.TrackingAcquired -= OnPoseEvent;
            OVRManager.HMDMounted -= OnPoseEvent;
        }
    }

    private void Update()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;                       // editor 單機：沒 colocation，閒置

        ReadManualButton();   // 按鍵在圖釘出現前也要有回饋（震動+log），方便展場排錯

        if (_anchor == null || _rig == null)
        {
            if (Time.time < _nextFind) return;
            _nextFind = Time.time + 1f;

            if (_anchor == null) _anchor = FindColocationAnchor();
            if (_rig == null)
            {
                var rig = FindAnyObjectByType<OVRCameraRig>();
                if (rig != null) _rig = rig.transform;
            }
            if (_anchor == null || _rig == null) return;
        }

        HookEventsOnce(nm);
        if (!nm.IsHost) DisableMetaAlignerPeriodically();

        if (!_anchor.Created || Camera.main == null) return;

        // ── 開場第一次：立即對齊（世界座標系 = anchor 座標系）─────────
        if (!_alignedOnce)
        {
            ComputeTargetRigPose(out var pos, out float yaw);
            ApplyRigPose(pos, yaw);
            _alignedOnce = true;
            AlignedOnce = true;
            Debug.Log($"[ColocationAlignment] Initial align to colocation anchor ({(nm.IsHost ? "host" : "guest")}).");
            return;
        }

        // ── 進行中的平滑修正 ─────────────────────────────────────
        if (_correcting)
        {
            StepCorrection();
            return;
        }

        TryEstablishRefAnchor(nm);

        // ── 監看誤差（anchor 對齊後世界姿態應恆為原點/yaw0，偏差即誤差）──
        MeasureError(out float posErr, out float yawErr);
        bool beyond = posErr > PosDeadband || yawErr > YawDeadbandDeg;

        if (!beyond)
        {
            _errorSince = -1f;
            return;
        }

        if (_errorSince < 0f) _errorSince = Time.time;

        if (Time.time - _errorSince >= PersistSeconds && Time.time >= _cooldownUntil)
            BeginCorrection($"auto (offset {posErr:F3}m, yaw {yawErr:F1}°)");
    }

    // ═════════════════════ 修正流程 ═════════════════════

    private void BeginCorrection(string reason)
    {
        if (!_alignedOnce || _correcting) return;
        _correcting = true;
        _correctStarted = Time.time;
        _errorSince = -1f;
        Debug.LogWarning($"[ColocationAlignment] Correcting world → anchor, trigger = {reason}");
    }

    private void StepCorrection()
    {
        if (!_anchor.Created || Camera.main == null) { _correcting = false; return; }

        ComputeCorrectionTarget(out var targetPos, out float targetYaw);

        // 指數趨近：每秒收掉大部分誤差，視覺上是平滑滑動而非瞬間跳
        float k = 1f - Mathf.Pow(2f, -Time.deltaTime / CorrectHalfLife);
        Vector3 newPos = Vector3.Lerp(_rig.position, targetPos, k);
        float newYaw = Mathf.LerpAngle(_rig.eulerAngles.y, targetYaw, k);
        ApplyRigPose(newPos, newYaw);

        MeasureError(out float posErr, out float yawErr);
        bool settled = posErr < SettlePosEps && yawErr < SettleYawEps;
        bool timedOut = Time.time - _correctStarted > CorrectTimeout;

        if (settled || timedOut)
        {
            _correcting = false;
            _cooldownUntil = Time.time + CooldownSeconds;

            if (settled) { _timeoutStreak = 0; _nextRefTry = 0f; }   // 收斂瞬間是建立量尺的最佳時機
            else _timeoutStreak++;

            Debug.Log($"[ColocationAlignment] Correction {(settled ? "settled" : "timed out")} " +
                      $"after {Time.time - _correctStarted:F1}s (residual {posErr:F3}m, {yawErr:F1}°, timeoutStreak={_timeoutStreak}).");
        }
    }

    /// 只認 colocation 圖釘 — 排除手動擺放的「中心圖釘」（RoomCenterAnchorTag）。
    /// 對齊到中心圖釘會把世界軸搬去它的軸 → 跟 guest（對 colocation 圖釘）分家。
    private static OVRSpatialAnchor FindColocationAnchor()
    {
        foreach (var a in FindObjectsByType<OVRSpatialAnchor>(FindObjectsSortMode.None))
            if (a != null && a.GetComponent<RoomCenterAnchorTag>() == null)
                return a;
        return null;
    }

    /// 漂移量測：優先用「房間中心基準圖釘」（近、準），沒有才退回 colocation 圖釘。
    private void MeasureError(out float posErr, out float yawErr)
    {
        if (_refAnchor != null && _refAnchor.Created)
        {
            posErr = (_refAnchor.transform.position - _refExpectedPos).magnitude;
            yawErr = Mathf.Abs(Mathf.DeltaAngle(_refAnchor.transform.eulerAngles.y, _refExpectedYaw));
            return;
        }
        MeasureColocationError(out posErr, out yawErr);
    }

    /// colocation 圖釘偏離「原點/yaw0」多少（對齊完成時應趨近 0）
    private void MeasureColocationError(out float posErr, out float yawErr)
    {
        posErr = _anchor.transform.position.magnitude;
        yawErr = Mathf.Abs(Mathf.DeltaAngle(_anchor.transform.eulerAngles.y, 0f));
    }

    /// 建立近距離基準：host 用 RoomCenterSetup 擺的中心圖釘；client 在房間中心自建一支
    /// 本地圖釘（不分享、不儲存）。只在「世界目前貼緊 colocation 圖釘」時建立，
    /// 基準才會落在正確的實體位置。
    private void TryEstablishRefAnchor(NetworkManager nm)
    {
        if (_refAnchor != null)
        {
            // 自建圖釘 15 秒還沒 Created（追蹤太差）→ 砍掉重建，避免卡在半殘狀態
            if (!_refAnchor.Created && _refIsLocallyCreated && Time.time - _refCreatedAt > 15f)
            {
                Destroy(_refAnchor.gameObject);
                _refAnchor = null;
                Debug.LogWarning("[ColocationAlignment] Drift reference anchor never localized — recreating.");
            }
            return;
        }
        if (Time.time < _nextRefTry) return;
        _nextRefTry = Time.time + 2f;

        var gf = GameFlowController.Instance;
        if (gf == null || !gf.RoomPoseReady.Value) return;

        MeasureColocationError(out float pe, out float ye);
        if (pe > PosDeadband || ye > YawDeadbandDeg)
        {
            // 理想上要等世界貼緊 colocation 圖釘才建立量尺。但遠圖釘若一直晃（連續
            // 兩次修正都收斂失敗），這一刻永遠不會來，系統會追著晃的圖釘讓世界游動。
            // → 放棄等待，直接以當下姿態建立近距離量尺（可能帶進幾公分誤差，但從此穩定；
            //   誤差可用左手 Start 長按手動修，或下次階段轉場自動收）。
            if (_timeoutStreak < 2) return;
            Debug.LogWarning($"[ColocationAlignment] Establishing drift reference despite unsettled colocation anchor (residual {pe:F3}m/{ye:F1}° — far-anchor wobble suspected).");
        }

        Vector3 expPos = gf.RoomCenter.Value;
        float expYaw = gf.RoomYawDeg.Value;

        if (nm.IsHost)
        {
            foreach (var a in FindObjectsByType<OVRSpatialAnchor>(FindObjectsSortMode.None))
            {
                if (a != null && a.Created && a.GetComponent<RoomCenterAnchorTag>() != null)
                {
                    _refAnchor = a;
                    _refIsLocallyCreated = false;
                    break;
                }
            }
            if (_refAnchor == null) return;   // 中心圖釘還沒好（或沒用中心圖釘流程）
        }
        else
        {
            var go = new GameObject("ClientDriftRefAnchor");
            go.transform.SetPositionAndRotation(expPos, Quaternion.Euler(0f, expYaw, 0f));
            go.AddComponent<RoomCenterAnchorTag>();   // 讓所有「找 colocation 圖釘」的程式跳過它
            _refAnchor = go.AddComponent<OVRSpatialAnchor>();
            _refIsLocallyCreated = true;
            _refCreatedAt = Time.time;
        }

        _refExpectedPos = expPos;
        _refExpectedYaw = expYaw;
        Debug.Log($"[ColocationAlignment] Drift reference anchor established at {expPos} yaw={expYaw:F1} ({(nm.IsHost ? "host center anchor" : "client local anchor")}).");
    }

    /// host 重擺房間後呼叫：client 的本地基準要在新位置重建（host 的舊中心圖釘會被
    /// RoomCenterSetup 銷毀，fake-null 自動觸發重建）。
    public static void InvalidateDriftReference()
    {
        var inst = FindAnyObjectByType<ColocationHostAlignment>();
        if (inst == null || inst._refAnchor == null) return;
        if (inst._refIsLocallyCreated) Destroy(inst._refAnchor.gameObject);
        inst._refAnchor = null;
    }

    /// 校正的 rig 目標姿態：有近距離基準 → 算「把基準圖釘搬回應在位置」所需的 rig 位移；
    /// 沒有 → 舊路徑（colocation 圖釘 → 原點）。
    private void ComputeCorrectionTarget(out Vector3 rigPos, out float rigYaw)
    {
        if (_refAnchor != null && _refAnchor.Created)
        {
            Vector3 cur = _refAnchor.transform.position;
            float curYaw = _refAnchor.transform.eulerAngles.y;
            float dYaw = Mathf.DeltaAngle(curYaw, _refExpectedYaw);
            Quaternion dq = Quaternion.Euler(0f, dYaw, 0f);

            // 繞著基準點旋轉 dYaw、再平移到 expected → rig 跟著同一個剛體變換走
            rigPos = _refExpectedPos + dq * (_rig.position - cur);
            rigYaw = _rig.eulerAngles.y + dYaw;
            return;
        }

        ComputeTargetRigPose(out rigPos, out rigYaw);
    }

    /// 讓「世界座標系 = anchor 座標系」的 rig 目標姿態（Meta AlignCameraToAnchor 的數學）
    private void ComputeTargetRigPose(out Vector3 pos, out float yaw)
    {
        var t = _anchor.transform;
        var prevScale = t.localScale;
        t.localScale = Vector3.one;

        // anchor 的 tracking-space 姿態（rig 怎麼擺都不影響的「物理」姿態）
        OVRPose tp = t.ToTrackingSpacePose(Camera.main);
        t.localScale = prevScale;

        pos = Quaternion.Inverse(tp.orientation) * (-tp.position);
        yaw = -tp.orientation.eulerAngles.y;
    }

    private void ApplyRigPose(Vector3 pos, float yaw)
    {
        _rig.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
    }

    // ═════════════════════ 觸發來源 ═════════════════════

    /// 左手 Start（選單鍵）長按 1 秒 = 手動校正（展場工作人員的保險）。
    /// 按滿必震動（確認按鍵有讀到）；圖釘還沒好時只 log 原因，不執行校正。
    private void ReadManualButton()
    {
        // grip+Start 是「重新擺中心圖釘」的組合鍵（RoomCenterSetup）→ grip 按著時不觸發校正
        if (OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, OVRInput.Controller.LTouch) > 0.5f)
        {
            _startHeld = 0f;
            _manualFired = false;
            return;
        }

        bool held = OVRInput.Get(OVRInput.Button.Start, OVRInput.Controller.LTouch)
                 || OVRInput.Get(OVRInput.Button.Start);   // 保險：有些機況 Start 只掛在複合 controller 上

        if (held)
        {
            _startHeld += Time.deltaTime;
            if (!_manualFired && _startHeld >= ManualHoldSeconds)
            {
                _manualFired = true;
                StartCoroutine(Haptics.Pulse(OVRInput.Controller.LTouch, 1f, 0.7f, 0.2f));

                if (_alignedOnce)
                    BeginCorrection("manual (Start held)");
                else
                    Debug.LogWarning("[ColocationAlignment] Manual correction pressed, but no colocation anchor yet — nothing to align to.");
            }
        }
        else
        {
            _startHeld = 0f;
            _manualFired = false;
        }
    }

    private void HookEventsOnce(NetworkManager nm)
    {
        // recenter / tracking 恢復 / HMD 重戴 → 誤差必大，立即修正
        if (!_ovrHooked && OVRManager.instance != null && OVRManager.display != null)
        {
            _ovrHooked = true;
            OVRManager.display.RecenteredPose += OnPoseEvent;
            OVRManager.TrackingAcquired += OnPoseEvent;
            OVRManager.HMDMounted += OnPoseEvent;
        }

        // 階段切換（本來就有視覺轉場）→ 順手把殘餘誤差修掉，玩家無感
        if (_flowHooked == null && GameFlowController.Instance != null)
        {
            _flowHooked = GameFlowController.Instance;
            _flowHooked.CurrentPhase.OnValueChanged += OnPhaseChanged;
        }
    }

    private void OnPoseEvent()
    {
        BeginCorrection("recenter / tracking regained / HMD remounted");
    }

    private void OnPhaseChanged(GamePhase prev, GamePhase next)
    {
        MeasureError(out float posErr, out float yawErr);
        if (posErr > SettlePosEps * 2f || yawErr > SettleYawEps * 2f)
            BeginCorrection($"phase transition {prev} → {next}");
    }

    /// Meta 的 AlignCameraToAnchor（internal、每幀貼死）與濾波策略衝突：
    /// guest 上找到就關掉，由本元件接管。關不掉不會壞——只是回到「每幀貼死」行為。
    private void DisableMetaAlignerPeriodically()
    {
        if (Time.time < _nextMetaScan) return;
        _nextMetaScan = Time.time + 2f;

        foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
        {
            if (mb != null && mb.enabled &&
                mb.GetType().FullName == "Meta.XR.MultiplayerBlocks.Colocation.AlignCameraToAnchor")
            {
                mb.enabled = false;
                Debug.Log("[ColocationAlignment] Disabled Meta's per-frame AlignCameraToAnchor (guest); filtered alignment takes over.");
            }
        }
    }
}
