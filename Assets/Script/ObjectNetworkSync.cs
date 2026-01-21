using UnityEngine;
using Unity.Netcode;
using System;

[RequireComponent(typeof(ObjectDisplayController))]
public class ObjectNetworkSync : NetworkBehaviour
{
    private ObjectDisplayController _logicController;

    // 同步目前的狀態 Enum
    private NetworkVariable<HouseState> currentHouseState = new NetworkVariable<HouseState>(
        HouseState.Unbuilt, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server
    );

    // 同步顏色索引 (0, 1, 2)
    private NetworkVariable<int> colorIndex = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server
    );

    private void Awake()
    {
        _logicController = GetComponent<ObjectDisplayController>();
    }

    public override void OnNetworkSpawn()
    {
        // 只有 Server 決定一次隨機顏色
        if (IsServer)
        {
            colorIndex.Value = UnityEngine.Random.Range(0, 3);
        }

        // 綁定狀態同步：當 state 改變時執行切換邏輯
        currentHouseState.OnValueChanged += (oldVal, newVal) => {
            _logicController.ApplyState(newVal);
        };

        // 綁定顏色同步
        colorIndex.OnValueChanged += (oldVal, newVal) => {
            _logicController.ApplyColor(newVal);
        };

        // 初始化目前的視覺狀態
        _logicController.ApplyState(currentHouseState.Value);
        _logicController.ApplyColor(colorIndex.Value);
    }

    void Update()
    {
        if (!IsServer) return;

        // 測試用：按下 Trigger 循環切換狀態 (Unbuilt -> Built -> Colored ...)
        if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch))
        {
            CycleStateOnServer();
        }
    }

    private void CycleStateOnServer()
    {
        // 取得 Enum 的所有數值
        Array states = Enum.GetValues(typeof(HouseState));
        int nextIndex = ((int)currentHouseState.Value + 1) % states.Length;
        currentHouseState.Value = (HouseState)states.GetValue(nextIndex);

        Debug.Log($"Server changed house state to: {currentHouseState.Value}, color: {colorIndex.Value}");
    }
    public int GetColorValue()
    {
        return colorIndex.Value;
    }
}