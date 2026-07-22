using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// v5:兩台跑同一套「靜止世界 + 事件式/deadband 校正」,對齊目標是同一支 colocation 圖釘。
///
/// 歷史(別再改回任一舊版):
///  - v1 (7/14):host 每幀對齊 → anchor 噪音帶著世界滑。
///  - v2 (7/20):host 開場對齊一次 → 穩定但漂移永不修(30cm 累積)。
///  - v3 (7/21-22):deadband 自動校正 + 每台「自建中央量尺」→ 兩台各自校正互相分歧。
///  - v4 (7/22):host 靜止+事件校正;guest 交還 Meta AlignCameraToAnchor 每幀貼緊
///    → 實測 client 比 host 飄:guest 對共享圖釘的位置估計本來就較噪(靠下載的特徵
///    地圖 localize),每幀貼緊 = 噪音直接搖整個世界。
///  - v5(本版):guest 也改用 v4 策略 —— 關掉 Meta 的每幀對齊器,開場對齊一次,
///    之後只有事件/deadband/手動會「平滑修一次」。與 v3 的關鍵差異:兩台校正的
///    目標是同一支實體圖釘(guest 只認 host 廣播的 UUID),不會互相分歧。
///
/// 校正時機(兩台相同):
///  (1) 左手 Start 長按 1 秒(手動;grip 按著時不觸發,那是重擺組合鍵)
///  (2) recenter / 追蹤恢復 / HMD 重戴
///  (3) 遊戲階段轉場(有視覺變化遮掩)
///  (4) deadband:誤差 > 4cm 或 1° 且持續 2 秒 → 自動修一次,冷卻 30 秒
///
/// 對齊數學照抄 Meta 的 AlignCameraToAnchor(internal 無法直接使用)。
/// 找圖釘一律走 FindSharedAlignmentAnchor():排除中心圖釘(RoomCenterAnchorTag);
/// guest 只認 host 廣播的 anchor UUID(重連殘留舊圖釘時抓錯 = 永久位移)。
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

    // deadband 自動校正(v3 教訓:目標必須是同一支圖釘,不能各自量尺)
    private const float AutoPosDeadband = 0.04f;    // 4cm
    private const float AutoYawDeadband = 1f;       // 1°
    private const float AutoSustainSeconds = 2f;    // 誤差要持續這麼久才修(濾掉短暫的追蹤噪音)
    private const float AutoCooldownSeconds = 30f;

    private OVRSpatialAnchor _anchor;
    private Transform _rig;
    private float _nextFind;
    private bool _alignedOnce;

    private bool _correcting;
    private float _correctStarted;
    private float _overSince = -1f;    // 誤差連續超標的起點(<0 = 未超標)
    private float _nextAutoAllowed;

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

        if (_anchor == null || _rig == null)
        {
            if (Time.time < _nextFind) return;
            _nextFind = Time.time + 1f;

            if (_anchor == null) _anchor = FindSharedAlignmentAnchor();
            if (_rig == null)
            {
                var rig = FindAnyObjectByType<OVRCameraRig>();
                if (rig != null) _rig = rig.transform;
            }
            if (_anchor == null || _rig == null) return;
        }

        bool isHost = nm.IsHost;

        // guest:接管對齊 → Meta 的每幀對齊器一出現就關掉(它把 anchor 噪音直接寫進世界)
        if (!isHost) SuppressMetaAligner();

        HookEventsOnce();
        ReadManualButton();

        if (!_anchor.Created || Camera.main == null) return;

        if (isHost && !_uuidPublished) TryPublishAnchorUuid();

        if (!_alignedOnce)
        {
            ComputeTargetRigPose(out var pos, out float yaw);
            ApplyRigPose(pos, yaw);
            _alignedOnce = true;
            AlignedOnce = true;
            Debug.Log($"[ColocationAlignment] {(isHost ? "Host" : "Guest")} aligned to colocation anchor ONCE " +
                      "(v5; corrections = manual / recenter / phase / deadband).");
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
                Debug.Log("[ColocationAlignment] Meta AlignCameraToAnchor disabled — guest runs the v5 policy instead.");
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

    // ═════════════════════ 校正 ═════════════════════

    private void BeginCorrection(string reason)
    {
        if (!_alignedOnce || _correcting) return;
        _correcting = true;
        _correctStarted = Time.time;
        MeasureError(out float pe, out float ye);
        Debug.LogWarning($"[ColocationAlignment] Correcting world → anchor (offset {pe:F3}m, {ye:F1}°), trigger = {reason}");
    }

    private void StepCorrection()
    {
        if (!_anchor.Created || Camera.main == null) { _correcting = false; return; }

        ComputeTargetRigPose(out var targetPos, out float targetYaw);

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
            _overSince = -1f;
            Debug.Log($"[ColocationAlignment] Correction {(settled ? "settled" : "timed out")} " +
                      $"after {Time.time - _correctStarted:F1}s (residual {posErr:F3}m, {yawErr:F1}°).");
        }
    }

    /// deadband 自動校正:誤差超標「持續」AutoSustainSeconds 才修,帶冷卻。
    private void AutoDeadbandWatch()
    {
        MeasureError(out float pe, out float ye);
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

    /// colocation 圖釘偏離「原點/yaw0」多少(對齊完成時應趨近 0)。
    /// 只量水平(XZ):高度政策是「跟本機地板、不跟圖釘」,把 y 算進誤差
    /// 會讓校正永遠收斂不了(y 殘差是故意留的)。
    private void MeasureError(out float posErr, out float yawErr)
    {
        Vector3 p = _anchor.transform.position;
        posErr = new Vector2(p.x, p.z).magnitude;
        yawErr = Mathf.Abs(Mathf.DeltaAngle(_anchor.transform.eulerAngles.y, 0f));
    }

    /// AlignmentErrorHud 用:量本機當下的對齊誤差(m, °)。沒有圖釘 = false。
    public static bool TryMeasureError(out float posErr, out float yawErr)
    {
        posErr = 0f; yawErr = 0f;
        var i = Instance;
        if (i == null || i._anchor == null || !i._anchor.Created) return false;
        i.MeasureError(out posErr, out yawErr);
        return true;
    }

    /// 兩台要綁「同一支」colocation 圖釘的唯一入口(SpawnArea / loader 也用這個):
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

    /// 讓「世界座標系 = anchor 座標系」的 rig 目標姿態(Meta AlignCameraToAnchor 的數學)
    private void ComputeTargetRigPose(out Vector3 pos, out float yaw)
    {
        var t = _anchor.transform;
        var prevScale = t.localScale;
        t.localScale = Vector3.one;

        OVRPose tp = t.ToTrackingSpacePose(Camera.main);
        t.localScale = prevScale;

        pos = Quaternion.Inverse(tp.orientation) * (-tp.position);
        yaw = -tp.orientation.eulerAngles.y;
    }

    private void ApplyRigPose(Vector3 pos, float yaw)
    {
        // 高度不信圖釘:FloorLevel 追蹤原點下,rig y=0 ⇒ 世界地板 = 本機實測地板。
        // 圖釘的高度估計(尤其 client 對分享圖釘的)常差幾公分且會漂 →
        // 之前 host 世界下沉、client 上浮就是它。XZ+朝向跟圖釘,高度跟自己的腳下。
        pos.y = 0f;
        _rig.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
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
                    Debug.LogWarning("[ColocationAlignment] Manual correction pressed, but no colocation anchor yet.");
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

    /// host 重開 app 重連後會廣播「新的」anchor UUID —— guest 丟掉舊圖釘、
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
        MeasureError(out float posErr, out float yawErr);
        if (posErr > SettlePosEps * 2f || yawErr > SettleYawEps * 2f)
            BeginCorrection($"phase transition {prev} → {next}");
    }
}
