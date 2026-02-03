using Unity.Netcode;
using UnityEngine;

public class ElfPlayEffects : NetworkBehaviour
{
    [Header("Auto find all ParticleSystems under Elf and play")]
    [SerializeField] private bool includeInactive = true;
    [SerializeField] private bool forceActivateGameObjects = true;

    private ParticleSystem[] systems;

    private void Cache()
    {
        systems = GetComponentsInChildren<ParticleSystem>(includeInactive);
        Debug.Log($"[ElfPlayEffects] Found {systems.Length} ParticleSystems under {name}");
    }

    private void PlayAll(string from)
    {
        if (systems == null || systems.Length == 0) Cache();

        foreach (var ps in systems)
        {
            if (ps == null) continue;

            if (forceActivateGameObjects && !ps.gameObject.activeInHierarchy)
                ps.gameObject.SetActive(true);

            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Clear(true);
            ps.Play(true);
        }

        Debug.Log($"[ElfPlayEffects] PlayAll from {from} on {name}");
    }

    private void OnEnable()
    {
        Cache();
        PlayAll("OnEnable");
    }

    private void Start()
    {
        PlayAll("Start");
    }

    public override void OnNetworkSpawn()
    {
        PlayAll("OnNetworkSpawn");
    }

    // 你也可以手動從 Inspector 右鍵/按按鈕呼叫（或在別的腳本呼叫）
    [ContextMenu("Play Effects Now")]
    public void PlayNow()
    {
        PlayAll("ContextMenu");
    }
}
