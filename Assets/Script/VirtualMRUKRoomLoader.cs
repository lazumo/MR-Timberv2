using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Meta.XR.MRUtilityKit;
using UnityEngine;

[DefaultExecutionOrder(-50)]
public class VirtualMRUKRoomLoader : MonoBehaviour
{
    public enum AlignmentMode
    {
        UseWallReference,
        UseCameraForward,
        UseMRUKWall,
        WorldAxes,        // 房間軸向對齊世界座標 X/Z 軸（yaw=0）——但 recenter 會讓世界軸偏離 anchor
        ColocationAnchor  // 房間軸向直接對齊 colocation alignment anchor 的軸（推薦，與 anchor 視覺永遠平行）
    }

    [Header("Room Size")]
    [SerializeField] private float roomWidth = 3f;
    [SerializeField] private float roomDepth = 3f;
    [SerializeField] private float ceilingHeight = 5f;
    [SerializeField] private float floorY = 0f;
    [SerializeField] private bool useRealCeilingHeightWhenLower = true;

    [Header("Alignment")]
    [SerializeField] private AlignmentMode alignmentMode = AlignmentMode.UseCameraForward;
    [SerializeField] private Transform wallReference;
    [SerializeField] private float cameraForwardWallDistance = 1.5f;
    [SerializeField] private bool loadRealRoomFromDeviceFirst = false;
    [SerializeField] private bool requestSceneCaptureIfNoDataFound = false;

    [Header("Startup")]
    [SerializeField] private bool loadOnStart = true;
    [SerializeField] private bool updateSpawnAreaCenter = true;

    [Header("ColocationAnchor 模式")]
    [Tooltip("等待 colocation alignment anchor 出現的秒數（host 按 create room 後幾秒內會建立）；逾時退回世界軸。")]
    [SerializeField] private float anchorWaitTimeout = 120f;
    [Tooltip("Editor / 單機測試沒有 colocation，等這麼久就放棄改用世界軸。")]
    [SerializeField] private float anchorWaitTimeoutEditor = 5f;

    /// true = 虛擬 3x3 房間已載入完成（HouseSpawner 等這個旗標才生房子，
    /// 避免 host 啟動搶在虛擬房之前、把房子生在「真實房間」的牆上）。
    public static bool VirtualRoomReady { get; private set; }

    private const string RoomUuid = "11111111111141118111111111111111";
    private const string FloorUuid = "22222222222242228222222222222222";
    private const string CeilingUuid = "33333333333343338333333333333333";
    private const string FrontWallUuid = "44444444444444448444444444444444";
    private const string BackWallUuid = "55555555555545558555555555555555";
    private const string LeftWallUuid = "66666666666646668666666666666666";
    private const string RightWallUuid = "77777777777747778777777777777777";

    private async void Start()
    {
        if (loadOnStart)
        {
            await LoadVirtualRoom();
        }
    }

    public async Task LoadVirtualRoom()
    {
        while (MRUK.Instance == null)
        {
            await Task.Yield();
        }

        Pose roomPose = await ResolveRoomPose();
        float resolvedCeilingHeight = ResolveCeilingHeight(roomPose.position.y);
        string json = BuildRoomJson(roomPose.position, roomPose.rotation.eulerAngles.y, resolvedCeilingHeight);
        MRUK.LoadDeviceResult result = await MRUK.Instance.LoadSceneFromJsonString(json, true);

        if (result != MRUK.LoadDeviceResult.Success)
        {
            Debug.LogError($"[VirtualMRUKRoomLoader] Failed to load virtual MRUK room: {result}");
            return;
        }

        if (updateSpawnAreaCenter && SpawnArea.Instance != null)
        {
            SpawnArea.Instance.SetPose(roomPose.position, roomPose.rotation.eulerAngles.y);
        }

        VirtualRoomReady = true;

        Debug.Log($"[VirtualMRUKRoomLoader] Loaded virtual room at {roomPose.position}, yaw={roomPose.rotation.eulerAngles.y}, ceiling={resolvedCeilingHeight}");
    }

    public void LoadVirtualRoomFromUnityEvent()
    {
        _ = LoadVirtualRoom();
    }

