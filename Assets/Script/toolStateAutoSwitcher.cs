using Unity.Netcode;
using UnityEngine;
public class ToolStateResolver : NetworkBehaviour
{
    public ToolController toolController;
    public ToolShadowReceiver shadowReceiver;

    public int sawState = 0;
    public int boxState = 2;
    public int juicerState = 1;

    void Update()
    {
        if (!IsOwner && !IsServer) return;

        int targetState = ResolveState();

        if (toolController.CurrentState != targetState)
        {
            toolController.SetStateServerRpc(targetState);
        }
    }

    int ResolveState()
    {
        int current = toolController.CurrentState;
        bool shadow = shadowReceiver.ShadowHitThisFrame;
        bool touching = shadowReceiver.IsTouchingFruit;

        if (current == boxState)
        {
            // 已經是 box → 只要還接觸水果就維持
            if (touching)
                return boxState;

            // 沒接觸 + 沒 shadow → 回 saw
            if (!shadow)
                return sawState;

            // 還有 shadow → 繼續 box
            return boxState;
        }
        else // current == sawState (或其他)
        {
            // 只有 shadow 才能進 box
            if (shadow)
                return boxState;

            return sawState;
        }
    }


    bool ShouldUseJuicer()
    {
        // 之後加：
        // fruit type == juice
        // user button
        // zone detection
        return false;
    }
}
