using UnityEngine;

public class ColorFactory : MonoBehaviour
{
    [Header("Server-only data")]
    public GameObject ownerHouse;

    // 快速存取（server 端用）
    public ObjectNetworkSync OwnerHouseSync =>
        ownerHouse != null ? ownerHouse.GetComponent<ObjectNetworkSync>() : null;
}
