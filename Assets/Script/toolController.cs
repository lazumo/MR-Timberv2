using System;
using UnityEngine;
using Unity.Netcode;

public class ToolController : NetworkBehaviour
{
    // =========================
    // Networked State
    // =========================

    private NetworkVariable<int> netState = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public int CurrentState => netState.Value;
    public event Action<int> OnToolStateChanged;

    // =========================
    // Tool Objects
    // =========================

    [Header("Tool Objects (children under this object, index = global state)")]
    public GameObject[] toolObjects;

    private int lastState = -1;

    // =========================
    // Network Lifecycle
    // =========================

    public override void OnNetworkSpawn()
    {
        netState.OnValueChanged += OnNetStateChanged;

        if (IsServer)
        {
            SetupInitialStateByStage();
        }
        
        // Initially hide all to ensure clean slate
        for (int i = 0; i < toolObjects.Length; i++)
        {
            SetToolVisible(toolObjects[i], false);
        }
        
        // Apply current state
        ApplyState(netState.Value, invokeEvent: false);
    }

    public override void OnNetworkDespawn()
    {
        netState.OnValueChanged -= OnNetStateChanged;
    }

    // =========================
    // Server Logic
    // =========================

    private void SetupInitialStateByStage()
    {
        // TUNED: Always default to 0 regardless of stage
        netState.Value = 0; 
    }

    [ServerRpc(RequireOwnership = false)]
    public void SetStateServerRpc(int newState)
    {
        if (!IsValidStateForStage(newState))
        {
            Debug.LogWarning($"[ToolController] Invalid state {newState} for stage {SceneController.Instance.GetCurrentStage()}");
            return;
        }

        if (newState == netState.Value)
            return;

        netState.Value = newState;
    }

    private bool IsValidStateForStage(int state)
    {
        // TUNED: Allow switching stages, but always permit 0
        int stage = SceneController.Instance.GetCurrentStage();

        if (stage == 1)
        {
            // Stage 1: Tools 0, 1, 2
            return (state >= 0 && state <= 2);
        }
        else if (stage == 2)
        {
            // Stage 2: Tools 3, 4 ... AND 0 (Default)
            return (state == 0) || (state >= 3 && state <= 4);
        }

        // Fallback for other stages / Lobby: Only 0
        return state == 0;
    }

    // =========================
    // Client Sync
    // =========================

    private void OnNetStateChanged(int oldValue, int newValue)
    {
        ApplyState(newValue, invokeEvent: true);
    }

    // =========================
    // Visual Application (Netcode-safe)
    // =========================

    private void ApplyState(int state, bool invokeEvent)
    {
        if (!IsSpawned)
            return;

        if (state < 0 || state >= toolObjects.Length)
        {
            Debug.LogWarning("[ToolController] Invalid tool index");
            return;
        }

        // Hide Old
        if (lastState >= 0 && lastState < toolObjects.Length)
        {
            SetToolVisible(toolObjects[lastState], false);
        }

        // Show New
        SetToolVisible(toolObjects[state], true);

        lastState = state;

        if (invokeEvent)
        {
            OnToolStateChanged?.Invoke(state);
        }
    }

    // =========================
    // Tool visibility (Renderer + Collider)
    // =========================

    private void SetToolVisible(GameObject tool, bool visible)
    {
        if (!tool) return;

        foreach (var r in tool.GetComponentsInChildren<Renderer>(true))
            r.enabled = visible;

        foreach (var c in tool.GetComponentsInChildren<Collider>(true))
            c.enabled = visible;
    }
}