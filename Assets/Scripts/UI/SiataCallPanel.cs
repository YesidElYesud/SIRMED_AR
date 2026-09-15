namespace SIRMED.UI
{
    using System.Collections;
    using SIRMED.Gameplay.Dialogue;
    using SIRMED.Gameplay.Hotspots;
    using SIRMED.Managers;
    using TMPro;
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>
    /// SiataCallPanel — Simula una llamada telefónica al SIATA (Sistema de Alerta Temprana).
    ///
    /// Soporta dos modos:
    ///   A) Legado: Show(NpcDialogueData) — texto único + pregunta.
    ///   B) Secuencia: Show(SiataDialogueSequence) — pasos mixtos Info/Question.
    ///      - Paso Info:     texto + botón "Continuar" (un solo botón generado internamente).
    ///      - Paso Question: texto + opciones A/B/C; el jugador debe responder correctamente
    ///                       antes de avanzar (MultipleChoicePanel maneja el retry).
    ///
    /// Ambos tipos de paso reutilizan el mismo MultipleChoicePanel — no se necesita UI extra.
    ///
    /// Singleton. Llamado desde HotspotController cuando actionType = SiataCall.
    /// </summary>
    public class SiataCallPanel : MonoBehaviour
    {
        // ── Singleton ─────────────────────────────────────────────────────────────
        public static SiataCallPanel Instance { get; private set; }

        // ── Inspector: Encabezado de llamada ──────────────────────────────────────
        [Header("Encabezado de llamada")]
        public Image callerPhoto;
        public TextMeshProUGUI callerNameText;
        public TextMeshProUGUI callStatusText;

        // ── Inspector: Cuerpo ─────────────────────────────────────────────────────
        [Header("Cuerpo")]
        public TextMeshProUGUI dialogueText;

        // ── Inspector: Panel de opciones ─────────────────────────────────────────
        [Header("Panel de opciones múltiples")]
        public MultipleChoicePanel choicePanel;

        // ── Inspector: Controles ──────────────────────────────────────────────────
        [Header("Controles")]
        public Button hangUpButton;

        // ── Inspector: Comportamiento ─────────────────────────────────────────────
        [Header("Comportamiento")]
        [Range(0f, 3f)]
        public float connectingDelay = 1.5f;
        public string continueButtonLabel = "Continuar";
        public string connectingText = "Llamando…";
        public string connectedText = "Conectado";

        // ── Opción interna para pasos Info (un único botón, siempre correcto) ─────
        private DialogueOption[] _continueOption;

        // ── Internos ──────────────────────────────────────────────────────────────
        private NpcDialogueData _legacyData;
        private SiataDialogueSequence _sequence;
        private int _currentStepIndex;
        private bool _isSequenceMode;
        private HotspotController _sourceHotspot;
        private Coroutine _connectRoutine;
        private Coroutine _correctRoutine;
        private Coroutine _wrongRoutine;

        // ── Lifecycle ─────────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            gameObject.SetActive(false);
        }

        private void Start()
        {
            if (hangUpButton != null)
                hangUpButton.onClick.AddListener(Hide);
        }

        // ── API pública ───────────────────────────────────────────────────────────

        /// <summary>
        /// Modo legado: texto único + pregunta de selección múltiple.
        /// </summary>
        public void Show(NpcDialogueData data, HotspotController source)
        {
            if (data == null)
            {
                Debug.LogWarning("[SiataCallPanel] NpcDialogueData es null.");
                return;
            }
            _legacyData = data;
            _sequence = null;
            _isSequenceMode = false;
            _sourceHotspot = source;
            OpenPanel();
        }

        /// <summary>
        /// Modo secuencia: pasos mixtos Info / Question.
        /// </summary>
        public void Show(SiataDialogueSequence sequence, HotspotController source)
        {
            if (sequence == null || sequence.steps == null || sequence.steps.Length == 0)
            {
                Debug.LogWarning("[SiataCallPanel] SiataDialogueSequence es null o no tiene pasos.");
                return;
            }
            _sequence = sequence;
            _legacyData = null;
            _isSequenceMode = true;
            _currentStepIndex = 0;
            _sourceHotspot = source;
            OpenPanel();
        }

        /// <summary>Cierra el panel, limpia estado y desbloquea input.</summary>
        public void Hide()
        {
            StopAllCoroutinesInternal();
            UnsubscribeChoiceEvents();

            if (choicePanel != null) choicePanel.Clear();

            BlockInput(false);

            if (_sourceHotspot != null)
            {
                _sourceHotspot.ClosePanel();
                _sourceHotspot = null;
            }

            _legacyData = null;
            _sequence = null;
            _isSequenceMode = false;
            gameObject.SetActive(false);
        }

        // ── Apertura del panel ────────────────────────────────────────────────────
        private void OpenPanel()
        {
            PopulateCallerInfo();

            if (dialogueText != null) dialogueText.gameObject.SetActive(false);
            if (choicePanel != null) choicePanel.gameObject.SetActive(false);

            gameObject.SetActive(true);
            BlockInput(true);
            _connectRoutine = StartCoroutine(ConnectingRoutine());
        }

        private void PopulateCallerInfo()
        {
            string name = _isSequenceMode ? _sequence.npcName : _legacyData.npcName;
            Sprite photo = _isSequenceMode ? _sequence.npcPhoto : _legacyData.npcPhoto;

            bool hasPhoto = photo != null;
            if (callerPhoto != null)
            {
                callerPhoto.gameObject.SetActive(hasPhoto);
                if (hasPhoto) callerPhoto.sprite = photo;
            }
            if (callerNameText != null) callerNameText.text = name;
            if (callStatusText != null) callStatusText.text = connectingText;
        }

        // ── Delay de conexión ─────────────────────────────────────────────────────
        private IEnumerator ConnectingRoutine()
        {
            yield return new WaitForSeconds(connectingDelay);

            if (callStatusText != null) callStatusText.text = connectedText;
            if (dialogueText != null) dialogueText.gameObject.SetActive(true);

            if (_isSequenceMode)
                ShowStep(0);
            else
                ShowLegacyContent();

            _connectRoutine = null;
        }

        // ── Modo legado ───────────────────────────────────────────────────────────
        private void ShowLegacyContent()
        {
            if (dialogueText != null) dialogueText.text = _legacyData.npcText;
            if (choicePanel != null)
            {
                choicePanel.gameObject.SetActive(true);
                SetupChoicePanel(_legacyData.options);
            }
        }

        // ── Modo secuencia ────────────────────────────────────────────────────────

        private void ShowStep(int index)
        {
            if (_sequence == null || index < 0 || index >= _sequence.steps.Length) return;

            // Cancelar auto-reintento pendiente al cambiar de paso
            if (_wrongRoutine != null) { StopCoroutine(_wrongRoutine); _wrongRoutine = null; }

            _currentStepIndex = index;
            SiataDialogueStep step = _sequence.steps[index];

            if (dialogueText != null) dialogueText.text = step.npcText;

            // Limpiar estado previo del choice panel antes del nuevo paso
            UnsubscribeChoiceEvents();
            if (choicePanel != null) choicePanel.Clear();

            if (step.stepType == SiataStepType.Info)
            {
                // Generar un único botón "Continuar" sin feedback (feedbackText vacío
                // hace que MultipleChoicePanel oculte la sección de feedback automáticamente)
                _continueOption = new[] { new DialogueOption { optionText = continueButtonLabel, isCorrect = true, feedbackText = "" } };
                if (choicePanel != null)
                {
                    choicePanel.gameObject.SetActive(true);
                    SetupChoicePanel(_continueOption);
                }
            }
            else // Question
            {
                if (choicePanel != null)
                {
                    choicePanel.gameObject.SetActive(true);
                    SetupChoicePanel(step.options);
                }
            }
        }

        private void AdvanceSequence()
        {
            int next = _currentStepIndex + 1;
            if (next < _sequence.steps.Length)
                ShowStep(next);
            else
                FinishSequence();
        }

        private void FinishSequence()
        {
            if (_sequence != null && _sequence.advancesStageOnComplete)
                StageManager.Instance?.NextStage();
            Hide();
        }

        // ── Panel de opciones ─────────────────────────────────────────────────────
        private void SetupChoicePanel(DialogueOption[] options)
        {
            if (choicePanel == null)
            {
                Debug.LogWarning("[SiataCallPanel] choicePanel no asignado en el Inspector.");
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
            if (_isSequenceMode && _sequence.steps[_currentStepIndex].stepType == SiataStepType.Info)
            {
                // Paso informativo: avanzar al instante, sin delay ni feedback
                AdvanceSequence();
            }
            else
            {
                // Paso de pregunta: esperar el delay antes de avanzar
                _correctRoutine = StartCoroutine(CorrectAnswerRoutine());
            }
        }

        private void HandleRetry()
        {
            // El jugador pulsó "Intentar de nuevo" — cancelar el auto-reintento por timer.
            if (_wrongRoutine != null) { StopCoroutine(_wrongRoutine); _wrongRoutine = null; }
        }

        private void HandleWrongAnswer()
        {
            // MultipleChoicePanel muestra el feedback rojo y el botón de reintento (si está asignado).
            // Como fallback, relanzamos las opciones automáticamente tras un delay, por si el
            // retryButton no estuviera configurado en el Inspector.
            if (_wrongRoutine != null) StopCoroutine(_wrongRoutine);
            _wrongRoutine = StartCoroutine(WrongAnswerRoutine());
        }

        private IEnumerator WrongAnswerRoutine()
        {
            // Esperar el mismo delay que para la respuesta correcta para que el jugador
            // pueda leer el feedback antes de que las opciones vuelvan a aparecer.
            float delay = _isSequenceMode
                ? (_sequence != null ? _sequence.correctAnswerDelay : 1.5f)
                : (_legacyData != null ? _legacyData.correctAnswerDelay : 1.5f);

            yield return new WaitForSeconds(delay);
            _wrongRoutine = null;

            // Regenerar las opciones del paso actual (sin cerrar el panel)
            if (_isSequenceMode && _sequence != null)
            {
                SiataDialogueStep step = _sequence.steps[_currentStepIndex];
                if (step.stepType == SiataStepType.Question)
                    SetupChoicePanel(step.options);
            }
            else if (_legacyData != null)
            {
                SetupChoicePanel(_legacyData.options);
            }
        }

        private IEnumerator CorrectAnswerRoutine()
        {
            float delay = _isSequenceMode
                ? _sequence.correctAnswerDelay
                : (_legacyData != null ? _legacyData.correctAnswerDelay : 1.5f);

            yield return new WaitForSeconds(delay);

            if (_isSequenceMode)
                AdvanceSequence();
            else
            {
                if (_legacyData != null && _legacyData.advancesStageOnCorrect)
                    StageManager.Instance?.NextStage();
                Hide();
            }
        }

        // ── Input / utilidades ────────────────────────────────────────────────────
        private void BlockInput(bool block)
        {
            StageManager.Instance?.SetPlayerInputBlocked(block);
        }

        private void StopAllCoroutinesInternal()
        {
            if (_connectRoutine != null) { StopCoroutine(_connectRoutine); _connectRoutine = null; }
            if (_correctRoutine != null) { StopCoroutine(_correctRoutine); _correctRoutine = null; }
            if (_wrongRoutine != null) { StopCoroutine(_wrongRoutine); _wrongRoutine = null; }
        }
    }
}
