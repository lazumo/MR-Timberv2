using System.Collections;
using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Rigidbody))]
public class FruitInfo : NetworkBehaviour
{
    [Header("Physics Settings")]
    public float firstBounceForce = 2.0f;
    public float secondBounceForce = 1.0f;
    public int maxBounces = 2;
    public float gravityMultiplier = 2f;
    public string floorTag = "Ground";

    private Rigidbody rb;
    private int bounceCount = 0;
    private float lastBounceTime = 0f;

    // 是否已經啟用物理（掉落後）
    private bool PhysicsEnabled => rb != null && !rb.isKinematic;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (rb)
        {
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.isKinematic = true;   // 一開始掛在樹上
            rb.useGravity = false;
        }
    }

    private void FixedUpdate()
    {
        // 只有 Server 處理物理（權威）
        if (!IsServer || !IsSpawned || !PhysicsEnabled)
            return;

        rb.AddForce(Physics.gravity * gravityMultiplier, ForceMode.Acceleration);

        // 防止往上亂飛（天花板保險）
        if (rb.velocity.y > 8f)
            rb.velocity = new Vector3(rb.velocity.x, 8f, rb.velocity.z);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!IsServer || !IsSpawned || !PhysicsEnabled)
            return;

        if (collision.contacts.Length == 0)
            return;

        Vector3 normal = collision.contacts[0].normal;

        // 天花板
        if (normal.y < -0.3f)
        {
            rb.velocity = new Vector3(rb.velocity.x, 0f, rb.velocity.z);
            return;
        }

        // 牆壁（忽略）
        if (normal.y < 0.5f)
            return;

        // 地板彈跳
        if (collision.gameObject.CompareTag(floorTag) &&
            bounceCount < maxBounces &&
            Time.time - lastBounceTime > 0.1f)
        {
            rb.velocity = new Vector3(rb.velocity.x, 0f, rb.velocity.z);

            float force = (bounceCount == 0)
                ? firstBounceForce
                : secondBounceForce;

            rb.AddForce(Vector3.up * force, ForceMode.Impulse);
            rb.AddTorque(Random.insideUnitSphere, ForceMode.Impulse);

            bounceCount++;
            lastBounceTime = Time.time;
        }
    }

    // =====================================================
    // Lifecycle
    // =====================================================
    public void SetAutoDestroy(float lifetime)
    {
        if (IsServer)
            StartCoroutine(AutoDespawnRoutine(lifetime));
    }

    private IEnumerator AutoDespawnRoutine(float lifetime)
    {
        yield return new WaitForSeconds(lifetime);

        if (IsServer && IsSpawned)
            NetworkObject.Despawn();
    }

    // =====================================================
    // Pickup
    // =====================================================
    public void PickUp(ulong playerId)
    {
        if (!IsServer)
        {
            SubmitPickupRequestServerRpc(playerId);
            return;
        }

        if (IsSpawned)
            NetworkObject.Despawn();
    }

    [ServerRpc(RequireOwnership = false)]
    private void SubmitPickupRequestServerRpc(ulong playerId)
    {
        PickUp(playerId);
    }
}
