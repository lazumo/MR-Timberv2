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

    // 公開唯讀狀態（給其他系統用）
    public int CurrentState => netState.Value;

    // 狀態變化事件（給 UI / SFX / 教學系統監聽）
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

    [Header("Hand Tracking")]
    public Transform leftHand;
    public Transform rightHand;

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

        // 確保 Host / Client / Late Join 都會初始化
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
    // Visual Application
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

        // 關掉舊的
        if (lastState >= 0 && lastState < toolObjects.Length)
        {
            toolObjects[lastState].SetActive(false);
        }

        // 開新的
        toolObjects[state].SetActive(true);

        lastState = state;

        if (invokeEvent)
        {
            OnToolStateChanged?.Invoke(state);
        }
    }

    // =========================
    // Follow hands
    // =========================

    void Update()
    {
        if (!followHands) return;
        if (!leftHand || !rightHand) return;
        if (lastState < 0 || lastState >= toolObjects.Length) return;

        GameObject currentTool = toolObjects[lastState];
        if (!currentTool.activeSelf) return;

        Vector3 mid = (leftHand.position + rightHand.position) * 0.5f;

        if (followSmooth <= 0f)
        {
            currentTool.transform.position = mid;
        }
        else
        {
            float t = 1f - Mathf.Exp(-followSmooth * Time.deltaTime);
            currentTool.transform.position = Vector3.Lerp(currentTool.transform.position, mid, t);
        }
    }


}
