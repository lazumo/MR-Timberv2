using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class FRUIT_TREE : NetworkBehaviour
{
   
    [Header("果實 Prefab")]
    public GameObject fruitPrefab;
    public Transform fruitsParent;

    [Header("假投影 Shadow")]
    public GameObject shadowPlanePrefab;

    [Header("果實顏色設定 (三種顏色)")]
    public Color[] treeFruitColors = new Color[]
    {
        new Color(1f, 0.2f, 0.2f),    // 紅色
        new Color(0.2f, 1f, 0.2f),    // 綠色
        new Color(0.2f, 0.5f, 1f)     // 藍色
    };

    [Header("選擇的顏色索引")]
    [Range(0, 2)]
    public int selectedColorIndex = 0; // 默認為第一種顏色

    [Header("成長時間")]
    public float growDuration = 2f;
    public float strongStageDuration = 30f; // 樹存在總時長

    [Header("果實參數")]
    public int fruitCount = 5;
    public float fruitSpawnMinDelay = 1f;
    public float fruitSpawnMaxDelay = 3f;
    public float fruitFadeInDuration = 1.5f;
    public float fruitFallMinDelay = 2f;
    public float fruitFallMaxDelay = 5f;
    public float fruitLifetimeAfterFall = 15f;

    [Header("果實物理設定")]
    public float fruitMass = 0.5f;
    public float gravityMultiplier = 2f;
    public float fruitDrag = 0.2f;
    public float fruitAngularDrag = 5f;
    public float initialDropForce = 2f;

    [Header("彈跳設定")]
    public float firstBounceForce = 3f;
    public float secondBounceForce = 1.5f;
    public int maxBounces = 2;

    [Header("投影設定")]
    public Material shadowProjectorMaterial; // Projector 材質
    public float shadowHeight = 10f;         // 投影器高度
    public float shadowSize = 1f;            // 投影大小

    [Header("Sound FX")]
    public AudioSource audioSource;
    public AudioClip sfxGrow;
    public AudioClip sfxFruitSpawn;
    public AudioClip sfxFruitFall;

    public NetworkVariable<Vector3> networkTargetScale = new NetworkVariable<Vector3>(
        Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private List<GameObject> spawnedFruits = new List<GameObject>();
    private List<Coroutine> runningCoroutines = new List<Coroutine>();
    private float timeAlive = 0f;

    private void Awake()
    {
        transform.localScale = Vector3.zero;
        if (!audioSource) audioSource = gameObject.AddComponent<AudioSource>();
    }

    public override void OnNetworkSpawn()
    {
        if (!audioSource) audioSource = gameObject.AddComponent<AudioSource>();

        networkTargetScale.OnValueChanged += OnScaleChanged;


        if (networkTargetScale.Value != Vector3.zero)
        {
            if (transform.localScale.magnitude < 0.01f)
                StartCoroutine(GrowTree(networkTargetScale.Value));
            else
                transform.localScale = networkTargetScale.Value;
        }

        if (IsServer)
            StartCoroutine(LifeRoutine());
    }

    public override void OnNetworkDespawn()
    {
        networkTargetScale.OnValueChanged -= OnScaleChanged;
        StopAllRunningCoroutines();
        ForceDropAllFruits();
    }

    private void Update()
    {
        timeAlive += Time.deltaTime;
        if (timeAlive > 1.0f)
        {
            Vector3 target = networkTargetScale.Value;
            if (target.magnitude > 0.01f && transform.localScale.magnitude < 0.001f)
            {
                transform.localScale = target;
            }
        }
    }


    private void OnScaleChanged(Vector3 oldVal, Vector3 newVal)
    {
        if (transform.localScale.magnitude < 0.01f && newVal.magnitude > 0.01f)
            StartCoroutine(GrowTree(newVal));
        else
            transform.localScale = newVal;
    }

    private IEnumerator LifeRoutine()
    {
        yield return null;
        if (networkTargetScale.Value == Vector3.zero)
            networkTargetScale.Value = Vector3.one;

        PlaySFX(sfxGrow);
        StartCoroutine(GrowTree(networkTargetScale.Value));

        yield return new WaitForSeconds(2f); // 等待樹長成
        yield return SpawnFruitsRandomly();

        yield return new WaitForSeconds(strongStageDuration);

        // 樹消失邏輯（可選）
        if (IsSpawned) NetworkObject.Despawn();
    }

    private IEnumerator GrowTree(Vector3 target)
    {
        float t = 0;
        Vector3 start = transform.localScale;

        while (t < growDuration)
        {
            t += Time.deltaTime;
            transform.localScale = Vector3.Lerp(start, target, t / growDuration);
            yield return null;
        }
        transform.localScale = target;
    }

    private IEnumerator SpawnFruitsRandomly()
    {
        if (!IsServer) yield break;

        List<Transform> spawnPoints = new List<Transform>();
        foreach (Transform child in fruitsParent)
        {
            if (child.name.Contains("SpawnPoint"))
                spawnPoints.Add(child);
        }

        if (spawnPoints.Count == 0) yield break;
        ShuffleList(spawnPoints);

        for (int i = 0; i < fruitCount; i++)
        {
            float randomDelay = Random.Range(fruitSpawnMinDelay, fruitSpawnMaxDelay);
            yield return new WaitForSeconds(randomDelay);

            Transform p = spawnPoints[i % spawnPoints.Count];
            GameObject fruit = Instantiate(fruitPrefab, p.position, Quaternion.identity);
            fruit.SetActive(true);

            NetworkObject no = fruit.GetComponent<NetworkObject>();
            no.Spawn();

            fruit.tag = "Fruit";
            spawnedFruits.Add(fruit);

            // 物理設定
            Rigidbody rb = fruit.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.mass = fruitMass;
                rb.drag = fruitDrag;
                rb.angularDrag = fruitAngularDrag;
                rb.useGravity = false; // FruitInfo 會處理重力
                rb.isKinematic = true;
            }

            // 使用選定的顏色
            SetupFruitClientRpc(no.NetworkObjectId, treeFruitColors[selectedColorIndex], fruitFadeInDuration);

            // 設定掉落
            float fallDelay = Random.Range(fruitFallMinDelay, fruitFallMaxDelay);
            StartCoroutine(FruitFallRoutine(fruit, fallDelay));

            PlaySFX(sfxFruitSpawn);
        }
    }

    [ClientRpc]
    private void SetupFruitClientRpc(ulong fruitId, Color fruitColor, float fadeInDuration)
    {
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(fruitId, out NetworkObject no))
            return;

        GameObject fruit = no.gameObject;
        MeshRenderer mr = fruit.GetComponent<MeshRenderer>();

        if (mr != null)
        {
            // 創建新材質並設定顏色
            Material fruitMat = new Material(mr.material);
            fruitMat.color = new Color(fruitColor.r, fruitColor.g, fruitColor.b, 0f);
            mr.material = fruitMat;

            // 淡入效果
            StartCoroutine(FruitFadeIn(fruitMat, fruitColor, fadeInDuration));
        }

        if (shadowPlanePrefab != null)
        {
            FruitShadowProjector indicator =
                fruit.AddComponent<FruitShadowProjector>();

            indicator.Initialize(
                fruitColor
            );
        }
    }

    private IEnumerator FruitFadeIn(Material mat, Color targetColor, float dur)
    {
        if (mat == null) yield break;
        float t = 0;
        Color startColor = new Color(targetColor.r, targetColor.g, targetColor.b, 0f);

        while (t < dur)
        {
            if (mat == null) yield break;
            t += Time.deltaTime;
            mat.color = Color.Lerp(startColor, targetColor, t / dur);
            yield return null;
        }
        if (mat != null) mat.color = targetColor;
    }

    private IEnumerator FruitFallRoutine(GameObject fruit, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (fruit == null) yield break;

        NetworkObject no = fruit.GetComponent<NetworkObject>();
        if (no == null) yield break;

        TriggerFruitDropClientRpc(no.NetworkObjectId);
    }

    [ClientRpc]
    private void TriggerFruitDropClientRpc(ulong fruitId)
    {
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(fruitId, out NetworkObject no))
            return;

        GameObject fruit = no.gameObject;
        Rigidbody rb = fruit.GetComponent<Rigidbody>();

        if (rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = false; // 手動處理重力
            rb.AddForce(Vector3.down * initialDropForce, ForceMode.Impulse);

            if (!fruit.TryGetComponent<FruitGravity>(out _))
            {
                FruitGravity gravityComponent = fruit.AddComponent<FruitGravity>();
                gravityComponent.gravityMultiplier = gravityMultiplier;
            }
        }

        if (!fruit.TryGetComponent(out FruitBounce bounce))
        {
            bounce = fruit.AddComponent<FruitBounce>();
            bounce.firstBounceForce = firstBounceForce;
            bounce.secondBounceForce = secondBounceForce;
            bounce.maxBounces = maxBounces;
        }

        if (IsServer)
        {
            PlaySFX(sfxFruitFall);
            FruitInfo info = fruit.GetComponent<FruitInfo>();
            if (info != null) info.SetAutoDestroy(fruitLifetimeAfterFall);
        }
    }

    public void ForceDropAllFruits()
    {
        if (!IsServer) return;

        foreach (GameObject fruit in spawnedFruits)
        {
            if (fruit != null)
            {
                fruit.transform.SetParent(null);
                Rigidbody rb = fruit.GetComponent<Rigidbody>();

                if (rb != null && rb.isKinematic)
                {
                    rb.isKinematic = false;
                    rb.useGravity = false;
                    rb.AddForce(Vector3.down * initialDropForce, ForceMode.Impulse);

                    if (!fruit.TryGetComponent<FruitGravity>(out _))
                    {
                        var g = fruit.AddComponent<FruitGravity>();
                        g.gravityMultiplier = gravityMultiplier;
                    }

                    FruitInfo info = fruit.GetComponent<FruitInfo>();
                    if (info != null) info.SetAutoDestroy(fruitLifetimeAfterFall);
                }
            }
        }
        spawnedFruits.Clear();
    }

    private void PlaySFX(AudioClip clip)
    {
        if (clip != null && audioSource != null)
            audioSource.PlayOneShot(clip);
    }

    private void ShuffleList<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private void StopAllRunningCoroutines()
    {
        foreach (Coroutine coroutine in runningCoroutines)
        {
            if (coroutine != null) StopCoroutine(coroutine);
        }
        runningCoroutines.Clear();
    }
}
