using UnityEngine;

public class MetaMidpointFollower : MonoBehaviour
{
    [Header("控制器參考 (若為空則自動尋找)")]
    public Transform leftControllerAnchor;
    public Transform rightControllerAnchor;

    void Start()
    {
        // 如果沒有手動指派，嘗試自動尋找 Meta 的 OVRCameraRig 節點
        if (leftControllerAnchor == null || rightControllerAnchor == null)
        {
            OVRCameraRig rig = FindObjectOfType<OVRCameraRig>();
            if (rig != null)
            {
                leftControllerAnchor = rig.leftControllerAnchor;
                rightControllerAnchor = rig.rightControllerAnchor;
            }
            else
            {
                Debug.LogWarning("找不到 OVRCameraRig，請手動指派控制器 Anchor。");
            }
        }
    }

    // 使用 LateUpdate 確保在控制器座標更新後才計算中心點
    void LateUpdate()
    {
        if (leftControllerAnchor == null || rightControllerAnchor == null) return;

        // 1. 計算兩手中心點位置 (世界座標)
        transform.position = (leftControllerAnchor.position + rightControllerAnchor.position) * 0.5f;

        // 2. 同步右手旋轉
        transform.rotation = rightControllerAnchor.rotation;
    }
}