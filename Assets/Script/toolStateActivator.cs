using UnityEngine;
using Unity.Netcode;

public class ToolStateStageActivator : NetworkBehaviour
{
    [Header("Dependencies")]
    public ToolController toolController;

    [Header("Systems enabled at state >= threshold")]
    public GameObject extinguisherManager;
    public GameObject extinguisherDistributor;

    [Header("Config")]
    public int enableFromState = 3; // 第四個 state（0-based）

    public override void OnNetworkSpawn()
    {
        if (toolController == null)
        {
            Debug.LogError("[ToolStateStageActivator] ToolController missing");
            return;
        }

        toolController.OnToolStateChanged += OnToolStateChanged;

        // 初始化（避免 late joiner 問題）
        Apply(toolController.CurrentState);
        Debug.Log($"[StageActivator] OnNetworkSpawn IsServer={IsServer}");
    }

    public override void OnNetworkDespawn()
    {
        if (toolController != null)
            toolController.OnToolStateChanged -= OnToolStateChanged;
    }

    private void OnToolStateChanged(int newState)
    {
        Apply(newState);
    }

    private void Apply(int state)
    {
        Debug.Log($"[StageActivator] Apply state={state}, IsServer={IsServer}");
        bool enable = state >= enableFromState;

        if (!IsServer) return;

        if (extinguisherManager != null)
        {
            var mgr = extinguisherManager.GetComponent<IStageEnable>();
            if (mgr != null)
                mgr.SetStageEnabled(enable);
        }

        if (extinguisherDistributor != null)
        {
            var dist = extinguisherDistributor.GetComponent<IStageEnable>();
            if (dist != null)
                dist.SetStageEnabled(enable);
        }
    }
}
