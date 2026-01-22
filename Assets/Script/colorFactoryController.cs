using UnityEngine;

public class ColorFactoryController : MonoBehaviour
{
    [Header("基礎設定")]
    [SerializeField] private BoxDetector detector;
    [SerializeField] private GameObject cylinder;
    [SerializeField] private int threshold = 5;

    [Header("Middle Point 偵測")]
    public Transform middlePoint;
    public Collider anchorCollider;

    // 讓這兩個變數可以被外部讀取
    public bool countSatisfied { get; private set; }
    public bool isMiddlePointInside { get; private set; }
    public bool shouldBeActive { get; private set; }

    void Update()
    {
        if (detector != null && anchorCollider != null && middlePoint != null)
        {
            countSatisfied = detector.itemsInBox.Count > threshold;
            isMiddlePointInside = IsPointInsideCollider(anchorCollider, middlePoint.position);

            // Cylinder 的邏輯：數量夠且手不在裡面才開
            shouldBeActive = countSatisfied && !isMiddlePointInside;

            if (cylinder != null && cylinder.activeSelf != shouldBeActive)
            {
                cylinder.SetActive(shouldBeActive);
            }
        }
    }

    private bool IsPointInsideCollider(Collider col, Vector3 point)
    {
        return col.ClosestPoint(point) == point;
    }
}