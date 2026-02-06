using UnityEngine;
using Unity.Netcode;

public class HouseFireController : NetworkBehaviour
{
    [SerializeField] private NetworkObject firePrefab;
    [SerializeField] private Transform fireSocket;

    private NetworkObject _currentFire;

    public bool IsBurning { get; private set; }

    public void OnHouseStateChanged(HouseState state)
    {
        if (!IsServer) return;
        if (state == HouseState.Destroyed)
        {
            ClearFire(); // 保險：確保火被清掉
            return;
        }
        if (state == HouseState.Firing)
        {
            SpawnFireIfNeeded();
        }
        else
        {
            // 一旦離開 Firing，就確保火被清掉
            ClearFire();
        }
    }

    private void SpawnFireIfNeeded()
    {
        if (_currentFire != null) return;
        if (firePrefab == null || fireSocket == null) return;

        var fire = Instantiate(
            firePrefab,
            fireSocket.position,
            fireSocket.rotation,
            fireSocket
        );

        fire.Spawn();
        _currentFire = fire;
        var fireCtrl = fire.GetComponent<NetworkFireController>();
        if (fireCtrl != null)
        {
            fireCtrl.BindHouse(this);
        }
        IsBurning = true;
    }

    public void ClearFire()
    {
        if (!IsServer) return;
        DespawnFireIfExists();
    }

    private void DespawnFireIfExists()
    {
        if (_currentFire == null) return;

        _currentFire.Despawn(true);
        _currentFire = null;

        IsBurning = false;
    }
    public void Ignite()
    {
        if (!IsServer) return;

        var house = GetComponent<ObjectNetworkSync>();
        if (house == null) return;

        // 已經在 Firing，就不用再請求
        if (house.CurrentState == HouseState.Firing || house.CurrentState == HouseState.Destroyed)
            return;

        house.SetState(HouseState.Firing);
    }
}
