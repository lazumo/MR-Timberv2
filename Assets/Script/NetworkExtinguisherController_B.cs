using Unity.Netcode;
using UnityEngine;

public class NetworkExtinguisherController_B : NetworkBehaviour
{
    [Header("References")]
    public Transform nozzlePoint;
    public ParticleSystem sprayVFX;

    [Header("Settings")]
    public float range = 10f;
    public float extinguishRate = 10f;
    public float triggerThreshold = 0.25f;

    [SerializeField] LayerMask fireLayer;
    // Server 同步噴射狀態，讓所有人看到 VFX
    public NetworkVariable<bool> isSpraying =
        new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Server 保存的「任一玩家 trigger」狀態
    private bool serverPressed;
    private bool clientPressed;

    // 每個玩家 cache，避免每幀送 RPC
    private bool lastLocalPressed;

    private AudioSource sprayAudio;

    public override void OnNetworkSpawn()
    {
        sprayAudio = SfxLib.AddLoop(gameObject, "SprayLoop", 0.85f);   // 水管比單支大聲
        isSpraying.OnValueChanged += OnSprayChanged;
        ApplySprayVFX(isSpraying.Value);
    }

    public override void OnNetworkDespawn()
    {
        isSpraying.OnValueChanged -= OnSprayChanged;
    }

    void OnSprayChanged(bool _, bool v) => ApplySprayVFX(v);

    void Update()
    {
        // 共用物件：每個 Client 各讀自己的 trigger（Host 也是 client）
        if (IsClient)
        {
            bool pressed = ReadAnyTrigger();

            // 只在變化時回報 Server
            if (pressed != lastLocalPressed)
            {
                lastLocalPressed = pressed;
                ReportTriggerServerRpc(pressed);
            }
        }

        // 只有 Server 做滅火 raycast
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

    // 共用物件通常 owner 不是每個人，所以要允許非 owner 呼叫
    [ServerRpc(RequireOwnership = false)]
    void ReportTriggerServerRpc(bool pressed, ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;

        if (senderId == NetworkManager.ServerClientId)
            serverPressed = pressed;
        else
            clientPressed = pressed;

        bool shouldSpray = serverPressed && clientPressed;

        if (isSpraying.Value != shouldSpray)
            isSpraying.Value = shouldSpray;
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

        Ray ray = new Ray(nozzlePoint.position, nozzlePoint.right);
        if (Physics.Raycast(ray, out RaycastHit hit, range, fireLayer, QueryTriggerInteraction.Collide))
        {
            Debug.Log($"[Raycast] Hit {hit.collider.name} at distance {hit.distance}");
            var fire = hit.collider.GetComponentInParent<NetworkFireController>();
            if (fire != null)
                fire.ApplyExtinguishServer(extinguishRate * Time.deltaTime);
        }
    }
}
