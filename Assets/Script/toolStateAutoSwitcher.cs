using Unity.Netcode;
using UnityEngine;

public class ToolStateResolver : NetworkBehaviour
{
    public ToolController toolController;
    public ToolShadowReceiver shadowReceiver;
    public MiddleAnchorZoneDetector zoneDetector;   // ⭐ 改成用 factory

    public int sawState = 0;
    public int boxState = 2;
    public int juicerState = 1;

    void Update()
    {
        if (!IsOwner && !IsServer) return;
        if (toolController == null) return;
        if (toolController.CurrentState >= 3)
            return;
        int targetState = ResolveState();

        if (toolController.CurrentState != targetState)
        {
            toolController.SetStateServerRpc(targetState);
        }
    }

    int ResolveState()
    {
        int current = toolController.CurrentState;

        bool shadow = shadowReceiver != null && shadowReceiver.ShadowHitThisFrame;
        bool touching = shadowReceiver != null && shadowReceiver.IsTouchingFruit;
        bool inFactory = zoneDetector != null && zoneDetector.isInsideFactory;

        // ===== 1️⃣ Factory zone → 強制 Juicer =====
        if (inFactory)
            return juicerState;

        // ===== 2️⃣ box 狀態維持 =====
        if (current == boxState)
        {
            if (touching)
                return boxState;

            if (!shadow)
                return sawState;

            return boxState;
        }

        // ===== 3️⃣ saw → box =====
        if (shadow)
            return boxState;

        return sawState;
    }
}
