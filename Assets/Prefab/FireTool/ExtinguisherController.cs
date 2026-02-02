using UnityEngine;

public class ExtinguisherController : MonoBehaviour
{
    [Header("References")]
    public Transform nozzlePoint;
    public ParticleSystem sprayVFX;

    [Header("Extinguish Settings")]
    public float range = 3f;
    public float extinguishRate = 10f;

    private bool isSpraying = false;

    void Update()
    {
        // ====== ªÅ¥ÕÁä´ú¸Õ ======
        if (Input.GetKeyDown(KeyCode.Space))
        {
            ToggleSpray();
        }

        if (!isSpraying) return;

        Ray ray = new Ray(nozzlePoint.position, nozzlePoint.forward);
        Debug.DrawRay(ray.origin, ray.direction * range, Color.cyan);

        if (Physics.Raycast(ray, out RaycastHit hit, range))
        {
            FireController fire = hit.collider.GetComponentInParent<FireController>();
            if (fire != null)
            {
                fire.ApplyExtinguish(extinguishRate * Time.deltaTime);
            }
        }
    }

    void ToggleSpray()
    {
        isSpraying = !isSpraying;

        if (sprayVFX != null)
        {
            if (isSpraying) sprayVFX.Play();
            else sprayVFX.Stop();
        }
    }
}
