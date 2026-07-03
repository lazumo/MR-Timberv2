using UnityEngine;
using Unity.Netcode;
using System.Collections;

public class NetworkResourceController : NetworkBehaviour
{
    public float moveSpeed = 1.5f;

    private int targetHouseId;
    private Vector3 targetPos;

    // 小精靈飛行聲：存活期間循環播（despawn 時隨物件一起消失）
    public override void OnNetworkSpawn()
    {
        var fly = SfxLib.AddLoop(gameObject, "ElfFly", 0.85f);
        if (fly != null)
        {
            fly.minDistance = 0.3f;    // 近距離更明顯的方向感
            fly.dopplerLevel = 1.5f;   // 移動時有都卜勒效果，更能聽出往哪飛
            fly.Play();
        }
    }

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