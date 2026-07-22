using Unity.Netcode;
using UnityEngine;

/// <summary>
/// staff 專用「對齊誤差儀表」:按住左手 grip 才顯示(玩家平常看不到)。
///
/// 每台每秒量一次「colocation 圖釘偏離原點多遠」(= 本機世界與圖釘的分歧量):
///  - host 直接寫進 GameFlowController.HostAlignError(NetworkVariable)
///  - guest 用 ServerRpc 回報 → GuestAlignError
/// 兩個值都廣播,所以任一台按住 grip 都能同時看到 HOST / CLIENT 兩個數字。
/// 判讀:< 2cm 綠、2–5cm 黃、> 5cm 該按左手 Start 長按手動校正(或等 deadband 自動修)。
///
/// 零接線:GameFlowController.OnNetworkSpawn 呼叫 Ensure()。
/// </summary>
public class AlignmentErrorHud : MonoBehaviour
{
    private const float ReportInterval = 1f;

    private TextMesh _text;
    private float _nextReport;

    public static void Ensure()
    {
        if (FindAnyObjectByType<AlignmentErrorHud>() != null) return;
        new GameObject("AlignmentErrorHud").AddComponent<AlignmentErrorHud>();
    }

    private void Update()
    {
        var nm = NetworkManager.Singleton;
        var gf = GameFlowController.Instance;
        if (nm == null || gf == null || !gf.IsSpawned) { SetVisible(false); return; }

        // 量自己的誤差 → 回報(顯示與否都持續回報,log/另一台才有資料)
        if (Time.time >= _nextReport)
        {
            _nextReport = Time.time + ReportInterval;
            if (ColocationHostAlignment.TryMeasureError(out float pe, out float ye))
            {
                var v = new Vector2(pe, ye);
                if (nm.IsHost)                    gf.SetHostAlignError(v);
                else if (nm.IsConnectedClient)    gf.ReportGuestAlignErrorServerRpc(v);
            }
        }

        // 按住左手 grip = 顯示(host 的 grip+Start 重擺組合鍵照常運作,不衝突)
        bool show = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, OVRInput.Controller.LTouch) > 0.5f;
        SetVisible(show);
        if (!show) return;

        var cam = Camera.main;
        if (cam == null) return;

        _text.text = $"ALIGN ERR   HOST {Fmt(gf.HostAlignError.Value)}   CLIENT {Fmt(gf.GuestAlignError.Value)}";

        Vector3 fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
        if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
        _text.transform.position = cam.transform.position + fwd * 1.0f + Vector3.down * 0.25f;
        _text.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
    }

    private static string Fmt(Vector2 e)
    {
        return e.x < 0f ? "--" : $"{e.x * 100f:F1}cm/{e.y:F1}°";
    }

    private void SetVisible(bool on)
    {
        if (on && _text == null)
        {
            var go = new GameObject("AlignmentErrorHudText");
            _text = go.AddComponent<TextMesh>();
            _text.characterSize = 0.008f;
            _text.fontSize = 64;
            _text.anchor = TextAnchor.MiddleCenter;
            _text.alignment = TextAlignment.Center;
            _text.color = new Color(0.4f, 1f, 0.9f);
        }
        if (_text != null && _text.gameObject.activeSelf != on)
            _text.gameObject.SetActive(on);
    }
}
