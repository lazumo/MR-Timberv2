using Unity.Netcode;
using UnityEngine;

/// <summary>
/// v4（穩定版）：回到 7/20 APK 的行為哲學 —— 世界預設完全靜止 —— 只加「事件式校正」。
///
/// 歷史（別再改回任一舊版）：
///  - v1 (7/14)：host 每幀對齊 → anchor 噪音帶著世界滑。
///  - v2 (7/20)：host 開場對齊一次 → 穩定但漂移永不修（30cm 累積）。
///  - v3 (7/21-22)：deadband 自動校正 + 每台自建中央量尺 → 實測兩台「各自」自動校正
///    會把彼此的世界修到分歧（隊友手上的工具看起來完全跑掉）。
///  - v4（本版）：v2 的靜止世界 + 事件式修正。展場有工作人員 —— 可預測 + 一鍵修，
///    勝過全自動但可能亂動。
///
/// 行為：
///  - host：開場對齊 colocation 圖釘一次；之後只有四種時機會「平滑修一次」：
///    (1) 左手 Start 長按 1 秒（手動；grip 按著時不觸發，那是重擺組合鍵）
///    (2) recenter / 追蹤恢復 / HMD 重戴
///    (3) 遊戲階段轉場（有視覺變化遮掩）
///  - guest：完全交還 Meta 的 AlignCameraToAnchor（每幀貼緊 —— 7/20 行為）；
///    本元件只負責回報 AlignedOnce 讓 loader 知道何時能用廣播座標。
///
/// 對齊數學照抄 Meta 的 AlignCameraToAnchor（internal 無法直接使用）。
/// 找圖釘時排除 RoomCenterAnchorTag（中心圖釘只管房間位置，不管世界對齊）。
/// 零接線：GameFlowController.OnNetworkSpawn 呼叫 Ensure()。
/// </summary>
[DefaultExecutionOrder(10)]
public class ColocationHostAlignment : MonoBehaviour
{
    /// 本機已完成開場對齊（host：對齊一次後；guest：Meta 元件接手後）。
    /// VirtualMRUKRoomLoader 等這個才讀中心圖釘/廣播座標。
    public static bool AlignedOnce { get; private set; }

    private const float CorrectHalfLife = 0.25f;   // 平滑修正半衰期（約 1 秒收斂九成五）
    private const float CorrectTimeout = 5f;
    private const float SettlePosEps = 0.005f;
    private const float SettleYawEps = 0.2f;
    private const float ManualHoldSeconds = 1f;

    private OVRSpatialAnchor _anchor;
    private Transform _rig;
    private float _nextFind;
    private bool _alignedOnce;

    private bool _correcting;
    private float _correctStarted;

    private float _startHeld;
    private bool _manualFired;

    private bool _ovrHooked;
    private GameFlowController _flowHooked;

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
        if (nm == null) return;   // editor 單機：閒置

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

        // guest：Meta 的 AlignCameraToAnchor 每幀對齊（7/20 行為）。
        // 圖釘一就緒即可視為已對齊（Meta 元件同幀就會貼上）。
        if (!nm.IsHost)
        {
            if (!AlignedOnce && _anchor.Created)
            {
                AlignedOnce = true;
                Debug.Log("[ColocationAlignment] Guest aligned (Meta per-frame aligner active).");
            }
            return;
        }

        HookEventsOnce();
        ReadManualButton();

        if (!_anchor.Created || Camera.main == null) return;

        if (!_alignedOnce)
        {
            ComputeTargetRigPose(out var pos, out float yaw);
            ApplyRigPose(pos, yaw);
            _alignedOnce = true;
            AlignedOnce = true;
            Debug.Log("[ColocationAlignment] Host aligned to colocation anchor ONCE (stable v4; corrections are event-driven only).");
            return;
        }

        if (_correcting)
            StepCorrection();
    }

    // ═════════════════════ 校正（事件觸發才會發生） ═════════════════════

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
            Debug.Log($"[ColocationAlignment] Correction {(settled ? "settled" : "timed out")} " +
                      $"after {Time.time - _correctStarted:F1}s (residual {posErr:F3}m, {yawErr:F1}°).");
        }
    }

    /// colocation 圖釘偏離「原點/yaw0」多少（對齊完成時應趨近 0）。
    /// 只量水平（XZ）：高度政策是「跟本機地板、不跟圖釘」，把 y 算進誤差
    /// 會讓校正永遠收斂不了（y 殘差是故意留的）。
    private void MeasureError(out float posErr, out float yawErr)
    {
        Vector3 p = _anchor.transform.position;
        posErr = new Vector2(p.x, p.z).magnitude;
        yawErr = Mathf.Abs(Mathf.DeltaAngle(_anchor.transform.eulerAngles.y, 0f));
    }

    /// 只認 colocation 圖釘 — 排除中心圖釘（RoomCenterAnchorTag）
    private static OVRSpatialAnchor FindColocationAnchor()
    {
        foreach (var a in FindObjectsByType<OVRSpatialAnchor>(FindObjectsSortMode.None))
            if (a != null && a.GetComponent<RoomCenterAnchorTag>() == null)
                return a;
        return null;
    }

    /// 讓「世界座標系 = anchor 座標系」的 rig 目標姿態（Meta AlignCameraToAnchor 的數學）
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
        // 高度不信圖釘：FloorLevel 追蹤原點下，rig y=0 ⇒ 世界地板 = 本機實測地板。
        // 圖釘的高度估計（尤其 client 對分享圖釘的）常差幾公分且會漂 →
        // 之前 host 世界下沉、client 上浮就是它。XZ+朝向跟圖釘，高度跟自己的腳下。
        pos.y = 0f;
        _rig.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
    }

    private void LateUpdate()
    {
        // guest：Meta 的每幀對齊會把圖釘高度誤差寫進 rig → 世界浮/沉。
        // 在它之後每幀把高度清回 0（= 本機地板），XZ/朝向不動、不跟它打架。
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.IsHost || _rig == null) return;
        var p = _rig.position;
        if (Mathf.Abs(p.y) > 0.0005f) _rig.position = new Vector3(p.x, 0f, p.z);
    }

    // ═════════════════════ 觸發來源 ═════════════════════

    /// 左手 Start（選單鍵）長按 1 秒 = 手動校正。按滿必震動；grip 按著時不觸發
    /// （grip+Start 是 RoomCenterSetup 的重擺組合鍵）。
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
}
