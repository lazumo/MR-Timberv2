using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 房間中心圖釘的專屬標籤 — 讓「找 colocation 圖釘」的程式碼能把它排除
/// （ColocationHostAlignment / SpawnArea / VirtualMRUKRoomLoader 都用 FindAnyObjectByType
/// 抓圖釘，沒有這個標籤會抓錯）。
/// </summary>
public class RoomCenterAnchorTag : MonoBehaviour { }

/// <summary>
/// 「中心圖釘」設定系統（host 專用；guest / editor 完全閒置）。
///
/// 目的：3x3 虛擬房間不再蓋在「host 頭的位置」，而是蓋在一支手動擺放、
/// 永久儲存的中心圖釘上 — 展場佈置好後校正一次，之後每天戴上就玩。
/// 順帶解掉 Meta 官方 FAQ 的「離圖釘 3 公尺開始漂」問題（圖釘在中央，3x3 內最遠 2.1m）。
///
/// 流程：
///  - 開場：VirtualMRUKRoomLoader 呼叫 GetRoomPoseAsync() → 有存過的圖釘就載入直接用；
///    沒有 → 自動進設定模式（Blocking=true，遊戲開始/房子生成都會等）。
///  - 設定模式（host）：右手控制器平放在場地中央地板、頭指向「正面」→ 按住扳機 1 秒
///    → 顯示 3x3 預覽框 → A 確認 / 再按住扳機重擺。姿態會「拍平」：只取 XZ + 水平朝向，
///    傾斜與高度全部拋棄（圖釘永遠直立、貼地）。
///  - 遊戲中重新設定：按住左手 grip + Start 1 秒 → 進設定模式 → 確認後重蓋房間、
///    重新廣播 pose 給 guest、走 Restart 重開一輪。
///  - 儲存：OVRSpatialAnchor.SaveAnchorAsync + PlayerPrefs 記 UUID。清 space setup 會
///    連圖釘一起洗掉 → 重掃後要重擺一次（展場 SOP）。
///
/// 按鍵注意：設定模式刻意不用 A/B 以外的鍵 — B 是全域 Restart，扳機/A 都安全。
/// </summary>
[DefaultExecutionOrder(5)]
public class RoomCenterSetup : MonoBehaviour
{
    private const string PrefsKey = "RoomCenterAnchorUuid";
    private const float PlaceHoldSeconds = 1f;
    private const float ReenterHoldSeconds = 1f;
    private const float RoomHalfSize = 1.5f;   // 3x3 房間

    public static RoomCenterSetup Instance { get; private set; }

    /// true = 設定模式進行中 → GameFlowController / HouseSpawner 都會等它結束
    public static bool Blocking { get; private set; }

    private enum State { Idle, Loading, Placing, Preview, Saving, Done }
    private State _state = State.Idle;

    private OVRSpatialAnchor _centerAnchor;   // 已載入/已建立的中心圖釘
    private Vector3 _capturedPos;             // 拍平後的擺放位置（y = floorY）
    private float _capturedYaw;
    private float _floorY;
    private bool _midGame;                    // true = 遊戲中重新設定（確認後要 Restart）

    private float _triggerHeld;
    private bool _triggerFired;
    private float _reenterHeld;
    private Transform _rightHand;             // OVRCameraRig.rightHandAnchor（世界座標）

    // 視覺（全程式生成，零資源）
    private LineRenderer _reticle;            // 跟著控制器的地板十字
    private LineRenderer _preview;            // 3x3 預覽框 + 正面記號
    private TextMesh _hint;                   // 說明文字（英文——內建字型沒有中文字）
    private Material _lineMat;