    private async Task<Pose> ResolveRoomPose()
    {
        Vector3 frontDirection;
        Vector3 frontWallPoint;

        (bool foundWall, Pose wallPose) = alignmentMode == AlignmentMode.UseMRUKWall
            ? await TryGetMrukWallPose()
            : (false, Pose.identity);

        if (foundWall)
        {
            Vector3 inwardNormal = Vector3.ProjectOnPlane(wallPose.rotation * Vector3.forward, Vector3.up).normalized;
            frontDirection = -inwardNormal;
            frontWallPoint = wallPose.position;
        }
        else if (alignmentMode == AlignmentMode.WorldAxes)
        {
            // colocation 對齊後兩台共用世界座標 → 房間直接對齊世界 X/Z 軸（yaw=0），
            // 中心 = 玩家開場位置（只取 XZ）。
            Transform cam = Camera.main != null ? Camera.main.transform : transform;
            frontDirection = Vector3.forward;
            frontWallPoint = cam.position + frontDirection * (roomDepth * 0.5f);
        }
        else if (alignmentMode == AlignmentMode.ColocationAnchor)
        {
            // 直接對齊 colocation alignment anchor 的軸向：anchor 鎖在物理位置，
            // 不受 recenter / tracking 漂移影響 → 房間框永遠跟 anchor 視覺平行。
            OVRSpatialAnchor anchor = await WaitForAlignmentAnchor();
            Transform cam = Camera.main != null ? Camera.main.transform : transform;

            if (anchor != null)
            {
                Vector3 fwd = Vector3.ProjectOnPlane(anchor.transform.forward, Vector3.up).normalized;
                if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
                frontDirection = fwd;
                Debug.Log($"[VirtualMRUKRoomLoader] Aligning room to colocation anchor (yaw={anchor.transform.eulerAngles.y:F1}).");
            }
            else
            {
                Debug.LogWarning("[VirtualMRUKRoomLoader] No colocation anchor appeared in time — falling back to world axes.");
                frontDirection = Vector3.forward;
            }

            frontWallPoint = cam.position + frontDirection * (roomDepth * 0.5f);
        }
        else if (alignmentMode == AlignmentMode.UseWallReference && wallReference != null)
        {
            Vector3 inwardNormal = Vector3.ProjectOnPlane(wallReference.forward, Vector3.up).normalized;
            if (inwardNormal.sqrMagnitude < 0.0001f)
            {
                inwardNormal = Vector3.forward;
            }

            frontDirection = -inwardNormal;
            frontWallPoint = wallReference.position;
        }
        else
        {
            Transform cameraTransform = Camera.main != null ? Camera.main.transform : transform;
            frontDirection = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
            if (frontDirection.sqrMagnitude < 0.0001f)
            {
                frontDirection = transform.forward;
            }

            frontWallPoint = cameraTransform.position + frontDirection * cameraForwardWallDistance;
        }

        Vector3 center = frontWallPoint - frontDirection * (roomDepth * 0.5f);
        center.y = floorY;

        float yaw = Mathf.Atan2(frontDirection.x, frontDirection.z) * Mathf.Rad2Deg;
        return new Pose(center, Quaternion.Euler(0f, yaw, 0f));
    }

    // 等 colocation alignment anchor 出現（host: 建立 room 後幾秒；guest: 對齊完成後）。
    // Editor / 單機沒有 colocation → 短逾時後回 null（改用世界軸）。
    private async Task<OVRSpatialAnchor> WaitForAlignmentAnchor()
    {
        float timeout = Application.isEditor ? anchorWaitTimeoutEditor : anchorWaitTimeout;
        float start = Time.realtimeSinceStartup;

        while (Time.realtimeSinceStartup - start < timeout)
        {
            var anchor = FindAnyObjectByType<OVRSpatialAnchor>();
            if (anchor != null && anchor.Created)
                return anchor;

            await Task.Delay(250);
        }
        return null;
    }

