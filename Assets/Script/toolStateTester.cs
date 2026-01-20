using UnityEngine;

public class XRToolStateController : MonoBehaviour
{
    public ToolController toolController;

    [Header("Random Interval (seconds)")]
    public float minInterval = 1.0f;
    public float maxInterval = 3.0f;

    private float timer;
    private float nextInterval;

    void Start()
    {
        ScheduleNext();
    }

    void Update()
    {
        if (toolController == null) return;

        timer += Time.deltaTime;

        if (timer >= nextInterval)
        {
            timer = 0f;
            ScheduleNext();
            CycleNextState();
        }
    }

    private void ScheduleNext()
    {
        nextInterval = Random.Range(minInterval, maxInterval);
        Debug.Log($"[XR-Test] Next switch in {nextInterval:F2} seconds");
    }

    private void CycleNextState()
    {
        int current = toolController.CurrentState;
        int next = current + 1;

        // 可選：簡單循環
        if (SceneController.CurrentLevel == 1 && next > 2) next = 0;
        if (SceneController.CurrentLevel == 2 && next > 4) next = 3;

        toolController.SetStateServerRpc(next);
    }

}
