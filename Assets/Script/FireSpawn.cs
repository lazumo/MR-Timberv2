using UnityEngine;
using Unity.Netcode;
using Meta.XR.MRUtilityKit;
using System.Collections;

public class FireSpawnerEverywhereNetworked : NetworkBehaviour
{
    [Header("Prefab (must have NetworkObject)")]
    public GameObject firePrefab;

    [Header("Spawn Settings")]
    public int targetFireCount = 30;
    public float startDelay = 1.0f;
    public float spawnInterval = 0.2f;

    [Header("Surface Settings")]
    public float edgeClearance = 0.1f;
    public float offsetFromSurface = 0.03f;

    [Header("Spawn Weights")]
    [Range(0, 1)] public float weightFloor = 0.6f;
    [Range(0, 1)] public float weightWall = 0.3f;
    [Range(0, 1)] public float weightCeil = 0.1f;

    [Header("Optional: Collision Check")]
    public bool enableSpaceCheck = false;
    public Vector3 safetyCheckSize = new Vector3(0.3f, 0.3f, 0.3f);
    public LayerMask collisionLayerMask;

    private int _currentFireCount = 0;
    private bool _started = false;

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        if (MRUK.Instance && MRUK.Instance.GetCurrentRoom() != null) HookStageListener();
        else if (MRUK.Instance) MRUK.Instance.RegisterSceneLoadedCallback(HookStageListener);
        else StartCoroutine(WaitMrukThenHook());
    }

    private IEnumerator WaitMrukThenHook()
    {
        while (MRUK.Instance == null) yield return null;
        if (MRUK.Instance.GetCurrentRoom() != null) HookStageListener();
        else MRUK.Instance.RegisterSceneLoadedCallback(HookStageListener);
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
        StartCoroutine(ManageFireLifecycle());
    }

    private IEnumerator ManageFireLifecycle()
    {
        yield return new WaitForSeconds(startDelay);

        while (IsServer)
        {
            if (_currentFireCount < targetFireCount)
            {
                bool success = SpawnOneFireEverywhere();
                yield return new WaitForSeconds(success ? spawnInterval : 0.4f);
            }
            else yield return new WaitForSeconds(1.0f);
        }
    }

    private bool SpawnOneFireEverywhere()
    {
        MRUKRoom room = MRUK.Instance.GetCurrentRoom();
        if (room == null) return false;

        int attempts = 0;
        while (attempts < 60)
        {
            attempts++;

            // 1) 隨機選要生成在哪種表面
            PickSurface(out MRUK.SurfaceType surfaceType, out MRUKAnchor.SceneLabels label);

            // 2) 用 label filter 限制到該類表面
            LabelFilter filter = new LabelFilter(label);

            // 3) MRUK 在表面給你 pos + normal
            if (room.GenerateRandomPositionOnSurface(surfaceType, edgeClearance, filter,
                    out Vector3 pos, out Vector3 normal))
            {
                Vector3 n = normal.normalized;

                // 4) 讓 Y 軸對齊表面法線
                Quaternion rot = Quaternion.FromToRotation(Vector3.up, n);

                // 5) 繞著法線再隨機轉一圈（看起來更自然）
                rot *= Quaternion.AngleAxis(Random.Range(0f, 360f), n);

                // 6) 往法線外推避免穿模
                Vector3 finalPos = pos + n * offsetFromSurface;

                if (enableSpaceCheck && !IsSpaceEmpty(finalPos))
                    continue;

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
            // 你專案的 label 名稱可能是 WALL_FACE / WALL / WALL_ART
            // 先用最常見的 WALL_FACE；如果編譯報錯，把它改成你 MRUK 有的那個
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
        obj.GetComponent<NetworkObject>().Spawn();
        _currentFireCount++;
    }
}
