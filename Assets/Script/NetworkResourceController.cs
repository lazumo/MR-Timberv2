using UnityEngine;
using Unity.Netcode;
using System.Collections;

public class NetworkResourceController : NetworkBehaviour
{
    public float moveSpeed = 1.5f;

    private int targetHouseId;
    private Vector3 targetPos;

    public void AssignJob(int houseId, Vector3 pos)
    {
        if (!IsServer) return;

        targetHouseId = houseId;
        targetPos = pos;

        StartCoroutine(MoveRoutine());
    }

    private IEnumerator MoveRoutine()
    {
        while (Vector3.Distance(transform.position, targetPos) > 0.05f)
        {
            transform.position = Vector3.MoveTowards(
                transform.position,
                targetPos,
                moveSpeed * Time.deltaTime
            );
            yield return null;
        }

        // 到達 → 建房
        if (HouseSpawnerNetworked.Instance.TryGetHouseObject(targetHouseId, out var houseObj))
        {
            ObjectNetworkSync sync = houseObj.GetComponent<ObjectNetworkSync>();
            if (sync != null)
            {
                sync.SetState(HouseState.Built);
            }
        }

        // 自己消失
        if (IsServer)
            GetComponent<NetworkObject>().Despawn();
    }
}