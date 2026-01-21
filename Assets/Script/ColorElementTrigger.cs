using UnityEngine;

public class ColorElementTrigger : MonoBehaviour
{
    [Header("物件引用")]
    [SerializeField] private Transform lid1; // Box3_lid
    [SerializeField] private Transform lid2; // Box3_lid (1)
    [SerializeField] private GameObject colorElement;

    [Header("設定參數")]
    [Tooltip("觸發距離 (0.1 代表 10 公分)")]
    public float triggerDistance = 0.1f;
    [Tooltip("噴出的力道大小")]
    public float popForce = 2.0f;

    private bool hasTriggered = false;
    private Rigidbody elementRb;

    void Start()
    {
        if (colorElement != null)
        {
            // 初始狀態設為關閉
            colorElement.SetActive(false);
            // 預先取得 Rigidbody
            elementRb = colorElement.GetComponent<Rigidbody>();
        }
    }

    void Update()
    {
        // 如果已經觸發過，或者引用遺失，就不再計算
        if (hasTriggered || lid1 == null || lid2 == null || colorElement == null) return;

        // 1. 計算兩片蓋子在世界空間的距離
        float currentDistance = Vector3.Distance(lid1.position, lid2.position);

        // 2. 當距離小於 10 公分 (0.1m) 時觸發
        if (currentDistance < triggerDistance)
        {
            TriggerPop();
        }
    }

    private void TriggerPop()
    {
        hasTriggered = true;

        // 啟動物件
        colorElement.SetActive(true);

        // 3. 施加物理力
        if (elementRb != null)
        {
            // 讓它向上方噴出，並帶一點點隨機角度看起來比較自然
            Vector3 forceDirection = transform.up + new Vector3(Random.Range(-0.2f, 0.2f), 0, Random.Range(-0.2f, 0.2f));

            // 使用 Impulse 模式適合這種瞬間噴出的效果
            elementRb.AddForce(forceDirection.normalized * popForce, ForceMode.Impulse);

            Debug.Log("蓋子已合攏，colorElement 噴出！");
        }
        else
        {
            Debug.LogWarning("colorElement 身上沒有 Rigidbody，無法噴出！");
        }
    }

    // 提供一個重設方法，如果之後要重新玩一次
    public void ResetTrigger()
    {
        hasTriggered = false;
        colorElement.SetActive(false);
    }
}