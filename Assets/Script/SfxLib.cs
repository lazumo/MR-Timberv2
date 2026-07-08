using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tiny SFX helper: loads clips from Resources/SFX by name (cached) — no prefab wiring needed.
/// Clips: SawLoop, ElfFly, WoodPop, GrowUp, FruitDrop, FruitPop.
/// </summary>
public static class SfxLib
{
    private static readonly Dictionary<string, AudioClip> _cache = new();

    public static AudioClip Get(string name)
    {
        if (_cache.TryGetValue(name, out var clip)) return clip;

        clip = Resources.Load<AudioClip>("SFX/" + name);
        if (clip == null)
            Debug.LogWarning($"[SfxLib] Missing clip Resources/SFX/{name}");

        _cache[name] = clip; // cache even if null so we warn only once
        return clip;
    }

    /// One-shot 3D sound at a world position.
    public static void PlayAt(string name, Vector3 pos, float volume = 1f)
    {
        var clip = Get(name);
        if (clip != null)
            AudioSource.PlayClipAtPoint(clip, pos, volume);
    }

    /// One-shot 2D sound（不受距離/位置影響，重要回饋用這個保證聽得到）.
    public static void Play2D(string name, float volume = 1f)
    {
        var clip = Get(name);
        if (clip == null) return;

        var go = new GameObject($"SFX2D_{name}");
        var src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.spatialBlend = 0f;   // 2D
        src.volume = volume;
        src.Play();
        Object.Destroy(go, clip.length + 0.2f);
    }

    /// Adds a configured looping AudioSource for a clip (caller controls Play/Stop).
    public static AudioSource AddLoop(GameObject go, string name, float volume = 0.5f)
    {
        var src = go.AddComponent<AudioSource>();
        src.clip = Get(name);
        src.loop = true;
        src.playOnAwake = false;
        src.spatialBlend = 1f;   // 3D
        src.volume = volume;
        src.rolloffMode = AudioRolloffMode.Linear;
        src.minDistance = 0.5f;
        src.maxDistance = 8f;
        return src;
    }
}
