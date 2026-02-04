using UnityEngine;
using Unity.Netcode;
using System;

[RequireComponent(typeof(ObjectDisplayController))]
[RequireComponent(typeof(HouseColorFactoryPlacer))]
public class ObjectNetworkSync : NetworkBehaviour
{
    private ObjectDisplayController _logicController;
    private HouseColorFactoryPlacer _factoryPlacer;
    private HouseFireController _fireController;

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
                TrySpawnAndBindFactory();
                break;

            case HouseState.Coloring:
                if (paintStage.Value == PaintStage.None)
                    paintStage.Value = PaintStage.One;
                break;

            case HouseState.Colored:
                paintStage.Value = PaintStage.Full;
                DespawnFactoryIfExists();
                break;
        }
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

    private void DespawnFactoryIfExists()
    {
        if (!IsServer) return;

        if (colorFactoryRef.Value.TryGet(out NetworkObject factory))
        {
            factory.Despawn(true);
            Debug.Log($"[House] Despawned ColorFactory id={factory.NetworkObjectId}");
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
