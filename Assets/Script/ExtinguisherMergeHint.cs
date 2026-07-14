using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 合體提示（老師 feedback 2026-07-09，取代藍色 buff 視覺）：
/// 滅火階段、未合體時，「充能完成」（= ChargeReady 叮聲響起、可以合體的那一刻）
/// 出現提示，並且一直顯示到真的合體成水管才消失：
///   - 自己與隊友的滅火器手上各出現一個 anchor 小圓柱（offset 可在 GameFlow Inspector 調）
///   - 一條發光光束（外層柔光 + 內層亮芯，UV 流動 + 寬度脈動）連向隊友的 anchor
///   - 光束顏色隨兩人距離變化：越近越紅、越遠越黃
///   - 兩個 anchor 上方各出現一個小動畫 UI（組員提供 prefab，拖進 GameFlow 的槽）
/// 純本地視覺，host / client 各自計時顯示（時間軸與 ExtinguisherChargeParticle 的
/// 充能音效一致：都是「分離累積 psm.extinguisherGlowAfter 秒」、都用 IsMergedNet 重置）。
/// </summary>
public class ExtinguisherMergeHint : MonoBehaviour
{
    [Header("Anchor（相對滅火器手的 world offset，可調）")]
    public Vector3 anchorOffset = new Vector3(0f, 0.18f, 0f);
    public float anchorScale = 0.06f;

    [Header("光束距離→顏色（越近越紅、越遠越黃）")]
    public float nearDistance = 0.25f;
    public float farDistance = 2.0f;
    public Color nearColor = Color.red;
    public Color farColor = Color.yellow;

    [Header("光束外觀")]
    public float glowWidth = 0.06f;    // 外層柔光寬度
    public float coreWidth = 0.032f;   // 內層亮芯寬度（Trail67 彗星串要夠寬才讀得到形狀）
    public float pulseSpeed = 5f;      // 寬度脈動頻率
    public float scrollSpeed = 1.2f;   // 流光速度（負值可反向）
    [Tooltip("亮芯貼圖覆寫（建議用 seamless 的 _Emission 貼圖）；留空 = 材質上烤好的 Hovl Trail67")]
    public Texture2D coreTextureOverride;
    [Tooltip("false = 亮芯用貼圖原色（適合綠/藍等非紅黃系貼圖，避免染髒）；距離紅黃訊號仍在柔光+anchor 上")]
    public bool tintCoreByDistance = true;

    [Header("Editor 預覽（Play Mode 按此鍵在面前生一條假光束，不用雙人）")]
    public KeyCode editorPreviewKey = KeyCode.B;

    [Header("小動畫 UI（組員的 prefab；上方浮出）")]
    public GameObject uiPrefab;
    public float uiHeight = 0.12f;     // UI 在 anchor 上方多高
    [Tooltip("true = 只在兩人中間生一個（適合已同時畫左右的教學面板）；false = 兩個 anchor 上各生一個")]
    public bool uiSingleAtMidpoint = true;
    [Tooltip("生成後乘上的縮放（教學 prefab 內建 scale 很大，通常要縮小很多）")]
    public float uiScale = 0.02f;
    [Tooltip("面板背對你時勾這個轉 180°")]
    public bool uiFlip180 = false;

    public static ExtinguisherMergeHint Spawn(
        Vector3 offset, GameObject ui, Texture2D beamTexture = null, bool tintCore = true)
    {
        var existing = FindAnyObjectByType<ExtinguisherMergeHint>();
        if (existing != null) return existing;

        var go = new GameObject("ExtinguisherMergeHint");
        var h = go.AddComponent<ExtinguisherMergeHint>();
        h.anchorOffset = offset;
        h.uiPrefab = ui;
        h.coreTextureOverride = beamTexture;
        h.tintCoreByDistance = tintCore;
        return h;
    }

    private ProximitySwitchManager _psm;
    private float _nextPsmFind;

    private HandFollower _myHand, _partnerHand;
    private float _nextHandFind;

    private bool _showing;

    // runtime visuals
    private GameObject _myAnchor, _partnerAnchor;
    private LineRenderer _glow, _core, _core2;
    private Material _glowMat, _coreMat, _core2Mat, _anchorMat;
    private Texture2D _beamTex;
    private readonly List<GameObject> _uiInstances = new();

    private bool _previewMode;

