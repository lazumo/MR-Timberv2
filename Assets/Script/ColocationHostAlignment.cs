using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// v6:對齊參考升級 —— 每台優先用「自己本機建的中心圖釘」對齊/校正,
/// 共享 colocation 圖釘降級為 bootstrap(pin 還沒好之前的過渡參考)。
///
/// 歷史(別再改回任一舊版):
///  - v1 (7/14):host 每幀對齊 → anchor 噪音帶著世界滑。
///  - v2 (7/20):host 開場對齊一次 → 穩定但漂移永不修(30cm 累積)。
///  - v3 (7/21-22):deadband + 每台自建量尺(無共同實體參考)→ 兩台互相分歧。
///  - v4 (7/22):host 靜止+事件校正;guest 交還 Meta 每幀對齊 → client 比 host 飄
///    (guest 對「共享」圖釘的估計靠下載地圖 localize,天生較噪)。
///  - v5 (7/22):兩台對稱(關掉 Meta 每幀對齊器、都走一次對齊+事件+deadband),
///    目標同一支共享圖釘 —— 解掉搖世界,但 client 對共享圖釘的估計噪音仍在。
///  - v6(本版):校正參考換成「各自本機建的中心圖釘」(RoomCenterSetup,兩台都
///    打在同一個實體十字上)。本機圖釘用自己的地圖 localize = 各台最高品質參考;
///    兩支 pin 釘同一實體點 → 收斂目標一致,不會重演 v3。目標姿態 = host 廣播的
///    房間 pose(RoomCenter/RoomYawDeg):host 的 pin 就是廣播源,guest 的 pin
///    應該出現在同一位置。pin + 廣播都就緒前,退回 v5 的共享圖釘行為。
///
/// 校正時機(不變):手動 Start 長按 / recenter / 追蹤恢復 / HMD 重戴 /
/// 階段轉場 / deadband(>4cm 或 1° 持續 2s,冷卻 30s)。
///
/// 注意:host 遊戲中「重擺」到不同實體位置後,guest 的 pin 就過期了(還釘在舊十字)
/// —— 會偵測 pin 與共享圖釘兩個參考嚴重不合並丟警告,SOP = guest 也 grip+Start 重擺。
/// 零接線:GameFlowController.OnNetworkSpawn 呼叫 Ensure()。
/// </summary>
[DefaultExecutionOrder(10)]
public class ColocationHostAlignment : MonoBehaviour
{
    public static ColocationHostAlignment Instance { get; private set; }

    /// 本機已完成開場對齊。VirtualMRUKRoomLoader 等這個才讀中心圖釘/廣播座標。
    public static bool AlignedOnce { get; private set; }

    private const float CorrectHalfLife = 0.25f;   // 平滑修正半衰期(約 1 秒收斂九成五)
    private const float CorrectTimeout = 5f;
    private const float SettlePosEps = 0.005f;
    private const float SettleYawEps = 0.2f;
    private const float ManualHoldSeconds = 1f;

    // deadband 自動校正
    private const float AutoPosDeadband = 0.04f;    // 4cm
    private const float AutoYawDeadband = 1f;       // 1°
    private const float AutoSustainSeconds = 2f;    // 誤差要持續這麼久才修(濾掉短暫的追蹤噪音)
    private const float AutoCooldownSeconds = 30f;

    // pin 與共享圖釘兩個參考互相打架的偵測(pin 過期 = host 重擺過但 guest 沒跟上)
    private const float StalePinWarnMeters = 0.2f;
    private const float StalePinWarnInterval = 30f;

    private OVRSpatialAnchor _anchor;               // 共享 colocation 圖釘(bootstrap)
    private Transform _rig;
    private float _nextFind;
    private bool _alignedOnce;

    private bool _correcting;
    private float _correctStarted;
    private float _overSince = -1f;    // 誤差連續超標的起點(<0 = 未超標)
    private float _nextAutoAllowed;
    private float _nextStaleWarn;

