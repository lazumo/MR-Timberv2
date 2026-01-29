using Unity.Netcode;
using UnityEngine;

public class HouseColorFactoryPlacer : NetworkBehaviour
{
    public Transform colorFactory;
    public LayerMask groundLayer;

    [Header("Offsets")]
    public float forwardOffset = 0.3f;

    public override void OnNetworkSpawn()
    {
        Debug.Log($"[HouseColorFactoryPlacer] OnNetworkSpawn IsServer={IsServer} name={name}");

        if (!IsServer) return;
        PlaceColorFactory();
    }

    void PlaceColorFactory()
    {
        Debug.Log("[HouseColorFactoryPlacer] PlaceColorFactory called");

        if (!IsHouseOnWall())
            return;

        // 1️⃣ 往 house 前方推出去一點
        Vector3 rayOrigin =
            transform.position +
            transform.up * forwardOffset;

        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 10f, groundLayer))
        {
            Debug.Log($"Hit {hit.collider.name} at {hit.point}");
            // position
            colorFactory.localPosition = transform.InverseTransformPoint(hit.point);

            Vector3 up = hit.normal;

            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, up).normalized;
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.ProjectOnPlane(transform.right, up).normalized;

            Quaternion worldRot = Quaternion.LookRotation(forward, up);
            colorFactory.localRotation = Quaternion.Inverse(transform.rotation) * worldRot;
        }
        else
        {
            Debug.LogWarning($"[HouseColorFactoryPlacer] Ground not found for {name}");
        }
    }

    bool IsHouseOnWall()
    {
        Debug.Log($"[HouseColorFactoryPlacer] dot={Vector3.Dot(transform.up, Vector3.up)}");

        float dot = Vector3.Dot(transform.up, Vector3.up);
        return dot < 0.7f;
    }
}
