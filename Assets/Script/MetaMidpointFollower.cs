using UnityEngine;

public class MetaMidpointFollower : MonoBehaviour
{
    [Header("控制器參考 (若為空則自動尋找)")]
    public Transform leftControllerAnchor;
    public Transform rightControllerAnchor;

    // 對外提供資料
    public Vector3 Midpoint { get; private set; }
    public Quaternion MidRotation { get; private set; }

    void Start()
    {
        AutoFindAnchorsIfNeeded();
    }

    void AutoFindAnchorsIfNeeded()
    {
        if (leftControllerAnchor != null && rightControllerAnchor != null)
            return;

        OVRCameraRig rig = FindObjectOfType<OVRCameraRig>();
        if (rig != null)
        {
            leftControllerAnchor = rig.leftControllerAnchor;
            rightControllerAnchor = rig.rightControllerAnchor;
        }
        else
        {
            Debug.LogWarning("[MetaMidpointFollower] 找不到 OVRCameraRig，請手動指派控制器 Anchor。");
        }
    }

    // LateUpdate 確保拿到最新 controller pose
    void LateUpdate()
    {
        if (leftControllerAnchor == null || rightControllerAnchor == null)
            return;

        Midpoint = (leftControllerAnchor.position + rightControllerAnchor.position) * 0.5f;
        MidRotation = rightControllerAnchor.rotation;

        // 如果你希望這個物件本身也跟著移動（可選）
        transform.position = Midpoint;
        transform.rotation = MidRotation;
    }
}
