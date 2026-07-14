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
    [Tooltip("整圈邊界的葉子總數（不用太密集）")]
    public int leafCount = 44;
    [Tooltip("落葉帶寬度：從邊界往內延伸幾公尺")]
    public float bandWidth = 0.35f;
    [Tooltip("葉片長度範圍（公尺）")]
    public Vector2 leafLength = new Vector2(0.06f, 0.11f);

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
        // 等虛擬房間載入、SpawnArea 圓心定位完成
        while (SpawnArea.Instance == null || !SpawnArea.Instance.IsInitialized)
            yield return null;

        BuildLeaves();

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

        for (int i = 0; i < leafCount; i++)
        {
            // 平均分給四條邊，沿邊隨機、往內隨機縮進（帶狀散佈、角落自然重疊）
            int edge = i % 4;
            float along = Next(-_halfSize, _halfSize);
            float inset = _halfSize - Next(0.03f, bandWidth);

            Vector3 local = edge switch
            {
                0 => new Vector3(along, 0f, inset),    // 前
                1 => new Vector3(along, 0f, -inset),   // 後
                2 => new Vector3(inset, 0f, along),    // 右
                _ => new Vector3(-inset, 0f, along),   // 左
            };
            local.y = 0.006f + 0.006f * (i % 3);   // 貼地 + 微錯層避免 z-fighting

            var leaf = new GameObject($"Leaf_{i}");
            leaf.transform.SetParent(transform, false);
            leaf.transform.localPosition = local;
            // 隨機朝向 + 微傾（落地的自然感）
            leaf.transform.localRotation = Quaternion.Euler(Next(-8f, 8f), Next(0f, 360f), Next(-8f, 8f));
            leaf.transform.localScale = Vector3.one * Next(leafLength.x, leafLength.y);

            leaf.AddComponent<MeshFilter>().sharedMesh = _leafMesh;
            var mr = leaf.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _mats[rng.Next(_mats.Length)];
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }
    }

    /// 葉片形狀：XZ 平面上的尖頭橢圓（單位長、沿 +Z），雙面三角。
    private static Mesh MakeLeafMesh()
    {
        const int seg = 8;
        var verts = new Vector3[(seg + 1) * 2];
        for (int i = 0; i <= seg; i++)
        {
            float t = i / (float)seg;                                   // 0=葉柄 1=葉尖
            float halfW = Mathf.Pow(Mathf.Sin(Mathf.PI * t), 0.8f) * 0.28f;
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
