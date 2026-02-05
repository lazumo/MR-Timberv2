using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Rigidbody))]
public class FruitBouncePhysics : NetworkBehaviour
{
    [Header("Bounce Settings")]
    public float firstBounceForce = 2.5f;
    public float secondBounceForce = 1.2f;
    public int maxBounces = 2;

    [Header("Destroy")]
    public float destroyDelay = 2.0f;
    public string groundLayerName = "Ground";

    private Rigidbody rb;
    private int bounceCount = 0;
    private bool physicsEnabled = false;

    private FruitDropState dropState;
    private AutoDestroyNetworkObject autoDestroy;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        dropState = GetComponent<FruitDropState>();
        autoDestroy = GetComponent<AutoDestroyNetworkObject>();
    }

    public override void OnNetworkSpawn()
    {
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    // ===== Server 呼叫 =====
    public void EnablePhysics()
    {
        if (!IsServer || physicsEnabled) return;

        physicsEnabled = true;
        rb.isKinematic = false;
        rb.useGravity = true;
        dropState?.MarkDropped();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!IsServer || !physicsEnabled) return;

        // ⭐ 撞到非 Box → 啟動 Despawn（是否重複由 AutoDestroyNetworkObject 處理）
        if (collision.gameObject.layer == LayerMask.NameToLayer(groundLayerName))
        {
            autoDestroy?.ScheduleDespawn(destroyDelay);
        }

        // ⭐ 超過 bounce 次數就不再彈
        if (bounceCount >= maxBounces) return;

        // ⭐ 第一次落地標記
        if (dropState != null && !dropState.HasLanded.Value)
        {
            dropState.MarkLanded();
        }

        // ⭐ 垂直速度歸零再彈
        rb.velocity = new Vector3(rb.velocity.x, 0f, rb.velocity.z);

        float force = (bounceCount == 0) ? firstBounceForce : secondBounceForce;
        rb.AddForce(Vector3.up * force, ForceMode.Impulse);

        bounceCount++;
    }
}
