using UnityEngine;
using UnityEngine.XR;
using System.Collections.Generic;

public class XRToolStateController : MonoBehaviour
{
    public ToolController toolController;

    private InputDevice rightHandDevice;
    private bool lastTriggerPressed = false;

    void Start()
    {
        TryInitializeDevice();
    }

    void Update()
    {
        if (toolController == null) return;

        if (!rightHandDevice.isValid)
        {
            TryInitializeDevice();
            return;
        }

        if (rightHandDevice.TryGetFeatureValue(CommonUsages.triggerButton, out bool triggerPressed))
        {
            // 只在「剛按下」時切換一次
            if (triggerPressed && !lastTriggerPressed)
            {
                CycleNextState();
            }

            lastTriggerPressed = triggerPressed;
        }
    }

    private void TryInitializeDevice()
    {
        var devices = new List<InputDevice>();
        InputDevices.GetDevicesAtXRNode(XRNode.RightHand, devices);

        if (devices.Count > 0)
        {
            rightHandDevice = devices[0];
            Debug.Log("[XR] Right hand device found: " + rightHandDevice.name);
        }
    }

    private void CycleNextState()
    {
        int current = toolController.CurrentState;
        int next = current + 1;

        // 合法範圍循環
        if (SceneController.CurrentLevel == 1 && next > 2) next = 0;
        if (SceneController.CurrentLevel == 2 && next > 4) next = 3;

        Debug.Log($"[XR] Switch tool: {current} → {next}");

        toolController.SetStateServerRpc(next);
    }
}
