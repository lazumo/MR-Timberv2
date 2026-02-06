using System.Collections;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(ObjectNetworkSync))]
[RequireComponent(typeof(HouseFireController))]
public class HouseFireStateController : NetworkBehaviour
{
    [Header("Fire Rule")]
    [SerializeField] private float fireTimeoutSeconds = 30f;

    private ObjectNetworkSync _house;
    private HouseFireController _fire;

    private Coroutine _fireCountdownCoroutine;

    private void Awake()
    {
        _house = GetComponent<ObjectNetworkSync>();
        _fire = GetComponent<HouseFireController>();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        // 監聽房子狀態改變
        _house.OnHouseStateChanged += HandleHouseStateChanged;
    }

    public override void OnNetworkDespawn()
    {
        if (!IsServer) return;

        if (_house != null)
            _house.OnHouseStateChanged -= HandleHouseStateChanged;
    }

    // =============================
    // State Listener
    // =============================

    private void HandleHouseStateChanged(HouseState newState)
    {
        if (!IsServer) return;

        if (newState == HouseState.Firing)
        {
            StartFireCountdown();
        }
        else
        {
            StopFireCountdown();
        }
    }

    // =============================
    // Fire Countdown Logic
    // =============================

    private void StartFireCountdown()
    {
        StopFireCountdown(); // 防止重複啟動
        _fireCountdownCoroutine = StartCoroutine(FireCountdownCoroutine());
    }

    private void StopFireCountdown()
    {
        if (_fireCountdownCoroutine != null)
        {
            StopCoroutine(_fireCountdownCoroutine);
            _fireCountdownCoroutine = null;
        }
    }

    private IEnumerator FireCountdownCoroutine()
    {
        float elapsed = 0f;

        while (elapsed < fireTimeoutSeconds)
        {
            // 🔥 火被滅掉 → Saved
            if (_fire != null && !_fire.IsBurning)
            {
                _house.SetState(HouseState.Saved);
                yield break;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        // ⏰ 時間到但火還在 → Destroyed
        _house.SetState(HouseState.Destroyed);
    }
}
