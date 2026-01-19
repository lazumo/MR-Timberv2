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
        int current = toolController.CurrentState.Value;
        int next = current + 1;

        Debug.Log($"[XR-Test] Switch tool: {current} → {next}");

        toolController.SetStateServerRpc(next);
    }
}
