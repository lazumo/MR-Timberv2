using UnityEngine;
using Unity.Netcode;
using System;

[RequireComponent(typeof(ObjectDisplayController))]
[RequireComponent(typeof(HouseColorFactoryPlacer))]
public class ObjectNetworkSync : NetworkBehaviour
{
    private ObjectDisplayController _logicController;
    private HouseColorFactoryPlacer _factoryPlacer;

    // ===== Network Variables =====

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

    // House ¡÷ ColorFactory reference
    private NetworkVariable<NetworkObjectReference> colorFactoryRef =
        new NetworkVariable<NetworkObjectReference>(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    // ===== Unity =====

    private void Awake()
    {
        _logicController = GetComponent<ObjectDisplayController>();
        _factoryPlacer = GetComponent<HouseColorFactoryPlacer>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            colorIndex.Value = UnityEngine.Random.Range(0, 3);
        }

        currentHouseState.OnValueChanged += OnHouseStateChanged;
        colorIndex.OnValueChanged += (oldVal, newVal) =>
        {
            _logicController.ApplyColor(newVal);
        };

        _logicController.ApplyState(currentHouseState.Value);
        _logicController.ApplyColor(colorIndex.Value);
    }

    // ===== State handling =====
    private void OnHouseStateChanged(HouseState oldVal, HouseState newVal)
    {
        _logicController.ApplyState(newVal);

        if (!IsServer) return;

        switch (newVal)
        {
            case HouseState.Built:
                TrySpawnAndBindFactory();
                break;

            case HouseState.Colored:
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

        // ²MªÅ reference
        colorFactoryRef.Value = default;
    }

    // ===== Public API =====

    public void SetState(HouseState newState)
    {
        if (!IsServer)
        {
            Debug.LogWarning("Only server can change house state.");
            return;
        }

        currentHouseState.Value = newState;
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

    // ===== Debug/Test =====

    void Update()
    {
        if (!IsServer) return;

        if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch))
        {
            CycleStateOnServer();
        }
    }

    private void CycleStateOnServer()
    {
        Array states = Enum.GetValues(typeof(HouseState));
        int nextIndex = ((int)currentHouseState.Value + 1) % states.Length;
        currentHouseState.Value = (HouseState)states.GetValue(nextIndex);

        Debug.Log($"Server changed house state to: {currentHouseState.Value}, color: {colorIndex.Value}");
    }
}
