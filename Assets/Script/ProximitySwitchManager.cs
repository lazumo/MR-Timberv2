using Unity.Netcode;
using UnityEngine;

public class ProximitySwitchManager : NetworkBehaviour
{
    [SerializeField] private MiddlePointProvider provider;

    [Header("距離門檻（exit > enter 防抖動）")]
    [SerializeField] private float enterDistance = 0.20f;
    [SerializeField] private float exitDistance = 0.25f;

    [Header("Pipe Prefab（必須有 NetworkObject + NetworkTransform）")]
    [SerializeField] private NetworkObject pipePrefab;

    [Header("滅火器充能門檻（分開狀態才計時）")]
    [SerializeField] public float extinguisherGlowAfter = 30f;

    [Header("Pipe 規則")]
    [SerializeField] public float pipeForceBackAfter = 15f;
    [SerializeField] public float warnBeforeForceBack = 5f;
    [SerializeField] public float blinkHzSlow = 2f;
    [SerializeField] public float blinkHzFast = 10f;

    [Header("冷卻機制（強制拆開後）")]
    [SerializeField] public float remergeCooldown = 10f;

    // ===== Network state (client 端可讀) =====
    public NetworkVariable<float> PipeAge = new(0f);
    public NetworkVariable<float> CooldownRemain = new(0f);

    // 合體判斷給滅火器特效用
    public bool IsMerged => isClose && pipeInstance != null;

    private bool isClose = false;
    private NetworkObject pipeInstance;

    // 冷卻結束後必須先離開，再靠近才可合體（避免貼著等冷卻結束就瞬間又合體）
    private bool needReleaseAfterCooldown = false;

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        // 保險：開場先確保滅火器可見、pipe 不存在
        ForceShowExtinguishers_Server();
    }

    private void Update()
    {
        if (!IsServer) return;
        if (provider == null) return;
        if (provider.HostHand == null || provider.ClientHand == null) return;

        float d = provider.Distance.Value;

        // 1) 冷卻倒數
        if (CooldownRemain.Value > 0f)
        {
            CooldownRemain.Value = Mathf.Max(0f, CooldownRemain.Value - Time.deltaTime);

            if (CooldownRemain.Value <= 0f)
                needReleaseAfterCooldown = true;
        }

        // 2) 冷卻剛結束後，必須先離開到 exitDistance 以外才能解除限制
        if (needReleaseAfterCooldown && d >= exitDistance)
            needReleaseAfterCooldown = false;

        // 3) 合體/分開狀態機
        if (!isClose)
        {
            bool canEnter = (CooldownRemain.Value <= 0f) && !needReleaseAfterCooldown;

            if (canEnter && d <= enterDistance)
                EnterClose_Server();
        }
        else
        {
            if (d >= exitDistance)
                ExitClose_Server(startCooldown: false);
            else
                UpdatePipePose_Server();
        }

        // 4) pipe 計時 + 強制拆開（觸發冷卻）
        if (isClose && pipeInstance != null)
        {
            PipeAge.Value += Time.deltaTime;

            if (PipeAge.Value >= pipeForceBackAfter)
            {
                ExitClose_Server(startCooldown: true);
            }
        }
    }

    private void EnterClose_Server()
    {
        isClose = true;

        // 隱藏滅火器（HandFollower）
        provider.HostHand.VisualsOn.Value = false;
        provider.ClientHand.VisualsOn.Value = false;

        // Spawn pipe
        if (pipeInstance == null)
        {
            pipeInstance = Instantiate(pipePrefab);
            pipeInstance.Spawn(true);
        }

        PipeAge.Value = 0f;

        UpdatePipePose_Server();
    }

    private void ExitClose_Server(bool startCooldown)
    {
        isClose = false;

        // 顯示滅火器（HandFollower）
        provider.HostHand.VisualsOn.Value = true;
        provider.ClientHand.VisualsOn.Value = true;

        // Despawn pipe
        if (pipeInstance != null)
        {
            pipeInstance.Despawn(true);
            pipeInstance = null;
        }

        PipeAge.Value = 0f;

        if (startCooldown)
        {
            CooldownRemain.Value = remergeCooldown;
            needReleaseAfterCooldown = true;
        }
    }

    private void UpdatePipePose_Server()
    {
        if (pipeInstance == null) return;

        Vector3 midPos = provider.MidPosition.Value;
        Quaternion rot = provider.HostRotation.Value;

        pipeInstance.transform.SetPositionAndRotation(midPos, rot);
    }

    private void ForceShowExtinguishers_Server()
    {
        isClose = false;
        needReleaseAfterCooldown = false;

        provider.HostHand.VisualsOn.Value = true;
        provider.ClientHand.VisualsOn.Value = true;

        if (pipeInstance != null)
        {
            pipeInstance.Despawn(true);
            pipeInstance = null;
        }

        PipeAge.Value = 0f;
        CooldownRemain.Value = 0f;
    }
}
