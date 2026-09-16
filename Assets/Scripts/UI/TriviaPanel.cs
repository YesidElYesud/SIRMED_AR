namespace SIRMED.UI
{
    using System.Collections;
    using SIRMED.Gameplay.Dialogue;
    using SIRMED.Gameplay.Hotspots;
    using SIRMED.Managers;
    using TMPro;
    using UnityEngine;

    /// <summary>
    /// TriviaPanel — Popup compacto de una pregunta de refuerzo (microtrivia).
    ///
    /// Disparado automáticamente desde HotspotController.ClosePanel() cuando el
    /// HotspotData del hotspot recién cerrado tiene un TriviaData asignado. Por
    /// diseño (GDD §14/§22) nunca se muestra durante N4 — evacuación no debe
    /// competir con trivias — y siempre aparece DESPUÉS del contenido principal,
    /// nunca antes.
    ///
    /// Reutiliza MultipleChoicePanel (el mismo componente que ya usa SiataCallPanel)
    /// en vez de duplicar la lógica de selección/feedback/reintento.
    ///
    /// Singleton. Al cerrarse, devuelve el control a HotspotController.ClosePanel()
    /// para que continúe con avance de etapa / fin de juego / reactivar el prompt.
    /// </summary>
    public class TriviaPanel : MonoBehaviour
    {
        // ── Singleton ─────────────────────────────────────────────────────────────
        public static TriviaPanel Instance { get; private set; }

        // ── Inspector: Cuerpo ─────────────────────────────────────────────────────
        [Header("Cuerpo")]
        public TextMeshProUGUI questionText;

        // ── Inspector: Panel de opciones múltiples ───────────────────────────────
        [Header("Panel de opciones múltiples")]
        public MultipleChoicePanel choicePanel;

        // ── Internos ──────────────────────────────────────────────────────────────
        private TriviaData _data;
        private HotspotController _sourceHotspot;
        private Coroutine _correctRoutine;
        private Coroutine _wrongRoutine;

        // ── Lifecycle ─────────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            gameObject.SetActive(false);
        }

        // ── API pública ───────────────────────────────────────────────────────────

        public void Show(TriviaData data, HotspotController source)
        {
            if (data == null)
            {
                Debug.LogWarning("[TriviaPanel] TriviaData es null.");
                return;
            }

            _data = data;
            _sourceHotspot = source;

            if (questionText != null) questionText.text = data.question;

            gameObject.SetActive(true);
            BlockInput(true);

            if (choicePanel != null) choicePanel.gameObject.SetActive(true);
            SetupChoicePanel(data.options);
        }

        /// <summary>Cierra el panel y devuelve el control al hotspot que lo abrió.</summary>
        public void Hide()
        {
            StopRoutines();
            UnsubscribeChoiceEvents();
            if (choicePanel != null) choicePanel.Clear();

            BlockInput(false);
            gameObject.SetActive(false);

            _data = null;

            if (_sourceHotspot != null)
            {
                HotspotController source = _sourceHotspot;
                _sourceHotspot = null;
                source.ClosePanel();
            }
        }

        // ── Panel de opciones ─────────────────────────────────────────────────────
        private void SetupChoicePanel(DialogueOption[] options)
        {
            if (choicePanel == null)
            {
                Debug.LogWarning("[TriviaPanel] choicePanel no asignado en el Inspector.");
                return;
            }
            UnsubscribeChoiceEvents();
            choicePanel.OnCorrect += HandleCorrectAnswer;
            choicePanel.OnWrong += HandleWrongAnswer;
            choicePanel.OnRetry += HandleRetry;
            choicePanel.SetOptions(options);
        }

        private void UnsubscribeChoiceEvents()
        {
            if (choicePanel == null) return;
            choicePanel.OnCorrect -= HandleCorrectAnswer;
            choicePanel.OnWrong -= HandleWrongAnswer;
            choicePanel.OnRetry -= HandleRetry;
        }

        // ── Respuestas ────────────────────────────────────────────────────────────
        private void HandleCorrectAnswer()
        {
            _correctRoutine = StartCoroutine(CorrectAnswerRoutine());
        }

        private void HandleWrongAnswer()
        {
            if (_wrongRoutine != null) StopCoroutine(_wrongRoutine);
            _wrongRoutine = StartCoroutine(WrongAnswerRoutine());
        }

        private void HandleRetry()
        {
            // El jugador pulsó "Intentar de nuevo" — cancelar el auto-reintento por timer.
            if (_wrongRoutine != null) { StopCoroutine(_wrongRoutine); _wrongRoutine = null; }
        }

        private IEnumerator CorrectAnswerRoutine()
        {
            yield return new WaitForSeconds(_data != null ? _data.answerDelay : 1.5f);
            Hide();
        }

        private IEnumerator WrongAnswerRoutine()
        {
            yield return new WaitForSeconds(_data != null ? _data.answerDelay : 1.5f);
            _wrongRoutine = null;

            // Regenerar las mismas opciones (sin cerrar el panel) — igual que SiataCallPanel.
            if (_data != null)
                SetupChoicePanel(_data.options);
        }

        // ── Input / utilidades ────────────────────────────────────────────────────
        private void BlockInput(bool block) =>
            StageManager.Instance?.SetPlayerInputBlocked(block);

        private void StopRoutines()
        {
            if (_correctRoutine != null) { StopCoroutine(_correctRoutine); _correctRoutine = null; }
            if (_wrongRoutine != null) { StopCoroutine(_wrongRoutine); _wrongRoutine = null; }
        }
    }
}
