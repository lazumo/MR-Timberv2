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

    // Server �I�s�GĲ�o�S�Ĩé��� despawn
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
        // 噗嘰啪 — 果子在地板爆掉
        SfxLib.PlayAt("FruitPop", transform.position, 0.9f);

        if (vfxPrefab == null) return;

        var t = vfxSpawnPoint != null ? vfxSpawnPoint : transform;
        var ps = Instantiate(vfxPrefab, t.position, t.rotation);
        ps.Play();

        // �۰ʲM���S�Ī���A�קK��U��
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
