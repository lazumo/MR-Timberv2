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
    }

    private void StartSpawningRoutine()
    {
        _started = true;
        StartCoroutine(FireManagementLoop());
    }

    // =================== 你要的新邏輯核心 ===================
    private IEnumerator FireManagementLoop()
    {
        yield return new WaitForSeconds(startDelay);

        // 先生成初始 4 個起火點
        for (int i = 0; i < initialIgnitionCount; i++)
        {
            SpawnOneIgnition();
            yield return new WaitForSeconds(0.15f);
        }

        while (IsServer)
        {
            int current = FireGrowServerOnly.TotalFires;   // 從繁殖腳本取得目前火數

            // 如果全場已經沒有火了 → 停止
            if (current <= 0)
            {
                Debug.Log("[FireSpawner] No more fires in scene. Fade passthrough back.");

                FadePassthroughBackClientRpc();

                yield break;
            }



            // 還有額度就補一個
            if (current < targetTotalFires)
            {
                bool success = SpawnOneIgnition();
                if (success)
                {
                    Debug.Log($"[FireSpawner] Replenish fire. Now: {current + 1}/{targetTotalFires}");
                }
            }

            yield return new WaitForSeconds(checkInterval);
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

            if (room.GenerateRandomPositionOnSurface(surfaceType, edgeClearance, filter,
                    out Vector3 pos, out Vector3 normal))
            {
                Vector3 n = normal.normalized;
                Vector3 finalPos = pos + n * offsetFromSurface;

                if (enableSpaceCheck && !IsSpaceEmpty(finalPos))
                    continue;

                Quaternion rot;
                if (fireForwardIntoSurface)
                {
                    rot = Quaternion.LookRotation(-n, Vector3.up);
                    rot *= Quaternion.AngleAxis(Random.Range(0f, 360f), n);
                }
                else
                {
                    rot = Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.up);
                }

                PerformSpawn(finalPos, rot);
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

    private void PerformSpawn(Vector3 pos, Quaternion rot)
    {
        GameObject obj = Instantiate(firePrefab, pos, rot);

        var no = obj.GetComponent<NetworkObject>();
        if (!no)
        {
            Debug.LogError("[FireSpawner] firePrefab missing NetworkObject!");
            Destroy(obj);
            return;
        }

        no.Spawn(true);
    }

    [ClientRpc]
    void FadePassthroughBackClientRpc()
    {
        if (PassthroughDarkener.Instance != null)
            PassthroughDarkener.Instance.Apply(false);
    }

}
