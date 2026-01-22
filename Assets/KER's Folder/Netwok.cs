using Unity.Netcode;
using UnityEngine;

public class NetworkAutoStart : MonoBehaviour
{
    void Start()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;
        if (nm.IsListening) return;

        nm.StartHost();
    }
}

