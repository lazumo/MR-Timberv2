using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 合體提示（老師 feedback 2026-07-09，取代藍色 buff 視覺）：
/// 滅火階段、未合體時，每 hintInterval 秒出現一次提示，持續 hintDuration 秒：
///   - 自己與隊友的滅火器手上各出現一個 anchor 標記（offset 可在 GameFlow Inspector 調）
///   - 一條光束從自己的 anchor 指向隊友的 anchor
///   - 光束顏色隨兩人距離變化：越近越紅、越遠越黃
///   - 兩個 anchor 上方各出現一個小動畫 UI（組員提供 prefab，拖進 GameFlow 的槽；留空則只有球+光束）
/// 純本地視覺，host / client 各自計時顯示（零接線，由 GameFlowController 生成）。
/// </summary>
public class ExtinguisherMergeHint : MonoBehaviour
{
    [Header("時機")]
    public float hintInterval = 15f;   // 每 N 秒提示一次
    public float hintDuration = 6f;    // 每次顯示 N 秒

    [Header("Anchor 位置（相對滅火器手的 world offset，可調）")]
    public Vector3 anchorOffset = new Vector3(0f, 0.18f, 0f);
    public float anchorScale = 0.06f;

    [Header("光束距離→顏色（越近越紅、越遠越黃）")]
    public float nearDistance = 0.25f;
    public float farDistance = 2.0f;
    public Color nearColor = Color.red;
    public Color yellowFar = Color.yellow;

    [Header("小動畫 UI（組員的 prefab；上方浮出）")]
    public GameObject uiPrefab;
    public float uiHeight = 0.12f;     // UI 在 anchor 上方多高

    public static ExtinguisherMergeHint Spawn(
        float interval, float duration, Vector3 offset, GameObject ui)
    {
        var existing = FindAnyObjectByType<ExtinguisherMergeHint>();
        if (existing != null) return existing;

        var go = new GameObject("ExtinguisherMergeHint");
        var h = go.AddComponent<ExtinguisherMergeHint>();
        h.hintInterval = interval;
        h.hintDuration = duration;
        h.anchorOffset = offset;
        h.uiPrefab = ui;
        return h;
    }

    private ProximitySwitchManager _psm;
    private float _nextPsmFind;

    private HandFollower _myHand, _partnerHand;
    private float _nextHandFind;

    private float _timer;
    private bool _showing;
    private float _showTimer;

    // runtime visuals
    private GameObject _myAnchor, _partnerAnchor;
    private LineRenderer _beam;
    private Material _mat;
    private readonly List<GameObject> _uiInstances = new();

    private void Update()
    {
        if (!ConditionsMet())
        {
            if (_showing) HideHint();
            _timer = 0f;
            return;
        }

        if (_showing)
        {
            _showTimer += Time.deltaTime;
            UpdateHintVisuals();
            if (_showTimer >= hintDuration)
            {
                HideHint();
                _timer = 0f;
            }
        }
        else
        {
            _timer += Time.deltaTime;
            if (_timer >= hintInterval)
                ShowHint();
        }
    }

    /// 只在「滅火階段、未合體、雙方滅火器手都在」時運作
    private bool ConditionsMet()
    {
        if (GameFlowController.Instance == null ||
            GameFlowController.Instance.CurrentPhase.Value != GamePhase.Firefighting)
            return false;

        // ProximitySwitchManager（合體狀態）
        if (_psm == null)
        {
            if (Time.time < _nextPsmFind) return false;
            _nextPsmFind = Time.time + 1f;
            _psm = FindAnyObjectByType<ProximitySwitchManager>();
            if (_psm == null) return false;
        }
        if (_psm.IsMergedNet.Value) return false;

        // 兩支滅火器手（HandFollower 是 networked，雙方 peer 都找得到）
        if (_myHand == null || _partnerHand == null)
        {
            if (Time.time < _nextHandFind) return false;
            _nextHandFind = Time.time + 1f;
            ResolveHands();
            if (_myHand == null || _partnerHand == null) return false;   // 單人測試 → 不提示
        }
        return true;
    }

