using System.Collections;
using System.Text;
using UnityEngine;

/// <summary>
/// 勝利排行榜（純本地視覺）：小精靈開跳 delay 秒後，浮在玩家視線前方、
/// 比頭高一點的位置（各 client 用自己的相機定位，只繞 Y 軸面向玩家）。
/// 內容：本組名次+總時間（高亮色）＋歷史前五名（本組入榜該行同高亮）。
/// 資料由 host 計算並透過 ClientRpc 廣播；restart 時 Clear()。
/// </summary>
public class VictoryRankingUI : MonoBehaviour
{
    private const string HighlightHex = "#FFD24A";   // 本組成績
    private const string HeaderHex = "#7FE0FF";
    private const string NormalHex = "#E8E8E8";
    private const string DimHex = "#9AA0B0";

    private static VictoryRankingUI _inst;
    private Transform _cam;

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

    private void BuildPanel(float myTime, int myRank, int totalGroups, float[] topTimes)
    {
        // 深色底板（URP Unlit、雙面）
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(quad.GetComponent<Collider>());
        quad.name = "Panel";
        quad.transform.SetParent(transform, false);
        quad.transform.localScale = new Vector3(0.62f, 0.56f, 1f);

        var baseMat = Resources.Load<Material>("VFX/RoomLineMat");
        var mat = baseMat != null ? new Material(baseMat)
                                  : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.SetColor("_BaseColor", new Color(0.09f, 0.09f, 0.13f, 1f));
        quad.GetComponent<MeshRenderer>().sharedMaterial = mat;

        // 文字（TextMesh + 內建字型，rich text 上色）
        var textGo = new GameObject("Text");
        textGo.transform.SetParent(transform, false);
        textGo.transform.localPosition = new Vector3(0f, 0f, -0.005f);   // 貼在板前一點點

        var tm = textGo.AddComponent<TextMesh>();
        tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        textGo.GetComponent<MeshRenderer>().sharedMaterial = tm.font.material;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.fontSize = 48;
        tm.characterSize = 0.009f;
        tm.richText = true;
        tm.text = BuildText(myTime, myRank, totalGroups, topTimes);
    }

    private static string BuildText(float myTime, int myRank, int totalGroups, float[] topTimes)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"<color={HeaderHex}><b>VICTORY!</b></color>");
        sb.AppendLine();
        sb.AppendLine($"<color={HighlightHex}>YOUR TEAM   #{myRank}   {Fmt(myTime)}</color>");
        sb.AppendLine($"<color={DimHex}>of {totalGroups} team{(totalGroups > 1 ? "s" : "")}</color>");
        sb.AppendLine();
        sb.AppendLine($"<color={HeaderHex}>— TOP 5 —</color>");

        for (int i = 0; i < topTimes.Length; i++)
        {
            bool isMine = (i + 1) == myRank;   // 本組進前五 → 該行同高亮
            string hex = isMine ? HighlightHex : NormalHex;
            sb.AppendLine($"<color={hex}>{i + 1}.   {Fmt(topTimes[i])}{(isMine ? "  ◄" : "")}</color>");
        }

        return sb.ToString();
    }

    private static string Fmt(float seconds) =>
        $"{(int)(seconds / 60f):00}:{Mathf.FloorToInt(seconds % 60f):00}";
}
