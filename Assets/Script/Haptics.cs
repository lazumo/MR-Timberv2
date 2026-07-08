using UnityEngine;

/// <summary>
/// 手把震動小工具（本機的兩支 controller）。
/// OVRInput 的震動要每幀重設（約 2 秒會自動停），持續震動請在 Update 連續呼叫，
/// 結束時務必呼叫 StopBoth()。
/// </summary>
public static class Haptics
{
    public static void SetBoth(float frequency, float amplitude)
    {
        OVRInput.SetControllerVibration(frequency, amplitude, OVRInput.Controller.LTouch);
        OVRInput.SetControllerVibration(frequency, amplitude, OVRInput.Controller.RTouch);
    }

    public static void StopBoth() => SetBoth(0f, 0f);

    /// 短促一震（給 MonoBehaviour 用 coroutine 跑）
    public static System.Collections.IEnumerator Pulse(float frequency, float amplitude, float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            SetBoth(frequency, amplitude);
            yield return null;
        }
        StopBoth();
    }
}
