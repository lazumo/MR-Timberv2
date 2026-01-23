//using UnityEngine;
//using UnityEngine.XR;
//using Unity.Netcode;
//using System.Collections.Generic;

//public class XRResourceSpawnTester : MonoBehaviour
//{
//    [Header("Reference")]
//    public ResourceHandlerNetworked resourceHandler;

//    private InputDevice rightHandDevice;
//    private bool lastTriggerPressed = false;

//    void Start()
//    {
//        TryInitializeDevice();
//    }

//    void Update()
//    {
//        if (resourceHandler == null) return;

//        if (!rightHandDevice.isValid)
//        {
//            TryInitializeDevice();
//            return;
//        }

//        if (rightHandDevice.TryGetFeatureValue(CommonUsages.triggerButton, out bool triggerPressed))
//        {
//            // 只在「剛按下」時執行一次
//            if (triggerPressed && !lastTriggerPressed)
//            {
//                SpawnResource();
//            }

//            lastTriggerPressed = triggerPressed;
//        }
//    }

//    private void TryInitializeDevice()
//    {
//        var devices = new List<InputDevice>();
//        InputDevices.GetDevicesAtXRNode(XRNode.RightHand, devices);

//        if (devices.Count > 0)
//        {
//            rightHandDevice = devices[0];
//            Debug.Log("[XR] Right hand device found: " + rightHandDevice.name);
//        }
//    }

//    private void SpawnResource()
//    {
//        Debug.Log("[XR] Trigger pressed → Request spawn resource");

//        // 呼叫 Server 生成 resource
//        resourceHandler.SpawnResourceServerRpc(transform.position);
//    }
//}