    private void Update()
    {
        // ===== Editor 外觀預覽：按鍵切換，在鏡頭前擺一條固定光束 =====
        if (Application.isEditor && editorPreviewKey != KeyCode.None && Input.GetKeyDown(editorPreviewKey))
        {
            _previewMode = !_previewMode;
            if (_previewMode && !_showing) ShowHint();
            else if (!_previewMode && _showing) HideHint();
        }
        if (_previewMode)
        {
            if (_showing) UpdateHintVisuals();
            return;
        }

        if (!ConditionsMet())
        {
            // 合體 / 離開滅火階段 / 手不見了 → 收掉（重新充能後 IsChargedNet 會再亮）
            if (_showing) HideHint();
            return;
        }

        // 充能狀態直接讀 server 同步的 IsChargedNet —— 跟 ChargeReady 叮聲、
        // 震動同一幀出現，直到合體（或充能被重置）才收
        bool charged = _psm.IsChargedNet.Value;

        if (charged && !_showing) ShowHint();
        else if (!charged && _showing) HideHint();

        if (_showing)
            UpdateHintVisuals();
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

        if (_myHand != null && _partnerHand != null)
            Debug.Log("[MergeHint] Hands resolved (mine + partner).");
    }

    // ===================== 顯示 / 隱藏 =====================

    private void ShowHint()
    {
        _showing = true;
        Debug.Log("[MergeHint] Charge ready — showing beam until merge.");

        EnsureMaterials();

        _myAnchor = MakeAnchor("MergeHintAnchor_Mine");
        _partnerAnchor = MakeAnchor("MergeHintAnchor_Partner");

        _glow = MakeBeam("MergeHintBeamGlow", _glowMat, glowWidth);
        _core = MakeBeam("MergeHintBeamCore", _coreMat, coreWidth);
        _core2 = MakeBeam("MergeHintBeamCore2", _core2Mat, coreWidth);

        if (uiPrefab != null)
        {
            int count = uiSingleAtMidpoint ? 1 : 2;
            for (int i = 0; i < count; i++)
            {
                var ui = Instantiate(uiPrefab, transform);
                ui.transform.localScale = uiPrefab.transform.localScale * uiScale;
                _uiInstances.Add(ui);
            }
        }

        UpdateHintVisuals();
    }

    private void EnsureMaterials()
    {
        if (_beamTex == null) _beamTex = MakeBeamTexture();

        if (_glowMat == null || _coreMat == null)
        {
            var baseMat = Resources.Load<Material>("VFX/MergeBeamMat");
            if (baseMat == null)
            {
                // 保底：沒有光束材質就用邊界線的 Unlit（不會發光但至少看得到）
                baseMat = Resources.Load<Material>("VFX/RoomLineMat");
            }

            // 外層柔光：程式生成的柔邊貼圖（負責距離顏色暈染）
            _glowMat = new Material(baseMat);
            _glowMat.mainTexture = _beamTex;

            // 內層亮芯：保留材質上烤好的 Hovl Trail67（白色彗星拖尾），
            // Tile + 捲動 = 一串發光彗星。彗星頭肥尾細會讓單向那層「一頭重」，
            // 所以跑兩層方向相反的流（我→隊友 + 隊友→我），亮度對稱、雙向靠攏。
            _coreMat = new Material(baseMat);
            _coreMat.mainTextureScale = new Vector2(2f, 1f);    // 約每 0.5m 一顆彗星

            _core2Mat = new Material(baseMat);
            _core2Mat.mainTextureScale = new Vector2(-2f, 1f);  // 鏡像 → 彗星朝反方向

            // 使用者挑的貼圖（GameFlow Inspector 拖入）覆寫亮芯
            if (coreTextureOverride != null)
            {
                _coreMat.mainTexture = coreTextureOverride;
                _core2Mat.mainTexture = coreTextureOverride;
            }
        }

        if (_anchorMat == null)
        {
            var lineMat = Resources.Load<Material>("VFX/RoomLineMat");
            _anchorMat = lineMat != null ? new Material(lineMat)
                                         : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        }
    }

    /// 柔邊光束貼圖：V 軸中央亮、邊緣淡出（光暈感），U 軸帶不規則亮度（流動時像能量）
    private Texture2D MakeBeamTexture()
    {
        const int w = 128, hgt = 16;
        var tex = new Texture2D(w, hgt, TextureFormat.RGBA32, false)
        {
            wrapModeU = TextureWrapMode.Repeat,
            wrapModeV = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };

        for (int y = 0; y < hgt; y++)
        {
            float v = Mathf.Abs((y + 0.5f) / hgt - 0.5f) * 2f;   // 0=中央 1=邊緣
            float edge = Mathf.Pow(1f - v, 2.2f);                 // 柔邊 falloff
            for (int x = 0; x < w; x++)
            {
                // 沿著光束的能量波紋（兩層 sin 疊 Perlin，捲動時有流動感）
                float u = x / (float)w;
                float streak = 0.62f
                             + 0.22f * Mathf.Sin(u * Mathf.PI * 2f * 3f)
                             + 0.16f * Mathf.PerlinNoise(u * 7.3f, y * 0.31f);
                float a = Mathf.Clamp01(edge * streak);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }

        tex.Apply(false, true);
        return tex;
    }

    private LineRenderer MakeBeam(string name, Material mat, float width)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.widthMultiplier = width;
        lr.textureMode = LineTextureMode.Tile;   // U 沿光束重複 → 捲動看得到流動
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.sharedMaterial = mat;
        return lr;
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
        go.GetComponent<Renderer>().sharedMaterial = _anchorMat;
        return go;
    }

