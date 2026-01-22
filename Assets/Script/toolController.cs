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
        for (int i = 0; i < toolObjects.Length; i++)
        {
            SetToolVisible(toolObjects[i], false);
        }
        // 初始化顯示狀態
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
        int stage = SceneController.CurrentLevel;

        switch (stage)
        {
            case 1: netState.Value = 0; break;
            case 2: netState.Value = 3; break;
            default: netState.Value = 0; break;
        }
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

        return
            (stage == 1 && state >= 0 && state <= 2) ||
            (stage == 2 && state >= 3 && state <= 4);
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

        // 關閉舊工具顯示
        if (lastState >= 0 && lastState < toolObjects.Length)
        {
            SetToolVisible(toolObjects[lastState], false);
        }

        // 開啟新工具顯示
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
