using UnityEngine;
using System.Collections;
using Unity.Netcode;

public class HullDisappear : MonoBehaviour
{
    public GameObject vfxPrefab;

    private float disappearTime = 3f;
    private bool hasHitGround = false;

    public void SetDisappearTime(float time)
    {
        disappearTime = time;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (hasHitGround) return;

        hasHitGround = true;
        StartCoroutine(DisappearCoroutine());
    }

    private IEnumerator DisappearCoroutine()
    {
        yield return new WaitForSeconds(disappearTime);

        // ===== VFX =====
        if (vfxPrefab != null)
        {
            GameObject vfx = Instantiate(vfxPrefab, transform.position, Quaternion.identity);
            ParticleSystem ps = vfx.GetComponent<ParticleSystem>();

            if (ps != null)
                Destroy(vfx, ps.main.duration + ps.main.startLifetime.constantMax);
            else
                Destroy(vfx, 2f);
        }

        // 找 ResourceHandler 並呼叫 ServerRpc
        ResourceHandlerNetworked handler = FindObjectOfType<ResourceHandlerNetworked>();

        if (handler != null)
        {
            Vector3 spawnPos = transform.position + Vector3.up * 0.1f; // 稍微浮起來
            handler.SpawnResourceServerRpc(spawnPos);
        }
        else
        {
            Debug.LogError("ResourceHandlerNetworked not found in scene!");
        }

        // Destroy Hull
        Destroy(gameObject);
    }
}
