using System.Collections;
using UnityEngine;
using Unity.Netcode;

public class FruitTree : NetworkBehaviour
{
    [Header("Fruit Color Settings")]
    public Color[] treeFruitColors = new Color[]
    {
        new Color(1f, 0.2f, 0.2f),
        new Color(0.2f, 1f, 0.2f),
        new Color(0.2f, 0.5f, 1f)
    };

    [Range(0, 2)]
    public int selectedColorIndex = 0;

    [Header("Growth")]
    public float growDuration = 2f;

    [Header("Tree Lifetime (after grown)")]
    public float aliveDuration = 30f;

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
            StartCoroutine(LifeRoutine());
    }

    public override void OnNetworkDespawn()
    {
        networkTargetScale.OnValueChanged -= OnScaleChanged;
        if (growCoroutine != null)
            StopCoroutine(growCoroutine);
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

        // ✅ 啟動果實生成
        if (fruitSpawnController != null)
            fruitSpawnController.StartFruitSpawn();

        // 成熟期
        yield return new WaitForSeconds(aliveDuration);

        if (IsSpawned)
            NetworkObject.Despawn();
    }

    public Color GetSelectedFruitColor()
    {
        if (treeFruitColors == null || treeFruitColors.Length == 0)
            return Color.white;

        return treeFruitColors[Mathf.Clamp(selectedColorIndex, 0, treeFruitColors.Length - 1)];
    }

    private void PlayGrowSFX()
    {
        if (audioSource && sfxGrow)
            audioSource.PlayOneShot(sfxGrow);
    }
}
