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

        // 先設定資料
        FruitData data = fruit.GetComponent<FruitData>();
        int colorIndex = tree.selectedColorIndex;
        Color color = tree.GetSelectedFruitColor();

        data.colorIndex = colorIndex;
        data.color = color;
        MeshRenderer mr = fruit.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            Material mat = new Material(mr.material);
            mat.color = new Color(color.r, color.g, color.b, 0f); // 從透明開始
            mr.material = mat;
        }
        netObj.Spawn();
        spawnedFruits.Add(fruit);

        SetupFruitClientRpc(netObj.NetworkObjectId, fruitFadeInDuration);

        float fallDelay = Random.Range(fruitFallMinDelay, fruitFallMaxDelay);
        StartCoroutine(FruitFallRoutine(netObj.NetworkObjectId, fallDelay));
    }


    private IEnumerator FruitFallRoutine(ulong fruitId, float delay)
    {
        yield return new WaitForSeconds(delay);

        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(fruitId, out NetworkObject no))
            yield break;

        no.GetComponent<FruitBouncePhysics>()?.EnablePhysics();
    }


    // =====================
    // Client Side
    // =====================

    [ClientRpc]
    private void SetupFruitClientRpc(ulong fruitId, float fadeDuration)
    {
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(fruitId, out NetworkObject netObj))
            return;

        GameObject fruit = netObj.gameObject;

        // ===== Fade In =====
        MeshRenderer mr = fruit.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            Material mat = mr.material; // 已是 instance

            StartCoroutine(FadeInRoutine(mat, mat.color, fadeDuration));
        }
    }

    private IEnumerator FadeInRoutine(Material mat, Color targetColor, float dur)
    {
        float t = 0f;
        Color start = new(targetColor.r, targetColor.g, targetColor.b, 0f);

        while (t < dur)
        {
            if (mat == null) yield break;
            t += Time.deltaTime;
            mat.color = Color.Lerp(start, targetColor, t / dur);
            yield return null;
        }

        if (mat != null)
            mat.color = targetColor;
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