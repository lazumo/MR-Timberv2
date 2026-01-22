using UnityEngine;

/// <summary>
/// 處理水果的彈跳邏輯
/// </summary>
public class FruitBounce : MonoBehaviour
{
    [Header("彈跳設定")]
    public float firstBounceForce = 3f;
    public float secondBounceForce = 1.5f;
    public int maxBounces = 2;

    [Header("碰撞檢測")]
    public string groundTag = "Ground";
    public LayerMask groundLayer;

    private Rigidbody rb;
    private int bounceCount = 0;
    private float lastBounceTime = 0f;
    private const float BOUNCE_COOLDOWN = 0.15f;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();

        // 如果沒有設定 Layer，使用預設的 Ground Layer
        if (groundLayer == 0)
        {
            groundLayer = LayerMask.GetMask("Ground", "Default");
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        // 防止短時間內重複彈跳
        if (Time.time - lastBounceTime < BOUNCE_COOLDOWN) return;

        // 檢查是否達到最大彈跳次數
        if (bounceCount >= maxBounces) return;

        // 檢查碰撞物件是否為地面
        bool isGround = false;

        if (!string.IsNullOrEmpty(groundTag))
        {
            isGround = collision.gameObject.CompareTag(groundTag);
        }

        if (!isGround && groundLayer != 0)
        {
            isGround = ((1 << collision.gameObject.layer) & groundLayer) != 0;
        }

        if (!isGround) return;

        // 檢查碰撞法線（確保是從上方碰撞）
        if (collision.contacts.Length > 0)
        {
            Vector3 normal = collision.contacts[0].normal;

            // 只在接近垂直向上的法線時彈跳（地板）
            if (normal.y < 0.5f) return;

            // 如果碰到天花板（法線向下），直接返回
            if (normal.y < -0.3f)
            {
                if (rb != null)
                {
                    rb.velocity = new Vector3(rb.velocity.x, 0f, rb.velocity.z);
                }
                return;
            }
        }

        // 執行彈跳
        PerformBounce();
    }

    private void PerformBounce()
    {
        if (rb == null) return;

        // 清除當前垂直速度
        rb.velocity = new Vector3(rb.velocity.x, 0f, rb.velocity.z);

        // 根據彈跳次數決定力度
        float force = (bounceCount == 0) ? firstBounceForce : secondBounceForce;

        // 應用向上的力
        rb.AddForce(Vector3.up * force, ForceMode.Impulse);

        // 添加隨機旋轉
        rb.AddTorque(Random.insideUnitSphere * 2f, ForceMode.Impulse);

        // 更新彈跳計數和時間
        bounceCount++;
        lastBounceTime = Time.time;

        Debug.Log($"Fruit bounce #{bounceCount} with force {force}");
    }

    /// <summary>
    /// 重置彈跳計數（當水果被重新使用時）
    /// </summary>
    public void ResetBounce()
    {
        bounceCount = 0;
        lastBounceTime = 0f;
    }
}
