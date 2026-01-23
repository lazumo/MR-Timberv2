using Unity.Netcode;
using UnityEngine;

public class ExtinguisherFollower : NetworkBehaviour
{
    private OVRInput.Controller targetController;

    // 當物件在網路生成或擁有權轉移時執行
    public override void OnNetworkSpawn()
    {
        UpdateControllerTracking();
    }

    public override void OnGainedOwnership()
    {
        UpdateControllerTracking();
    }

    private void UpdateControllerTracking()
    {
        // 只有擁有權者 (Owner) 需要決定追蹤哪隻手
        if (IsOwner)
        {
            // 如果我是 Server (Host)，追蹤右手
            if (IsServer)
            {
                targetController = OVRInput.Controller.RTouch;
                Debug.Log($"{gameObject.name}: 我是 Server，追蹤右手");
            }
            // 如果我是 Client，追蹤左手
            else
            {
                targetController = OVRInput.Controller.LTouch;
                Debug.Log($"{gameObject.name}: 我是 Client，追蹤左手");
            }
        }
    }

    void Update()
    {
        // 核心：只有 Owner 負責驅動座標，ClientNetworkTransform 會幫你同步給別人
        if (IsOwner)
        {
            transform.position = OVRInput.GetLocalControllerPosition(targetController);
            transform.rotation = OVRInput.GetLocalControllerRotation(targetController);
        }
    }
}