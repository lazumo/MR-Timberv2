using System.Collections;
using UnityEngine;
using Unity.Netcode;

public class FruitTree : NetworkBehaviour
{
    [Range(0, 2)]
    public int selectedColorIndex = 0;

    [Header("Growth")]
    public float growDuration = 2f;

    [Header("Tree Lifetime (after grown)")]
    public float aliveDuration = 30f;

    [Tooltip("若為 true，樹永不消失，並持續循環生成果實。")]
    public bool keepAliveForever = true;

    [Header("Fruit Spawner")]
    public FruitSpawnController fruitSpawnController;

    [Header("Sound FX")]
    public AudioSource audioSource;
    public AudioClip sfxGrow;

    [Header("Network")]
    public NetworkVariable<Vector3> networkTargetScale =
        new NetworkVariable<Vector3>(Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private Coroutine growCoroutine;

    private void Awake()
    {
        transform.localScale = Vector3.zero;
        if (!audioSource)
            audioSource = gameObject.AddComponent<AudioSource>();
    }

    public override void OnNetworkSpawn()
    {
        networkTargetScale.OnValueChanged += OnScaleChanged;

        if (networkTargetScale.Value != Vector3.zero)
            StartGrow(networkTargetScale.Value);

        if (IsServer)
        {
            StartCoroutine(LifeRoutine());
            if (SceneController.Instance != null)
                SceneController.Instance.CurrentLevel.OnValueChanged += OnStageChanged;
        }
    }

    public override void OnNetworkDespawn()
    {
        networkTargetScale.OnValueChanged -= OnScaleChanged;
        if (growCoroutine != null)
            StopCoroutine(growCoroutine);

        if (IsServer && SceneController.Instance != null)
            SceneController.Instance.CurrentLevel.OnValueChanged -= OnStageChanged;
    }

    // 進入滅火 stage（stage > 1）時，停止生果實並把現有的果子消掉
    private void OnStageChanged(int oldStage, int newStage)
    {
        if (newStage > 1 && fruitSpawnController != null)
            fruitSpawnController.StopAndDespawnFruits();
    }

    private void OnScaleChanged(Vector3 oldVal, Vector3 newVal)
    {
        if (newVal.magnitude < 0.01f) return;
        StartGrow(newVal);
    }

    private void StartGrow(Vector3 targetScale)
    {
        if (growCoroutine != null)
            StopCoroutine(growCoroutine);

        growCoroutine = StartCoroutine(GrowTreeRoutine(targetScale));
    }

    private IEnumerator GrowTreeRoutine(Vector3 target)
    {
        PlayGrowSFX();

        float t = 0f;
        Vector3 start = transform.localScale;

        while (t < growDuration)
        {
            t += Time.deltaTime;
            transform.localScale = Vector3.Lerp(start, target, t / growDuration);
            yield return null;
        }

        transform.localScale = target;
    }

    private IEnumerator LifeRoutine()
    {
        yield return null;

        if (networkTargetScale.Value == Vector3.zero)
            networkTargetScale.Value = Vector3.one;

        // 等樹長好
        yield return new WaitForSeconds(growDuration);

        // ✅ 啟動果實生成（若已切到 stage 2 則不啟動）
        bool inFireStage = SceneController.Instance != null && SceneController.Instance.GetCurrentStage() > 1;
        if (fruitSpawnController != null && !inFireStage)
            fruitSpawnController.StartFruitSpawn();

        if (keepAliveForever)
            yield break;

        // 成熟期
        yield return new WaitForSeconds(aliveDuration);

        if (fruitSpawnController != null)
            fruitSpawnController.ForceDropAllFruits();

        if (IsServer && TreeSpawnerNetworked.Instance != null && SceneController.Instance.GetCurrentStage() == 1)
        {
            TreeSpawnerNetworked.Instance.NotifyTreeDestroyed(TreeSpawnerNetworked.TreeType.Fruit);
            StartCoroutine(DelayedSpawnTree(8f));
        }
        // 小延遲，確保掉落狀態同步（可選但推薦）
        yield return new WaitForSeconds(0.1f);
        if (IsSpawned)
            NetworkObject.Despawn();
    }

    private void PlayGrowSFX()
    {
        // prefab 有指定專屬音效才用它；否則一律走 SfxLib（保證出聲）
        if (audioSource && sfxGrow)
            audioSource.PlayOneShot(sfxGrow);
        else
            SfxLib.PlayAt("GrowUp", transform.position, 1f);
    }
    private IEnumerator DelayedSpawnTree(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (TreeSpawnerNetworked.Instance != null)
        {
            TreeSpawnerNetworked.Instance.SpawnTree(TreeSpawnerNetworked.TreeType.Fruit);
            Debug.Log("[FruitTree] Delayed Fruit Tree generation.");
        }
    }

}