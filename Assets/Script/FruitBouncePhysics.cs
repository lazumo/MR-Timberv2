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

    [Tooltip("高於此高度的『水平』靜態表面視為天花板，果子直接穿過。果樹吊點（3.5m）可能比 " +
             "MRUK 天花板 collider（真實房約 2.4m）高，果子會生在天花板上面、被當成落地而消失。")]
    public float ceilingIgnoreAboveY = 2.2f;

    private Rigidbody rb;
    private Collider _col;
    private int bounceCount = 0;
    private bool physicsEnabled = false;

    private FruitDropState dropState;
    private AutoDestroyNetworkObject autoDestroy;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        _col = GetComponent<Collider>();
        dropState = GetComponent<FruitDropState>();
        autoDestroy = GetComponent<AutoDestroyNetworkObject>();
    }

    public override void OnNetworkSpawn()
    {
        rb.isKinematic = true;
        rb.useGravity = false;

        // 第一次落地時所有 client 播「咚」（HasLanded 是同步變數）
        if (dropState != null)
            dropState.HasLanded.OnValueChanged += OnLandedChanged;
    }

    public override void OnNetworkDespawn()
    {
        if (dropState != null)
            dropState.HasLanded.OnValueChanged -= OnLandedChanged;
    }

    private void OnLandedChanged(bool oldVal, bool landed)
    {
        if (landed)
            SfxLib.PlayAt("FruitDrop", transform.position, 0.8f);
    }

    // MRUK 房間（實體或 3x3 虛擬房）的 collider 在 Default layer，不是 "Ground" —
    // 所以除了 Ground layer 外，「靜態的 Default 表面」也算地板；
    // 排除工具(box/鋸)、factory 容器、房子，避免收集中的果子被誤爆。
    private bool IsGroundSurface(Collision collision)
    {
        int layer = collision.gameObject.layer;

        if (layer == LayerMask.NameToLayer(groundLayerName)) return true;
        if (layer != 0) return false;                                   // 只有 Default 視為房間表面
        if (collision.collider.attachedRigidbody != null) return false; // 動態物件
        if (collision.collider.GetComponentInParent<ToolController>() != null) return false;
        if (collision.collider.GetComponentInParent<ColorFactory>() != null) return false;
        if (collision.collider.GetComponentInParent<ObjectNetworkSync>() != null) return false;

        return true;
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

        // ⭐ 天花板穿透：果子撞到「高處的水平靜態表面」= 天花板 collider →
        // 忽略碰撞讓它穿過繼續落下（不彈跳、不排程消失）。
        var contact = collision.GetContact(0);
        if (contact.point.y > ceilingIgnoreAboveY &&
            Mathf.Abs(contact.normal.y) > 0.5f &&
            collision.collider.attachedRigidbody == null)
        {
            Debug.Log($"[FruitBounce] Passing through ceiling-like collider '{collision.collider.name}' at y={contact.point.y:F2}.");
            if (_col != null)
                Physics.IgnoreCollision(collision.collider, _col);
            return;
        }

        // ⭐ 落在地板 → 啟動 Despawn（是否重複由 AutoDestroyNetworkObject 處理）
        if (IsGroundSurface(collision))
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
