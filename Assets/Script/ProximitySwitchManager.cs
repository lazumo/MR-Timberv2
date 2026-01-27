using Unity.Netcode;
using UnityEngine;

public class ProximitySwitchManager : NetworkBehaviour
{
    [SerializeField] private MiddlePointProvider provider;

    [Header("距離門檻（exit > enter 防抖動）")]
    [SerializeField] private float enterDistance = 0.20f;
    [SerializeField] private float exitDistance = 0.25f;

    [Header("物件A Prefab（必須有 NetworkObject + NetworkTransform）")]
    [SerializeField] private NetworkObject objectAPrefab;

    private bool isClose = false;
    private NetworkObject objectAInstance;

    private void Update()
    {
        if (!IsServer) return;
        if (provider == null) return;
        if (provider.HostHand == null || provider.ClientHand == null) return;

        float d = provider.Distance.Value;

        if (!isClose)
        {
            if (d <= enterDistance) EnterClose();
        }
        else
        {
            if (d >= exitDistance) ExitClose();
            else UpdateObjectA(); // close 狀態持續更新A
        }
    }

    private void EnterClose()
    {
        isClose = true;

        // 1) 隱藏兩邊 HandFollower
        provider.HostHand.VisualsOn.Value = false;
        provider.ClientHand.VisualsOn.Value = false;

        // 2) Spawn A 在中點 (rotation 以 host 為主)
        if (objectAInstance == null)
        {
            objectAInstance = Instantiate(objectAPrefab);
            objectAInstance.Spawn(true);
        }

        UpdateObjectA();
    }

    private void ExitClose()
    {
        isClose = false;

        // 1) 顯示兩邊 HandFollower
        provider.HostHand.VisualsOn.Value = true;
        provider.ClientHand.VisualsOn.Value = true;

        // 2) Despawn A
        if (objectAInstance != null)
        {
            objectAInstance.Despawn(true);
            objectAInstance = null;
        }
    }

    private void UpdateObjectA()
    {
        if (objectAInstance == null) return;

        Vector3 midPos = provider.MidPosition.Value;
        Quaternion rot = provider.HostRotation.Value; // 以 server 手把為主

        objectAInstance.transform.SetPositionAndRotation(midPos, rot);
    }
}
