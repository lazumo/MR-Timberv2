using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class FruitExplodeVfx : NetworkBehaviour
{
    [Header("VFX (local instantiate on each client)")]
    [SerializeField] private ParticleSystem vfxPrefab;
    [SerializeField] private Transform vfxSpawnPoint;  // optional
    [SerializeField] private float despawnDelay = 0.2f;

    private bool exploded;

    // Server 呼叫：觸發特效並延遲 despawn
    public void ExplodeServer()
    {
        if (!IsServer) return;
        if (exploded) return;
        exploded = true;

        PlayVfxClientRpc();

        StartCoroutine(DespawnAfterDelay());
    }

    [ClientRpc]
    private void PlayVfxClientRpc()
    {
        if (vfxPrefab == null) return;

        var t = vfxSpawnPoint != null ? vfxSpawnPoint : transform;
        var ps = Instantiate(vfxPrefab, t.position, t.rotation);
        ps.Play();

        // 自動清掉特效物件，避免堆垃圾
        float life = ps.main.duration;
        if (ps.main.startLifetime.mode == ParticleSystemCurveMode.TwoConstants)
            life += ps.main.startLifetime.constantMax;
        else if (ps.main.startLifetime.mode == ParticleSystemCurveMode.Constant)
            life += ps.main.startLifetime.constant;

        Destroy(ps.gameObject, life + 0.5f);
    }

    private IEnumerator DespawnAfterDelay()
    {
        yield return new WaitForSeconds(despawnDelay);

        var netObj = GetComponent<NetworkObject>();
        if (netObj != null && netObj.IsSpawned)
            netObj.Despawn(true);
    }
}
