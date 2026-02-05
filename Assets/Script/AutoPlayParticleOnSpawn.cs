using Unity.Netcode;
using UnityEngine;

public class ElfPlayEffects : NetworkBehaviour
{
    [Header("Effect Control")]
    [SerializeField] private bool includeInactive = true;
    [SerializeField] private bool forceActivateGameObjects = true;

    // ⭐ 核心修改：使用 NetworkVariable 存儲「目標 Driver」的 Reference
    // 預設權限是 Server 寫入，所有 Client 讀取
    public NetworkVariable<NetworkObjectReference> TargetDriverRef = new NetworkVariable<NetworkObjectReference>();

    private ColorFactoryNetDriver currentDriver;
    private ParticleSystem[] systems;

    private void Awake()
    {
        Cache();
    }

    private void Cache()
    {
        systems = GetComponentsInChildren<ParticleSystem>(includeInactive);
    }

    public override void OnNetworkSpawn()
    {
        // 1. 監聽目標改變事件 (當 Server 指定了新的 Driver 時)
        TargetDriverRef.OnValueChanged += OnTargetChanged;

        // 2. 檢查當前是否已經有目標 (處理 Late Join 或 Server 先設值後 Spawn 的情況)
        // NetworkObjectReference.TryGet 會在 Client 端嘗試透過 ID 查找對應的 NetworkObject
        if (TargetDriverRef.Value.TryGet(out NetworkObject targetObj))
        {
            BindDriver(targetObj);
        }
    }

    public override void OnNetworkDespawn()
    {
        // 清理事件監聽
        TargetDriverRef.OnValueChanged -= OnTargetChanged;
        UnbindDriver();
    }

    // 當 TargetDriverRef 變數改變時觸發
    private void OnTargetChanged(NetworkObjectReference prev, NetworkObjectReference curr)
    {
        // 嘗試從 Reference ID 獲取實際的 NetworkObject
        if (curr.TryGet(out NetworkObject targetObj))
        {
            BindDriver(targetObj);
        }
        else
        {
            // 如果 curr 是空的 (Server 解除了綁定)，則執行解綁
            UnbindDriver();
        }
    }

    // ⭐ 綁定邏輯：取得 Driver 組件並監聽它的 IsActive
    private void BindDriver(NetworkObject targetObj)
    {
        // 安全起見，先解綁舊的
        UnbindDriver();

        if (targetObj.TryGetComponent(out ColorFactoryNetDriver driver))
        {
            currentDriver = driver;

            // 訂閱 Driver 的開關狀態
            currentDriver.IsActive.OnValueChanged += OnActiveChanged;

            Debug.Log($"[ElfPlayEffects] BindDriver Success: Linked to {targetObj.name}");

            // ⭐ 關鍵：立即手動觸發一次狀態更新
            // 因為 OnValueChanged 只有在「變更時」觸發，如果你連上去時它已經是 True，
            // 不手動呼叫的話，特效不會播放。
            OnActiveChanged(false, currentDriver.IsActive.Value);
        }
        else
        {
            Debug.LogWarning($"[ElfPlayEffects] Target object {targetObj.name} does not have ColorFactoryNetDriver component!");
        }
    }

    // 解綁邏輯
    private void UnbindDriver()
    {
        if (currentDriver != null)
        {
            currentDriver.IsActive.OnValueChanged -= OnActiveChanged;
            currentDriver = null;
        }
    }

    // 真正的開關邏輯 (來自 Driver 的 NetworkVariable)
    private void OnActiveChanged(bool prev, bool curr)
    {
        // curr 為最新狀態
        if (curr)
            PlayAll("Driver IsActive -> True");
        else
            StopAll("Driver IsActive -> False");
    }

    // --- 以下是你原本的特效控制邏輯 (保持原樣) ---

    private void PlayAll(string from)
    {
        if (systems == null || systems.Length == 0)
            Cache();

        foreach (var ps in systems)
        {
            if (ps == null) continue;

            if (forceActivateGameObjects && !ps.gameObject.activeInHierarchy)
                ps.gameObject.SetActive(true);

            // 你的播放順序邏輯
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Clear(true);
            ps.Play(true);
        }

        Debug.Log($"[ElfPlayEffects] PlayAll from: {from}");
    }

    private void StopAll(string from)
    {
        if (systems == null || systems.Length == 0)
            Cache();

        foreach (var ps in systems)
        {
            if (ps == null) continue;

            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Clear(true);
        }

        Debug.Log($"[ElfPlayEffects] StopAll from: {from}");
    }
}