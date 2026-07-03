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

    private void Update()
    {
        if (!IsServer) return;

        if (OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger))
            AdvancePaintStage();

        if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch))
            DebugCycleState();
    }

    private void DebugCycleState()
    {
        Array states = Enum.GetValues(typeof(HouseState));
        int nextIndex = ((int)currentHouseState.Value + 1) % states.Length;

        HouseState nextState = (HouseState)states.GetValue(nextIndex);

        Debug.Log($"[DEBUG] Force switch state: {currentHouseState.Value} → {nextState}");
        SetState(nextState);
    }
}
