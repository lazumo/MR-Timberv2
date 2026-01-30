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
        ApplyColor();
    }

    private void ApplyColor()
    {
        int index = fruitData.colorIndex.Value;

        for (int i = 0; i < colorObjects.Length; i++)
        {
            colorObjects[i].SetActive(i == index);
        }
    }
}
