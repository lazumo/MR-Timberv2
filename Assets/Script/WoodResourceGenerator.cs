using UnityEngine;
using Unity.Netcode;

public class WoodResourceGenerator : NetworkBehaviour
{
    public static WoodResourceGenerator Instance { get; private set; }

    [Header("Prefabs")]
    [SerializeField] private GameObject woodDropPrefab; // 先掉落的木材素材 prefab（NetworkObject）

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public void SpawnWoodDrop(Vector3 pos)
    {
        if (!IsServer) return;
        if (woodDropPrefab == null)
        {
            Debug.LogError("[WoodResourceGenerator] woodDropPrefab missing!");
            return;
        }

        var obj = Instantiate(woodDropPrefab, pos, Quaternion.identity);
        obj.GetComponent<NetworkObject>().Spawn();
    }
}
