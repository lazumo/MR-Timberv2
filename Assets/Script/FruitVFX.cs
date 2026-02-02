using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class FruitDestroyVFX : NetworkBehaviour
{
    [Header("VFX List (index = colorIndex)")]
    [SerializeField] private List<GameObject> destroyVfxList = new();

    private FruitData fruitData;

    private void Awake()
    {
        fruitData = GetComponent<FruitData>();
    }

    /// <summary>
    /// Server 呼叫：依照 colorIndex 播放對應 VFX
    /// </summary>
    public void PlayDestroyVFX()
    {
        if (!IsServer) return;

        int colorIndex = 0;
        if (fruitData != null)
            colorIndex = fruitData.colorIndex.Value;

        PlayDestroyVFXClientRpc(colorIndex);
    }

    [ClientRpc]
    private void PlayDestroyVFXClientRpc(int colorIndex)
    {
        if (colorIndex < 0 || colorIndex >= destroyVfxList.Count)
        {
            Debug.LogWarning($"[FruitDestroyVFX] Invalid colorIndex: {colorIndex}");
            return;
        }

        GameObject vfxPrefab = destroyVfxList[colorIndex];
        if (vfxPrefab == null) return;

        GameObject vfx = Instantiate(
            vfxPrefab,
            transform.position,
            Quaternion.identity
        );

        // 自動銷毀（Particle System）
        var ps = vfx.GetComponent<ParticleSystem>();
        if (ps != null)
        {
            Destroy(vfx, ps.main.duration + ps.main.startLifetime.constantMax);
        }
        else
        {
            Destroy(vfx, 2f);
        }
    }
}
