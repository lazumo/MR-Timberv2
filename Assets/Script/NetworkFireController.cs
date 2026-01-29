using Unity.Netcode;
using UnityEngine;

public class NetworkFireController : NetworkBehaviour
{
    [Header("State")]
    public float maxFireIntensity = 30f;

    public NetworkVariable<float> fireIntensity =
        new NetworkVariable<float>(30f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [Header("Visual")]
    public ParticleSystem fireVFX;
    public Light fireLight;
    public AnimationCurve sizeCurve;

    public override void OnNetworkSpawn()
    {
        fireIntensity.OnValueChanged += (_, __) => UpdateVisual();
        UpdateVisual();
    }

    public override void OnNetworkDespawn()
    {
        fireIntensity.OnValueChanged -= (_, __) => UpdateVisual();
    }

    // ✅ 只讓 Server 扣火
    public void ApplyExtinguishServer(float amount)
    {
        if (!IsServer) return;

        float v = Mathf.Clamp(fireIntensity.Value - amount, 0f, maxFireIntensity);
        fireIntensity.Value = v;

        if (v <= 0f)
            ExtinguishCompletelyServer();
    }

    void UpdateVisual()
    {
        float t = maxFireIntensity <= 0f ? 0f : fireIntensity.Value / maxFireIntensity;
        float sizeFactor = sizeCurve != null ? sizeCurve.Evaluate(t) : t;

        if (fireVFX != null)
        {
            var main = fireVFX.main;
            main.startSize = Mathf.Lerp(0.1f, 1.2f, sizeFactor);
        }

        if (fireLight != null)
        {
            fireLight.intensity = Mathf.Lerp(0f, 3f, t);
            fireLight.enabled = fireIntensity.Value > 0f;
        }
    }

    void ExtinguishCompletelyServer()
    {
        if (fireVFX != null)
            fireVFX.Stop(true, ParticleSystemStopBehavior.StopEmitting);

        if (fireLight != null)
            fireLight.enabled = false;

        var no = GetComponent<NetworkObject>();
        if (no != null && no.IsSpawned)
            no.Despawn(true);
        else
            Destroy(gameObject);
    }
}
