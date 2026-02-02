using UnityEngine;
using System.Collections.Generic;

public class BoxDetector : MonoBehaviour
{
    // �ΨӰO���ثe�b���l�����ؼЪ���M��
    public List<GameObject> itemsInBox = new List<GameObject>();

    // �]�w�n���������ҦW��
    [SerializeField] private string targetTag = "Fruit";

    // ������i�J���l��
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(targetTag))
        {
            if (!itemsInBox.Contains(other.gameObject))
            {
                itemsInBox.Add(other.gameObject);
            }
        }
    }

    // ���������}���l��
    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag(targetTag))
        {
            if (itemsInBox.Contains(other.gameObject))
            {
                itemsInBox.Remove(other.gameObject);
            }
        }
    }

    // ���Ѥ@��²�檺 API �ѥ~���d��
    public bool HasTargetObject()
    {
        return itemsInBox.Count > 0;
    }
}