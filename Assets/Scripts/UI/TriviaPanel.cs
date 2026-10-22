namespace SIRMED.UI
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using Cysharp.Threading.Tasks;
    using SIRMED.Gameplay.Dialogue;
    using SIRMED.Gameplay.Hotspots;
    using SIRMED.Managers;
    using TMPro;
    using UnityEngine;

    /// <summary>
    /// TriviaPanel — Popup compacto de una pregunta de refuerzo (microtrivia).
    ///
    /// Disparado automáticamente desde HotspotController.DispatchAction() cuando el
    /// HotspotData del hotspot tiene trivia asignada (trivia o triviaPool) — entra
    /// directo a la trivia, sin mostrar el panel principal (InfoPanel/NPC/SIATA/
    /// slides) primero. Por diseño (GDD §14/§22) nunca se muestra durante N4 —
    /// evacuación no debe competir con trivias.
    ///
    /// Reutiliza MultipleChoicePanel (el mismo componente que ya usa SiataCallPanel)
    /// en vez de duplicar la lógica de selección/feedback/reintento.
    ///
    /// Singleton. Al cerrarse, devuelve el control a HotspotController.ClosePanel()
    /// para que continúe con avance de etapa / fin de juego / reactivar el prompt.
    ///
    /// Trivias por etapa (_stageTrivias): además de las de hotspot, una trivia puede salir
    /// la primera vez que se entra a una etapa (DAGRD trivia 5: "cuando el usuario decida
    /// ir al riesgo N2" → Etapa2). Espera a que no haya otro panel modal ni consejo del
    /// Director en pantalla, y se descarta si la etapa cambió mientras esperaba. Nunca en N4.
    /// </summary>
    public class TriviaPanel : MonoBehaviour
    {
        // ── Singleton ─────────────────────────────────────────────────────────────
        public static TriviaPanel Instance { get; private set; }

        // ── Inspector: Cuerpo ─────────────────────────────────────────────────────
        [Header("Cuerpo")]
        [Tooltip("Muestra TriviaData.topic. Se oculta si la trivia no tiene tema.")]
        public TextMeshProUGUI topicText;
        public TextMeshProUGUI questionText;

        // ── Inspector: Panel de opciones múltiples ───────────────────────────────
        [Header("Panel de opciones múltiples")]
        public MultipleChoicePanel choicePanel;

        [Tooltip("Texto del área de feedback antes de responder.")]
        [SerializeField] private string _feedbackPlaceholder = "Elige la respuesta correcta.";

        // ── Inspector: Trivias por etapa ──────────────────────────────────────────
        [Serializable]
        private class StageTrivia
        {
            public StageManager.Stage stage = StageManager.Stage.Etapa2;
            public TriviaData trivia;
        }

        [Header("Trivias por etapa")]
        [Tooltip("Trivia que sale una sola vez al entrar a la etapa (p. ej. Etapa2 → Trivia-N2-Quebrada).")]
        [SerializeField] private StageTrivia[] _stageTrivias;

        // ── HUD ───────────────────────────────────────────────────────────────────
        [Header("HUD — ocultar durante la trivia")]
        [SerializeField] private GameObject[] _hudElementsToHide;

        // ── Internos ──────────────────────────────────────────────────────────────
        private TriviaData _data;
        private HotspotController _sourceHotspot;
        private Coroutine _correctRoutine;
        private Coroutine _wrongRoutine;
        private readonly HashSet<StageManager.Stage> _stageTriviasShown = new HashSet<StageManager.Stage>();

        // ── Lifecycle ─────────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            gameObject.SetActive(false);

            // El panel queda inactivo (sin Start/Update) y StageManager puede despertar
            // después: suscribirse cuando exista.
            SubscribeToStagesAsync().Forget();
        }

        private void OnDestroy()
        {
            if (StageManager.Instance != null)
                StageManager.Instance.OnStageChanged -= HandleStageChanged;
        }

        // ── API pública ───────────────────────────────────────────────────────────

        /// <summary>False si no se mostró (data null o ya hay una trivia abierta).</summary>
        public bool Show(TriviaData data, HotspotController source)
        {
            if (data == null)
            {
                Debug.LogWarning("[TriviaPanel] TriviaData es null.");
                return false;
            }
            if (gameObject.activeSelf)
            {
                Debug.LogWarning($"[TriviaPanel] Ya hay una trivia abierta; se descarta '{data.name}'.");
                return false;
            }

            _data = data;
            _sourceHotspot = source;

            if (string.IsNullOrWhiteSpace(data.question))
                Debug.LogWarning($"[TriviaPanel] La trivia '{data.name}' no tiene pregunta.");

            // Activar antes de asignar los textos: un TMP con auto-size que recibe el
            // texto estando inactivo a veces no se redibuja al activarse (pregunta vacía).
            gameObject.SetActive(true);

            if (topicText != null)
            {
                bool hasTopic = !string.IsNullOrWhiteSpace(data.topic);
                topicText.gameObject.SetActive(hasTopic);
                topicText.text = hasTopic ? data.topic : string.Empty;
                topicText.ForceMeshUpdate();
            }
            if (questionText != null)
            {
                questionText.gameObject.SetActive(true);
                questionText.text = data.question;
                questionText.ForceMeshUpdate();
            }

            BlockInput(true);
            SaveAndHideHud();

            if (choicePanel != null) choicePanel.gameObject.SetActive(true);
            SetupChoicePanel(data.options);
            return true;
        }

        /// <summary>Cierra el panel y devuelve el control al hotspot que lo abrió.</summary>
        public void Hide()
        {
            StopRoutines();
            UnsubscribeChoiceEvents();
            if (choicePanel != null) choicePanel.Clear();

            BlockInput(false);
            RestoreHud();
            gameObject.SetActive(false);

            _data = null;

            if (_sourceHotspot != null)
            {
                HotspotController source = _sourceHotspot;
                _sourceHotspot = null;
                source.ClosePanel();
            }
        }

        // ── Trivias por etapa ─────────────────────────────────────────────────────
        private async UniTaskVoid SubscribeToStagesAsync()
        {
            if (_stageTrivias == null || _stageTrivias.Length == 0) return;
            if (await UniTask.WaitUntil(() => StageManager.Instance != null,
                    cancellationToken: destroyCancellationToken).SuppressCancellationThrow())
                return;
            StageManager.Instance.OnStageChanged += HandleStageChanged;
        }

        private void HandleStageChanged(StageManager.Stage previous, StageManager.Stage current)
        {
            if (current == StageManager.Stage.Etapa4 || _stageTriviasShown.Contains(current)) return;

            foreach (StageTrivia st in _stageTrivias)
            {
                if (st == null || st.stage != current || st.trivia == null) continue;
                _stageTriviasShown.Add(current);
                ShowStageTriviaWhenFreeAsync(st.trivia, current).Forget();
                return;
            }
        }

        private async UniTaskVoid ShowStageTriviaWhenFreeAsync(TriviaData trivia, StageManager.Stage stage)
        {
            // Un frame extra tras quedar libre: el panel que se está cerrando (o el hotspot que
            // avanzó la etapa en ClosePanel) termina su propio cierre antes de abrir la trivia.
            do
            {
                if (await UniTask.WaitUntil(() => !IsScreenBusy(), cancellationToken: destroyCancellationToken)
                        .SuppressCancellationThrow())
                    return;
                if (await UniTask.Yield(destroyCancellationToken).SuppressCancellationThrow())
                    return;
            } while (IsScreenBusy());

            if (StageManager.Instance == null || StageManager.Instance.CurrentStage != stage) return;
            Show(trivia, null);
        }

        // Ocupado = otra trivia abierta, un panel modal (bloquea input vía StageManager) o
        // un consejo del Director visible (mismo criterio que DirectorAdviceController).
        private bool IsScreenBusy() =>
            gameObject.activeSelf ||
            (StageManager.Instance != null && StageManager.Instance.IsPlayerInputBlocked) ||
            (DirectorAdvicePanel.Instance != null && DirectorAdvicePanel.Instance.IsShowing);

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
            choicePanel.SetOptions(options, _feedbackPlaceholder);
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

        // ── HUD ───────────────────────────────────────────────────────────────────

        private void SaveAndHideHud() => HudHider.Hide(this, _hudElementsToHide);

        private void RestoreHud() => HudHider.Restore(this);

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