    private void ResolveHands()
    {
        _myHand = null;
        _partnerHand = null;

        foreach (var h in FindObjectsByType<HandFollower>(FindObjectsSortMode.None))
        {
            if (h == null || h.NetworkObject == null || !h.NetworkObject.IsSpawned) continue;
            if (h.NetworkObject.IsOwner) _myHand = h;
            else                         _partnerHand = h;
        }
    }

    // ===================== 顯示 / 隱藏 =====================

    private void ShowHint()
    {
        _showing = true;
        _showTimer = 0f;

        if (_mat == null)
        {
            var baseMat = Resources.Load<Material>("VFX/RoomLineMat");
            _mat = baseMat != null ? new Material(baseMat)
                                   : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        }

        _myAnchor = MakeAnchor("MergeHintAnchor_Mine");
        _partnerAnchor = MakeAnchor("MergeHintAnchor_Partner");

        var beamGo = new GameObject("MergeHintBeam");
        beamGo.transform.SetParent(transform, false);
        _beam = beamGo.AddComponent<LineRenderer>();
        _beam.useWorldSpace = true;
        _beam.positionCount = 2;
        _beam.widthMultiplier = 0.008f;
        _beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _beam.receiveShadows = false;
        _beam.sharedMaterial = _mat;

        if (uiPrefab != null)
        {
            _uiInstances.Add(Instantiate(uiPrefab, transform));
            _uiInstances.Add(Instantiate(uiPrefab, transform));
        }

        UpdateHintVisuals();
    }

    // 橫躺的小圓柱（長軸水平、朝向隊友方向；Unity 圓柱軸向是 Y，靠旋轉放倒）
    private GameObject MakeAnchor(string name)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.transform.SetParent(transform, false);
        // 圓柱原始高度 2、直徑 1 → 直徑 anchorScale*0.5、長度 anchorScale
        go.transform.localScale = Vector3.one * (anchorScale * 0.5f);
        Destroy(go.GetComponent<Collider>());
        go.GetComponent<Renderer>().sharedMaterial = _mat;
        return go;
    }

    private void UpdateHintVisuals()
    {
        if (_myHand == null || _partnerHand == null) { HideHint(); return; }

        Vector3 a = _myHand.transform.position + anchorOffset;
        Vector3 b = _partnerHand.transform.position + anchorOffset;

        // 圓柱橫躺：長軸保持水平、對齊兩人連線的方向
        Vector3 flat = b - a;
        flat.y = 0f;
        Quaternion lie = flat.sqrMagnitude > 0.0001f
            ? Quaternion.FromToRotation(Vector3.up, flat.normalized)
            : Quaternion.Euler(0f, 0f, 90f);

        if (_myAnchor != null)
        {
            _myAnchor.transform.position = a;
            _myAnchor.transform.rotation = lie;
        }
        if (_partnerAnchor != null)
        {
            _partnerAnchor.transform.position = b;
            _partnerAnchor.transform.rotation = lie;
        }

        if (_beam != null)
        {
            _beam.SetPosition(0, a);
            _beam.SetPosition(1, b);
        }

        // 越近越紅、越遠越黃
        float t = Mathf.InverseLerp(nearDistance, farDistance, Vector3.Distance(a, b));
        Color c = Color.Lerp(nearColor, yellowFar, t);
        if (_mat != null) _mat.SetColor("_BaseColor", c);

        // UI 浮在兩個 anchor 上方，面向自己的頭
        var cam = Camera.main;
        for (int i = 0; i < _uiInstances.Count; i++)
        {
            var ui = _uiInstances[i];
            if (ui == null) continue;
            ui.transform.position = (i == 0 ? a : b) + Vector3.up * uiHeight;
            if (cam != null)
                ui.transform.rotation = Quaternion.LookRotation(ui.transform.position - cam.transform.position);
        }
    }

    private void HideHint()
    {
        _showing = false;
        _showTimer = 0f;

        if (_myAnchor != null) Destroy(_myAnchor);
        if (_partnerAnchor != null) Destroy(_partnerAnchor);
        if (_beam != null) Destroy(_beam.gameObject);
        _myAnchor = _partnerAnchor = null;
        _beam = null;

        foreach (var ui in _uiInstances)
            if (ui != null) Destroy(ui);
        _uiInstances.Clear();
    }

    private void OnDestroy()
    {
        if (_mat != null) Destroy(_mat);
    }
}
