using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 按下 trigger（= isSpraying 為 true）時，把手零件移動/旋轉到「按下姿勢」；
/// 放開就回到原本姿勢。純視覺、boolean 驅動。
///
/// 用法：掛在滅火器 prefab 根物件（跟 NetworkExtinguisherController 同一層），
/// 指定 sprayController，然後在 parts 裡為每個要動的零件填：
///   target = 零件 Transform
///   pressedLocalPosition / pressedLocalEuler = 按下時的 local 姿勢
///   （在編輯器裡先把零件擺到按下的樣子，把數值抄進欄位，再還原零件）
/// 未按下的姿勢會在啟動時自動記錄，不用填。
/// </summary>
public class ExtinguisherHandlePress : NetworkBehaviour
{
    [System.Serializable]
    public class PressPart
    {
        [Tooltip("要動的零件（例如把手）")]
        public Transform target;
        [Tooltip("按下時的 localPosition")]
        public Vector3 pressedLocalPosition;
        [Tooltip("按下時的 localEulerAngles")]
        public Vector3 pressedLocalEuler;

        [HideInInspector] public Vector3 restPos;
        [HideInInspector] public Quaternion restRot;
    }

    [Header("Parts（每個零件：按下時要到的 local 姿勢）")]
    [SerializeField] private PressPart[] parts;

    [Header("Drive")]
    [Tooltip("布林來源：isSpraying（按 trigger 噴水時 = 按下）。跟噴水同步、雙方都看得到。")]
    [SerializeField] private NetworkExtinguisherController sprayController;
    [Tooltip("姿勢切換的跟隨速度")]
    [SerializeField] private float followSpeed = 18f;

    private float _press;   // 0 = 放開, 1 = 按下（平滑過渡）

    private void Awake()
    {
        // 記錄「沒按下」的原始姿勢
        if (parts == null) return;
        foreach (var p in parts)
        {
            if (p.target == null) continue;
            p.restPos = p.target.localPosition;
            p.restRot = p.target.localRotation;

            // pressedLocalPosition 留 (0,0,0) = 位置不動、只旋轉
            if (p.pressedLocalPosition == Vector3.zero)
                p.pressedLocalPosition = p.restPos;
        }
    }

    private void Update()
    {
        bool pressed = sprayController != null && sprayController.isSpraying.Value;

        _press = Mathf.MoveTowards(_press, pressed ? 1f : 0f, Time.deltaTime * followSpeed);

        if (parts == null) return;
        foreach (var p in parts)
        {
            if (p.target == null) continue;
            p.target.localPosition = Vector3.Lerp(p.restPos, p.pressedLocalPosition, _press);
            p.target.localRotation = Quaternion.Slerp(p.restRot, Quaternion.Euler(p.pressedLocalEuler), _press);
        }
    }
}
