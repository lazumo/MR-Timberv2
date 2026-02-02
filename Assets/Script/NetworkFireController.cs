using Unity.Netcode;
using UnityEngine;

public class NetworkFireController : NetworkBehaviour
{
    // =============================
    // State
    // =============================

    [Header("State")]
    [SerializeField] private float maxFireIntensity = 30f;

    public NetworkVariable<float> fireIntensity =
        new NetworkVariable<float>(
            30f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    // =============================
    // Visual
    // =============================

    [Header("VFX")]
    [SerializeField] private ParticleSystem[] fireVFXs;
    [SerializeField] private Light[] fireLights;
    [SerializeField] private AnimationCurve sizeCurve;

    // =============================
    // Audio
    // =============================

    [Header("Audio")]
    [SerializeField] private AudioSource fireAudio;

    // =============================
    // Network
    // =============================

    public override void OnNetworkSpawn()
    {
        fireIntensity.OnValueChanged += OnFireIntensityChanged;
        UpdateVisualAndAudio();
    }

    public override void OnNetworkDespawn()
    {
        fireIntensity.OnValueChanged -= OnFireIntensityChanged;
    }

    private void OnFireIntensityChanged(float _, float __)
    {
        UpdateVisualAndAudio();
    }

    // =============================
    // Fire logic (Server only)
    // =============================

    public void ApplyExtinguishServer(float amount)
    {
        if (!IsServer) return;

        float v = Mathf.Clamp(
            fireIntensity.Value - amount,
            0f,
            maxFireIntensity
        );
        Debug.Log($"{v}");
        fireIntensity.Value = v;

        if (v <= 0f)
            ExtinguishCompletelyServer();
    }

    // =============================
    // Visual + Audio update (All clients)
    // =============================

    private void UpdateVisualAndAudio()
    {
        float t = maxFireIntensity <= 0f
            ? 0f
            : fireIntensity.Value / maxFireIntensity;

        float sizeFactor = sizeCurve != null
            ? sizeCurve.Evaluate(t)
            : t;

        // 🔥 Particle Systems
        foreach (var ps in fireVFXs)
        {
            if (ps == null) continue;

            var main = ps.main;
            main.startSize = Mathf.Lerp(0.1f, 1.2f, sizeFactor);

            var emission = ps.emission;
            emission.rateOverTime = Mathf.Lerp(0f, 60f, sizeFactor);

            if (fireIntensity.Value > 0f && !ps.isPlaying)
                ps.Play();
        }

        // 💡 Lights
        foreach (var light in fireLights)
        {
            if (light == null) continue;

            light.intensity = Mathf.Lerp(0f, 3f, t);
            light.enabled = fireIntensity.Value > 0f;
        }

        if (fireAudio != null)
        {
            if (fireIntensity.Value > 0f)
            {
                if (!fireAudio.isPlaying)
                    fireAudio.Play();

                fireAudio.volume = Mathf.Lerp(0.1f, 1f, sizeFactor);
            }
            else
            {
                if (fireAudio.isPlaying)
                    fireAudio.Stop();
            }
        }
    }

    // =============================
    // Extinguish
    // =============================

    private void ExtinguishCompletelyServer()
    {
        // Stop VFX
        foreach (var ps in fireVFXs)
        {
            if (ps != null)
                ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        foreach (var light in fireLights)
        {
            if (light != null)
                light.enabled = false;
        }

        // Stop Audio
        if (fireAudio != null && fireAudio.isPlaying)
            fireAudio.Stop();

        // Despawn network object
        var netObj = GetComponent<NetworkObject>();
        if (netObj != null && netObj.IsSpawned)
            netObj.Despawn(true);
        else
            Destroy(gameObject);
    }
}
