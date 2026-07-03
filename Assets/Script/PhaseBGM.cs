using UnityEngine;

/// <summary>
/// Plays looping background music during the "peaceful" phases (Logging / Catching / Juicing)
/// and fades it out when Firefighting starts; fades back in after a restart.
/// Local per-client (each headset plays its own copy) — CurrentPhase is already synced,
/// so everyone stays in step. Drop a music clip into <see cref="musicClip"/> and go.
/// </summary>
public class PhaseBGM : MonoBehaviour
{
    [Header("Music")]
    [Tooltip("Looping BGM for 砍樹/接果子/榨汁. Leave empty = silent.")]
    [SerializeField] private AudioClip musicClip;
    [SerializeField] private AudioSource source;

    [Header("Fire Music (滅火階段)")]
    [Tooltip("Urgent looping BGM for Firefighting. Leave empty = silence during the fire.")]
    [SerializeField] private AudioClip fireMusicClip;
    [SerializeField] private AudioSource fireSource;
    [Range(0f, 1f)] [SerializeField] private float fireVolume = 0.35f;

    [Header("Levels")]
    [Range(0f, 1f)] [SerializeField] private float volume = 0.35f;
    [Tooltip("Seconds to fade in/out when the phase changes.")]
    [SerializeField] private float fadeSeconds = 1.5f;

    private GameFlowController _flow;
    private float _level;      // calm-music fade level 0..1
    private float _fireLevel;  // fire-music fade level 0..1

    private void Update()
    {
        if (_flow == null)
        {
            _flow = GameFlowController.Instance;   // late-bind (spawns after us)
            if (_flow == null) return;
        }

        bool firePhase = _flow.CurrentPhase.Value == GamePhase.Firefighting;

        float step = fadeSeconds > 0f ? Time.deltaTime / fadeSeconds : 1f;
        _level     = Mathf.MoveTowards(_level,     firePhase ? 0f : 1f, step);
        _fireLevel = Mathf.MoveTowards(_fireLevel, firePhase ? 1f : 0f, step);

        Drive(source,     musicClip,     volume,     _level);
        Drive(fireSource, fireMusicClip, fireVolume, _fireLevel);
    }

    private static void Drive(AudioSource src, AudioClip clip, float baseVolume, float level)
    {
        if (src == null) return;

        if (clip != null && src.clip != clip)
        {
            src.clip = clip;
            src.loop = true;
        }

        src.volume = baseVolume * level;

        if (level > 0f && src.clip != null && !src.isPlaying)
            src.Play();
        else if (level <= 0f && src.isPlaying)
            src.Pause();   // pause (not stop) so it resumes where it left off
    }
}
