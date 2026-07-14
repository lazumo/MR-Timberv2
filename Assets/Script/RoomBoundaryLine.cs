using System.Collections;
using UnityEngine;

/// <summary>
/// 3x3 遊戲範圍的邊界標示：等 SpawnArea 定位好後，沿房間邊界「內側」散落
/// 一圈低飽和度的落葉（程式生成葉片 mesh，不吃任何美術資源），
/// 取代原本太突兀的綠色細線框。純本地視覺，每個 client 各自生成（零接線）；
/// 固定亂數 seed → 兩台頭盔看到同一批葉子。
/// 用法：RoomBoundaryLine.Spawn(halfSize)。
/// </summary>
public class RoomBoundaryLine : MonoBehaviour
{
    [Header("落葉散佈")]
    [Tooltip("內部填充的葉子數（全區都有、稀疏）")]
    public int leafCount = 45;
    [Tooltip("沿邊界排一圈、畫出方形輪廓的葉子數（密集、大致順著邊的方向）")]
    public int borderLeafCount = 64;
    [Tooltip("離邊界至少留這個距離（公尺），避免葉子壓在界線外")]
    public float edgeMargin = 0.05f;
    [Tooltip("內部填充的靠邊傾向：1 = 全面均勻，越大越往邊界集中")]
    public float edgeBias = 1.5f;
    [Tooltip("葉片長度範圍（公尺）")]
    public Vector2 leafLength = new Vector2(0.09f, 0.15f);

    // 低飽和秋葉色盤（乾枯感、不搶戲）
    private static readonly Color[] Palette =
    {
        new Color(0.55f, 0.58f, 0.42f),   // 乾橄欖綠
        new Color(0.62f, 0.66f, 0.50f),   // 淡灰綠
        new Color(0.72f, 0.63f, 0.45f),   // 枯黃
        new Color(0.70f, 0.54f, 0.40f),   // 土橘
        new Color(0.55f, 0.45f, 0.35f),   // 淺褐
    };

    private const int RandomSeed = 20260713;   // 固定 seed：host / client 散佈一致

    private float _halfSize;
    private Mesh _leafMesh;
    private Material[] _mats;

    public static RoomBoundaryLine Spawn(float halfSize = 1.5f)
    {
        // 一場只要一圈（重連/restart 不重複生成）
        var existing = FindAnyObjectByType<RoomBoundaryLine>();
        if (existing != null) return existing;

        var go = new GameObject("RoomBoundaryLeaves");
        var b = go.AddComponent<RoomBoundaryLine>();
        b._halfSize = halfSize;
        return b;
    }

    private IEnumerator Start()
    {
        // 等虛擬房間載入、SpawnArea 圓心定位完成（每 5 秒報一次還在等，方便 logcat 診斷）
        float nextWaitLog = Time.time + 5f;
        while (SpawnArea.Instance == null || !SpawnArea.Instance.IsInitialized)
        {
            if (Time.time >= nextWaitLog)
            {
                nextWaitLog = Time.time + 5f;
                Debug.Log("[RoomBoundaryLeaves] waiting for SpawnArea...");
            }
            yield return null;
        }

        BuildLeaves();
        Debug.Log($"[RoomBoundaryLeaves] {leafCount} fill + {borderLeafCount} border leaves scattered, center={SpawnArea.Instance.GetCenter()}, half={_halfSize}");

        // 持續跟隨：SpawnArea 的 pose 之後還會被修正
        // （host：虛擬房 SetPose；client：收到 host 廣播的權威 pose）。
        // 葉子是本物件的 children（房間 local 座標），跟著根一起動。
        while (true)
        {
            Vector3 c = SpawnArea.Instance.GetCenter();
            c.y = 0f;
            transform.SetPositionAndRotation(c, SpawnArea.Instance.GetRotation());
            yield return new WaitForSeconds(0.5f);
        }
    }

