using UnityEngine;

/// <summary>
/// 為水果添加額外的重力效果
/// </summary>
public class FruitGravity : MonoBehaviour
{
    public float gravityMultiplier = 2f;
    private Rigidbody rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.useGravity = false; // 關閉預設重力，使用自訂重力
        }
    }

    private void FixedUpdate()
    {
        if (rb != null && !rb.isKinematic)
        {
            // 應用自訂重力
            rb.AddForce(Physics.gravity * gravityMultiplier, ForceMode.Acceleration);
        }
    }
}

