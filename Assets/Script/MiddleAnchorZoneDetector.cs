using UnityEngine;

public class MiddleAnchorZoneDetector : MonoBehaviour
{
    public string factoryTag = "FactoryZone";
    public bool isInsideFactory { get; private set; }
    private void Start()
    {
        isInsideFactory = false;
    }
    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(factoryTag)) return;
        if (isInsideFactory) return;
        isInsideFactory = true;
        // 從被撞到的物件往上找 factory
        ColorFactoryController factory =
            other.GetComponentInParent<ColorFactoryController>();

        if (factory != null)
        {
            factory.SetMiddleInside(true);
            Debug.Log($"[MiddleAnchor] Enter factory zone → factory instance {factory.GetInstanceID()}");
        }
        else
        {
            Debug.LogWarning("[MiddleAnchor] 找不到 ColorFactoryController in parent!");
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(factoryTag)) return;
        if (!isInsideFactory) return;
        isInsideFactory = false;
        ColorFactoryController factory =
            other.GetComponentInParent<ColorFactoryController>();

        if (factory != null)
        {
            factory.SetMiddleInside(false);
            Debug.Log($"[MiddleAnchor] Exit factory zone → factory instance {factory.GetInstanceID()}");
        }
    }
}
