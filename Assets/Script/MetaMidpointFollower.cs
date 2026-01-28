using UnityEngine;
using Unity.Netcode;

public class MetaMidpointFollower : NetworkBehaviour
{
    public Transform leftControllerAnchor;
    public Transform rightControllerAnchor;

    private Transform toolsRoot;

    void Start()
    {
        toolsRoot = transform.parent;
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
    }

    void LateUpdate()
    {
        // 只讓「擁有者 client」計算
        if (!IsServer) return;

        if (leftControllerAnchor == null || rightControllerAnchor == null)
            return;

        Vector3 midpoint = (leftControllerAnchor.position + rightControllerAnchor.position) * 0.5f;
        Quaternion midRot = rightControllerAnchor.rotation;

        // 直接移動 Tools（NetworkTransform 會自動同步）
        toolsRoot.position = midpoint;
        toolsRoot.rotation = midRot;
    }
}
