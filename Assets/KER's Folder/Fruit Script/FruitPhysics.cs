using UnityEngine;
using System;

[RequireComponent(typeof(Rigidbody))]
public class FruitPhysics : MonoBehaviour
{
    public float gravityMultiplier = 2f;

    public event Action OnDropStarted;

    private Rigidbody rb;
    private bool hasDropped = false;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
    }

    public void EnablePhysics()
    {
        if (hasDropped) return;
        hasDropped = true;

        rb.isKinematic = false;
        rb.useGravity = true;

        OnDropStarted?.Invoke();   // ★ 通知其他系統
    }

    void FixedUpdate()
    {
        if (rb.isKinematic) return;
        rb.AddForce(Physics.gravity * gravityMultiplier, ForceMode.Acceleration);
    }
}

