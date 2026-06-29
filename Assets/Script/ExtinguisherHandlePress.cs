using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Layer 3 — drives a press animation on the VIRTUAL extinguisher handle from the
/// PHYSICAL controller trigger. Pure visual; extinguishing stays in NetworkExtinguisherController.
///
/// Two drive modes (pick via the inspector):
///  • Linked (recommended): assign <see cref="sprayController"/>. The handle follows its
///    already-synced <c>isSpraying</c> flag, so BOTH players see the squeeze with zero
///    ownership concerns (it's the same trigger signal that fires the spray).
///  • Analog (fallback): leave sprayController empty. The owner reads the analog index-trigger
///    (0..1) and syncs it for a smooth partial-press — only works while this prop is owned by
///    the player holding it.
///
/// Put this on the extinguisher prop (same object as NetworkExtinguisherController),
/// assign <see cref="handle"/> to the lever pivot and tune <see cref="pressedLocalEuler"/>.
/// </summary>
public class ExtinguisherHandlePress : NetworkBehaviour
{
    [Header("Handle")]
    [Tooltip("The lever/handle pivot transform that rotates when the trigger is pressed.")]
    [SerializeField] private Transform handle;
    [Tooltip("Local rotation (degrees) added at FULL press, relative to the handle's rest pose.")]
    [SerializeField] private Vector3 pressedLocalEuler = new Vector3(-20f, 0f, 0f);
    [Tooltip("Optional: a local position offset added at full press (e.g. handle slides in).")]
    [SerializeField] private Vector3 pressedLocalOffset = Vector3.zero;
    [Tooltip("How fast the visual catches up to the trigger value.")]
    [SerializeField] private float followSpeed = 22f;

    [Header("Drive (recommended: link the spray controller)")]
    [Tooltip("If assigned, the handle follows this controller's synced isSpraying flag " +
             "(robust + both players see it). If empty, falls back to analog owner-read.")]
    [SerializeField] private NetworkExtinguisherController sprayController;

    [Header("Analog fallback (only used when sprayController is empty)")]
    [Tooltip("Ignore tiny trigger values below this.")]
    [SerializeField] private float deadzone = 0.02f;

    // Owner writes the analog press amount (fallback mode); everyone reads it.
    private NetworkVariable<float> press = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    private Quaternion _restLocalRot;
    private Vector3 _restLocalPos;
    private float _display;

    private void Awake()
    {
        if (handle != null)
        {
            _restLocalRot = handle.localRotation;
            _restLocalPos = handle.localPosition;
        }
    }

    private void Update()
    {
        float target = ReadTarget();

        if (handle == null) return;

        // Smoothly drive the handle on every peer.
        _display = Mathf.Lerp(_display, target, Time.deltaTime * followSpeed);
        handle.localRotation = _restLocalRot * Quaternion.Euler(pressedLocalEuler * _display);
        handle.localPosition = _restLocalPos + pressedLocalOffset * _display;
    }

    // Returns the press amount [0..1] every peer should display.
    private float ReadTarget()
    {
        // Linked mode — read the synced spray flag (visible to everyone).
        if (sprayController != null)
            return sprayController.isSpraying.Value ? 1f : 0f;

        // Analog fallback — owner reads the physical trigger and syncs it.
        if (IsOwner)
        {
            float t = Mathf.Max(
                OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.LTouch),
                OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.RTouch));

            if (t < deadzone) t = 0f;
            if (Mathf.Abs(t - press.Value) > 0.01f)
                press.Value = t;
        }
        return press.Value;
    }
}
