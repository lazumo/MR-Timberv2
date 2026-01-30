using System.Collections.Generic;
using UnityEngine;

public class FruitSqueezeInContainer_Tag : MonoBehaviour
{
    [Header("Bars (drag from colorfactory)")]
    [SerializeField] private Transform barB;
    [SerializeField] private Transform barC;

    [Header("Fruit Tag")]
    [SerializeField] private string fruitTag = "Fruit";

    [Header("Squeeze Axis (your bars move on local Z)")]
    [SerializeField] private float minSqueeze = 0.35f; // 最扁 35%
    [SerializeField] private float maxSqueeze = 1.0f;  // 原大小
    [SerializeField] private float lerpSpeed = 15f;

    [Header("Optional volume compensate")]
    [SerializeField] private bool volumeCompensate = true;
    [SerializeField] private float compensateStrength = 0.5f;

    private readonly HashSet<Transform> fruits = new();
    private readonly Dictionary<Transform, Vector3> initialScale = new();

    private float gap0;

    private void Start()
    {
        gap0 = GetGap();
        if (gap0 <= 0.0001f) gap0 = 0.0001f;
    }

    private void Update()
    {
        if (barB == null || barC == null) return;
        if (fruits.Count == 0) return;

        float gap = GetGap();
        float t = gap / gap0;                    // 1=原開口，越小越擠
        float squeeze = Mathf.Clamp(t, minSqueeze, maxSqueeze);

        foreach (var fruit in fruits)
        {
            if (fruit == null) continue;
            if (!initialScale.TryGetValue(fruit, out var s0)) continue;

            // 你 bar 沿 local Z 夾，所以 fruit 沿 local Z 壓扁
            Vector3 target = s0;
            target.z = s0.z * squeeze;

            if (volumeCompensate)
            {
                float expand = Mathf.Pow(1f / squeeze, compensateStrength);
                target.x = s0.x * expand;
                target.y = s0.y * expand;
            }

            fruit.localScale = Vector3.Lerp(fruit.localScale, target, Time.deltaTime * lerpSpeed);
        }
    }

    private float GetGap()
    {
        return Vector3.Distance(barB.position, barC.position);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(fruitTag)) return;

        Transform fruit = other.transform;

        if (fruits.Add(fruit))
            initialScale[fruit] = fruit.localScale;
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(fruitTag)) return;

        Transform fruit = other.transform;

        fruits.Remove(fruit);
        initialScale.Remove(fruit);
    }
}