    private float _startHeld;
    private bool _manualFired;

    private bool _ovrHooked;
    private GameFlowController _flowHooked;
    private bool _uuidPublished;
    private MonoBehaviour _metaAligner;             // Meta 的 AlignCameraToAnchor(internal → 用型別名抓)
    private readonly List<MonoBehaviour> _scanBuf = new();

    public static void Ensure()
    {
        if (FindAnyObjectByType<ColocationHostAlignment>() != null) return;
        new GameObject("ColocationHostAlignment").AddComponent<ColocationHostAlignment>();
    }

    private void Awake()
    {
        if (Instance == null) Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
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
        if (nm == null) return;   // editor 單機:閒置

        if (_rig == null)
        {
            if (Time.time < _nextFind) return;
            _nextFind = Time.time + 1f;
            var rig = FindAnyObjectByType<OVRCameraRig>();
            if (rig == null) return;
            _rig = rig.transform;
        }

        bool isHost = nm.IsHost;

        // guest:接管對齊 → Meta 的每幀對齊器一出現就關掉(它把 anchor 噪音直接寫進世界)
        if (!isHost) SuppressMetaAligner();

        HookEventsOnce();
        ReadManualButton();

        // 共享 colocation 圖釘:pin 接手後仍繼續補抓(當 pin 過期偵測的對照組)
        if (_anchor == null && Time.time >= _nextFind)
        {
            _nextFind = Time.time + 1f;
            _anchor = FindSharedAlignmentAnchor();
        }

        if (Camera.main == null) return;

        if (isHost && !_uuidPublished && _anchor != null && _anchor.Created)
            TryPublishAnchorUuid();

        if (!_alignedOnce)
        {
            // pin+廣播優先(guest 有存 pin 時,連共享圖釘都不用等);否則共享圖釘
            if (!ComputeTargetRigPose(out var pos, out float yaw)) return;
            ApplyRigPose(pos, yaw);
            _alignedOnce = true;
            AlignedOnce = true;
            Debug.Log($"[ColocationAlignment] {(isHost ? "Host" : "Guest")} aligned ONCE via {ActiveReferenceName()} " +
                      "(v6; corrections = manual / recenter / phase / deadband).");
            return;
        }

        if (_correcting) StepCorrection();
        else             AutoDeadbandWatch();
    }

    /// guest 專用:找到 Meta 動態掛上 rig 的 AlignCameraToAnchor 就永久停用。
    /// (internal class,只能用型別名比對;它可能在我們對齊之後才被加上來 → 每幀盯著)
    private void SuppressMetaAligner()
    {
        if (_metaAligner != null)
        {
            if (_metaAligner.enabled) _metaAligner.enabled = false;
            return;
        }

        _rig.GetComponents(_scanBuf);
        foreach (var mb in _scanBuf)
        {
            if (mb != null && mb.GetType().Name == "AlignCameraToAnchor")
            {
                _metaAligner = mb;
                mb.enabled = false;
                Debug.Log("[ColocationAlignment] Meta AlignCameraToAnchor disabled — guest runs the v6 policy instead.");
                return;
            }
        }
    }

    /// host 把 alignment anchor 的 UUID 廣播出去,guest 只綁這一支。
    private void TryPublishAnchorUuid()
    {
        var gf = GameFlowController.Instance;
        if (gf == null || !gf.IsSpawned) return;
        gf.PublishAlignAnchorUuid(_anchor.Uuid);
        _uuidPublished = true;
    }

    // ═════════════════════ 對齊參考(v6 核心) ═════════════════════

