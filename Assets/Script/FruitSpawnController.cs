using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class FruitSpawnController : NetworkBehaviour
{
    [Header("References")]
    public GameObject fruitPrefab;

    [Tooltip("固定的果實生成點")]
    public Transform[] spawnPoints;

    [Header("Fruit Parameters")]
    public int fruitCount = 5;
    public float fruitSpawnMinDelay = 1f;
    public float fruitSpawnMaxDelay = 3f;
    public float fruitFadeInDuration = 1.5f;
    public float fruitFallMinDelay = 2f;
    public float fruitFallMaxDelay = 5f;

    private FruitTree tree;
    private bool hasStarted = false;

    private readonly List<GameObject> spawnedFruits = new();

    private void Awake()
    {
        tree = GetComponentInParent<FruitTree>();
    }

    // =====================
    // Called by FruitTree (Server only)
    // =====================

    public void StartFruitSpawn()
    {
        if (!IsServer) return;
        if (hasStarted) return;

        hasStarted = true;
        StartCoroutine(SpawnRoutine());
    }

    // =====================
    // Server Side
    // =====================

    private IEnumerator SpawnRoutine()
    {
        int spawnIndex = 0;

        for (int i = 0; i < fruitCount; i++)
        {
            float delay = Random.Range(fruitSpawnMinDelay, fruitSpawnMaxDelay);
            yield return new WaitForSeconds(delay);

            Transform point = spawnPoints[spawnIndex % spawnPoints.Length];
            spawnIndex++;

            SpawnSingleFruit(point);
        }
    }

    private void SpawnSingleFruit(Transform spawnPoint)
    {
        GameObject fruit = Instantiate(fruitPrefab, spawnPoint.position, Quaternion.identity);
        NetworkObject netObj = fruit.GetComponent<NetworkObject>();
        FruitData data = fruit.GetComponent<FruitData>();
        netObj.Spawn();
        data.colorIndex.Value = tree.selectedColorIndex;

        spawnedFruits.Add(fruit);

        float fallDelay = Random.Range(fruitFallMinDelay, fruitFallMaxDelay);
        fruit.GetComponent<FruitDropState>()?.SetDropAfterSeconds(fallDelay);

        StartCoroutine(FruitFallRoutine(netObj.NetworkObjectId, fallDelay));
    }

    private IEnumerator FruitFallRoutine(ulong fruitId, float delay)
    {
        yield return new WaitForSeconds(delay);

        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(fruitId, out NetworkObject no))
            yield break;

        no.GetComponent<FruitBouncePhysics>()?.EnablePhysics();
    }


    public void ForceDropAllFruits()
    {
        if (!IsServer) return;

        foreach (var fruit in spawnedFruits)
        {
            if (fruit == null) continue;

            var netObj = fruit.GetComponent<NetworkObject>();
            if (netObj == null || !netObj.IsSpawned) continue;

            fruit.GetComponent<FruitBouncePhysics>()?.EnablePhysics();
        }

        spawnedFruits.Clear();
    }
}