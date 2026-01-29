using UnityEngine;

public class ColorFactoryController : MonoBehaviour
{
    [Header("基礎設定")]
    [SerializeField] private BoxDetector detector;
    [SerializeField] private GameObject cylinder;
    [SerializeField] private int threshold = 5;

    [Header("Runtime State")]
    public bool isMiddlePointInside;   // 由 Trigger 更新
    public bool countSatisfied { get; private set; }
    public bool shouldBeActive { get; private set; }

    // 給 MiddleAnchorZoneDetector 呼叫
    public void SetMiddleInside(bool inside)
    {
        isMiddlePointInside = inside;
    }

    void Update()
    {
        if (detector == null) return;

        countSatisfied = detector.itemsInBox.Count >= threshold;

        // 數量夠 + 手不在裡面 → 顯示 cylinder
        shouldBeActive = countSatisfied && !isMiddlePointInside;

        if (cylinder != null && cylinder.activeSelf != shouldBeActive)
        {
            cylinder.SetActive(shouldBeActive);
        }
    }
}
