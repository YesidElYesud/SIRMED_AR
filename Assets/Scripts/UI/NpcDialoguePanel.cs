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
    /// NpcDialoguePanel — Panel de conversación con NPC, paginado, con audio de voz
    /// y hasta 4 opciones de respuesta en la última línea.
    ///
    /// Jerarquía sugerida en escena:
    ///   NpcDialoguePanel            [este script]
    ///   ├── OptionsGrid             [Image, VLG]              ← _optionsContainer (oculto en modo statement)
    ///   │   ├── Pregunta            [HLG]
    ///   │   │   └── Text (TMP)                               ← _preguntaText (enunciado sobre botones)
    ///   │   └── grid                [GridLayoutGroup 2 cols]
    ///   │       ├── OptionBtn_0..3  [Button + Image + TMP]   ← _optionButtons[0..3]
    ///   └── Dialogo                 [Image, HLG]              (siempre visible)
    ///       ├── Text (TMP)                                    ← _dialogueText
    ///       └── contiBTN            [Button]                  ← _continueButton (▶)
    ///
    /// Modos:
    ///   • Statement: OptionsGrid oculto, ▶ activo → avanza línea o cierra.
    ///   • Question:  OptionsGrid visible, ▶ deshabilitado → el jugador responde via botones.
    /// </summary>
    public class NpcDialoguePanel : MonoBehaviour
    {
        // ── Singleton ─────────────────────────────────────────────────────────────
        public static NpcDialoguePanel Instance { get; private set; }

        // ── Panel Dialogo (solo texto + continuar) ─────────────────────────────────
        [Header("Panel Dialogo (sin opciones)")]
        [SerializeField] private GameObject _dialogoContainer;
        [SerializeField] private TextMeshProUGUI _dialogueText;
        [SerializeField] private Button _continueButton;

        // ── Cuadrícula de opciones ─────────────────────────────────────────────────
        [Header("Cuadrícula de opciones")]
        [SerializeField] private GameObject _optionsContainer;
        [SerializeField] private TextMeshProUGUI _preguntaText;
        [Tooltip("Exactamente 4 botones en orden A), B), C), D). Los sobrantes se ocultan si hay menos opciones.")]
        [SerializeField] private Button[] _optionButtons = new Button[4];

        // ── Colores de feedback ────────────────────────────────────────────────────
        [Header("Colores de feedback")]
        [SerializeField] private Color _colorDefault = new Color(1.00f, 0.80f, 0.00f, 1f); // amarillo
        [SerializeField] private Color _colorCorrect = new Color(0.13f, 0.69f, 0.30f, 1f); // verde
        [SerializeField] private Color _colorWrong = new Color(0.86f, 0.20f, 0.18f, 1f); // rojo

        [Header("Comportamiento")]
        [Range(0.3f, 3f)]
        [SerializeField] private float _feedbackDuration = 1.2f;

        // ── Info NPC (opcional) ────────────────────────────────────────────────────
        [Header("Info NPC (opcional)")]
        [SerializeField] private Image _npcPhoto;
        [SerializeField] private TextMeshProUGUI _npcNameText;

        // ── HUD ───────────────────────────────────────────────────────────────────
        [Header("HUD — ocultar durante el diálogo")]
        [SerializeField] private GameObject[] _hudElementsToHide;

        // ── Audio de voz ──────────────────────────────────────────────────────────
        [Header("Audio de voz del NPC")]
        [SerializeField] private AudioSource _voiceSource;
        [Range(0f, 1f)]
        [SerializeField] private float _ambientDuckVolume = 0.15f;
        [Range(0f, 2f)]
        [SerializeField] private float _duckFadeDuration = 0.4f;

        // ── Estado interno ─────────────────────────────────────────────────────────
        private NpcDialogueData _currentData;
        private IHotspotInteractable _sourceHotspot;
        private System.Action _onCorrectCallback;
        private string[] _lines;
        private AudioClip[] _lineAudios; // paralelo a _lines; puede ser null
        private int _lineIndex;
        private Coroutine _feedbackRoutine;
        private bool[] _hudWasActive;

        private static readonly string[] _prefixes = { "A)", "B)", "C)", "D)" };

        // ── Lifecycle ─────────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            gameObject.SetActive(false);

            // Auto-crear AudioSource de voz si no está asignado en Inspector
            if (_voiceSource == null)
            {
                _voiceSource = gameObject.AddComponent<AudioSource>();
                _voiceSource.playOnAwake = false;
                _voiceSource.loop = false;
                _voiceSource.spatialBlend = 0f; // 2D — la voz es siempre omnidireccional
                _voiceSource.volume = 1f;
            }
        }

        private void Start()
        {
            if (_continueButton != null)
                _continueButton.onClick.AddListener(OnContinueClicked);
        }

        // ── API pública ───────────────────────────────────────────────────────────

        /// <summary>
        /// Abre el panel con el diálogo indicado.
        /// Llamado desde HotspotController (o cualquier IHotspotInteractable).
        /// </summary>
        public void Show(NpcDialogueData data, IHotspotInteractable source, System.Action onCorrect = null)
        {
            if (data == null)
            {
                Debug.LogWarning("[NpcDialoguePanel] NpcDialogueData es null.");
                return;
            }

            _currentData = data;
            _sourceHotspot = source;
            _onCorrectCallback = onCorrect;

            // Prioridad: dialogueEntries (texto + audio) → dialogueLines (solo texto) → npcText
            bool hasEntries = data.dialogueEntries != null && data.dialogueEntries.Length > 0;
            bool hasLines = data.dialogueLines != null && data.dialogueLines.Length > 0;

            if (hasEntries)
            {
                _lines = new string[data.dialogueEntries.Length];
                _lineAudios = new AudioClip[data.dialogueEntries.Length];
                for (int i = 0; i < data.dialogueEntries.Length; i++)
                {
                    _lines[i] = data.dialogueEntries[i].text;
                    _lineAudios[i] = data.dialogueEntries[i].audio;
                }
            }
            else
            {
                _lines = hasLines ? data.dialogueLines : new[] { data.npcText };
                _lineAudios = null;
            }

            _lineIndex = 0;

            PopulateNpcInfo();
            SaveAndHideHud();
            ScreenBackground.Instance?.SetSuppressed(true);

            // Activar el GO ANTES de ShowLine: AudioSource.Play() es ignorado en GOs inactivos,
            // por lo que el clip de la primera línea no sonaría si SetActive(true) va después.
            gameObject.SetActive(true);
            BlockInput(true);

            // Bajar el volumen del ambiente para que la voz del NPC se oiga claramente
            AudioStageManager.Instance?.DuckAmbient(_ambientDuckVolume, _duckFadeDuration);

            ShowLine(0);
        }

        /// <summary>Cierra el panel y desbloquea el input.</summary>
        public void Hide()
        {
            if (_feedbackRoutine != null) { StopCoroutine(_feedbackRoutine); _feedbackRoutine = null; }

            // Detener audio de voz y restaurar volumen ambiente
            StopVoice();
            AudioStageManager.Instance?.RestoreVolume(_duckFadeDuration);

            ResetOptionColors();
            if (_optionsContainer != null) _optionsContainer.SetActive(false);
            if (_dialogoContainer != null) _dialogoContainer.SetActive(true);

            RestoreHud();
            ScreenBackground.Instance?.SetSuppressed(false);

            BlockInput(false);

            if (_sourceHotspot != null)
            {
                _sourceHotspot.ClosePanel();
                _sourceHotspot = null;
            }

            _currentData = null;
            _onCorrectCallback = null;
            _lineAudios = null;
            gameObject.SetActive(false);
        }

        // ── Paginación ────────────────────────────────────────────────────────────

        private void ShowLine(int index)
        {
            if (_lines == null || index < 0 || index >= _lines.Length) return;

            if (_feedbackRoutine != null) { StopCoroutine(_feedbackRoutine); _feedbackRoutine = null; }

            _lineIndex = index;
            if (_dialogueText != null) _dialogueText.text = _lines[index];

            // Reproducir audio de esta línea (corta el anterior si aún sonaba)
            PlayVoice(index);

            bool isLast = index >= _lines.Length - 1;
            bool hasOpts = HasOptions();

            if (isLast && hasOpts)
                EnterQuestionMode();
            else
                EnterStatementMode();
        }

        // ── Audio de voz ──────────────────────────────────────────────────────────

        /// <summary>
        /// Detiene la voz anterior (si la hay) y reproduce el clip de la línea indicada.
        /// Si la línea no tiene clip asignado, simplemente para el audio anterior.
        /// </summary>
        private void PlayVoice(int lineIndex)
        {
            if (_voiceSource == null) return;

            _voiceSource.Stop();

            AudioClip clip = (_lineAudios != null && lineIndex < _lineAudios.Length)
                ? _lineAudios[lineIndex]
                : null;

            if (clip == null) return;

            _voiceSource.clip = clip;
            _voiceSource.Play();
        }

        private void StopVoice()
        {
            if (_voiceSource != null && _voiceSource.isPlaying)
                _voiceSource.Stop();
        }

        // ── Modos de UI ───────────────────────────────────────────────────────────

        private void EnterStatementMode()
        {
            if (_dialogoContainer != null) _dialogoContainer.SetActive(true);
            if (_optionsContainer != null) _optionsContainer.SetActive(false);
            if (_continueButton != null) _continueButton.interactable = true;
        }

        private void EnterQuestionMode()
        {
            if (_dialogoContainer != null) _dialogoContainer.SetActive(false);

            if (_preguntaText != null && _lines != null)
                _preguntaText.text = _lines[_lineIndex];

            SetupOptionButtons(_currentData.options);
            if (_optionsContainer != null) _optionsContainer.SetActive(true);
        }

        // ── Botones de opción ──────────────────────────────────────────────────────

        private void SetupOptionButtons(DialogueOption[] options)
        {
            ResetOptionColors();

            for (int i = 0; i < _optionButtons.Length; i++)
            {
                if (_optionButtons[i] == null) continue;

                bool visible = options != null && i < options.Length;
                _optionButtons[i].gameObject.SetActive(visible);
                if (!visible) continue;

                _optionButtons[i].interactable = true;

                var label = _optionButtons[i].GetComponentInChildren<TextMeshProUGUI>();
                if (label != null)
                    label.text = _prefixes[i] + " " + options[i].optionText;

                int captured = i;
                _optionButtons[i].onClick.RemoveAllListeners();
                _optionButtons[i].onClick.AddListener(() => OnOptionClicked(captured));
            }
        }

        private void OnOptionClicked(int index)
        {
            if (_currentData?.options == null || index >= _currentData.options.Length) return;

            DialogueOption opt = _currentData.options[index];

            SetOptionsInteractable(false);
            SetButtonColor(index, opt.isCorrect ? _colorCorrect : _colorWrong);

            _feedbackRoutine = opt.isCorrect
                ? StartCoroutine(CorrectAnswerRoutine())
                : StartCoroutine(WrongAnswerRoutine());
        }

        private IEnumerator CorrectAnswerRoutine()
        {
            yield return new WaitForSeconds(_feedbackDuration);

            if (_currentData != null && _currentData.advancesStageOnCorrect)
                StageManager.Instance?.NextStage();

            System.Action cb = _onCorrectCallback;
            Hide();
            cb?.Invoke();
        }

        private IEnumerator WrongAnswerRoutine()
        {
            yield return new WaitForSeconds(_feedbackDuration);
            _feedbackRoutine = null;

            // Resetear colores y permitir reintento
            if (_currentData?.options != null)
                SetupOptionButtons(_currentData.options);
        }

        // ── Botón continuar ────────────────────────────────────────────────────────

        private void OnContinueClicked()
        {
            if (!IsOnLastLine())
            {
                _lineIndex++;
                ShowLine(_lineIndex);
            }
            else
            {
                Hide(); // última línea sin opciones → cerrar
            }
        }

        // ── Colores ───────────────────────────────────────────────────────────────

        private void SetButtonColor(int index, Color color)
        {
            if (index < 0 || index >= _optionButtons.Length || _optionButtons[index] == null) return;
            var img = _optionButtons[index].GetComponent<Image>();
            if (img != null) img.color = color;
        }

        private void ResetOptionColors()
        {
            for (int i = 0; i < _optionButtons.Length; i++)
                SetButtonColor(i, _colorDefault);
        }

        private void SetOptionsInteractable(bool interactable)
        {
            foreach (var btn in _optionButtons)
                if (btn != null) btn.interactable = interactable;
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private bool HasOptions() =>
            _currentData?.options != null && _currentData.options.Length > 0;

        private bool IsOnLastLine() =>
            _lines == null || _lineIndex >= _lines.Length - 1;

        // ── NPC Info ──────────────────────────────────────────────────────────────

        private void PopulateNpcInfo()
        {
            if (_currentData == null) return;

            bool hasPhoto = _currentData.npcPhoto != null;
            if (_npcPhoto != null)
            {
                _npcPhoto.gameObject.SetActive(hasPhoto);
                if (hasPhoto) _npcPhoto.sprite = _currentData.npcPhoto;
            }

            if (_npcNameText != null) _npcNameText.text = _currentData.npcName;
        }

        // ── HUD ───────────────────────────────────────────────────────────────────

        private void SaveAndHideHud()
        {
            if (_hudElementsToHide == null || _hudElementsToHide.Length == 0) return;

            _hudWasActive = new bool[_hudElementsToHide.Length];
            for (int i = 0; i < _hudElementsToHide.Length; i++)
            {
                if (_hudElementsToHide[i] == null) continue;
                _hudWasActive[i] = _hudElementsToHide[i].activeSelf;
                _hudElementsToHide[i].SetActive(false);
            }
        }

        private void RestoreHud()
        {
            if (_hudElementsToHide == null || _hudWasActive == null) return;

            for (int i = 0; i < _hudElementsToHide.Length; i++)
            {
                if (_hudElementsToHide[i] == null) continue;
                if (i < _hudWasActive.Length)
                    _hudElementsToHide[i].SetActive(_hudWasActive[i]);
            }
            _hudWasActive = null;
        }

        // ── Input ─────────────────────────────────────────────────────────────────

        private void BlockInput(bool block) =>
            StageManager.Instance?.SetPlayerInputBlocked(block);
    }
}
