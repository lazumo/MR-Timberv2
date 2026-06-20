using UnityEngine;

/// <summary>
/// Layer 3 — shows a juicing UI hint (arrow direction + hand model) on a color factory
/// during the Juicing phase only.
///
/// Visual-only and phase-driven: it reads the synced GameFlowController.CurrentPhase, so it
/// works on every client and is robust to factory spawn timing / late join (factories spawn
/// when a house becomes Built, which is before Juicing). A plain MonoBehaviour is enough
/// because the phase NetworkVariable is readable everywhere and the hint is purely cosmetic.
/// </summary>
public class FactoryJuiceHint : MonoBehaviour
{
    [Tooltip("The hint visuals (arrow + hand model). Active only during the Juicing phase.")]
    [SerializeField] private GameObject hintRoot;

    private GameFlowController _flow;

    private void OnEnable()
    {
        TryBind();
        Refresh();
    }

    private void OnDisable()
    {
        if (_flow != null)
            _flow.CurrentPhase.OnValueChanged -= OnPhaseChanged;
        _flow = null;
    }

    private void Update()
    {
        // Late-bind if the factory spawned before the GameFlowController existed.
        if (_flow == null)
        {
            TryBind();
            Refresh();
        }
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

        bool show = _flow != null && _flow.CurrentPhase.Value == GamePhase.Juicing;
        if (hintRoot.activeSelf != show)
            hintRoot.SetActive(show);
    }
}
