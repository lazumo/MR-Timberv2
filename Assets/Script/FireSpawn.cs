using UnityEngine;
using Unity.Netcode;
using Meta.XR.MRUtilityKit;
using System.Collections;

public class FireSpawnerIgnitionPointsNetworked : NetworkBehaviour
{
    [Header("Prefab (must have NetworkObject)")]
    public GameObject firePrefab;

    [Header("Global Fire Control")]
    public int targetTotalFires = 180;   // 全場火焰上限
    public float checkInterval = 20f;   // 每 N 秒檢查一次

    [Header("Initial Ignition")]
    public int initialIgnitionCount = 4; // 一開始先點幾個火
    public float startDelay = 1.0f;

    [Header("Surface Settings")]
    public float edgeClearance = 0.1f;
    public float offsetFromSurface = 0.03f;

    [Header("Spawn Weights")]
    [Range(0, 1)] public float weightFloor = 0.6f;
    [Range(0, 1)] public float weightWall = 0.3f;
    [Range(0, 1)] public float weightCeil = 0.1f;

    [Header("Rotation / Prefab Axis")]
    public bool fireForwardIntoSurface = true;

    [Header("Optional: Collision Check")]
    public bool enableSpaceCheck = false;
    public Vector3 safetyCheckSize = new Vector3(0.3f, 0.3f, 0.3f);
    public LayerMask collisionLayerMask;