    /// 首選參考:本機中心圖釘(pin)+ host 廣播的房間 pose。
    /// pin 是「自己地圖」建的圖釘 = 本機最高品質;host 的 pin 就是廣播源,
    /// guest 的 pin 打在同一個實體十字 → 對齊完成時 pin 的世界姿態應等於廣播 pose。
    private bool TryGetPinReference(out Transform pin, out Vector3 targetPos, out float targetYaw)
    {
        pin = null; targetPos = default; targetYaw = 0f;

        var rcs = RoomCenterSetup.Instance;
        var gf = GameFlowController.Instance;
        if (rcs == null || gf == null || !gf.IsSpawned || !gf.RoomPoseReady.Value) return false;

        var a = rcs.CenterAnchor;
        if (a == null || !a.Created) return false;

        pin = a.transform;
        targetPos = gf.RoomCenter.Value;
        targetYaw = gf.RoomYawDeg.Value;
        return true;
    }

    private string ActiveReferenceName()
    {
        return TryGetPinReference(out _, out _, out _) ? "local center pin"
             : _anchor != null && _anchor.Created      ? "shared colocation anchor"
             : "none";
    }

    /// 量「本機世界」與參考的分歧。pin 模式:pin 世界姿態 vs 廣播 pose;
    /// 共享圖釘模式:圖釘 vs 原點/yaw0。只量水平(XZ)—— 高度政策是跟本機地板。
    private bool MeasureError(out float posErr, out float yawErr)
    {
        if (TryGetPinReference(out var pin, out var tPos, out float tYaw))
        {
            Vector3 d = pin.position - tPos;
            posErr = new Vector2(d.x, d.z).magnitude;
            yawErr = Mathf.Abs(Mathf.DeltaAngle(pin.eulerAngles.y, tYaw));
            return true;
        }

        if (_anchor != null && _anchor.Created)
        {
            Vector3 p = _anchor.transform.position;
            posErr = new Vector2(p.x, p.z).magnitude;
            yawErr = Mathf.Abs(Mathf.DeltaAngle(_anchor.transform.eulerAngles.y, 0f));
            return true;
        }

        posErr = 0f; yawErr = 0f;
        return false;
    }

    /// AlignmentErrorHud 用:量本機當下的對齊誤差(m, °)。沒有任何參考 = false。
    public static bool TryMeasureError(out float posErr, out float yawErr)
    {
        posErr = 0f; yawErr = 0f;
        var i = Instance;
        return i != null && i.MeasureError(out posErr, out yawErr);
    }

    /// 兩台要綁「同一支」共享 colocation 圖釘的唯一入口(SpawnArea / loader 也用這個):
    ///  - 永遠排除中心圖釘(RoomCenterAnchorTag)。
    ///  - guest:只認 host 廣播的 UUID;UUID 還沒到就回 null 繼續等。
    ///  - host / 單機:第一支(host 是圖釘建立者,正常只有一支)。
    public static OVRSpatialAnchor FindSharedAlignmentAnchor()
    {
        var nm = NetworkManager.Singleton;
        bool guest = nm != null && nm.IsConnectedClient && !nm.IsHost;

        Guid wanted = Guid.Empty;
        if (guest)
        {
            var gf = GameFlowController.Instance;
            if (gf == null ||
                !Guid.TryParse(gf.AlignAnchorUuid.Value.ToString(), out wanted) ||
                wanted == Guid.Empty)
                return null;   // 等 host 廣播 UUID
        }

        foreach (var a in FindObjectsByType<OVRSpatialAnchor>(FindObjectsSortMode.None))
        {
            if (a == null || a.GetComponent<RoomCenterAnchorTag>() != null) continue;
            if (guest && a.Uuid != wanted) continue;
            return a;
        }
        return null;
    }

