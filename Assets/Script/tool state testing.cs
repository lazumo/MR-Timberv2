using UnityEngine;
using Unity.Netcode;

public class ThumbstickStateSwitcher : NetworkBehaviour
{
    public ToolController toolController;

    [Header("State Range")]
    public int minState = 0;
    public int maxState = 4;

    void Update()
    {
        if (!IsOwner) return;
        if (toolController == null) return;

        // ⭐ Thumbstick Press（Quest / PC 對應）
        if (Input.GetKeyDown(KeyCode.JoystickButton9)) // Thumbstick Press
        {
            SceneController.NextLevel();
            int next = 3;

            toolController.SetStateServerRpc(next);

            Debug.Log($"[Thumbstick] Switch to state {next}");
        }
    }
}
