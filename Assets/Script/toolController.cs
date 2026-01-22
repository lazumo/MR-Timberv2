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
    // Hand Tracking
    // =========================

    [Header("Midpoint Provider")]
    public MetaMidpointFollower midpointProvider;

    [Header("Tool Follow Settings")]
    public bool followHands = true;
    public float followSmooth = 12f;

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
        
        // Hide all initially
        for (int i = 0; i < toolObjects.Length; i++)
        {
            SetToolVisible(toolObjects[i], false);
        }
        
        // Apply the initial state immediately
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
        // CHANGE 1: We want the DEFAULT to always be 0, regardless of the level.
        // We do not switch(stage) here anymore.
        netState.Value = 0; 
    }

    [ServerRpc(RequireOwnership = false)]
    public void SetStateServerRpc(int newState)
    {
        if (!IsValidStateForStage(newState))
        {
            Debug.LogWarning($"[ToolController] Invalid state {newState} for stage {SceneController.CurrentLevel}");
            return;
        }

        if (newState == netState.Value)
            return;

        netState.Value = newState;
    }

    private bool IsValidStateForStage(int state)
    {
        int stage = SceneController.CurrentLevel;

        // CHANGE 2: We must ensure '0' is allowed in Stage 2.
        // Stage 1: Allows 0, 1, 2
        // Stage 2: Allows 0, 3, 4 (Added 'state == 0' here)
        
        if (stage == 1)
        {
            return (state >= 0 && state <= 2);
        }
        else if (stage == 2)
        {
            // Allow 3 and 4 (standard stage 2 tools) BUT ALSO allow 0 (default)
            return (state == 0) || (state >= 3 && state <= 4);
        }

        // Default fallback (e.g. Lobby) -> Only allow 0
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

        // Hide old
        if (lastState >= 0 && lastState < toolObjects.Length)
        {
            SetToolVisible(toolObjects[lastState], false);
        }

        // Show new
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

    // =========================
    // Follow hands
    // =========================

    void Update()
    {
        if (!followHands) return;
        if (!midpointProvider) return;
        if (lastState < 0 || lastState >= toolObjects.Length) return;

        GameObject currentTool = toolObjects[lastState];

        Vector3 target = midpointProvider.Midpoint;

        float t = 1f - Mathf.Exp(-followSmooth * Time.deltaTime);
        currentTool.transform.position =
            Vector3.Lerp(currentTool.transform.position, target, t);

        currentTool.transform.rotation = midpointProvider.MidRotation;
    }
}