    /// 讓「參考出現在目標姿態」的 rig 目標(yaw-only 版對齊數學)。
    /// pin 模式:pin → 廣播 pose;共享圖釘模式:圖釘 → 原點(Meta AlignCameraToAnchor 的數學)。
    private bool ComputeTargetRigPose(out Vector3 pos, out float yaw)
    {
        if (TryGetPinReference(out var pin, out var tPos, out float tYaw))
        {
            OVRPose tp = pin.ToTrackingSpacePose(Camera.main);
            float yawT = tp.orientation.eulerAngles.y;
            yaw = tYaw - yawT;
            pos = tPos - Quaternion.Euler(0f, yaw, 0f) * tp.position;
            return true;
        }

        if (_anchor != null && _anchor.Created)
        {
            var t = _anchor.transform;
            var prevScale = t.localScale;
            t.localScale = Vector3.one;

            OVRPose tp = t.ToTrackingSpacePose(Camera.main);
            t.localScale = prevScale;

            pos = Quaternion.Inverse(tp.orientation) * (-tp.position);
            yaw = -tp.orientation.eulerAngles.y;
            return true;
        }

        pos = default; yaw = 0f;
        return false;
    }

    private void ApplyRigPose(Vector3 pos, float yaw)
    {
        // 高度不信任何圖釘:FloorLevel 追蹤原點下,rig y=0 ⇒ 世界地板 = 本機實測地板。
        pos.y = 0f;
        _rig.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
    }

    // ═════════════════════ 校正 ═════════════════════

    private void BeginCorrection(string reason)
    {
        if (!_alignedOnce || _correcting) return;
        if (!MeasureError(out float pe, out float ye)) return;
        _correcting = true;
        _correctStarted = Time.time;
        Debug.LogWarning($"[ColocationAlignment] Correcting world → {ActiveReferenceName()} " +
                         $"(offset {pe:F3}m, {ye:F1}°), trigger = {reason}");
    }

    private void StepCorrection()
    {
        if (Camera.main == null || !ComputeTargetRigPose(out var targetPos, out float targetYaw))
        {
            _correcting = false;
            return;
        }

        float k = 1f - Mathf.Pow(2f, -Time.deltaTime / CorrectHalfLife);
        Vector3 newPos = Vector3.Lerp(_rig.position, targetPos, k);
        float newYaw = Mathf.LerpAngle(_rig.eulerAngles.y, targetYaw, k);
        ApplyRigPose(newPos, newYaw);

        if (!MeasureError(out float posErr, out float yawErr)) { _correcting = false; return; }
        bool settled = posErr < SettlePosEps && yawErr < SettleYawEps;
        bool timedOut = Time.time - _correctStarted > CorrectTimeout;

        if (settled || timedOut)
        {
            _correcting = false;
            _overSince = -1f;
            Debug.Log($"[ColocationAlignment] Correction {(settled ? "settled" : "timed out")} " +
                      $"after {Time.time - _correctStarted:F1}s (residual {posErr:F3}m, {yawErr:F1}°).");
        }
    }

    /// deadband 自動校正:誤差超標「持續」AutoSustainSeconds 才修,帶冷卻。
    /// 順便盯「pin 過期」:pin 模式下共享圖釘若嚴重偏離原點,代表 pin 與共享圖釘
    /// 指向不同的世界(通常 = host 重擺過房間但這台的 pin 沒跟上)。
    private void AutoDeadbandWatch()
    {
        if (!MeasureError(out float pe, out float ye)) return;

        WarnIfPinStale();

        bool over = pe > AutoPosDeadband || ye > AutoYawDeadband;
        if (!over) { _overSince = -1f; return; }
        if (_overSince < 0f) _overSince = Time.time;

        if (Time.time - _overSince >= AutoSustainSeconds && Time.time >= _nextAutoAllowed)
        {
            _nextAutoAllowed = Time.time + AutoCooldownSeconds;
            _overSince = -1f;
            BeginCorrection($"auto deadband ({pe * 100f:F1}cm, {ye:F1}°)");
        }
    }

