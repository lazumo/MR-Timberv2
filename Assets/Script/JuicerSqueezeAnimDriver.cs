using UnityEngine;

/// <summary>
/// Drives the juicer's baked "Jucier" animation by the live two-hand squeeze amount instead of
/// letting it play on a timer. The hand squeeze (0 = open / hands at baseline, 1 = fully squeezed)
/// is mapped linearly to the animation's normalized time, and the Animator is frozen (speed 0)
/// so we scrub the clip by hand every frame.
///
/// Reads the synced values off ColorFactoryNetDriver, so this runs on every client (host included)
/// and everyone sees the same pose. Use this INSTEAD of JuicerBarNetView on the new juicer — the
/// clip already animates the L-push / R-push parts, so if those parts are also assigned as the
/// factory's barB / barC, FruitSqueezeInContainer_Tag still reads the gap and pops fruits as before.
/// </summary>
[RequireComponent(typeof(Animator))]
public class JuicerSqueezeAnimDriver : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private ColorFactoryNetDriver driver;
    [SerializeField] private string stateName = "Jucier";
    [SerializeField] private int layer = 0;

    [Header("Hand squeeze range (m)")]
    [Tooltip("How far the hands must close below the baseline distance for a full squeeze. " +
             "JuicerBarNetView used 0.30 (offsetScale 1.0, clamp ±0.30).")]
    [SerializeField] private float squeezeRange = 0.30f;

    [Tooltip("Flip if the animation runs the wrong way (closing hands should drive time 0→1).")]
    [SerializeField] private bool invert = false;

    [Tooltip("Smoothing of the scrub. 0 = snap to the hand value.")]
    [SerializeField] private float lerpSpeed = 25f;

    private Animator _animator;
    private int _stateHash;
    private float _normTime;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _stateHash = Animator.StringToHash(stateName);
    }

    private void OnEnable()
    {
        if (_animator != null)
            _animator.speed = 0f; // we scrub manually
    }

    private void Update()
    {
        float target = 0f;

        if (driver != null && driver.IsActive.Value)
        {
            float baseD = driver.HandDistanceBase.Value;
            float curD  = driver.HandDistance.Value;

            float closed = baseD - curD;                 // >0 when hands close in
            if (invert) closed = -closed;

            target = squeezeRange > 0.0001f
                ? Mathf.Clamp01(closed / squeezeRange)
                : 0f;
        }

        _normTime = lerpSpeed > 0f
            ? Mathf.Lerp(_normTime, target, Time.deltaTime * lerpSpeed)
            : target;

        _animator.Play(_stateHash, layer, _normTime);
    }
}
