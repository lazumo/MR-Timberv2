using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Rigidbody))]
public class FruitBouncePhysics : NetworkBehaviour
{
    [Header("Bounce Settings")]
    public float firstBounceForce = 2.5f;
    public float secondBounceForce = 1.2f;
    public int maxBounces = 2;

    private Rigidbody rb;
    private int bounceCount = 0;
    private bool physicsEnabled = false;
    private FruitDropState dropState;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = true;

        dropState = GetComponent<FruitDropState>();
    }

    // ===== Server 呼叫 =====
    public void EnablePhysics()
    {
        if (!IsServer) return;
        if (physicsEnabled) return;

        physicsEnabled = true;

        rb.isKinematic = false;          // ⭐ 真正開始掉落
        dropState?.MarkDropped();        // ⭐ 同步邏輯狀態
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!IsServer || !physicsEnabled) return;
        if (bounceCount >= maxBounces) return;

        rb.velocity = new Vector3(rb.velocity.x, 0f, rb.velocity.z);

        float force = bounceCount == 0 ? firstBounceForce : secondBounceForce;
        rb.AddForce(Vector3.up * force, ForceMode.Impulse);

        bounceCount++;
    }
}
