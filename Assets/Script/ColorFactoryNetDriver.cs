using Unity.Netcode;
using UnityEngine;

public class ColorFactoryNetDriver : NetworkBehaviour
{
    [Header("Target (A)")]
    [SerializeField] private Transform factoryTransform;

    [Header("Rotate (Yaw only)")]
    [SerializeField] private float rotateLerp = 20f;

    [Header("Networking")]
    public NetworkVariable<bool> IsActive = new(false);
    public NetworkVariable<float> HandDistance = new(0f);      // current (m)
    public NetworkVariable<float> HandDistanceBase = new(0f);  // baseline at start (m)

    // Host hand controllers (server only)
    private Transform leftController;
    private Transform rightController;

    // Meta Building Blocks rig
    private const string RigName = "[BuildingBlock] Camera Rig";
    private const string LeftPath = "TrackingSpace/LeftHandAnchor/LeftControllerAnchor";
    private const string RightPath = "TrackingSpace/RightHandAnchor/RightControllerAnchor";

    private void Reset()
    {
        factoryTransform = transform;
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        TryBindHostControllers();
    }

    private void TryBindHostControllers()
    {
        if (leftController != null && rightController != null) return;

        var rig = GameObject.Find(RigName);
        if (rig == null) return;

        leftController = rig.transform.Find(LeftPath);
        rightController = rig.transform.Find(RightPath);
    }

    private void Update()
    {
        if (!IsServer) return;
        if (!IsActive.Value) return;

        TryBindHostControllers();
        if (leftController == null || rightController == null) return;

        Vector3 leftPos = leftController.position;
        Vector3 rightPos = rightController.position;

        // distance (功能二)
        float dist = Vector3.Distance(leftPos, rightPos);
        HandDistance.Value = dist;

        // baseline once per interaction (解決 BC 本來就有距離 → 用 ΔD)
        if (HandDistanceBase.Value <= 0.0001f)
            HandDistanceBase.Value = dist;

        // yaw only rotation (功能一)
        Vector3 dir = rightPos - leftPos;
        dir.y = 0f;

        if (dir.sqrMagnitude > 0.0001f)
        {
            Quaternion targetYaw = Quaternion.LookRotation(dir.normalized, Vector3.up);
            factoryTransform.rotation =
                Quaternion.Slerp(factoryTransform.rotation, targetYaw, Time.deltaTime * rotateLerp);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;
        if (!other.CompareTag("MiddlePoint")) return;
        Debug.Log("[ColorFactoryNetDriver] Trigger Enter by MiddlePoint");

        IsActive.Value = true;
        HandDistance.Value = 0f;
        HandDistanceBase.Value = 0f; // reset baseline for this interaction
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsServer) return;
        if (!other.CompareTag("MiddlePoint")) return;

        IsActive.Value = false;
        HandDistance.Value = 0f;
        HandDistanceBase.Value = 0f;
    }
}
