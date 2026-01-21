using UnityEngine;

public class ColorFactoryController : MonoBehaviour
{
    [SerializeField] private BoxDetector detector;
    [SerializeField] private GameObject cylinder;

    // 盢跑计砞 public瞷ウ穦瞷 Inspector 狾い
    public bool shouldBeActive;
    public int threshold = 5;

    void Update()
    {
        if (detector != null)
        {
            // 1. 穝硂 public 跑计计
            // 璶盎代ン计秖 0shouldBeActive 碞穦琌 true
            shouldBeActive = detector.itemsInBox.Count > threshold;

            // 2. 沮赣跑计北 Cylinder 秨闽
            if (cylinder != null && cylinder.activeSelf != shouldBeActive)
            {
                cylinder.SetActive(shouldBeActive);
                Debug.Log($"Cylinder 篈穝: {shouldBeActive}");
            }
        }
    }
}