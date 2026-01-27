using Unity.Netcode;
using UnityEngine;

public class NetworkExtinguisherController : NetworkBehaviour
{
    [Header("References")]
    public Transform nozzlePoint;
    public ParticleSystem sprayVFX;

    [Header("Settings")]
    public float range = 3f;
    public float extinguishRate = 10f;
    public float triggerThreshold = 0.25f;

    // Server 同步噴射狀態，讓兩端都能看到 VFX
    public NetworkVariable<bool> isSpraying =
        new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        isSpraying.OnValueChanged += (_, v) => ApplySprayVFX(v);
        ApplySprayVFX(isSpraying.Value);
    }

    public override void OnNetworkDespawn()
    {
        isSpraying.OnValueChanged -= (_, v) => ApplySprayVFX(v);
    }

    void Update()
    {
        // 1) Owner 讀左右手 trigger（任一按下就噴）
        if (IsOwner)
        {
            bool want = ReadAnyTrigger();

            // 只在狀態變化時送 RPC（省流量）
            if (want != isSpraying.Value)
                SetSprayingServerRpc(want);
        }

        // 2) 只有 Server 做 Raycast 扣火（結果一致）
        if (IsServer && isSpraying.Value)
            DoExtinguishRaycast();
    }

    bool ReadAnyTrigger()
    {
        var L = OVRInput.Controller.LTouch;
        var R = OVRInput.Controller.RTouch;

        float li = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, L);
        float ri = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, R);

        return (li >= triggerThreshold) || (ri >= triggerThreshold);
    }

    [ServerRpc]
    void SetSprayingServerRpc(bool on)
    {
        isSpraying.Value = on;
    }

    void ApplySprayVFX(bool on)
    {
        if (sprayVFX == null) return;
        if (on) sprayVFX.Play();
        else sprayVFX.Stop();
    }

    void DoExtinguishRaycast()
    {
        if (nozzlePoint == null) return;

        Ray ray = new Ray(nozzlePoint.position, nozzlePoint.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, range))
        {
            var fire = hit.collider.GetComponentInParent<NetworkFireController>();
            if (fire != null)
                fire.ApplyExtinguishServer(extinguishRate * Time.deltaTime);
        }
    }
}