    public static void Ensure()
    {
        if (Instance != null) return;
        new GameObject("RoomCenterSetup").AddComponent<RoomCenterSetup>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private bool IsHost =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;

    // ════════════════ 對外入口（loader 呼叫） ════════════════

    /// 房間該蓋在哪。有存檔 → 載入圖釘回傳其 pose；沒有 → 進設定模式等 staff 擺完。
    /// 回傳 null = 不適用（guest / editor / 載入且擺放都失敗）→ loader 走原本的 fallback。
    public async Task<Pose?> GetRoomPoseAsync(float floorY)
    {
        if (Application.isEditor || !IsHost) return null;
        _floorY = floorY;

        // 1) 試載入存過的圖釘
        _state = State.Loading;
        if (await TryLoadSavedAnchor())
        {
            _state = State.Done;
            Debug.Log($"[RoomCenterSetup] Loaded saved center anchor at {_centerAnchor.transform.position}.");
            return PoseFromAnchor();
        }

        // 2) 沒存檔 → 設定模式（擋住遊戲開始）
        Debug.Log("[RoomCenterSetup] No saved center anchor — entering setup mode.");
        BeginSetup(midGame: false);
        while (_state != State.Done) await Task.Yield();
        return PoseFromAnchor();
    }

    private Pose PoseFromAnchor()
    {
        Vector3 p = _centerAnchor.transform.position;
        p.y = _floorY;
        float yaw = _centerAnchor.transform.eulerAngles.y;
        return new Pose(p, Quaternion.Euler(0f, yaw, 0f));
    }

    // ════════════════ 儲存 / 載入 ════════════════

    private async Task<bool> TryLoadSavedAnchor()
    {
        string s = PlayerPrefs.GetString(PrefsKey, "");
        if (string.IsNullOrEmpty(s) || !Guid.TryParse(s, out Guid uuid)) return false;

        var unbound = new List<OVRSpatialAnchor.UnboundAnchor>();
        var result = await OVRSpatialAnchor.LoadUnboundAnchorsAsync(new[] { uuid }, unbound);
        if (!result.Success || unbound.Count == 0)
        {
            Debug.LogWarning($"[RoomCenterSetup] Saved anchor {uuid} not found (space setup cleared?).");
            return false;
        }

        var ub = unbound[0];
        if (!await ub.LocalizeAsync(10.0))
        {
            Debug.LogWarning("[RoomCenterSetup] Saved anchor failed to localize in 10s.");
            return false;
        }

        var go = new GameObject("RoomCenterAnchor");
        go.AddComponent<RoomCenterAnchorTag>();
        var anchor = go.AddComponent<OVRSpatialAnchor>();
        ub.BindTo(anchor);

        float t = 0f;
        while (!anchor.Created && t < 10f) { t += Time.deltaTime; await Task.Yield(); }
        if (!anchor.Created) { Destroy(go); return false; }

        _centerAnchor = anchor;
        return true;
    }

    private async Task<bool> CreateAndSaveAnchor(Vector3 pos, float yaw)
    {
        // 舊圖釘：從儲存與場景中一併移除
        if (_centerAnchor != null)
        {
            try { _ = _centerAnchor.EraseAnchorAsync(); } catch { }
            Destroy(_centerAnchor.gameObject);
            _centerAnchor = null;
        }

        var go = new GameObject("RoomCenterAnchor");
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        go.AddComponent<RoomCenterAnchorTag>();
        var anchor = go.AddComponent<OVRSpatialAnchor>();

        float t = 0f;
        while (!anchor.Created && t < 10f) { t += Time.deltaTime; await Task.Yield(); }
        if (!anchor.Created)
        {
            Debug.LogError("[RoomCenterSetup] Anchor creation failed (tracking too poor here?).");
            Destroy(go);
            return false;
        }

        var save = await anchor.SaveAnchorAsync();
        if (!save.Success)
        {
            Debug.LogError($"[RoomCenterSetup] SaveAnchorAsync failed: {save.Status}. Anchor works this session only.");
            // 不儲存也照用（本場有效）；不寫 PlayerPrefs
        }
        else
        {
            PlayerPrefs.SetString(PrefsKey, anchor.Uuid.ToString());
            PlayerPrefs.Save();
            Debug.Log($"[RoomCenterSetup] Center anchor saved ({anchor.Uuid}).");
        }

        _centerAnchor = anchor;
        return true;
    }

    // ════════════════ 設定模式 ════════════════

    private void BeginSetup(bool midGame)
    {
        _midGame = midGame;
        Blocking = true;
        _state = State.Placing;
        _triggerHeld = 0f;
        _triggerFired = false;
        EnsureVisuals();
        Debug.Log($"[RoomCenterSetup] Setup mode begun (midGame={midGame}).");
    }

    private void Update()
    {
        if (Application.isEditor || !IsHost) return;

        // 遊戲中重新設定：左手 grip + Start 按住 1 秒
        if (_state == State.Done)
        {
            bool combo = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, OVRInput.Controller.LTouch) > 0.5f
                      && OVRInput.Get(OVRInput.Button.Start, OVRInput.Controller.LTouch);
            if (combo)
            {
                _reenterHeld += Time.deltaTime;
                if (_reenterHeld >= ReenterHoldSeconds)
                {
                    _reenterHeld = 0f;
                    StartCoroutine(Haptics.Pulse(OVRInput.Controller.LTouch, 1f, 0.7f, 0.2f));
                    BeginSetup(midGame: true);
                }
            }
            else _reenterHeld = 0f;
            return;
        }

        if (_state == State.Placing || _state == State.Preview)
            RunPlacementInput();
    }