    private void UpdateHintVisuals()
    {
        Vector3 a, b;

        if (_previewMode)
        {
            // 預覽：鏡頭前 1.2m、左右各 0.7m 的固定兩點
            var pc = Camera.main != null ? Camera.main.transform : transform;
            Vector3 mid0 = pc.position + pc.forward * 1.2f;
            a = mid0 - pc.right * 0.7f;
            b = mid0 + pc.right * 0.7f;
        }
        else
        {
            if (_myHand == null || _partnerHand == null) { HideHint(); return; }
            a = _myHand.transform.position + anchorOffset;
            b = _partnerHand.transform.position + anchorOffset;
        }

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

        // 越近越紅、越遠越黃
        float t = Mathf.InverseLerp(nearDistance, farDistance, Vector3.Distance(a, b));
        Color c = Color.Lerp(nearColor, farColor, t);

        // 寬度脈動（呼吸感）
        float pulse = 1f + 0.18f * Mathf.Sin(Time.time * pulseSpeed);

        if (_glow != null)
        {
            _glow.SetPosition(0, a);
            _glow.SetPosition(1, b);
            _glow.widthMultiplier = glowWidth * pulse;
        }
        if (_core != null)
        {
            _core.SetPosition(0, a);
            _core.SetPosition(1, b);
            _core.widthMultiplier = coreWidth * pulse;
        }
        if (_core2 != null)
        {
            _core2.SetPosition(0, a);
            _core2.SetPosition(1, b);
            _core2.widthMultiplier = coreWidth * pulse;
        }

        if (_glowMat != null)
        {
            var gc = c; gc.a = 0.55f;
            _glowMat.color = gc;
            _glowMat.mainTextureOffset = new Vector2(-Time.time * scrollSpeed, 0f);
        }
        if (_coreMat != null || _core2Mat != null)
        {
            // 亮芯偏白，看起來像過曝的光（兩層各半亮度，疊加後跟原本一樣亮）；
            // 不染色模式 = 貼圖原色（綠/藍系貼圖乘紅黃會變髒）
            var cc = tintCoreByDistance ? Color.Lerp(c, Color.white, 0.65f) : Color.white;
            cc.a = 0.55f;
            if (_coreMat != null)
            {
                _coreMat.color = cc;
                _coreMat.mainTextureOffset = new Vector2(-Time.time * scrollSpeed * 1.8f, 0f);
            }
            if (_core2Mat != null)
            {
                _core2Mat.color = cc;
                // 反向捲動：另一串彗星從隊友那端流回來（雙向靠攏）
                _core2Mat.mainTextureOffset = new Vector2(Time.time * scrollSpeed * 1.8f, 0f);
            }
        }
        if (_anchorMat != null)
            _anchorMat.SetColor("_BaseColor", c);

        // UI 浮出（單一置中 = 兩人中間；否則兩個 anchor 上各一個），面向自己的頭
        var cam = Camera.main;
        Vector3 mid = (a + b) * 0.5f;
        for (int i = 0; i < _uiInstances.Count; i++)
        {
            var ui = _uiInstances[i];
            if (ui == null) continue;

            Vector3 basePos = uiSingleAtMidpoint ? mid : (i == 0 ? a : b);
            ui.transform.position = basePos + Vector3.up * uiHeight;

            if (cam != null)
            {
                Quaternion look = Quaternion.LookRotation(ui.transform.position - cam.transform.position);
                if (uiFlip180) look *= Quaternion.Euler(0f, 180f, 0f);
                ui.transform.rotation = look;
            }
        }
    }

    private void HideHint()
    {
        _showing = false;

        if (_myAnchor != null) Destroy(_myAnchor);
        if (_partnerAnchor != null) Destroy(_partnerAnchor);
        if (_glow != null) Destroy(_glow.gameObject);
        if (_core != null) Destroy(_core.gameObject);
        if (_core2 != null) Destroy(_core2.gameObject);
        _myAnchor = _partnerAnchor = null;
        _glow = _core = _core2 = null;

        foreach (var ui in _uiInstances)
            if (ui != null) Destroy(ui);
        _uiInstances.Clear();
    }

    private void OnDestroy()
    {
        if (_glowMat != null) Destroy(_glowMat);
        if (_coreMat != null) Destroy(_coreMat);
        if (_core2Mat != null) Destroy(_core2Mat);
        if (_anchorMat != null) Destroy(_anchorMat);
        if (_beamTex != null) Destroy(_beamTex);
    }
}
