using System.Collections;
using UnityEngine;

public class LowerHullShrinkDisappear : MonoBehaviour
{
    [Header("Shrink Settings")]
    public float delayBeforeShrink = 2f;
    public float disappearTime = 3f;
    public GameObject vfxPrefab;

    public void StartShrinkDisappear()
    {
        StartCoroutine(ShrinkDisappearCoroutine());
    }

    private IEnumerator ShrinkDisappearCoroutine()
    {
        if (delayBeforeShrink > 0)
            yield return new WaitForSeconds(delayBeforeShrink);

        float elapsed = 0f;
        Vector3 initialScale = transform.localScale;


        while (elapsed < disappearTime)
        {
            float t = elapsed / disappearTime;
            transform.localScale = Vector3.Lerp(initialScale, Vector3.zero, t);
            elapsed += Time.deltaTime;
            yield return null;
        }

        transform.localScale = Vector3.zero;


        if (vfxPrefab != null)
        {
            GameObject vfx = Instantiate(vfxPrefab, transform.position, Quaternion.identity);
            Destroy(vfx, 2f);
        }


        Destroy(gameObject);
    }
}