    private void RunPlacementInput()
    {
        var cam = Camera.main;
        if (cam == null) return;

        // 右手控制器的世界位置/朝向（拍平：只取 XZ + yaw）。
        // rightHandAnchor 已是世界座標（rig 底下的 controller 追蹤節點）。
        if (_rightHand == null)
        {
            var rig = FindAnyObjectByType<OVRCameraRig>();
            if (rig == null) return;
            _rightHand = rig.rightHandAnchor;
            if (_rightHand == null) return;
        }
        Vector3 worldPos = _rightHand.position;
        Vector3 worldFwd = _rightHand.forward;

        Vector3 flatPos = new Vector3(worldPos.x, _floorY, worldPos.z);
        Vector3 flatFwd = Vector3.ProjectOnPlane(worldFwd, Vector3.up).normalized;
        if (flatFwd.sqrMagnitude < 0.0001f) flatFwd = Vector3.forward;
        float yaw = Mathf.Atan2(flatFwd.x, flatFwd.z) * Mathf.Rad2Deg;

        DrawReticle(flatPos, yaw);
        UpdateHint(cam);

        // 扳機長按 = 取樣（Placing 和 Preview 都可以重按重擺）
        bool trig = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.RTouch) > 0.6f;
        if (trig)
        {
            _triggerHeld += Time.deltaTime;
            if (!_triggerFired && _triggerHeld >= PlaceHoldSeconds)
            {
                _triggerFired = true;
                _capturedPos = flatPos;
                _capturedYaw = yaw;
                _state = State.Preview;
                DrawPreview(_capturedPos, _capturedYaw);
                StartCoroutine(Haptics.Pulse(OVRInput.Controller.RTouch, 1f, 0.6f, 0.15f));
                Debug.Log($"[RoomCenterSetup] Captured pose {_capturedPos}, yaw={_capturedYaw:F1}.");
            }
        }
        else { _triggerHeld = 0f; _triggerFired = false; }

