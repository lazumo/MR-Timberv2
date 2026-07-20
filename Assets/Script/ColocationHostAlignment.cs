using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Host 端把 camera rig 對齊 colocation alignment anchor —— 只在進場時對齊「一次」。
/// （曾經是每幀持續對齊，但 anchor 追蹤估計的噪音會帶著整個世界連續滑動，
/// 使用者實測樹葉範圍一直漂移後決定改回一次性。代價：host 中途 recenter/休眠喚醒
/// 的話世界會偏掉，demo 時避免這兩個動作即可。）
/// Meta 的 colocation building block 只讓 guest 對齊（AlignCameraToAnchor 只加在 guest），
/// host 停留在開機 tracking frame —— 一旦 host recenter / tracking 漂移修正，
/// 世界軸就偏離鎖在物理空間的 anchor，綠框/房間/所有視覺就跟 anchor 軸歪掉。
/// 對齊之後：兩台的世界座標系恆等於 anchor 座標系（anchor ≈ 原點、yaw≈0），
/// 房間 yaw=0 = anchor 軸永遠成立，recenter 也不會歪。
/// 對齊數學照抄 Meta 的 AlignCameraToAnchor（該 class 是 internal，無法直接使用）。
/// </summary>
[DefaultExecutionOrder(10)]
public class ColocationHostAlignment : MonoBehaviour
{
    private OVRSpatialAnchor _anchor;
    private Transform _rig;
    private float _nextFind;
    private bool _alignedOnce;   // 只對齊一次：持續每幀對齊會讓 anchor 估計的噪音帶著整個世界滑動

    /// 零接線生成（GameFlowController.OnNetworkSpawn 呼叫；guest 上是 no-op）
    public static void Ensure()
    {
        if (FindAnyObjectByType<ColocationHostAlignment>() != null) return;
        new GameObject("ColocationHostAlignment").AddComponent<ColocationHostAlignment>();
    }

    private void Update()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsHost) return;   // guest 已由 Meta 的 AlignCameraToAnchor 對齊

        if (_anchor == null || _rig == null)
        {
            if (Time.time < _nextFind) return;
            _nextFind = Time.time + 1f;

            if (_anchor == null) _anchor = FindAnyObjectByType<OVRSpatialAnchor>();
            if (_rig == null)
            {
                var rig = FindAnyObjectByType<OVRCameraRig>();
                if (rig != null) _rig = rig.transform;
            }
            if (_anchor == null || _rig == null) return;
        }

        if (_alignedOnce) return;   // 進場對齊一次就收工，之後世界不再被動
        if (!_anchor.Created) return;

        Align(_anchor.transform);
        _alignedOnce = true;
        Debug.Log("[ColocationHostAlignment] Host aligned to colocation anchor ONCE at session start (continuous alignment disabled).");
    }

    private void Align(Transform anchorTransform)
    {
        var cam = Camera.main;
        if (cam == null) return;

        var prevScale = anchorTransform.localScale;
        anchorTransform.localScale = Vector3.one;

        // anchor 的 tracking-space 姿態（不受 rig 位置影響的「物理」姿態）
        var trackingSpacePose = anchorTransform.ToTrackingSpacePose(cam);
        anchorTransform.SetPositionAndRotation(trackingSpacePose.position, trackingSpacePose.orientation);

        // 把 rig 變換到 anchor 的反姿態 → 世界座標系以 anchor 為原點/軸向
        _rig.position = anchorTransform.InverseTransformPoint(Vector3.zero);
        _rig.eulerAngles = new Vector3(0f, -anchorTransform.eulerAngles.y, 0f);

        // 還原 anchor 的 world-space 姿態，維持 world-locked 渲染
        var worldSpacePose = trackingSpacePose.ToWorldSpacePose(cam);
        anchorTransform.SetPositionAndRotation(worldSpacePose.position, worldSpacePose.orientation);

        anchorTransform.localScale = prevScale;
    }
}
