using UnityEngine;

/// <summary>
/// 榨汁 UI 提示（ghost 手 + 箭頭）的顯示時機：
///   1. 只在 Juicing 階段顯示（讀同步的 GameFlowController.CurrentPhase）。
///   2. 玩家實際開始擠壓時（ColorFactoryNetDriver.IsActive，中點進入 factory）自動收起，
///      避免「兩組手」同時在動很亂；停手一段時間後再出現。
/// 純視覺、每個 client 各自運作（兩個依據都是同步變數，結果自然一致）。
/// </summary>
public class FactoryJuiceHint : MonoBehaviour
{
    [Tooltip("提示視覺（ghost 手 + 箭頭）。")]
    [SerializeField] private GameObject hintRoot;

    [Tooltip("擠壓驅動（同物件上的 ColorFactoryNetDriver）。留空自動抓。")]
    [SerializeField] private ColorFactoryNetDriver driver;

    [Tooltip("玩家停手後，提示要等幾秒才重新出現。")]
    [SerializeField] private float reappearDelay = 2f;

    private GameFlowController _flow;
    private float _idleTime = 999f;   // 一開始視為「已閒置很久」→ 進 Juicing 立即顯示

    private void Awake()
    {
        if (driver == null)
            driver = GetComponent<ColorFactoryNetDriver>();
    }

    private void OnDisable()
    {
        if (_flow != null)
            _flow.CurrentPhase.OnValueChanged -= OnPhaseChanged;
        _flow = null;
    }

    private void Update()
    {
        // Factory 可能比 GameFlowController 先生成 → 持續補綁
        if (_flow == null)
            TryBind();

        // 追蹤「距離上次擠壓」的閒置時間
        bool squeezing = driver != null && driver.IsActive.Value;
        _idleTime = squeezing ? 0f : _idleTime + Time.deltaTime;

        Refresh();
    }

    private void TryBind()
    {
        var flow = GameFlowController.Instance;
        if (flow == null || flow == _flow) return;

        _flow = flow;
        _flow.CurrentPhase.OnValueChanged += OnPhaseChanged;
    }

    private void OnPhaseChanged(GamePhase oldPhase, GamePhase newPhase) => Refresh();

    private void Refresh()
    {
        if (hintRoot == null) return;

        bool inJuicing = _flow != null && _flow.CurrentPhase.Value == GamePhase.Juicing;
        bool idleLongEnough = _idleTime >= reappearDelay;

        bool show = inJuicing && idleLongEnough;
        if (hintRoot.activeSelf != show)
            hintRoot.SetActive(show);
    }
}
