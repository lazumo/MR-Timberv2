using System.Collections;
using UnityEngine;

/// <summary>
/// 3x3 遊戲範圍的地板細線框：等 SpawnArea 定位好後，在房間邊界畫一圈細線，
/// 讓玩家知道可活動/生成範圍。純本地視覺，每個 client 各自生成（零接線）。
/// 用法：RoomBoundaryLine.Spawn(halfSize)。
/// </summary>
public class RoomBoundaryLine : MonoBehaviour
{
    private float _halfSize;

    public static RoomBoundaryLine Spawn(float halfSize = 1.5f)
    {
        // 一場只要一條線（重連/restart 不重複生成）
        var existing = FindAnyObjectByType<RoomBoundaryLine>();
        if (existing != null) return existing;

        var go = new GameObject("RoomBoundaryLine");
        var b = go.AddComponent<RoomBoundaryLine>();
        b._halfSize = halfSize;
        return b;
    }

    private IEnumerator Start()
    {
        // 等虛擬房間載入、SpawnArea 圓心定位完成
        while (SpawnArea.Instance == null || !SpawnArea.Instance.IsInitialized)
            yield return null;

        var lr = gameObject.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.loop = true;
        lr.positionCount = 4;
        lr.widthMultiplier = 0.01f;   // 細線
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;

        var mat = Resources.Load<Material>("VFX/RoomLineMat");
        if (mat != null) lr.sharedMaterial = mat;

        // 持續跟隨：SpawnArea 的 pose 之後還會被修正
        // （host：虛擬房 SetPose；client：收到 host 廣播的權威 pose），線跟著更新。
        while (true)
        {
            Vector3 c = SpawnArea.Instance.GetCenter();
            c.y = 0.02f;   // 貼地、避免 z-fighting
            Quaternion yaw = SpawnArea.Instance.GetRotation();

            float h = _halfSize;
            lr.SetPositions(new[]
            {
                c + yaw * new Vector3(-h, 0, -h),
                c + yaw * new Vector3(-h, 0,  h),
                c + yaw * new Vector3( h, 0,  h),
                c + yaw * new Vector3( h, 0, -h),
            });

            yield return new WaitForSeconds(0.5f);
        }
    }
}
