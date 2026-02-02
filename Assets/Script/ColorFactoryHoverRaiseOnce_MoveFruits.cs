using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

public class ColorFactoryRaiseToMiddlePointOnce_Smooth_PhysicsSafe : NetworkBehaviour
{
    [Header("Requirement")]
    [SerializeField] private BarShowWhenEnoughMatchingFruits requirementChecker;

    [Header("Refs")]
    [SerializeField] private Transform factoryRoot;
    [SerializeField] private FruitSqueezeInContainer_Tag fruitDetect;

    [Header("MiddlePoint Tag")]
    [SerializeField] private string middlePointTag = "MiddlePoint";

    [Header("Smooth Move")]
    [SerializeField] private float moveLerpSpeed = 5f;

    private NetworkVariable<bool> hasRaised =
        new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    private float targetY;
    private float deltaY;
    private bool moving = false;

    // 記錄這次要一起抬的水果
    private List<Rigidbody> movingFruits = new List<Rigidbody>();

    private void Reset()
    {
        factoryRoot = transform.root;
        fruitDetect = GetComponentInParent<FruitSqueezeInContainer_Tag>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;
        if (hasRaised.Value) return;
        if (!other.CompareTag(middlePointTag)) return;
        // ⭐ 新增：必須先達到水果 requirement 才能抬升
        if (requirementChecker != null && !requirementChecker.IsRequirementMet())
        {
            Debug.Log("[RaisePhysicsSafe] Requirement not met, skip raising.");
            return;
        }

        hasRaised.Value = true;

        targetY = other.transform.position.y;
        deltaY = targetY - factoryRoot.position.y;

        PrepareFruitsForMove();

        moving = true;

        Debug.Log($"[RaisePhysicsSafe] Start moving to Y={targetY:F3}");
    }

    private void PrepareFruitsForMove()
    {
        movingFruits.Clear();

        if (fruitDetect == null) return;

        var fruits = fruitDetect.GetFruitsSnapshot();

        foreach (var f in fruits)
        {
            var rb = f.GetComponent<Rigidbody>();
            if (rb != null)
            {
                // 抬升期間暫時停止物理
                rb.isKinematic = true;
                movingFruits.Add(rb);
            }
        }
    }

    private void RestoreFruitPhysics()
    {
        foreach (var rb in movingFruits)
        {
            if (rb != null)
            {
                rb.isKinematic = false;
            }
        }

        movingFruits.Clear();
    }

    private void Update()
    {
        if (!IsServer) return;
        if (!moving) return;

        // ---- 1) 平滑移動 ColorFactory ----
        Vector3 p = factoryRoot.position;
        float newY = Mathf.Lerp(p.y, targetY, Time.deltaTime * moveLerpSpeed);

        p.y = newY;
        factoryRoot.position = p;

        // ---- 2) 平滑移動水果 ----
        foreach (var rb in movingFruits)
        {
            if (rb == null) continue;

            Vector3 fp = rb.position;
            float fruitTargetY = fp.y + deltaY;

            fp.y = Mathf.Lerp(fp.y, fruitTargetY, Time.deltaTime * moveLerpSpeed);

            rb.MovePosition(fp);
        }

        // ---- 3) 接近目標就結束 ----
        if (Mathf.Abs(factoryRoot.position.y - targetY) < 0.001f)
        {
            Vector3 final = factoryRoot.position;
            final.y = targetY;
            factoryRoot.position = final;

            // 抬升完成，恢復物理
            RestoreFruitPhysics();

            moving = false;

            Debug.Log("[RaisePhysicsSafe] Move finished & physics restored");
        }
    }
}
