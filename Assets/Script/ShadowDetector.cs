using UnityEngine;
using Unity.Netcode;

public class ToolShadowReceiver : NetworkBehaviour
{
    public ToolController toolController;
    public int boxStateIndex = 2;

    public float cooldown = 0.5f;
    private float lastTriggerTime = -10f;

    public void OnShadowHit()
    {
        if (Time.time - lastTriggerTime < cooldown) return;
        lastTriggerTime = Time.time;

        if (toolController.CurrentState == boxStateIndex)
            return;

        toolController.SetStateServerRpc(boxStateIndex);

        Debug.Log("[ToolShadowReceiver] Switch to BOX");
    }
}
