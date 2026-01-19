using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(HouseStateApplier))]
public class HouseNetworkHandler : NetworkBehaviour
{
    private HouseStateApplier _applier;

    // 使用 Enum 作為 NetworkVariable 的類型
    public NetworkVariable<HouseStateApplier.HouseState> CurrentState =
        new NetworkVariable<HouseStateApplier.HouseState>(HouseStateApplier.HouseState.None);

    private void Awake()
    {
        _applier = GetComponent<HouseStateApplier>();
    }

    public override void OnNetworkSpawn()
    {
        // 初始化視覺
        _applier.ApplyState(CurrentState.Value);

        // 訂閱網路變數更動事件
        CurrentState.OnValueChanged += OnStateChanged;
    }

    public override void OnNetworkDespawn()
    {
        CurrentState.OnValueChanged -= OnStateChanged;
    }

    private void OnStateChanged(HouseStateApplier.HouseState oldVal, HouseStateApplier.HouseState newVal)
    {
        _applier.ApplyState(newVal);
    }

    // 提供給 Controller 呼叫，用來改變狀態 (僅 Server 有權限)
    public void SetState(HouseStateApplier.HouseState newState)
    {
        if (IsServer)
        {
            CurrentState.Value = newState;
        }
    }
}