using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Host 端把 camera rig「持續」對齊 colocation alignment anchor。
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
    private bool _logged;

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

        if (!_anchor.Created) return;

        if (!_logged)
        {
            _logged = true;
            Debug.Log("[ColocationHostAlignment] Host camera rig now continuously aligned to the colocation anchor.");
        }

        Align(_anchor.transform);
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
