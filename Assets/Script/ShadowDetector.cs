using Unity.Netcode;
using UnityEngine;

public class ToolShadowReceiver : NetworkBehaviour
{
    private int lastShadowHitFrame = -1;

    public bool ShadowHitThisFrame =>
        lastShadowHitFrame <= Time.frameCount && lastShadowHitFrame >= Time.frameCount - 5;

    public bool IsTouchingFruit { get; private set; }

    [Header("Touch release buffer")]
    public float releaseDelay = 0.3f;

    private float lastTouchTime = -10f;

    public void RegisterShadowHit()
    {
        lastShadowHitFrame = Time.frameCount;
    }

    private void LateUpdate()
    {
        if (IsTouchingFruit && Time.time - lastTouchTime > releaseDelay)
        {
            IsTouchingFruit = false;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Fruit"))
        {
            IsTouchingFruit = true;
            lastTouchTime = Time.time;
        }
    }

    private void OnTriggerStay(Collider other)
    {
        if (other.CompareTag("Fruit"))
        {
            IsTouchingFruit = true;
            lastTouchTime = Time.time;
        }
    }
}
