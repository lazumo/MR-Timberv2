using Unity.Netcode;
using UnityEngine;

public class ProximitySwitchManager : NetworkBehaviour
{
    [SerializeField] private MiddlePointProvider provider;

    [Header("�Z�����e�]exit > enter ���ݰʡ^")]
    [SerializeField] private float enterDistance = 0.20f;
    [SerializeField] private float exitDistance = 0.25f;

    [Header("Pipe Prefab�]������ NetworkObject + NetworkTransform�^")]
    [SerializeField] private NetworkObject pipePrefab;

    [Header("�������R����e�]���}���A�~�p�ɡ^")]
    [SerializeField] public float extinguisherGlowAfter = 10f;

    [Header("Pipe �W�h")]
    [SerializeField] public float pipeForceBackAfter = 25f;
    [SerializeField] public float warnBeforeForceBack = 5f;
    [SerializeField] public float blinkHzSlow = 2f;
    [SerializeField] public float blinkHzFast = 10f;

    [Header("�N�o����]�j���}��^")]
    [SerializeField] public float remergeCooldown = 10f;

    // ===== Network state (client �ݥiŪ) =====
    public NetworkVariable<float> PipeAge = new(0f);
    public NetworkVariable<float> CooldownRemain = new(0f);

    // �X��P�_���������S�ĥ�
    public bool IsMerged => isClose && pipeInstance != null;

    private bool isClose = false;
    private NetworkObject pipeInstance;

    // ⭐ Buff gate：分離累積時間 ≥ extinguisherGlowAfter（充能完成）才允許合體
    private float _separatedTime = 0f;

    // �N�o�����ᥲ�������}�A�A�a��~�i�X��]�קK�K�۵��N�o�����N�����S�X��^
    private bool needReleaseAfterCooldown = false;

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        // �O�I�G�}�����T�O�������i���Bpipe ���s�b
        ForceShowExtinguishers_Server();
    }

    private void Update()
    {
        if (!IsServer) return;
        if (provider == null) return;
        if (provider.HostHand == null || provider.ClientHand == null) return;

        float d = provider.Distance.Value;

        // 1) �N�o�˼�
        if (CooldownRemain.Value > 0f)
        {
            CooldownRemain.Value = Mathf.Max(0f, CooldownRemain.Value - Time.deltaTime);

            if (CooldownRemain.Value <= 0f)
                needReleaseAfterCooldown = true;
        }

        // 2) �N�o�赲����A���������}�� exitDistance �H�~�~��Ѱ�����
        if (needReleaseAfterCooldown && d >= exitDistance)
            needReleaseAfterCooldown = false;

        // 2.5) Buff 充能：分離狀態累積時間（跟 ExtinguisherChargeParticle 的視覺同步）
        if (!isClose)
            _separatedTime += Time.deltaTime;

        // 3) �X��/���}���A��
        if (!isClose)
        {
            bool charged = _separatedTime >= extinguisherGlowAfter;   // ⭐ 沒充能不能合體
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

        // 4) pipe �p�� + �j���}�]Ĳ�o�N�o�^
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
        _separatedTime = 0f;   // 用掉這次充能

        // ���÷������]HandFollower�^
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
        _separatedTime = 0f;   // 變回後要重新充能

        // ��ܷ������]HandFollower�^
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
        _separatedTime = 0f;
        needReleaseAfterCooldown = false;

        // provider / hands may not be assigned or spawned yet at OnNetworkSpawn time.
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