    private async Task<(bool found, Pose pose)> TryGetMrukWallPose()
    {
        MRUKRoom room = MRUK.Instance.GetCurrentRoom();
        if (room == null && loadRealRoomFromDeviceFirst)
        {
            MRUK.LoadDeviceResult result = await MRUK.Instance.LoadSceneFromDevice(requestSceneCaptureIfNoDataFound, true);
            if (result != MRUK.LoadDeviceResult.Success)
            {
                Debug.LogWarning($"[VirtualMRUKRoomLoader] Could not load real MRUK room before wall alignment: {result}");
            }

            room = MRUK.Instance.GetCurrentRoom();
        }

        if (room == null)
        {
            Debug.LogWarning("[VirtualMRUKRoomLoader] UseMRUKWall selected, but no MRUK room is loaded. Falling back to camera forward.");
            return (false, Pose.identity);
        }

        Transform cameraTransform = Camera.main != null ? Camera.main.transform : transform;
        Vector3 cameraForward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
        if (cameraForward.sqrMagnitude < 0.0001f)
        {
            cameraForward = transform.forward;
        }

        MRUKAnchor bestWall = null;
        float bestScore = float.NegativeInfinity;

        foreach (MRUKAnchor anchor in room.Anchors)
        {
            if ((anchor.Label & MRUKAnchor.SceneLabels.WALL_FACE) == 0)
            {
                continue;
            }

            Vector3 wallNormal = Vector3.ProjectOnPlane(anchor.transform.forward, Vector3.up).normalized;
            if (wallNormal.sqrMagnitude < 0.0001f)
            {
                continue;
            }

            Vector3 directionToWall = -wallNormal;
            float facingScore = Vector3.Dot(cameraForward, directionToWall);
            float areaScore = anchor.PlaneRect.HasValue ? anchor.PlaneRect.Value.width * anchor.PlaneRect.Value.height : 1f;
            float distance = Vector3.Distance(cameraTransform.position, anchor.transform.position);
            float score = facingScore * 10f + areaScore * 0.1f - distance * 0.05f;

            if (score > bestScore)
            {
                bestScore = score;
                bestWall = anchor;
            }
        }

        if (bestWall == null)
        {
            Debug.LogWarning("[VirtualMRUKRoomLoader] Loaded MRUK room has no WALL_FACE anchor. Falling back to camera forward.");
            return (false, Pose.identity);
        }

        Pose wallPose = new Pose(bestWall.transform.position, bestWall.transform.rotation);
        Debug.Log($"[VirtualMRUKRoomLoader] Selected MRUK wall '{bestWall.name}' at {wallPose.position}");
        return (true, wallPose);
    }

    private float ResolveCeilingHeight(float virtualFloorY)
    {
        if (!useRealCeilingHeightWhenLower)
        {
            return ceilingHeight;
        }

        MRUKRoom room = MRUK.Instance.GetCurrentRoom();
        if (room == null || room.CeilingAnchor == null)
        {
            return ceilingHeight;
        }

        float realFloorY = room.FloorAnchor != null ? room.FloorAnchor.transform.position.y : virtualFloorY;
        float realCeilingHeight = room.CeilingAnchor.transform.position.y - realFloorY;

        if (realCeilingHeight > 0.5f && realCeilingHeight < ceilingHeight)
        {
            Debug.Log($"[VirtualMRUKRoomLoader] Using lower real ceiling height {realCeilingHeight}m instead of {ceilingHeight}m.");
            return realCeilingHeight;
        }

        return ceilingHeight;
    }