        // A 鍵 = 確認（只在 Preview；刻意避開 B —— B 是全域 Restart）
        if (_state == State.Preview && OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch))
            _ = ConfirmAsync();
    }

    private async Task ConfirmAsync()
    {
        _state = State.Saving;
        UpdateHintText("Saving anchor...");

        bool ok = await CreateAndSaveAnchor(_capturedPos, _capturedYaw);
        if (!ok) { _state = State.Placing; return; }   // 失敗回到擺放，重試

        StartCoroutine(Haptics.Pulse(OVRInput.Controller.RTouch, 1f, 0.9f, 0.3f));
        ClearVisuals();
        Blocking = false;

        bool wasMidGame = _midGame;
        _state = State.Done;

        if (wasMidGame)
            await ApplyMidGameAsync();
        // 首次流程：GetRoomPoseAsync 的等待迴圈會接手，把 pose 交給 loader
    }

    /// 遊戲中重擺：重蓋虛擬房 → 更新 SpawnArea → 重新廣播給 guest → Restart
    private async Task ApplyMidGameAsync()
    {
        Pose pose = PoseFromAnchor();
        float yaw = pose.rotation.eulerAngles.y;

        var loader = FindAnyObjectByType<VirtualMRUKRoomLoader>();
        if (loader != null)
            await loader.ReloadAtPose(pose.position, yaw);

        if (SpawnArea.Instance != null)
            SpawnArea.Instance.SetPose(pose.position, yaw);

        if (GameFlowController.Instance != null)
        {
            GameFlowController.Instance.RepublishRoomPose();
            GameFlowController.Instance.Restart();
        }
        Debug.Log("[RoomCenterSetup] Mid-game re-place applied; game restarted.");
    }

    // ════════════════ 視覺 ════════════════

    private void EnsureVisuals()
    {
        if (_lineMat == null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            _lineMat = new Material(sh != null ? sh : Shader.Find("Unlit/Color"));
            _lineMat.SetColor("_BaseColor", new Color(0.2f, 1f, 0.5f));
            if (_lineMat.HasProperty("_Color")) _lineMat.SetColor("_Color", new Color(0.2f, 1f, 0.5f));
        }

        if (_reticle == null) _reticle = MakeLine("SetupReticle", 0.012f);
        if (_preview == null) _preview = MakeLine("SetupPreview", 0.02f);
        _preview.positionCount = 0;

        if (_hint == null)
        {
            var go = new GameObject("SetupHint");
            _hint = go.AddComponent<TextMesh>();
            _hint.characterSize = 0.012f;
            _hint.fontSize = 64;
            _hint.anchor = TextAnchor.MiddleCenter;
            _hint.alignment = TextAlignment.Center;
            _hint.color = Color.white;
        }
        UpdateHintText("SETUP  -  Put RIGHT controller on the floor\nat the ROOM CENTER, pointing FORWARD.\nHold TRIGGER 1s to place.");
    }

    private LineRenderer MakeLine(string name, float width)
    {
        var go = new GameObject(name);
        var lr = go.AddComponent<LineRenderer>();
        lr.material = _lineMat;
        lr.widthMultiplier = width;
        lr.useWorldSpace = true;
        lr.loop = false;
        lr.positionCount = 0;
        return lr;
    }

    private void DrawReticle(Vector3 pos, float yaw)
    {
        if (_reticle == null) return;
        Quaternion q = Quaternion.Euler(0f, yaw, 0f);
        float a = 0.25f;
        Vector3 f = q * Vector3.forward, r = q * Vector3.right;
        Vector3 y = Vector3.up * 0.005f;
        // 十字 + 前向箭頭桿（一筆畫：右-左-中-後-前）
        _reticle.positionCount = 5;
        _reticle.SetPositions(new[]
        {
            pos + r * a + y, pos - r * a + y, pos + y, pos - f * a + y, pos + f * (a * 1.6f) + y
        });
    }

    private void DrawPreview(Vector3 center, float yaw)
    {
        if (_preview == null) return;
        Quaternion q = Quaternion.Euler(0f, yaw, 0f);
        Vector3 f = q * Vector3.forward * RoomHalfSize;
        Vector3 r = q * Vector3.right * RoomHalfSize;
        Vector3 y = Vector3.up * 0.01f;
        // 3x3 外框（閉合）+ 正面中點小凸出
        _preview.positionCount = 7;
        _preview.SetPositions(new[]
        {
            center + f + r + y, center + f - r + y, center - f - r + y, center - f + r + y,
            center + f + r + y, center + f + y, center + f * 1.15f + y
        });
        UpdateHintText("Preview: green frame = 3x3 room.\nA = CONFIRM     hold TRIGGER = redo");
    }

    private void UpdateHint(Camera cam)
    {
        if (_hint == null) return;
        Vector3 fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
        if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
        _hint.transform.position = cam.transform.position + fwd * 1.2f;
        _hint.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
    }

    private void UpdateHintText(string s)
    {
        if (_hint != null) _hint.text = s;
    }

    private void ClearVisuals()
    {
        if (_reticle != null) { Destroy(_reticle.gameObject); _reticle = null; }
        if (_preview != null) { Destroy(_preview.gameObject); _preview = null; }
        if (_hint != null) { Destroy(_hint.gameObject); _hint = null; }
    }
}
