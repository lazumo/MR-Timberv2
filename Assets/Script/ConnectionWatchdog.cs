using System.Collections;
using Meta.XR.MultiplayerBlocks.Shared;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// client 斷線自動重連(host 端不動作 —— host 掛了由 staff 重開 app,兩台會重新配對)。
///
/// 流程:NGO 一斷線 → Shutdown → 用 Meta LocalMatchmaking 的公開 API 重新開始
/// colocation session 探索(不逾時)→ 探索到 host 的廣播就自動 JoinRoom → NGO 重連,
/// NetworkVariable(房間 pose、階段、anchor UUID)自動重同步,場景 NetworkObject 重生。
///
/// 前提:host 配對成功後持續 advertise(LocalMatchmaking 建房後沒有停止廣播的呼叫,
/// 且已用 prox_close 關掉近距離感應器 → host 摘下頭盔也不會暫停)。
///
/// 零接線:RuntimeInitializeOnLoadMethod 自我生成(僅裝置上;editor 不啟動)。
/// </summary>
public class ConnectionWatchdog : MonoBehaviour
{
    private const float RetryInterval = 15f;   // 探索是持續的;每 15 秒再下一次指令當保險

    private bool _hooked;
    private bool _reconnecting;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Application.isEditor) return;
        var go = new GameObject("ConnectionWatchdog");
        go.AddComponent<ConnectionWatchdog>();
        DontDestroyOnLoad(go);
    }

    private void Update()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || _hooked) return;
        _hooked = true;
        nm.OnClientDisconnectCallback += OnClientDisconnect;
    }

    private void OnClientDisconnect(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.IsHost) return;            // host:是別人離開,不干我們的事
        if (clientId != nm.LocalClientId) return;       // client 只理「自己斷線」
        if (_reconnecting) return;

        StartCoroutine(ReconnectLoop());
    }

    private IEnumerator ReconnectLoop()
    {
        _reconnecting = true;
        Debug.LogWarning("[ConnectionWatchdog] Disconnected from host — auto-reconnect started.");

        var nm = NetworkManager.Singleton;
        if (nm != null && nm.IsListening) nm.Shutdown();
        while (nm != null && nm.ShutdownInProgress) yield return null;
        yield return new WaitForSeconds(2f);

        var lm = FindAnyObjectByType<LocalMatchmaking>();
        if (lm == null)
            Debug.LogError("[ConnectionWatchdog] No LocalMatchmaking in scene — cannot auto-reconnect.");

        int attempt = 0;
        while (lm != null)
        {
            nm = NetworkManager.Singleton;
            if (nm != null && nm.IsConnectedClient) break;

            attempt++;
            Debug.Log($"[ConnectionWatchdog] Discovery attempt {attempt} — listening for host's colocation session...");
            _ = lm.StartAsGuest(stopAfterTimeout: false);   // 重複呼叫安全:AlreadyDiscovering = no-op

            float t = 0f;
            while (t < RetryInterval)
            {
                nm = NetworkManager.Singleton;
                if (nm != null && nm.IsConnectedClient) break;
                t += 0.5f;
                yield return new WaitForSeconds(0.5f);
            }
        }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient)
            Debug.Log("[ConnectionWatchdog] Reconnected to host.");
        _reconnecting = false;
    }
}
