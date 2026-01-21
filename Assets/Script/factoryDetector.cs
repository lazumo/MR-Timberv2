using UnityEngine;
using System.Collections.Generic;

public class BoxDetector : MonoBehaviour
{
    // 用來記錄目前在盒子內的目標物件清單
    public List<GameObject> itemsInBox = new List<GameObject>();

    // 設定要偵測的標籤名稱
    [SerializeField] private string targetTag = "Fruit";

    // 當物件進入盒子時
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(targetTag))
        {
            if (!itemsInBox.Contains(other.gameObject))
            {
                itemsInBox.Add(other.gameObject);
                Debug.Log($"{other.name} 進入了盒子！目前數量: {itemsInBox.Count}");
            }
        }
    }

    // 當物件離開盒子時
    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag(targetTag))
        {
            if (itemsInBox.Contains(other.gameObject))
            {
                itemsInBox.Remove(other.gameObject);
                Debug.Log($"{other.name} 離開了盒子！目前數量: {itemsInBox.Count}");
            }
        }
    }

    // 提供一個簡單的 API 供外部查詢
    public bool HasTargetObject()
    {
        return itemsInBox.Count > 0;
    }
}