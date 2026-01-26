using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class HandFollower : NetworkBehaviour
{
    [Header("追蹤設定")]
    [SerializeField] private string cameraRigName = "[BuildingBlock] Camera Rig";
    [SerializeField] private string handPath = "TrackingSpace/RightHandAnchor/RightControllerAnchor";

    private Transform _targetAnchor;
    private Rigidbody _rb;

    public override void OnNetworkSpawn()
    {
        _rb = GetComponent<Rigidbody>();

        if (IsOwner)
        {
            // 如果我是主人，嘗試尋找本地的相機與控制器
            FindLocalHandAnchor();

            // 確保主人的物理狀態是正常的（如果需要物理碰撞可設為 false）
            if (_rb != null) _rb.isKinematic = false;
        }
        else
        {
            // 重要：如果不是本人，必須關閉物理模擬，否則會與網路同步的位置產生衝突
            if (_rb != null)
            {
                _rb.isKinematic = true;
                _rb.useGravity = false;
            }

            // 非主人不需要執行 Update 裡的追蹤邏輯，直接由 ClientNetworkTransform 處理
            enabled = false;
        }
    }

    private void FindLocalHandAnchor()
    {
        // 1. 根據你截圖中的名稱尋找 Camera Rig
        GameObject cameraRig = GameObject.Find(cameraRigName);

        if (cameraRig != null)
        {
            // 2. 根據層級結構尋找最底層的 Anchor
            _targetAnchor = cameraRig.transform.Find(handPath);

            if (_targetAnchor != null)
            {
                Debug.Log($"<color=green>[HandFollower]</color> 成功對齊：{_targetAnchor.name}");
            }
            else
            {
                Debug.LogError($"<color=red>[HandFollower]</color> 找不到路徑：{handPath}");
            }
        }
        else
        {
            Debug.LogError($"<color=red>[HandFollower]</color> 找不到物件：{cameraRigName}。請檢查場景物件名稱。");
        }
    }

    private void LateUpdate()
    {
        // 只有擁有者會執行此段，將物件強行校準到控制器位置
        if (IsOwner && _targetAnchor != null)
        {
            transform.position = _targetAnchor.position;
            transform.rotation = _targetAnchor.rotation;
        }
    }
}