    private bool _started = false;
    private Coroutine _fireLoop;

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        if (MRUK.Instance && MRUK.Instance.GetCurrentRoom() != null)
            HookStageListener();
        else if (MRUK.Instance)
            MRUK.Instance.RegisterSceneLoadedCallback(HookStageListener);
        else
            StartCoroutine(WaitMrukThenHook());
    }

    private IEnumerator WaitMrukThenHook()
    {
        while (MRUK.Instance == null) yield return null;

        if (MRUK.Instance.GetCurrentRoom() != null)
            HookStageListener();
        else
            MRUK.Instance.RegisterSceneLoadedCallback(HookStageListener);
    }

    private void HookStageListener()
    {
        if (_started) return;

        if (SceneController.Instance == null)
        {
            Debug.LogError("[FireSpawner] SceneController.Instance is null");
            return;
        }

        SceneController.Instance.CurrentLevel.OnValueChanged += OnStageChanged;

        if (SceneController.Instance.GetCurrentStage() == 2)
            StartSpawningRoutine();
    }

    private void OnDestroy()
    {
        if (SceneController.Instance != null)
            SceneController.Instance.CurrentLevel.OnValueChanged -= OnStageChanged;
    }

    private void OnStageChanged(int prev, int cur)
    {
        if (!IsServer) return;
        if (cur == 2 && !_started) StartSpawningRoutine();
        else if (cur != 2 && _started) ExtinguishAllAndStop();
    }

    private void StartSpawningRoutine()
    {
        _started = true;
        _fireLoop = StartCoroutine(FireManagementLoop());
    }

    // Restart / leaving stage 2: stop replenishing and despawn every fire
    // (each fire object owns its own crackle audio, so despawning also stops the sound).
    public void ExtinguishAllAndStop()
    {
        if (!IsServer) return;

        if (_fireLoop != null) { StopCoroutine(_fireLoop); _fireLoop = null; }
        _started = false;

        foreach (var fire in FindObjectsByType<FireGrowServerOnly>(FindObjectsSortMode.None))
        {
            var no = fire.GetComponent<NetworkObject>();
            if (no != null && no.IsSpawned) no.Despawn(true);
        }

        // Catch any remaining fire objects (e.g. house fires) so their audio stops too.
        foreach (var fire in FindObjectsByType<NetworkFireController>(FindObjectsSortMode.None))
        {
            var no = fire.GetComponent<NetworkObject>();
            if (no != null && no.IsSpawned) no.Despawn(true);
        }
    }

    // =================== 你要的新邏輯核心 ===================
    private IEnumerator FireManagementLoop()
    {
        yield return new WaitForSeconds(startDelay);

        // 先生成初始 4 個起火點
        int ignited = 0;
        for (int i = 0; i < initialIgnitionCount; i++)
        {
            if (SpawnOneIgnition()) ignited++;
            yield return new WaitForSeconds(0.15f);
        }
        Debug.Log($"[FireSpawner] Initial ignition: {ignited}/{initialIgnitionCount} (TotalFires={FireGrowServerOnly.TotalFires})");
        if (ignited == 0)
            Debug.LogError("[FireSpawner] No ignition point found space! Check MRUK surfaces / spawn weights.");

        float nextReplenish = Time.time + checkInterval;
        bool everHadFire = FireGrowServerOnly.TotalFires > 0;

        while (IsServer)
        {
            int current = FireGrowServerOnly.TotalFires;   // 從繁殖腳本取得目前火數
            if (current > 0) everHadFire = true;

            // ✅ 火全滅 → 立即恢復場景（只有「曾經有火」才算勝利，避免生成失敗被誤判）
            if (current <= 0 && everHadFire)
            {
                Debug.Log("[FireSpawner] All fires out — restoring passthrough.");
                FadePassthroughBackClientRpc();

                // 過關音效 + 特效（GameFlow 統一播放）
                if (GameFlowController.Instance != null)
                    GameFlowController.Instance.NotifyFiresExtinguished();

                _started = false;   // allow a future stage-2 to start a fresh loop
                _fireLoop = null;
                yield break;
            }

            // 補火維持原本的 checkInterval 節奏
            if (Time.time >= nextReplenish)
            {
                nextReplenish = Time.time + checkInterval;

                if (current < targetTotalFires)
                {
                    bool success = SpawnOneIgnition();
                    if (success)
                        Debug.Log($"[FireSpawner] Replenish fire. Now: {current + 1}/{targetTotalFires}");
                }
            }

            yield return new WaitForSeconds(1f);
        }
    }
    // =======================================================

    private bool SpawnOneIgnition()
    {
        MRUKRoom room = MRUK.Instance.GetCurrentRoom();
        if (room == null) return false;

        int attempts = 0;
        while (attempts < 80)
        {
            attempts++;

            PickSurface(out MRUK.SurfaceType surfaceType, out MRUKAnchor.SceneLabels label);
            LabelFilter filter = new LabelFilter(label);

            if (room.GenerateRandomPositionOnSurface(surfaceType, edgeClearance, filter, out Vector3 pos, out Vector3 normal))
            {
                Vector3 n = normal.normalized;
                Vector3 finalPos = pos + n * offsetFromSurface;

                // 火必須落在 3x3 遊戲範圍內（方形判定：牆面整面可用、不出房間）
                if (SpawnArea.Instance != null && !SpawnArea.Instance.IsInsideBox(finalPos)) continue;

                if (enableSpaceCheck && !IsSpaceEmpty(finalPos)) return false;

                // 照你的要求：火的 Y 軸永遠是世界的 UP
                Quaternion rot = Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.up);

                // 傳遞 pos, rot 以及偵測到的 normal
                PerformSpawn(finalPos, rot, n);
                return true;
            }
        }

        return false;
    }

    private void PickSurface(out MRUK.SurfaceType surfaceType, out MRUKAnchor.SceneLabels label)
    {
        float sum = Mathf.Max(0.0001f, weightFloor + weightWall + weightCeil);
        float r = Random.value * sum;

        if (r < weightFloor)
        {
            surfaceType = MRUK.SurfaceType.FACING_UP;
            label = MRUKAnchor.SceneLabels.FLOOR;
            return;
        }

        r -= weightFloor;
        if (r < weightWall)
        {
            surfaceType = MRUK.SurfaceType.VERTICAL;
            label = MRUKAnchor.SceneLabels.WALL_FACE;
            return;
        }

        surfaceType = MRUK.SurfaceType.FACING_DOWN;
        label = MRUKAnchor.SceneLabels.CEILING;
    }

    private bool IsSpaceEmpty(Vector3 center)
    {
        Collider[] hits = Physics.OverlapBox(center, safetyCheckSize, Quaternion.identity, collisionLayerMask);
        return hits.Length == 0;
    }

    private void PerformSpawn(Vector3 pos, Quaternion rot, Vector3 normal)
    {
        GameObject obj = Instantiate(firePrefab, pos, rot);

        var no = obj.GetComponent<NetworkObject>();
        if (no != null)
        {
            // 在 Spawn 之前設定法線
            if (obj.TryGetComponent<FireGrowServerOnly>(out var growScript))
            {
                growScript.InitializeNormal(normal);
            }

            no.Spawn(true);
        }
        else
        {
            Destroy(obj);
        }
    }

    [ClientRpc]
    void FadePassthroughBackClientRpc()
    {
        if (PassthroughDarkener.Instance != null)
            PassthroughDarkener.Instance.Apply(false);
    }

}