    private void WarnIfPinStale()
    {
        if (Time.time < _nextStaleWarn) return;
        if (_anchor == null || !_anchor.Created) return;
        if (!TryGetPinReference(out _, out _, out _)) return;

        // pin 模式下,若世界跟著 pin 校正得很好,共享圖釘也應該接近原點;
        // 它偏很多 = 兩個參考不一致(pin 釘錯位置/過期,或共享圖釘壞掉)。
        Vector3 p = _anchor.transform.position;
        float coloErr = new Vector2(p.x, p.z).magnitude;
        if (coloErr > StalePinWarnMeters)
        {
            _nextStaleWarn = Time.time + StalePinWarnInterval;
            Debug.LogWarning($"[ColocationAlignment] Pin vs shared-anchor disagree by {coloErr:F2}m — " +
                             "center pin may be stale (host re-placed the room?). Re-place pin: hold left grip+Start.");
        }
    }

    private void LateUpdate()
    {
        // guest 保險絲:萬一 Meta 對齊器在我們關掉它之前跑了一幀,把它寫進 rig 的
        // 圖釘高度誤差清回 0(= 本機地板)。我們自己的 ApplyRigPose 本來就 y=0,不打架。
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.IsHost || _rig == null) return;
        var p = _rig.position;
        if (Mathf.Abs(p.y) > 0.0005f) _rig.position = new Vector3(p.x, 0f, p.z);
    }

    // ═════════════════════ 觸發來源 ═════════════════════

    /// 左手 Start(選單鍵)長按 1 秒 = 手動校正(兩台都可用)。按滿必震動;
    /// grip 按著時不觸發(grip+Start 是 RoomCenterSetup 的重擺組合鍵)。
    private void ReadManualButton()
    {
        if (OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, OVRInput.Controller.LTouch) > 0.5f)
        {
            _startHeld = 0f;
            _manualFired = false;
            return;
        }

        bool held = OVRInput.Get(OVRInput.Button.Start, OVRInput.Controller.LTouch)
                 || OVRInput.Get(OVRInput.Button.Start);

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
                    Debug.LogWarning("[ColocationAlignment] Manual correction pressed, but no alignment reference yet.");
            }
        }
        else
        {
            _startHeld = 0f;
            _manualFired = false;
        }
    }

    private void HookEventsOnce()
    {
        if (!_ovrHooked && OVRManager.instance != null && OVRManager.display != null)
        {
            _ovrHooked = true;
            OVRManager.display.RecenteredPose += OnPoseEvent;
            OVRManager.TrackingAcquired += OnPoseEvent;
            OVRManager.HMDMounted += OnPoseEvent;
        }

        if (_flowHooked == null && GameFlowController.Instance != null)
        {
            _flowHooked = GameFlowController.Instance;
            _flowHooked.CurrentPhase.OnValueChanged += OnPhaseChanged;
            _flowHooked.AlignAnchorUuid.OnValueChanged += OnAnchorUuidChanged;
        }
    }

    /// host 重開 app 重連後會廣播「新的」anchor UUID —— guest 丟掉舊的共享圖釘、
    /// 改綁新的那支;已對齊過就交給 deadband/手動平滑修過去(不硬跳)。
    private void OnAnchorUuidChanged(Unity.Collections.FixedString64Bytes prev, Unity.Collections.FixedString64Bytes next)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.IsHost || _anchor == null) return;
        if (!Guid.TryParse(next.ToString(), out Guid wanted) || wanted == Guid.Empty) return;
        if (_anchor.Uuid == wanted) return;

        Debug.LogWarning($"[ColocationAlignment] Alignment anchor UUID changed → rebinding (old {_anchor.Uuid}, new {wanted}).");
        _anchor = null;   // 下一輪 find 會用新 UUID 抓;誤差由 deadband/手動校正收斂
        _nextFind = 0f;
    }

    private void OnPoseEvent()
    {
        BeginCorrection("recenter / tracking regained / HMD remounted");
    }

    private void OnPhaseChanged(GamePhase prev, GamePhase next)
    {
        if (!MeasureError(out float posErr, out float yawErr)) return;
        if (posErr > SettlePosEps * 2f || yawErr > SettleYawEps * 2f)
            BeginCorrection($"phase transition {prev} → {next}");
    }
}