    private string BuildRoomJson(Vector3 center, float yaw, float resolvedCeilingHeight)
    {
        float halfWidth = roomWidth * 0.5f;
        float halfDepth = roomDepth * 0.5f;
        float halfHeight = resolvedCeilingHeight * 0.5f;

        Quaternion roomRotation = Quaternion.Euler(0f, yaw, 0f);
        Vector3 forward = roomRotation * Vector3.forward;
        Vector3 right = roomRotation * Vector3.right;

        StringBuilder sb = new StringBuilder(4096);
        sb.Append("{\"CoordinateSystem\":\"Unity\",\"Rooms\":[{");
        AppendProp(sb, "UUID", RoomUuid);
        sb.Append("\"RoomLayout\":{");
        AppendProp(sb, "FloorUuid", FloorUuid);
        AppendProp(sb, "CeilingUuid", CeilingUuid);
        sb.Append("\"WallsUUid\":[");
        AppendQuoted(sb, FrontWallUuid); sb.Append(',');
        AppendQuoted(sb, BackWallUuid); sb.Append(',');
        AppendQuoted(sb, LeftWallUuid); sb.Append(',');
        AppendQuoted(sb, RightWallUuid);
        sb.Append("]},\"Anchors\":[");

        AppendPlaneAnchor(sb, FloorUuid, "FLOOR", center, 270f, yaw, 0f, halfWidth, halfDepth, false);
        AppendPlaneAnchor(sb, CeilingUuid, "CEILING", center + Vector3.up * resolvedCeilingHeight, 90f, yaw + 180f, 0f, halfWidth, halfDepth);
        AppendPlaneAnchor(sb, FrontWallUuid, "WALL_FACE", center + forward * halfDepth + Vector3.up * halfHeight, 0f, yaw + 180f, 0f, halfWidth, halfHeight);
        AppendPlaneAnchor(sb, BackWallUuid, "WALL_FACE", center - forward * halfDepth + Vector3.up * halfHeight, 0f, yaw, 0f, halfWidth, halfHeight);
        AppendPlaneAnchor(sb, LeftWallUuid, "WALL_FACE", center - right * halfWidth + Vector3.up * halfHeight, 0f, yaw + 90f, 0f, halfDepth, halfHeight);
        AppendPlaneAnchor(sb, RightWallUuid, "WALL_FACE", center + right * halfWidth + Vector3.up * halfHeight, 0f, yaw - 90f, 0f, halfDepth, halfHeight);

        sb.Append("]}]}");
        return sb.ToString();
    }

    private static void AppendPlaneAnchor(
        StringBuilder sb,
        string uuid,
        string label,
        Vector3 position,
        float rotX,
        float rotY,
        float rotZ,
        float halfX,
        float halfY,
        bool prependComma = true)
    {
        if (prependComma)
        {
            sb.Append(',');
        }

        sb.Append('{');
        AppendProp(sb, "UUID", uuid);
        sb.Append("\"SemanticClassifications\":[");
        AppendQuoted(sb, label);
        sb.Append("],");
        sb.Append("\"Transform\":{");
        sb.Append("\"Translation\":");
        AppendVector3(sb, position);
        sb.Append(",\"Rotation\":");
        AppendVector3(sb, new Vector3(rotX, NormalizeAngle(rotY), rotZ));
        sb.Append(",\"Scale\":[1,1,1]},");
        sb.Append("\"PlaneBounds\":{\"Min\":");
        AppendVector2(sb, -halfX, -halfY);
        sb.Append(",\"Max\":");
        AppendVector2(sb, halfX, halfY);
        sb.Append("},\"PlaneBoundary2D\":[");
        AppendVector2(sb, -halfX, -halfY); sb.Append(',');
        AppendVector2(sb, halfX, -halfY); sb.Append(',');
        AppendVector2(sb, halfX, halfY); sb.Append(',');
        AppendVector2(sb, -halfX, halfY);
        sb.Append("]}");
    }

    private static void AppendProp(StringBuilder sb, string name, string value, bool comma = true)
    {
        AppendQuoted(sb, name);
        sb.Append(':');
        AppendQuoted(sb, value);
        if (comma)
        {
            sb.Append(',');
        }
    }

    private static void AppendQuoted(StringBuilder sb, string value)
    {
        sb.Append('"').Append(value).Append('"');
    }

    private static void AppendVector2(StringBuilder sb, float x, float y)
    {
        sb.Append('[')
            .Append(Format(x)).Append(',')
            .Append(Format(y)).Append(']');
    }

    private static void AppendVector3(StringBuilder sb, Vector3 value)
    {
        sb.Append('[')
            .Append(Format(value.x)).Append(',')
            .Append(Format(value.y)).Append(',')
            .Append(Format(value.z)).Append(']');
    }

    private static string Format(float value)
    {
        return value.ToString("0.######", CultureInfo.InvariantCulture);
    }

    private static float NormalizeAngle(float angle)
    {
        angle %= 360f;
        return angle < 0f ? angle + 360f : angle;
    }
}
