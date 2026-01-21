using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(ObjectDisplayController))]
public class ObjectNetworkSync : NetworkBehaviour
{
    private ObjectDisplayController _logicController;

    // 狀態管理
    private NetworkVariable<int> activeIndex = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private void Awake()
    {
        Debug.Log($"house exist ");
        _logicController = GetComponent<ObjectDisplayController>();
    }

    public override void OnNetworkSpawn()
    {
        Debug.Log($"house spawned ");
        // 1. 綁定同步回呼
        activeIndex.OnValueChanged += (oldVal, newVal) => {
            _logicController.SwitchToIndex(newVal);
        };

        // 2. 初始化目前狀態
        _logicController.SwitchToIndex(activeIndex.Value);
    }

    void Update()
    {
        // 只有 Server 擁有修改狀態的權限
        if (!IsServer) return;

        // 偵測 Meta Quest Trigger (Server 端的輸入)
        if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch))
        {
            UpdateStateOnServer();
            Debug.Log($"current index : {activeIndex.Value}");
        }
    }

    private void UpdateStateOnServer()
    {
        int count = _logicController.GetObjectCount();
        if (count == 0) return;

        // 更新 NetworkVariable，這會自動觸發所有 Client 的 OnValueChanged
        activeIndex.Value = (activeIndex.Value + 1) % count;
    }
}