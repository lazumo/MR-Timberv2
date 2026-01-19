using UnityEngine;
using Unity.Netcode;

public class ToolController : NetworkBehaviour
{
    public NetworkVariable<int> CurrentState = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    [Header("Tool Objects (children under this object, index = global state)")]
    public GameObject[] toolObjects;

    private int lastState = -1;

    public override void OnNetworkSpawn()
    {
        CurrentState.OnValueChanged += OnStateChanged;

        if (IsServer)
        {
            SetupInitialStateByStage();
        }

        // 初始化顯示
        ApplyState(CurrentState.Value);
    }

    public override void OnNetworkDespawn()
    {
        CurrentState.OnValueChanged -= OnStateChanged;
    }

    private void SetupInitialStateByStage()
    {
        int stage = SceneController.CurrentLevel;

        switch (stage)
        {
            case 1:
                CurrentState.Value = 0;
                break;
            case 2:
                CurrentState.Value = 3;
                break;
            default:
                CurrentState.Value = 0;
                break;
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void SetStateServerRpc(int state)
    {
        int stage = SceneController.CurrentLevel;

        bool invalid =
            (stage == 1 && (state < 0 || state > 2)) ||
            (stage == 2 && (state < 3 || state > 4));

        if (invalid)
        {
            Debug.LogWarning($"Wrong state {state} for stage {stage}");
            return;
        }

        CurrentState.Value = state;
    }

    private void OnStateChanged(int oldValue, int newValue)
    {
        ApplyState(newValue);
    }

    private void ApplyState(int state)
    {
        if (!IsSpawned) return;

        if (state < 0 || state >= toolObjects.Length)
        {
            Debug.LogWarning("Invalid tool index");
            return;
        }

        // 關掉舊的
        if (lastState >= 0 && lastState < toolObjects.Length)
        {
            toolObjects[lastState].SetActive(false);
        }

        // 打開新的
        toolObjects[state].SetActive(true);

        lastState = state;
    }
}
