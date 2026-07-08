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

    [SerializeField] LayerMask fireLayer;
    // Server 同步噴射狀態，讓客戶端也能看到 VFX
    public NetworkVariable<bool> isSpraying =
        new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private AudioSource sprayAudio;

    public override void OnNetworkSpawn()
    {
        sprayAudio = SfxLib.AddLoop(gameObject, "SprayLoop", 0.7f);
        isSpraying.OnValueChanged += (_, v) => ApplySprayVFX(v);
        ApplySprayVFX(isSpraying.Value);
    }

    public override void OnNetworkDespawn()
    {
        isSpraying.OnValueChanged -= (_, v) => ApplySprayVFX(v);
    }

    private bool _wasVibrating;

    void Update()
    {
        // 1) Owner 讀取右手 trigger（任一按下就算）
        if (IsOwner)
        {
            bool want = ReadAnyTrigger();

            // 只在狀態變化時送 RPC（省流量）
            if (want != isSpraying.Value)
                SetSprayingServerRpc(want);

            // 噴水時持續震動（只震持有者自己的手把）
            if (isSpraying.Value)
            {
                Haptics.SetBoth(1f, 0.5f);
                _wasVibrating = true;
            }
            else if (_wasVibrating)
            {
                Haptics.StopBoth();
                _wasVibrating = false;
            }
        }

        // 2) 只有 Server 做 Raycast 滅火（結果一致）
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
        if (sprayAudio != null)
        {
            if (on && !sprayAudio.isPlaying) sprayAudio.Play();
            else if (!on && sprayAudio.isPlaying) sprayAudio.Stop();
        }

        if (sprayVFX == null) return;
        if (on) sprayVFX.Play();
        else sprayVFX.Stop();
    }

    void DoExtinguishRaycast()
    {
        if (nozzlePoint == null) return;

        Ray ray = new Ray(nozzlePoint.position, -nozzlePoint.right);
        if (Physics.Raycast(ray, out RaycastHit hit, range, fireLayer, QueryTriggerInteraction.Collide))
        {
            var fire = hit.collider.GetComponent<NetworkFireController>();
            if (fire != null)
                fire.ApplyExtinguishServer(extinguishRate * Time.deltaTime);
        }
    }
}
