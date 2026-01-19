using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(HouseNetworkHandler))]
public class HouseStateController : NetworkBehaviour
{
    private HouseNetworkHandler _networkHandler;

    private void Awake()
    {
        _networkHandler = GetComponent<HouseNetworkHandler>();
    }

    // 在 HouseStateController.cs 的 Update 加入這行
    void Update()
    {
        // 只有當物件在網路中「出生」後，才偵測輸入
        if (!IsSpawned) return;

        // 只有 Server (Host) 端的輸入能控制
        if (!IsServer) return;

        if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch))
        {
            CycleHouseState();
        }
    }

    private void CycleHouseState()
    {
        // 1. 取得目前的狀態 (轉換為 int)
        int currentStateInt = (int)_networkHandler.CurrentState.Value;

        // 2. 計算下一個狀態
        // Enum.GetValues(typeof(Type)).Length 可以動態取得 Enum 總共有幾種定義
        int totalStates = System.Enum.GetValues(typeof(HouseStateApplier.HouseState)).Length;
        int nextStateInt = (currentStateInt + 1) % totalStates;

        // 3. 轉換回 Enum 並更新
        HouseStateApplier.HouseState nextState = (HouseStateApplier.HouseState)nextStateInt;

        Debug.Log($"[Server] Trigger Pressed. Switching state from {_networkHandler.CurrentState.Value} to {nextState}");

        _networkHandler.SetState(nextState);
    }
}