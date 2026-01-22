using UnityEngine;

public class LidController : MonoBehaviour
{
    [Header("引用設定")]
    [SerializeField] private ColorFactoryController factory;
    [SerializeField] private Transform leftController;
    [SerializeField] private Transform rightController;

    [Header("蓋子物件")]
    [SerializeField] private Transform lidPositive; // Z = 0.35 那個
    [SerializeField] private Transform lidNegative; // Z = -0.35 那個

    [Header("距離設定")]
    [Tooltip("手把分開到多遠時蓋子完全打開 (公尺)")]
    public float maxHandDistance = 0.35f;

    // 預設的 Z 偏移量
    private float defaultZ = 0.35f;

    void LateUpdate()
    {
        if (factory == null || lidPositive == null || lidNegative == null ||
            leftController == null || rightController == null) return;

        // 判斷條件：數量達標 且 手在 Anchor 內
        if (factory.countSatisfied && factory.isMiddlePointInside)
        {
            // 1. 計算當前兩手距離
            float currentDist = Vector3.Distance(leftController.position, rightController.position);

            // 2. 計算比例 (0 到 1 之間)
            // 距離 0 時 t=0, 距離達到 maxHandDistance 時 t=1
            float t = Mathf.Clamp01(currentDist / maxHandDistance);

            // 3. 根據比例計算 Z 值
            float targetZ = t * defaultZ;

            // 4. 更新位置 (保持原始的 local X 和 Y)
            lidPositive.localPosition = new Vector3(lidPositive.localPosition.x, lidPositive.localPosition.y, targetZ);
            lidNegative.localPosition = new Vector3(lidNegative.localPosition.x, lidNegative.localPosition.y, -targetZ);
        }
        else
        {
            // 如果條件不滿足，可以選擇讓蓋子回到 0 或保持 0.35
            // 這裡設定為回到 0.35 (打開狀態)
            UpdateLidPosition(defaultZ);
        }
    }

    private void UpdateLidPosition(float z)
    {
        lidPositive.localPosition = new Vector3(lidPositive.localPosition.x, lidPositive.localPosition.y, z);
        lidNegative.localPosition = new Vector3(lidNegative.localPosition.x, lidNegative.localPosition.y, -z);
    }
}