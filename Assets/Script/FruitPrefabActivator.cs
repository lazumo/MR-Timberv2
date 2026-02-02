using Unity.Netcode;
using UnityEngine;

public class FruitColorActivator : NetworkBehaviour
{
    [SerializeField] private GameObject[] colorObjects;
    private FruitData fruitData;

    private void Awake()
    {
        fruitData = GetComponent<FruitData>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        fruitData.colorIndex.OnValueChanged += OnColorChanged;

        ApplyColor(fruitData.colorIndex.Value);
    }

    public override void OnNetworkDespawn()
    {
        // ⭐ 記得解除訂閱
        if (fruitData != null)
        {
            fruitData.colorIndex.OnValueChanged -= OnColorChanged;
        }
    }

    private void OnColorChanged(int oldValue, int newValue)
    {
        ApplyColor(newValue);
    }

    private void ApplyColor(int index)
    {
        for (int i = 0; i < colorObjects.Length; i++)
        {
            if (colorObjects[i] != null)
                colorObjects[i].SetActive(i == index);
        }
    }
}