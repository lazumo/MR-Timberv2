using UnityEngine;
using Unity.Netcode;

public class ToolController : NetworkBehaviour
{
    public NetworkVariable<int> CurrentState = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    [Header("Tool Prefabs (global state index)")]
    public GameObject[] toolPrefabs;

    private GameObject currentToolInstance;

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            SetupInitialStateByStage();
        }

        CurrentState.OnValueChanged += OnStateChanged;
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
                CurrentState.Value = 0; // Stage1 State0
                break;
            case 2:
                CurrentState.Value = 3; // Stage2 State0
                break;
            default:
                CurrentState.Value = 0;
                break;
        }
    }

    // ===== Server 控制切換 =====
    [ServerRpc(RequireOwnership = false)]
    public void SetStateServerRpc(int state)
    {
        int stage = SceneController.CurrentLevel;

        bool invalid = false;

        if (stage == 1)
        {
            // only allow 0,1,2
            invalid = (state < 0 || state > 2);
        }
        else if (stage == 2)
        {
            // only allow 3,4
            invalid = (state < 3 || state > 4);
        }

        if (invalid)
        {
            Debug.LogWarning($"Wrong state {state} for stage {stage}");
            return;
        }

        CurrentState.Value = state;
    }

    // ===== Client 監聽 =====
    private void OnStateChanged(int oldValue, int newValue)
    {
        Debug.Log($"Tool State: {oldValue} → {newValue}");
        ApplyState(newValue);
    }

    private void ApplyState(int state)
    {
        if (currentToolInstance != null)
        {
            Destroy(currentToolInstance);
        }

        if (state < 0 || state >= toolPrefabs.Length)
        {
            Debug.LogWarning("Invalid prefab index");
            return;
        }

        currentToolInstance = Instantiate(toolPrefabs[state], transform);
        currentToolInstance.transform.localPosition = Vector3.zero;
        currentToolInstance.transform.localRotation = Quaternion.identity;
    }
}
