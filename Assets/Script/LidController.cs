using UnityEngine;
using System.Collections;
using Unity.Netcode;

public class LidController : MonoBehaviour
{
    [SerializeField] private ColorFactoryController factory;

    [Header("XR Rig")]
    [SerializeField] private string cameraRigName = "[BuildingBlock] Camera Rig";
    [SerializeField] private string rightHandPath = "TrackingSpace/RightHandAnchor/RightControllerInHandAnchor";
    [SerializeField] private string leftHandPath = "TrackingSpace/LeftHandAnchor/LeftControllerInHandAnchor";

    private Transform leftController;
    private Transform rightController;

    [SerializeField] private Transform lidPositive;
    [SerializeField] private Transform lidNegative;

    public float maxHandDistance = 0.35f;
    private float defaultZ = 0.35f;

    IEnumerator Start()
    {
        // ¥u¦b Server °õ¦æ
        if (!NetworkManager.Singleton.IsServer)
            yield break;

        while (leftController == null || rightController == null)
        {
            TryFindControllers();
            yield return null;
        }

        Debug.Log("[LidController] Server controllers found");
    }

    void TryFindControllers()
    {
        GameObject rig = GameObject.Find(cameraRigName);
        if (rig == null) return;

        if (leftController == null)
            leftController = rig.transform.Find(leftHandPath);

        if (rightController == null)
            rightController = rig.transform.Find(rightHandPath);
    }

    void LateUpdate()
    {
        if (!NetworkManager.Singleton.IsServer)
            return;

        if (factory == null || leftController == null || rightController == null)
            return;

        if (factory.countSatisfied && factory.isMiddlePointInside)
        {
            float dist = Vector3.Distance(leftController.position, rightController.position);
            float t = Mathf.Clamp01(dist / maxHandDistance);
            float targetZ = t * defaultZ;

            lidPositive.localPosition =
                new Vector3(lidPositive.localPosition.x, lidPositive.localPosition.y, targetZ);

            lidNegative.localPosition =
                new Vector3(lidNegative.localPosition.x, lidNegative.localPosition.y, -targetZ);
        }
        else
        {
            ResetLid();
        }
    }

    void ResetLid()
    {
        lidPositive.localPosition =
            new Vector3(lidPositive.localPosition.x, lidPositive.localPosition.y, defaultZ);

        lidNegative.localPosition =
            new Vector3(lidNegative.localPosition.x, lidNegative.localPosition.y, -defaultZ);
    }
}
