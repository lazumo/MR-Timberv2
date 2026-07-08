using UnityEngine;

/// <summary>
/// 手把震動小工具（本機的兩支 controller）。
/// OVRInput 的震動要每幀重設（約 2 秒會自動停），持續震動請在 Update 連續呼叫，
/// 結束時務必呼叫 StopBoth()。
/// </summary>
public static class Haptics
{
    private static bool IsHost =>
        Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsHost;

    /// 握「共享 prop（鋸子/箱子/榨汁）」的手：Host=左手、Client=右手
    public static OVRInput.Controller PropHand =>
        IsHost ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;

    /// 握「滅火器/水管」的手：Host=右手、Client=左手
    public static OVRInput.Controller ExtinguisherHand =>
        IsHost ? OVRInput.Controller.RTouch : OVRInput.Controller.LTouch;

    public static void Set(OVRInput.Controller hand, float frequency, float amplitude)
        => OVRInput.SetControllerVibration(frequency, amplitude, hand);

    public static void Stop(OVRInput.Controller hand) => Set(hand, 0f, 0f);

    public static void SetBoth(float frequency, float amplitude)
    {
        Set(OVRInput.Controller.LTouch, frequency, amplitude);
        Set(OVRInput.Controller.RTouch, frequency, amplitude);
    }

    public static void StopBoth() => SetBoth(0f, 0f);

    /// 短促一震（給 MonoBehaviour 用 coroutine 跑）
    public static System.Collections.IEnumerator Pulse(OVRInput.Controller hand, float frequency, float amplitude, float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            Set(hand, frequency, amplitude);
            yield return null;
        }
        Stop(hand);
    }
}
