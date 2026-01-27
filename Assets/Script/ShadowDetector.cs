using Unity.Netcode;
using UnityEngine;

public class ToolShadowReceiver : NetworkBehaviour
{
    public bool ShadowHitThisFrame { get; private set; }
    public bool IsTouchingFruit { get; private set; }

    [Header("Touch release buffer")]
    public float releaseDelay = 0.3f;

    private float lastTouchTime = -10f;
    private void Update()
    {
        // 先假設這幀沒有被任何水果投影打到
        // 由於 FruitShadowProjector 是在 LateUpdate (比 Update 晚) 執行，
        // 如果有被 Raycast 到，RegisterShadowHit 會在稍後把這裡改成 true。
        ShadowHitThisFrame = false;
    }
    public void RegisterShadowHit()
    {
        ShadowHitThisFrame = true;
    }

    private void LateUpdate()
    {
        // 延遲釋放 touch 狀態
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
        // 只要水果還在盒子裡，就不斷更新計時器，讓 LateUpdate 裡的超時檢查永遠不會成立
        if (other.CompareTag("Fruit"))
        {
            IsTouchingFruit = true;
            lastTouchTime = Time.time;
        }
    }
}
