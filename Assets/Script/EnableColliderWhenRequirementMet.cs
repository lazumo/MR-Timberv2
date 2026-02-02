using Unity.Netcode;
using UnityEngine;

public class EnableColliderWhenRequirementMet : NetworkBehaviour
{
    [Header("Requirement Source")]
    [SerializeField] private BarShowWhenEnoughMatchingFruits requirementChecker;

    [Header("Target Collider")]
    [SerializeField] private Collider targetCollider;

    [Header("Option")]
    [Tooltip("If true: enable when met. If false: disable when met (invert).")]
    [SerializeField] private bool enableWhenMet = true;

    // 同步：所有 client 都一致看到 collider 開關
    private NetworkVariable<bool> colliderEnabled =
        new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    private void Reset()
    {
        targetCollider = GetComponent<Collider>();
        requirementChecker = GetComponentInParent<BarShowWhenEnoughMatchingFruits>();
    }

    public override void OnNetworkSpawn()
    {
        colliderEnabled.OnValueChanged += OnColliderEnabledChanged;

        // 初始套用（含 late join）
        ApplyCollider(colliderEnabled.Value);
    }

    public override void OnNetworkDespawn()
    {
        colliderEnabled.OnValueChanged -= OnColliderEnabledChanged;
    }

    private void Update()
    {
        if (!IsServer) return;
        if (requirementChecker == null) return;

        bool met = requirementChecker.IsRequirementMet();
        bool wantEnabled = enableWhenMet ? met : !met;

        // 避免每幀寫值
        if (colliderEnabled.Value != wantEnabled)
            colliderEnabled.Value = wantEnabled;
    }

    private void OnColliderEnabledChanged(bool prev, bool next)
    {
        ApplyCollider(next);
    }

    private void ApplyCollider(bool enabled)
    {
        if (targetCollider != null)
            targetCollider.enabled = enabled;
    }
}
