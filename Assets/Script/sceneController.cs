using Unity.Netcode;
using UnityEngine;

public class SceneController : NetworkBehaviour
{
    public static SceneController Instance { get; private set; }

    // ⭐ Server 權威的 Stage
    public NetworkVariable<int> CurrentLevel =
        new NetworkVariable<int>(
            1,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    // =========================
    // Server-only API
    // =========================
    [ServerRpc(RequireOwnership = false)]
    public void NextLevelServerRpc()
    {
        CurrentLevel.Value++;
        Debug.Log($"[SceneController][Server] Level = {CurrentLevel.Value}");
    }

    [ServerRpc(RequireOwnership = false)]
    public void ResetLevelServerRpc()
    {
        CurrentLevel.Value = 1;
    }

    // =========================
    // Read-only helper
    // =========================
    public int GetCurrentStage()
    {
        return CurrentLevel.Value;
    }
}
