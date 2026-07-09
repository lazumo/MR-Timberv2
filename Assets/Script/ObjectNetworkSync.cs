using UnityEngine;
using Unity.Netcode;
using System;
using System.Collections;
[RequireComponent(typeof(ObjectDisplayController))]
[RequireComponent(typeof(HouseColorFactoryPlacer))]
public class ObjectNetworkSync : NetworkBehaviour
{
    private ObjectDisplayController _logicController;
    private HouseColorFactoryPlacer _factoryPlacer;
    private HouseFireController _fireController;
    private Coroutine _despawnCoroutine;

    [Header("Build VFX (played when the house is built — assign the tree-vanish VFX prefab)")]
    [SerializeField] private GameObject buildVfxPrefab;

    [Header("Burn-away（失火過場：房子焦黑 + 灰燼飄散後消失）")]
    [Tooltip("灰燼粒子 prefab（可用 smoke.prefab 的 Ashes 子物件）")]
    [SerializeField] private GameObject ashesVfxPrefab;
    [Tooltip("Optional spawn point; defaults to this house's transform.")]
    [SerializeField] private Transform buildVfxAnchor;
    [SerializeField] private float buildVfxLifetime = 3f;

    // =============================
    // Network Variables
    // =============================

    private NetworkVariable<HouseState> currentHouseState =
        new NetworkVariable<HouseState>(
            HouseState.Unbuilt,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    private NetworkVariable<int> colorIndex =
        new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    private NetworkVariable<PaintStage> paintStage =
        new NetworkVariable<PaintStage>(
            PaintStage.None,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    private NetworkVariable<NetworkObjectReference> colorFactoryRef =
        new NetworkVariable<NetworkObjectReference>(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    // =============================
    // ✅ Public read access + event
    // =============================

    public HouseState CurrentState => currentHouseState.Value;
    public PaintStage CurrentPaintStage => paintStage.Value;

    public event Action<HouseState> OnHouseStateChanged;

    // =============================
    // Unity
    // =============================

    private void Awake()
    {
        _logicController = GetComponent<ObjectDisplayController>();
        _factoryPlacer = GetComponent<HouseColorFactoryPlacer>();
        _fireController = GetComponent<HouseFireController>();
    }

    public override void OnNetworkSpawn()
    {
        currentHouseState.OnValueChanged += (_, _) => RefreshVisual();
        colorIndex.OnValueChanged += (_, _) => RefreshVisual();
        paintStage.OnValueChanged += (_, _) => RefreshVisual();

        RefreshVisual();
    }

    private void RefreshVisual()
    {
        _logicController.ApplyVisual(
            currentHouseState.Value,
            colorIndex.Value,
            paintStage.Value
        );
    }

    private void OnHouseStateEntered(HouseState newState)
    {
        _fireController?.OnHouseStateChanged(newState);

        switch (newState)
        {
            case HouseState.Built:
                PlayBuildVfxClientRpc();
                TrySpawnAndBindFactory();
                // House is now up → let the director advance Logging → Catching.
                if (GameFlowController.Instance != null)
                    GameFlowController.Instance.NotifyHouseBuilt();
                break;

            case HouseState.Coloring:
                if (paintStage.Value == PaintStage.None)
                    paintStage.Value = PaintStage.One;
                break;

            case HouseState.Colored:
                paintStage.Value = PaintStage.Full;
                DespawnFactoryIfExists();
                // Goal reached → let the director run the fire bridge into Firefighting.
                if (GameFlowController.Instance != null)
                    GameFlowController.Instance.NotifyHouseColored(transform);
                break;
        }
    }

    // ============================================================
    // 失火過場：房子「燒成灰燼」消失（取代 scale shrink）
    // Server 呼叫 BurnAway(duration)；視覺在每個 client 本地跑。
    // ============================================================

    /// Server-only：開始燒毀演出，durationseconds 後由呼叫端負責 despawn。
    public void BurnAway(float duration)
    {
        if (!IsServer) return;
        BurnAwayClientRpc(duration);
    }

    [ClientRpc]
    private void BurnAwayClientRpc(float duration)
    {
        StartCoroutine(BurnAwayLocal(duration));
    }

    private IEnumerator BurnAwayLocal(float duration)
    {
        // 灰燼粒子：房子中心往上飄
        if (ashesVfxPrefab != null)
        {
            var ashes = Instantiate(ashesVfxPrefab, transform.position, Quaternion.identity);
            Destroy(ashes, duration + 3f);
        }

        // 只取「有顯示中」的 renderer（house 有多個 state 物件，關著的不用管）
        var all = GetComponentsInChildren<Renderer>();
        var renderers = new System.Collections.Generic.List<Renderer>();
        foreach (var r in all)
            if (r != null && r.enabled && r.gameObject.activeInHierarchy) renderers.Add(r);

        // 打散順序 → 碎片會隨機一塊塊飄走
        for (int i = renderers.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (renderers[i], renderers[j]) = (renderers[j], renderers[i]);
        }

        var block = new MaterialPropertyBlock();
        Color charcoal = new Color(0.06f, 0.05f, 0.04f, 1f);

        // 每個碎片的「飄走」排程：焦黑期(前40%)之後開始，錯開起飛
        float scatterStart = duration * 0.4f;
        float driftTime = Mathf.Min(0.9f, duration * 0.25f);          // 每片飄升時間
        float lastLaunch = duration - driftTime;                       // 最後一片起飛時間
        var basePos = new Vector3[renderers.Count];
        var launchAt = new float[renderers.Count];
        for (int i = 0; i < renderers.Count; i++)
        {
            basePos[i] = renderers[i].transform.localPosition;
            launchAt[i] = renderers.Count > 1
                ? Mathf.Lerp(scatterStart, lastLaunch, (float)i / (renderers.Count - 1))
                : scatterStart;
        }

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;

            // 焦黑漸變（前 40% 完成）
            Color c = Color.Lerp(Color.white, charcoal, Mathf.Clamp01(t / (duration * 0.4f)));

            for (int i = 0; i < renderers.Count; i++)
            {
                var r = renderers[i];
                if (r == null || !r.enabled) continue;

                r.GetPropertyBlock(block);
                block.SetColor("_BaseColor", c);
                block.SetColor("_Color", c);      // 涵蓋 URP 與 toon shader 兩種命名
                r.SetPropertyBlock(block);

                // 到了自己的起飛時間 → 向上飄 + 搖曳，飄完消失
                float k = (t - launchAt[i]) / driftTime;
                if (k > 0f)
                {
                    if (k >= 1f)
                    {
                        r.enabled = false;
                        continue;
                    }
                    // 上飄 0.5m、輕微左右搖、越飄越快（像灰燼被熱氣帶走）
                    float rise = k * k * 0.5f;
                    float sway = Mathf.Sin((t + i) * 6f) * 0.03f * k;
                    r.transform.localPosition = basePos[i] + new Vector3(sway, rise, sway * 0.7f);
                }
            }
            yield return null;
        }

        foreach (var r in renderers)
            if (r != null) r.enabled = false;
    }

    // Same effect as the tree-vanish VFX, played at the house when the elf finishes building it.
    [ClientRpc]
    private void PlayBuildVfxClientRpc()
    {
        Vector3 sfxPos = buildVfxAnchor != null ? buildVfxAnchor.position : transform.position;
        SfxLib.PlayAt("GrowUp", sfxPos);   // 馬力歐變大風的生長音

        if (buildVfxPrefab == null) return;

        Vector3 pos = sfxPos;
        GameObject vfx = Instantiate(buildVfxPrefab, pos, Quaternion.identity);

        var ps = vfx.GetComponent<ParticleSystem>();
        if (ps != null)
            Destroy(vfx, ps.main.duration + ps.main.startLifetime.constantMax);
        else
            Destroy(vfx, buildVfxLifetime);
    }

    private void TrySpawnAndBindFactory()
    {
        if (_factoryPlacer == null) return;
        if (colorFactoryRef.Value.TryGet(out _)) return;

        NetworkObject factory = _factoryPlacer.SpawnColorFactory(colorIndex.Value);
        if (factory != null)
        {
            colorFactoryRef.Value = factory;
            Debug.Log($"[House] Bound ColorFactory id={factory.NetworkObjectId}");
        }
    }
    private IEnumerator DespawnFactoryWithScale(
        NetworkObject factory,
        float duration = 2.0f
    )
    {
        Transform t = factory.transform;

        Vector3 startScale = t.localScale;
        float time = 0f;

        while (time < duration)
        {
            time += Time.deltaTime;
            float alpha = time / duration;
            t.localScale = Vector3.Lerp(startScale, Vector3.zero, alpha);
            yield return null;
        }

        t.localScale = Vector3.zero;

        // ✅ 最後由 Server despawn
        factory.Despawn(true);
        Debug.Log($"[House] Despawned ColorFactory id={factory.NetworkObjectId}");
    }
    private void DespawnFactoryIfExists()
    {
        if (!IsServer) return;

        if (colorFactoryRef.Value.TryGet(out NetworkObject factory))
        {
            // 防止重複啟動 coroutine
            if (_despawnCoroutine != null)
                StopCoroutine(_despawnCoroutine);

            _despawnCoroutine = StartCoroutine(
                DespawnFactoryWithScale(factory)
            );
        }

        colorFactoryRef.Value = default;
    }

    // =============================
    // ✅ Public API (Server only)
    // =============================

    public void SetState(HouseState newState)
    {
        if (!IsServer)
        {
            Debug.LogWarning("Only server can change house state.");
            return;
        }

        if (currentHouseState.Value == newState)
            return;

        currentHouseState.Value = newState;

        // ✅ 關鍵修正：先通知外部（此時 factory 還活著）
        OnHouseStateChanged?.Invoke(newState);

        // ✅ 再做進入狀態行為（這裡可能會 despawn factory）
        OnHouseStateEntered(newState);
    }

    public void InitializeColorIndex(int index)
    {
        if (!IsServer)
        {
            Debug.LogWarning("Only server can initialize colorIndex.");
            return;
        }

        colorIndex.Value = index;
    }

    public void AdvancePaintStage()
    {
        if (!IsServer) return;

        if (currentHouseState.Value != HouseState.Coloring)
        {
            SetState(HouseState.Coloring);
            return;
        }

        if (paintStage.Value < PaintStage.Full)
            paintStage.Value++;

        if (paintStage.Value == PaintStage.Full)
            SetState(HouseState.Colored);
    }

    // ============================================================
    // DEBUG 後門（demo 前停用）：
    //   grip = 強制上色一階、左手食指扳機 = 強制切換房子狀態
    //   需要時把下面整段取消註解即可。
    // ============================================================
    //private void Update()
    //{
    //    if (!IsServer) return;
    //
    //    if (OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger))
    //        AdvancePaintStage();
    //
    //    if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch))
    //        DebugCycleState();
    //}
    //
    //private void DebugCycleState()
    //{
    //    Array states = Enum.GetValues(typeof(HouseState));
    //    int nextIndex = ((int)currentHouseState.Value + 1) % states.Length;
    //
    //    HouseState nextState = (HouseState)states.GetValue(nextIndex);
    //
    //    Debug.Log($"[DEBUG] Force switch state: {currentHouseState.Value} → {nextState}");
    //    SetState(nextState);
    //}
}
