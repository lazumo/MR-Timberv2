using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 兩支滅火器靠近 → 合體成強力水管；拉開 / 逾時 → 變回兩支。
/// 合體需要先「充能」（分離狀態累積 extinguisherGlowAfter 秒，與 Buff 特效同步）。
/// Server 權威：所有狀態切換只在 server 端執行，client 透過 NetworkVariable 讀取。
/// </summary>
public class ProximitySwitchManager : NetworkBehaviour
{
    [SerializeField] private MiddlePointProvider provider;

    [Header("距離門檻（exit > enter 防抖動）")]
    [SerializeField] private float enterDistance = 0.20f;
    [SerializeField] private float exitDistance = 0.25f;

    [Header("Pipe Prefab（需含 NetworkObject + NetworkTransform）")]
    [SerializeField] private NetworkObject pipePrefab;

    [Header("滅火器充能秒數（分離狀態才計時）")]
    [SerializeField] public float extinguisherGlowAfter = 10f;

    [Header("Pipe 規則")]
    [SerializeField] public float pipeForceBackAfter = 25f;
    [SerializeField] public float warnBeforeForceBack = 5f;
    [SerializeField] public float blinkHzSlow = 2f;
    [SerializeField] public float blinkHzFast = 10f;

    [Header("冷卻秒數（強制分開後）")]
    [SerializeField] public float remergeCooldown = 10f;

    // ===== Network state（client 可讀）=====
    public NetworkVariable<float> PipeAge = new(0f);
    public NetworkVariable<float> CooldownRemain = new(0f);

    // 合體狀態同步版：client 也讀得到（Buff 充能特效的重置要用這個）
    public NetworkVariable<bool> IsMergedNet = new(false);

    // 充能完成同步版（server 權威）：叮聲 / 震動 / 合體光束全部吃這個旗標，
    // 保證三者同一幀出現、且跟「實際可以合體」完全一致。
    public NetworkVariable<bool> IsChargedNet = new(false);

    // ⚠️ server-only：client 上永遠 false，勿用於視覺判斷（視覺請用 IsMergedNet）
    public bool IsMerged => isClose && pipeInstance != null;

    private bool isClose = false;
    private NetworkObject pipeInstance;

    // Buff gate：分離累積時間 ≥ extinguisherGlowAfter（充能完成）才允許合體
    private float _separatedTime = 0f;

    // 冷卻結束後必須先拉開距離、再靠近才可合體（避免貼著等冷卻結束就瞬間合體）
    private bool needReleaseAfterCooldown = false;

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        // 保險：開場時確保滅火器可見、pipe 不存在
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

        // 2) 冷卻結束後，必須先拉開到 exitDistance 以外才解除鎖定
        if (needReleaseAfterCooldown && d >= exitDistance)
            needReleaseAfterCooldown = false;

        // 2.5) Buff 充能：分離狀態累積時間（跟 ExtinguisherChargeParticle 的視覺同步）
        if (!isClose)
            _separatedTime += Time.deltaTime;

        IsChargedNet.Value = !isClose && _separatedTime >= extinguisherGlowAfter;

        // 3) 合體 / 分開狀態機
        if (!isClose)
        {
            bool charged = _separatedTime >= extinguisherGlowAfter;   // 沒充能不能合體
            bool canEnter = charged && (CooldownRemain.Value <= 0f) && !needReleaseAfterCooldown;

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

        // 4) pipe 計時 + 逾時強制分開（觸發冷卻）
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
        IsMergedNet.Value = true;
        _separatedTime = 0f;   // 用掉這次充能

        // 隱藏兩支滅火器（HandFollower）
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
        IsMergedNet.Value = false;
        _separatedTime = 0f;   // 變回後要重新充能

        // 顯示兩支滅火器（HandFollower）
        provider.HostHand.VisualsOn.Value = true;
        provider.ClientHand.VisualsOn.Value = true;

        // Despawn pipe（先在原地播 puff）
        if (pipeInstance != null)
        {
            PlayPipePoofClientRpc(pipeInstance.transform.position);
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

    [ClientRpc]
    private void PlayPipePoofClientRpc(Vector3 pos)
    {
        SfxLib.PlayAt("Poof", pos, 0.9f);
    }

    private void ForceShowExtinguishers_Server()
    {
        isClose = false;
        IsMergedNet.Value = false;
        _separatedTime = 0f;
        needReleaseAfterCooldown = false;

        // provider / hands 可能在 OnNetworkSpawn 時還沒就緒
        if (provider != null && provider.HostHand != null && provider.ClientHand != null)
        {
            provider.HostHand.VisualsOn.Value = true;
            provider.ClientHand.VisualsOn.Value = true;
        }

        if (pipeInstance != null)
        {
            pipeInstance.Despawn(true);
            pipeInstance = null;
        }

        PipeAge.Value = 0f;
        CooldownRemain.Value = 0f;
    }
}
