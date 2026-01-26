using UnityEngine;

public class FireController : MonoBehaviour
{
    [Header("Fire State")]
    public float fireIntensity = 30f;
    public float maxFireIntensity = 30f;

    [Header("References")]
    public ParticleSystem fireVFX;
    public Light fireLight;

    [Header("Visual Curve")]
    public AnimationCurve sizeCurve;

    public void ApplyExtinguish(float amount)
    {
        fireIntensity -= amount;
        fireIntensity = Mathf.Clamp(fireIntensity, 0f, maxFireIntensity);

        UpdateVisual();

        if (fireIntensity <= 0f)
        {
            ExtinguishCompletely();
        }
    }

    void UpdateVisual()
    {
        float t = fireIntensity / maxFireIntensity;

        if (fireVFX != null)
        {
            var main = fireVFX.main;

            float sizeFactor = sizeCurve.Evaluate(t);
            main.startSize = Mathf.Lerp(0.1f, 1.2f, sizeFactor);
        }

        if (fireLight != null)
        {
            fireLight.intensity = Mathf.Lerp(0f, 3f, t);
        }
    }

    void ExtinguishCompletely()
    {
        if (fireVFX != null)
        {
            fireVFX.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        if (fireLight != null)
        {
            fireLight.enabled = false;
        }

        Destroy(gameObject, 2f);
    }
}

