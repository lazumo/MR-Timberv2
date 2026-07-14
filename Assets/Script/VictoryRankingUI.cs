using System.Collections;
using UnityEngine;

/// <summary>
/// 勝利排行榜（純本地視覺）：小精靈開跳 delay 秒後，浮在玩家視線前方、
/// 比頭高一點的位置（各 client 用自己的相機定位，只繞 Y 軸面向玩家）。
/// 版面照排行榜 UI 慣例：圓角漸層深色底板、標題帶、本組成績卡（高亮底色）、
/// 前五名列表（金/銀/銅名次色、行底條、本組入榜行同高亮 + ◄ YOU）。
/// 全程式生成（圓角貼圖 + TextMesh），零素材零接線；restart 時 Clear()。
/// </summary>
public class VictoryRankingUI : MonoBehaviour
{
    // 色票
    private static readonly Color Gold = FromHex(0xFFD24A);
    private static readonly Color Silver = FromHex(0xC9D2DC);
    private static readonly Color Bronze = FromHex(0xD09A62);
    private static readonly Color TextNormal = FromHex(0xE8E8E8);
    private static readonly Color TextDim = FromHex(0x9AA0B0);
    private static readonly Color Header = FromHex(0x7FE0FF);
    private static readonly Color PanelTop = new Color(0.13f, 0.14f, 0.22f, 0.96f);
    private static readonly Color PanelBottom = new Color(0.07f, 0.07f, 0.12f, 0.96f);

    private static VictoryRankingUI _inst;
    private Transform _cam;

    private Texture2D _panelTex, _rowTex;
    private Material _panelMat;
    private Font _font;

    public static void Show(float delay, float myTime, int myRank, int totalGroups, float[] topTimes)
    {
        Clear();
        var go = new GameObject("VictoryRankingUI");
        _inst = go.AddComponent<VictoryRankingUI>();
        _inst.StartCoroutine(_inst.ShowRoutine(delay, myTime, myRank, totalGroups, topTimes));
    }

    public static void Clear()
    {
        if (_inst != null) Destroy(_inst.gameObject);
        _inst = null;
    }

