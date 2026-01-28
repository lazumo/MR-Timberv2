using UnityEngine;
using System.Collections;

public class LidController : MonoBehaviour
{
    [Header("引用設定")]
    [SerializeField] private ColorFactoryController factory;

    [Header("XR Rig 尋找設定")]
    [SerializeField] private string cameraRigName = "[BuildingBlock] Camera Rig";
    [SerializeField] private string rightHandPath = "TrackingSpace/RightHandAnchor/RightControllerInHandAnchor";
    [SerializeField] private string leftHandPath = "TrackingSpace/LeftHandAnchor/LeftControllerInHandAnchor";

    private Transform leftController;
    private Transform rightController;

    [Header("蓋子物件")]
    [SerializeField] private Transform lidPositive;
    [SerializeField] private Transform lidNegative;

    [Header("距離設定")]
    public float maxHandDistance = 0.35f;

    private float defaultZ = 0.35f;

    IEnumerator Start()
    {
        // 等待 XR Rig + Controllers 出現
        while (leftController == null || rightController == null)
        {
            TryFindControllers();
            yield return null;
        }
    }

    void TryFindControllers()
    {
        GameObject rig = GameObject.Find(cameraRigName);
        if (rig == null) return;

        if (leftController == null)
        {
            Transform t = rig.transform.Find(leftHandPath);
            if (t != null) leftController = t;
        }

        if (rightController == null)
        {
            Transform t = rig.transform.Find(rightHandPath);
            if (t != null) rightController = t;
        }
        Debug.Log("found two controllers");
    }

    void LateUpdate()
    {
        if (factory == null || lidPositive == null || lidNegative == null ||
            leftController == null || rightController == null)
            return;

        // 條件：數量達標 + 手在工廠內
        if (factory.countSatisfied && factory.isMiddlePointInside)
        {
            Debug.Log("tracking 2 controllers");
            float currentDist = Vector3.Distance(leftController.position, rightController.position);
            float t = Mathf.Clamp01(currentDist / maxHandDistance);
            float targetZ = t * defaultZ;

            lidPositive.localPosition =
                new Vector3(lidPositive.localPosition.x, lidPositive.localPosition.y, targetZ);

            lidNegative.localPosition =
                new Vector3(lidNegative.localPosition.x, lidNegative.localPosition.y, -targetZ);
        }
        else
        {
            UpdateLidPosition(defaultZ);
        }
    }

    private void UpdateLidPosition(float z)
    {
        lidPositive.localPosition =
            new Vector3(lidPositive.localPosition.x, lidPositive.localPosition.y, z);

        lidNegative.localPosition =
            new Vector3(lidNegative.localPosition.x, lidNegative.localPosition.y, -z);
    }
}
