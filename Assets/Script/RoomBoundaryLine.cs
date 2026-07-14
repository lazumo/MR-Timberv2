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
    [Header("落葉散佈（堆積在方框邊上的自然感）")]
    [Tooltip("內部零星的葉子數（很稀疏，像被風吹散的幾片）")]
    public int strayCount = 16;
    [Tooltip("沿框稀疏撒一圈的葉子數（讓輪廓在堆與堆之間保持連續）")]
    public int sprinkleCount = 30;
    [Tooltip("邊上的小堆數（每堆 4~8 片，位置隨機）")]
    public int edgeClusterCount = 8;
    [Tooltip("離邊界至少留這個距離（公尺），避免葉子壓在界線外")]
    public float edgeMargin = 0.05f;
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
        Debug.Log($"[RoomBoundaryLeaves] {transform.childCount} leaves piled along the boundary, center={SpawnArea.Instance.GetCenter()}, half={_halfSize}");

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
        float ring = 2f * range;        // 每邊長
        float total = 4f * ring;        // 周長
        int idx = 0;

        // 周長參數 s（0 起點 = 左前角、順時針繞一圈）→ 邊界上往內縮 depth 的點
        Vector3 PerimeterPoint(float s, float depth)
        {
            s = ((s % total) + total) % total;
            int e = (int)(s / ring);
            float along = (s % ring) - range;
            return e switch
            {
                0 => new Vector3(along, 0f, range - depth),    // 前（左→右）
                1 => new Vector3(range - depth, 0f, -along),   // 右（前→後）
                2 => new Vector3(-along, 0f, -range + depth),  // 後（右→左）
                _ => new Vector3(-range + depth, 0f, along),   // 左（後→前）
            };
        }

        // 高斯（沿邊聚成一撮）與指數（貼線最密、往內遞減）分佈 —— 堆積的關鍵
        float Gauss(float sigma)
        {
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            return sigma * (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u1)) *
                                   System.Math.Cos(2.0 * System.Math.PI * u2));
        }
        float ExpDepth(float scale, float max) =>
            Mathf.Min(max, 0.02f - Mathf.Log(1f - (float)rng.NextDouble()) * scale);

        // ── 四個角落：明顯的堆（風把葉子掃進角落的感覺）──
        for (int c = 0; c < 4; c++)
        {
            float sc = c * ring;   // 角落在周長上的位置
            int n = rng.Next(8, 13);
            for (int k = 0; k < n; k++)
                SpawnLeaf(PerimeterPoint(sc + Gauss(0.14f), ExpDepth(0.07f, 0.35f)),
                          Next(0f, 360f), rng, ref idx);
        }

        // ── 邊上的小堆：位置隨機、一撮一撮 ──
        for (int j = 0; j < edgeClusterCount; j++)
        {
            float sc = Next(0f, total);
            int n = rng.Next(4, 9);
            for (int k = 0; k < n; k++)
                SpawnLeaf(PerimeterPoint(sc + Gauss(0.16f), ExpDepth(0.05f, 0.30f)),
                          Next(0f, 360f), rng, ref idx);
        }

        // ── 沿框稀疏撒一圈：堆與堆之間輪廓不斷線 ──
        for (int i = 0; i < sprinkleCount; i++)
            SpawnLeaf(PerimeterPoint(Next(0f, total), ExpDepth(0.04f, 0.25f)),
                      Next(0f, 360f), rng, ref idx);

        // ── 內部零星幾片：被吹散的感覺 ──
        for (int i = 0; i < strayCount; i++)
            SpawnLeaf(new Vector3(Next(-range, range), 0f, Next(-range, range)),
                      Next(0f, 360f), rng, ref idx);
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
