using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class RaiseFactoryWhenRequirementMet : NetworkBehaviour
{
    [Header("Requirement")]
    [SerializeField] private BarShowWhenEnoughMatchingFruits requirementChecker;

    [Header("Target")]
    [SerializeField] private Transform factoryRoot;

    [Header("Move")]
    [Tooltip("Move up distance in cm.")]
    [SerializeField] private float moveUpCm = 50f;

    [Tooltip("Wait seconds after requirement met.")]
    [SerializeField] private float delaySeconds = 2f;

    [Tooltip("Smooth speed (bigger = faster).")]
    [SerializeField] private float lerpSpeed = 3f;
    [Header("Physics Fix")]
    [Tooltip("向上推的瞬間力道，大約 0.5f ~ 2.0f 視水果重量而定")]
    [SerializeField] private float jumpForce = 1.0f;
    private NetworkVariable<bool> hasStarted =
        new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    private bool moving = false;
    private Vector3 startPos;
    private Vector3 targetPos;
    private float t = 0f;
    private Coroutine co;

    private void Reset()
    {
        factoryRoot = transform.root;
        requirementChecker = GetComponentInParent<BarShowWhenEnoughMatchingFruits>();
    }

    private void Update()
    {
        if (!IsServer) return;

        // 已經開始過（或做完）就不再觸發
        if (hasStarted.Value) return;

        // 還沒達標就不做事
        if (requirementChecker != null && !requirementChecker.IsRequirementMet())
            return;

        // 達標 -> 開始一次流程
        hasStarted.Value = true;
        co = StartCoroutine(BeginAfterDelay());
    }

    private IEnumerator BeginAfterDelay()
    {
        yield return new WaitForSeconds(delaySeconds);

        startPos = factoryRoot.position;
        targetPos = startPos + Vector3.up * (moveUpCm * 0.01f);

        // 準備移動
        moving = true;
        t = 0f;

        // ⭐ 物理防穿模處理
        var nm = NetworkManager.Singleton;

        // 為了安全，還是建立一個清單快照，雖然不改 Parent 不會觸發 Exit，但這是好習慣
        var fruitsToPush = new List<ulong>(requirementChecker.InsideFruitIds);

        foreach (var id in fruitsToPush)
        {
            if (!nm.SpawnManager.SpawnedObjects.TryGetValue(id, out var netObj))
                continue;

            var rb = netObj.GetComponent<Rigidbody>();
            if (rb != null)
            {
                // 1. 喚醒它！避免因為物理休眠導致被地板穿過去
                rb.WakeUp();
                rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
            }
        }
    }

    private void LateUpdate()
    {
        if (!IsServer) return;
        if (!moving) return;

        // 用指數式的 Lerp，體感順順、也不需要算 duration
        t = 1f - Mathf.Exp(-lerpSpeed * Time.deltaTime);

        factoryRoot.position = Vector3.Lerp(factoryRoot.position, targetPos, t);

        // 到位就停止
        if ((factoryRoot.position - targetPos).sqrMagnitude < 1e-6f)
        {
            factoryRoot.position = targetPos;
            moving = false;
        }
    }
}
