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

    // House → ColorFactory reference
    private NetworkVariable<NetworkObjectReference> colorFactoryRef =
        new NetworkVariable<NetworkObjectReference>(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

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
        // 🔑 所有顯示都只走這裡
        currentHouseState.OnValueChanged += (_, _) => RefreshVisual();
        colorIndex.OnValueChanged += (_, _) => RefreshVisual();
        paintStage.OnValueChanged += (_, _) => RefreshVisual();

        // 初始化顯示
        RefreshVisual();
    }

    // =============================
    // Visual
    // =============================

    private void RefreshVisual()
    {
        _logicController.ApplyVisual(
            currentHouseState.Value,
            colorIndex.Value,
            paintStage.Value
        );
    }

    // =============================
    // State handling (Server only)
    // =============================

    private void OnHouseStateEntered(HouseState newState)
    {
        _fireController?.OnHouseStateChanged(newState);
        switch (newState)
        {
            case HouseState.Built:
                TrySpawnAndBindFactory();
                break;

            case HouseState.Coloring:
                // 進入上色流程時，至少是 1/3
                if (paintStage.Value == PaintStage.None)
                    paintStage.Value = PaintStage.One;
                break;

            case HouseState.Colored:
                // ⭐ 保證 Colored 一定是 3/3
                paintStage.Value = PaintStage.Full;
                DespawnFactoryIfExists();
                break;
        }
    }

    // =============================
    // Factory
    // =============================

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
    // Public API (Server only)
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
        {
            paintStage.Value++;
        }

        // 自動完成
        if (paintStage.Value == PaintStage.Full)
        {
            SetState(HouseState.Colored);
        }
    }

    // =============================
    // Debug / Test
    // =============================

    private void Update()
    {
        if (!IsServer) return;

        // 右手扳機：上色進度 +1
        if (OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger))
        {
            AdvancePaintStage();
        }

        // 左手扳機：強制切換 HouseState（debug only）
        if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch))
        {
            DebugCycleState();
        }
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