    private void BuildLeaves()
    {
        _leafMesh = MakeLeafMesh();

        var baseMat = Resources.Load<Material>("VFX/RoomLineMat");
        _mats = new Material[Palette.Length];
        for (int i = 0; i < Palette.Length; i++)
        {
            _mats[i] = baseMat != null ? new Material(baseMat)
                                       : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            _mats[i].SetColor("_BaseColor", Palette[i]);
            if (_mats[i].HasProperty("_Color")) _mats[i].SetColor("_Color", Palette[i]);
        }

        var rng = new System.Random(RandomSeed);
        float Next(float min, float max) => min + (float)rng.NextDouble() * (max - min);

        float range = _halfSize - edgeMargin;
        int idx = 0;

        // ── 內部填充：整個正方形都有（輕微靠邊傾向）──
        for (int i = 0; i < leafCount; i++)
        {
            float r = range * Mathf.Pow((float)rng.NextDouble(), 1f / Mathf.Max(1f, edgeBias));
            float along = Next(-r, r);
            Vector3 local = rng.Next(4) switch
            {
                0 => new Vector3(along, 0f, r),    // 前
                1 => new Vector3(along, 0f, -r),   // 後
                2 => new Vector3(r, 0f, along),    // 右
                _ => new Vector3(-r, 0f, along),   // 左
            };
            SpawnLeaf(local, Next(0f, 360f), rng, ref idx);
        }

        // ── 邊界一圈：葉子密集排出方形輪廓，大致順著邊的方向躺 ──
        int perEdge = Mathf.Max(1, borderLeafCount / 4);
        for (int e = 0; e < 4; e++)
        {
            for (int k = 0; k < perEdge; k++)
            {
                // 沿邊等距 + 少量抖動；貼著邊界內側一窄條
                float along = Mathf.Lerp(-range, range, (k + 0.5f) / perEdge) + Next(-0.06f, 0.06f);
                float inset = _halfSize - Next(0.03f, 0.12f);
                Vector3 local = e switch
                {
                    0 => new Vector3(along, 0f, inset),
                    1 => new Vector3(along, 0f, -inset),
                    2 => new Vector3(inset, 0f, along),
                    _ => new Vector3(-inset, 0f, along),
                };
                // 葉長軸大致沿著邊的方向（±30° 自然抖動），視覺上像葉子畫的線
                float edgeYaw = e < 2 ? 90f : 0f;
                SpawnLeaf(local, edgeYaw + Next(-30f, 30f), rng, ref idx);
            }
        }
    }

    private void SpawnLeaf(Vector3 local, float yaw, System.Random rng, ref int idx)
    {
        float Next(float min, float max) => min + (float)rng.NextDouble() * (max - min);

        // 跟舊綠線同高（2cm 起跳）：更貼地會被 passthrough 地板深度吃掉看不見
        local.y = 0.02f + 0.008f * (idx % 3);

        var leaf = new GameObject($"Leaf_{idx}");
        leaf.transform.SetParent(transform, false);
        leaf.transform.localPosition = local;
        // 指定朝向 + 微傾（落地的自然感）
        leaf.transform.localRotation = Quaternion.Euler(Next(-8f, 8f), yaw, Next(-8f, 8f));
        leaf.transform.localScale = Vector3.one * Next(leafLength.x, leafLength.y);

        leaf.AddComponent<MeshFilter>().sharedMesh = _leafMesh;
        var mr = leaf.AddComponent<MeshRenderer>();
        mr.sharedMaterial = _mats[rng.Next(_mats.Length)];
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        idx++;
    }

    /// 葉片形狀：XZ 平面上的尖頭橢圓（單位長、沿 +Z），雙面三角。
    private static Mesh MakeLeafMesh()
    {
        const int seg = 8;
        var verts = new Vector3[(seg + 1) * 2];
        for (int i = 0; i <= seg; i++)
        {
            float t = i / (float)seg;                                   // 0=葉柄 1=葉尖
            // Max(0,·)：Sin(PI) 浮點誤差是 -8.7e-8，負數的 0.8 次方 = NaN
            // → 頂點/bounds 全 NaN → 整個 mesh 被視錐剔除，一片都不會畫
            float halfW = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * t)), 0.8f) * 0.28f;
            verts[i * 2] = new Vector3(-halfW, 0f, t - 0.5f);
            verts[i * 2 + 1] = new Vector3(halfW, 0f, t - 0.5f);
        }

        var tris = new int[seg * 6 * 2];   // 每段 2 三角 × 正反兩面
        int k = 0;
        for (int i = 0; i < seg; i++)
        {
            int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
            tris[k++] = a; tris[k++] = c; tris[k++] = b;
            tris[k++] = b; tris[k++] = c; tris[k++] = d;
            tris[k++] = a; tris[k++] = b; tris[k++] = c;   // 反面
            tris[k++] = b; tris[k++] = d; tris[k++] = c;
        }

        var mesh = new Mesh { name = "BoundaryLeaf" };
        mesh.vertices = verts;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private void OnDestroy()
    {
        if (_leafMesh != null) Destroy(_leafMesh);
        if (_mats != null)
            foreach (var m in _mats)
                if (m != null) Destroy(m);
    }
}
