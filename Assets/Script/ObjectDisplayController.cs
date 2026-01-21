using UnityEngine;

public class ObjectDisplayController : MonoBehaviour
{
    [SerializeField] private GameObject[] childObjects;

    // 單純的邏輯：根據索引值切換顯示
    public void SwitchToIndex(int index)
    {
        if (childObjects == null || childObjects.Length == 0) return;

        // 確保索引不越界
        int safeIndex = index % childObjects.Length;

        for (int i = 0; i < childObjects.Length; i++)
        {
            if (childObjects[i] != null)
            {
                childObjects[i].SetActive(i == safeIndex);
            }
        }
    }

    public int GetObjectCount()
    {
        return childObjects.Length;
    }
}