    private IEnumerator ShowRoutine(float delay, float myTime, int myRank, int totalGroups, float[] topTimes)
    {
        yield return new WaitForSeconds(delay);

        _cam = Camera.main != null ? Camera.main.transform : null;

        Vector3 eye = _cam != null ? _cam.position : Vector3.up * 1.6f;
        Vector3 fwd = _cam != null ? Vector3.ProjectOnPlane(_cam.forward, Vector3.up).normalized : Vector3.forward;
        if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;

        // 視線前方 1.4m、比眼睛高 0.35m（= 比頭高一點）
        transform.position = eye + fwd * 1.4f + Vector3.up * 0.35f;

        BuildPanel(myTime, myRank, totalGroups, topTimes);

        // 彈出動畫（ease-out）
        float t = 0f;
        while (t < 0.3f)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / 0.3f);
            transform.localScale = Vector3.one * (1f - (1f - k) * (1f - k));   // easeOutQuad
            yield return null;
        }
        transform.localScale = Vector3.one;
    }

    private void Update()
    {
        if (_cam == null) return;

        // 只繞 Y 軸面向玩家（保持直立、不跟著低頭抬頭傾斜）
        Vector3 look = transform.position - _cam.position;
        look.y = 0f;
        if (look.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(look);
    }

    // ===================== 版面 =====================

    private void BuildPanel(float myTime, int myRank, int totalGroups, float[] topTimes)
    {
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // 底板：圓角 + 上下漸層 + 細邊框（貼圖程式生成）
        _panelTex = MakeRoundedRect(256, 288, 22f, PanelTop, PanelBottom, new Color(1f, 1f, 1f, 0.10f));
        _rowTex = MakeRoundedRect(256, 48, 14f, Color.white, Color.white, Color.clear);   // 白色圓角條，靠材質染色

        _panelMat = MakeTransparentUnlit(_panelTex, Color.white);
        MakeQuad("Panel", Vector3.zero, new Vector2(0.60f, 0.64f), _panelMat);

        // ── 標題帶 ─────────────────────────────
        MakeText("Title", new Vector3(0f, 0.245f, -0.004f), "VICTORY!", 72, 0.010f, Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
        MakeText("Subtitle", new Vector3(0f, 0.198f, -0.004f), "TEAM TIME RANKING", 40, 0.006f, Header, TextAnchor.MiddleCenter);

        // ── 本組成績卡（高亮底色）───────────────
        MakeRow(new Vector3(0f, 0.135f, -0.003f), new Vector2(0.52f, 0.075f), new Color(Gold.r, Gold.g, Gold.b, 0.22f));
        MakeText("MyRank", new Vector3(-0.24f, 0.135f, -0.006f), $"YOUR TEAM  #{myRank}", 52, 0.0075f, Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
        MakeText("MyTime", new Vector3(0.24f, 0.135f, -0.006f), Fmt(myTime), 52, 0.0075f, Gold, TextAnchor.MiddleRight, FontStyle.Bold);
        MakeText("MyTotal", new Vector3(0f, 0.085f, -0.004f), $"of {totalGroups} team{(totalGroups > 1 ? "s" : "")}", 36, 0.0055f, TextDim, TextAnchor.MiddleCenter);

        // ── 分隔線 ─────────────────────────────
        MakeRow(new Vector3(0f, 0.055f, -0.003f), new Vector2(0.50f, 0.0025f), new Color(1f, 1f, 1f, 0.25f));
        MakeText("Top5Header", new Vector3(0f, 0.025f, -0.004f), "TOP 5", 40, 0.006f, Header, TextAnchor.MiddleCenter, FontStyle.Bold);

        // ── 前五名列表 ─────────────────────────
        float y = -0.030f;
        const float step = 0.062f;
        for (int i = 0; i < topTimes.Length && i < 5; i++)
        {
            int rank = i + 1;
            bool isMine = rank == myRank;

            Color rankColor = rank == 1 ? Gold : rank == 2 ? Silver : rank == 3 ? Bronze : TextNormal;
            Color rowColor = isMine
                ? new Color(Gold.r, Gold.g, Gold.b, 0.22f)                       // 本組入榜 → 金色底條
                : new Color(1f, 1f, 1f, i % 2 == 0 ? 0.06f : 0.03f);             // 其他 → 交錯淡底

            MakeRow(new Vector3(0f, y, -0.003f), new Vector2(0.52f, 0.055f), rowColor);
            MakeText($"Rank{rank}", new Vector3(-0.24f, y, -0.006f), Ordinal(rank), 48, 0.007f, rankColor, TextAnchor.MiddleLeft,
                     rank <= 3 || isMine ? FontStyle.Bold : FontStyle.Normal);
            MakeText($"Time{rank}", new Vector3(0.24f, y, -0.006f), Fmt(topTimes[i]) + (isMine ? "  ◄ YOU" : ""), 48, 0.007f,
                     isMine ? Gold : TextNormal, TextAnchor.MiddleRight,
                     isMine ? FontStyle.Bold : FontStyle.Normal);
            y -= step;
        }
    }

    private GameObject MakeQuad(string name, Vector3 localPos, Vector2 size, Material mat)
    {
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(quad.GetComponent<Collider>());
        quad.name = name;
        quad.transform.SetParent(transform, false);
        quad.transform.localPosition = localPos;
        quad.transform.localScale = new Vector3(size.x, size.y, 1f);
        quad.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return quad;
    }

    private void MakeRow(Vector3 localPos, Vector2 size, Color tint)
    {
        MakeQuad("Row", localPos, size, MakeTransparentUnlit(_rowTex, tint));
    }

    private void MakeText(string name, Vector3 localPos, string text, int fontSize, float charSize,
                          Color color, TextAnchor anchor, FontStyle style = FontStyle.Normal)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPos;

        var tm = go.AddComponent<TextMesh>();
        tm.font = _font;
        go.GetComponent<MeshRenderer>().sharedMaterial = _font.material;
        tm.anchor = anchor;
        tm.alignment = anchor == TextAnchor.MiddleLeft ? TextAlignment.Left
                     : anchor == TextAnchor.MiddleRight ? TextAlignment.Right
                     : TextAlignment.Center;
        tm.fontSize = fontSize;
        tm.characterSize = charSize;
        tm.fontStyle = style;
        tm.color = color;
        tm.text = text;
    }

    // ===================== 程式生成素材 =====================

    /// 圓角矩形貼圖：上下漸層 + 邊框（borderColor.a=0 就沒邊框）
    private static Texture2D MakeRoundedRect(int w, int h, float radius, Color top, Color bottom, Color border)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };

        float halfW = w * 0.5f, halfH = h * 0.5f;
        for (int y = 0; y < h; y++)
        {
            Color fill = Color.Lerp(bottom, top, (y + 0.5f) / h);
            for (int x = 0; x < w; x++)
            {
                // 圓角矩形 SDF：>0 在外、<0 在內
                float dx = Mathf.Abs(x + 0.5f - halfW) - (halfW - radius);
                float dy = Mathf.Abs(y + 0.5f - halfH) - (halfH - radius);
                float dist = new Vector2(Mathf.Max(dx, 0f), Mathf.Max(dy, 0f)).magnitude
                           + Mathf.Min(Mathf.Max(dx, dy), 0f) - radius;

                Color c;
                if (dist > 0f) c = Color.clear;
                else if (border.a > 0f && dist > -2.5f) c = border;   // 邊框帶
                else c = fill;

                // 邊緣 1px 抗鋸齒
                if (dist > -1f && dist <= 0f) c.a *= -dist;

                tex.SetPixel(x, y, c);
            }
        }

        tex.Apply(false, true);
        return tex;
    }

    /// URP Unlit 半透明材質（alpha blend；shader 已隨 RoomLineMat 進 build）
    private static Material MakeTransparentUnlit(Texture2D tex, Color tint)
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = 3000;
        mat.mainTexture = tex;
        mat.SetTexture("_BaseMap", tex);
        mat.SetColor("_BaseColor", tint);
        return mat;
    }

    private static Color FromHex(int hex) =>
        new Color(((hex >> 16) & 0xFF) / 255f, ((hex >> 8) & 0xFF) / 255f, (hex & 0xFF) / 255f, 1f);

    private static string Ordinal(int n) => n switch
    {
        1 => "1st", 2 => "2nd", 3 => "3rd", _ => $"{n}th",
    };

    private static string Fmt(float seconds) =>
        $"{(int)(seconds / 60f):00}:{Mathf.FloorToInt(seconds % 60f):00}";

    private void OnDestroy()
    {
        if (_panelTex != null) Destroy(_panelTex);
        if (_rowTex != null) Destroy(_rowTex);
        // 每個 quad 的材質實例
        foreach (var mr in GetComponentsInChildren<MeshRenderer>())
            if (mr != null && mr.sharedMaterial != null && mr.sharedMaterial != _font?.material)
                Destroy(mr.sharedMaterial);
    }
}
