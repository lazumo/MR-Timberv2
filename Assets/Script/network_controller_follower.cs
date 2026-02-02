using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class HandFollower : NetworkBehaviour
{
    [Header("追蹤設定")]
    [SerializeField] private string cameraRigName = "[BuildingBlock] Camera Rig";

    [Header("手把路徑")]
    [SerializeField] private string rightHandPath = "TrackingSpace/RightHandAnchor/RightControllerInHandAnchor";
    [SerializeField] private string leftHandPath = "TrackingSpace/LeftHandAnchor/LeftControllerInHandAnchor";

    private Transform _targetAnchor;
    private Rigidbody _rb;
    private Renderer[] _renderers;
    private Collider[] _colliders;

    // Server 控制顯示/隱藏（所有人同步）
    public NetworkVariable<bool> VisualsOn =
        new NetworkVariable<bool>(true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        _rb = GetComponent<Rigidbody>();
        _renderers = GetComponentsInChildren<Renderer>(true);
        _colliders = GetComponentsInChildren<Collider>(true);

        VisualsOn.OnValueChanged += (_, v) => ApplyVisuals(v);
        ApplyVisuals(VisualsOn.Value);

        if (IsOwner)
        {
            // Host -> 右手；Client -> 左手
            bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
            string handPath = isHost ? rightHandPath : leftHandPath;

            FindLocalHandAnchor(handPath);

            if (_rb != null) _rb.isKinematic = false;
        }
        else
        {
            // 非 owner：關掉物理避免跟同步打架（但不要 disabled，後面需要同步隱藏/顯示）
            if (_rb != null)
            {
                _rb.isKinematic = true;
                _rb.useGravity = false;
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        VisualsOn.OnValueChanged -= (_, v) => ApplyVisuals(v);
    }

    private void ApplyVisuals(bool on)
    {
        if (_renderers != null)
            foreach (var r in _renderers) if (r != null) r.enabled = on;

        if (_colliders != null)
            foreach (var c in _colliders) if (c != null) c.enabled = on;
    }

    private void FindLocalHandAnchor(string handPath)
    {
        GameObject cameraRig = GameObject.Find(cameraRigName);
        if (cameraRig == null)
        {
            Debug.LogError($"[HandFollower] 找不到物件：{cameraRigName}");
            return;
        }

        _targetAnchor = cameraRig.transform.Find(handPath);
        if (_targetAnchor == null)
            Debug.LogError($"[HandFollower] 找不到路徑：{handPath}");
    }

    private void LateUpdate()
    {
        // 只有擁有者才跟隨自己的手把
        if (IsOwner && _targetAnchor != null)
        {
            transform.position = _targetAnchor.position;
            transform.rotation = _targetAnchor.rotation;
        }
    }
}
