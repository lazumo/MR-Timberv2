using UnityEngine;

public class ColorFactory : MonoBehaviour
{
    [Header("Server-only data")]
    public int factoryColor;

    [HideInInspector]
    public GameObject ownerHouse;   // 或 ObjectNetworkSync / NetworkObject

    // 方便 server 端使用
    public ObjectNetworkSync OwnerHouseSync =>
        ownerHouse != null ? ownerHouse.GetComponent<ObjectNetworkSync>() : null;
}
