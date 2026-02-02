using UnityEngine;
using Unity.Netcode;
using System.Collections;

public class FruitVisualController : NetworkBehaviour
{
    public float fadeDuration = 1.5f;
    public override void OnNetworkSpawn()
    {
        FruitData data = GetComponent<FruitData>();
        MeshRenderer mr = GetComponent<MeshRenderer>();
        if (data == null || mr == null) return;

        int index = data.colorIndex.Value;
        Color target = ColorTable.Get(index);

        Material mat = new Material(mr.sharedMaterial);
        mat.color = new Color(target.r, target.g, target.b, 0f);
        mr.material = mat;

        StartCoroutine(FadeIn(mat, target, fadeDuration));
    }


    private IEnumerator FadeIn(Material mat, Color target, float dur)
    {
        float t = 0f;
        Color start = new(target.r, target.g, target.b, 0f);

        while (t < dur)
        {
            if (mat == null) yield break;
            t += Time.deltaTime;
            mat.color = Color.Lerp(start, target, t / dur);
            yield return null;
        }

        if (mat != null)
            mat.color = target;
    }
}
