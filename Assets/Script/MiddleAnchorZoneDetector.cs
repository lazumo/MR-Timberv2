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
        Debug.Log($"[MiddleAnchor] Enter factory zone");
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(factoryTag)) return;
        if (!isInsideFactory) return;
        isInsideFactory = false;
        Debug.Log($"[MiddleAnchor] Exit factory zone");
    }
}
