using UnityEngine;

/// <summary>
/// 失火警告橫布條：沿 3x3 房間四周生成一圈警戒帶（黑黃斜紋 + 警告三角 + 火焰 icon），
/// 貼圖 UV 持續橫向滾動、亮度閃爍，lifetime 到後淡出自毀。
/// 純本地視覺（各 client 自己生成），用法：RoomWarningBanner.Spawn(center, ...)。
/// 材質/貼圖從 Resources/VFX 載入，零接線。
/// </summary>
public class RoomWarningBanner : MonoBehaviour
{
    private Material _mat;           // instance（共用給四面）
    private float _life;
    private float _lifetime;
    private float _scrollSpeed;

    /// 在 center 周圍生成一圈布條。halfSize=房間半寬(3x3→1.5)，height=布條中心高度。
    public static RoomWarningBanner Spawn(
        Vector3 center,
        float halfSize = 1.5f,
        float height = 1.4f,
        float bandHeight = 0.35f,
        float lifetime = 10f,
        float scrollSpeed = 0.25f)
    {
        var srcMat = Resources.Load<Material>("VFX/WarningBannerMat");
        if (srcMat == null)
        {
            Debug.LogWarning("[RoomWarningBanner] Missing Resources/VFX/WarningBannerMat");
            return null;
        }

        var root = new GameObject("RoomWarningBanner");
        root.transform.position = center;
        if (SpawnArea.Instance != null)
            root.transform.rotation = SpawnArea.Instance.GetRotation();   // 跟虛擬房間的牆對齊

        var banner = root.AddComponent<RoomWarningBanner>();
        banner._lifetime = lifetime;
        banner._scrollSpeed = scrollSpeed;
        banner._mat = new Material(srcMat);   // instance，滾動不影響原資產

        // 四面牆：N/S/E/W，各一片 quad（雙面材質，室內外都看得到）
        var sides = new (Vector3 pos, float yaw)[]
        {
            (new Vector3(0, 0,  halfSize), 180f),
            (new Vector3(0, 0, -halfSize),   0f),
            (new Vector3( halfSize, 0, 0),  270f),
            (new Vector3(-halfSize, 0, 0),   90f),
        };

        foreach (var s in sides)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(quad.GetComponent<Collider>());
            quad.name = "BannerSide";
            quad.transform.SetParent(root.transform, false);
            quad.transform.localPosition = s.pos + Vector3.up * height;
            quad.transform.localRotation = Quaternion.Euler(0f, s.yaw, 0f);
            quad.transform.localScale = new Vector3(halfSize * 2f, bandHeight, 1f);
            quad.GetComponent<MeshRenderer>().sharedMaterial = banner._mat;
        }

        return banner;
    }

    private void Update()
    {
        _life += Time.deltaTime;

        if (_mat != null)
        {
            // 橫向滾動（警戒帶繞圈的感覺）
            _mat.mainTextureOffset = new Vector2(_life * _scrollSpeed, 0f);

            // 亮度閃爍（紅色警戒節奏，跟警報聲 2.2Hz 對拍）
            float blink = 0.75f + 0.25f * Mathf.Sin(_life * Mathf.PI * 2f * 2.2f);

            // 結尾 1 秒淡出
            float fade = Mathf.Clamp01((_lifetime - _life) / 1f);

            var c = Color.white * blink;
            c.a = fade;
            _mat.SetColor("_BaseColor", c);
        }

        if (_life >= _lifetime)
            Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (_mat != null) Destroy(_mat);
    }
}
