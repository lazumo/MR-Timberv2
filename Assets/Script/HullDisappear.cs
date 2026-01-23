using System.Collections;
using System.Resources;
using Unity.Netcode;
using UnityEngine;

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

        if (NetworkManager.Singleton.IsServer)
        {
            ResourceManager rm = ResourceManager.Instance;
            if (rm == null) rm = FindObjectOfType<ResourceManager>();

            if (rm != null)
            {
                rm.SpawnResource(transform.position + Vector3.up * 0.1f);
            }
        }

        // Destroy Hull
        Destroy(gameObject);
    }
}