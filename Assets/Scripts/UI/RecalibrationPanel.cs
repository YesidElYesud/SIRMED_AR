namespace SIRMED.UI
{
    using System.Collections;
    using SIRMED.Managers;
    using TMPro;
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>
    /// RecalibrationPanel — Panel grande para recalibrar las anclas (se abre desde Ajustes).
    ///
    /// Flujo:
    ///   SettingsPanel "Recalibrar" → Open() → vista Intro (imagen a pantalla completa +
    ///   botón "Recalibrar") → GeoAnchorManager.Recalibrate() → vista Progreso (banner
    ///   pequeño: la cámara queda visible para apuntar a fachadas) con la precisión actual
    ///   → resultado (éxito / sin precisión) unos segundos → se cierra solo.
    ///
    /// Jerarquía sugerida:
    ///   RecalibrationPanel            [este script]  (ACTIVO en la escena; Awake lo oculta)
    ///   ├── IntroRoot                 ← _introRoot
    ///   │   ├── Imagen                [Image, stretch]
    ///   │   ├── RecalibrateButton     [Button]        ← _recalibrateButton
    ///   │   ├── NoteText              [TMP]           ← _introNoteText (opcional: cooldown, etc.)
    ///   │   └── CloseButton           [Button]        ← _closeButton
    ///   └── ProgressRoot              ← _progressRoot (banner pequeño, NO tapa la cámara)
    ///       └── ProgressText          [TMP]           ← _progressText
    /// </summary>
    public class RecalibrationPanel : MonoBehaviour
    {
        public static RecalibrationPanel Instance { get; private set; }

        // ── Referencias ────────────────────────────────────────────────────────────
        [Header("Vista Intro (imagen + botón)")]
        [SerializeField] private GameObject _introRoot;
        [SerializeField] private Button _recalibrateButton;
        [SerializeField] private Button _closeButton;
        [Tooltip("Opcional: explica por qué el botón no está disponible (cooldown, anclaje inicial).")]
        [SerializeField] private TMP_Text _introNoteText;

        [Header("Vista Progreso (banner, deja ver la cámara)")]
        [SerializeField] private GameObject _progressRoot;
        [SerializeField] private TMP_Text _progressText;

        [Header("HUD — ocultar mientras el panel está abierto")]
        [SerializeField] private GameObject[] _hudElementsToHide;

        [Header("Tiempos")]
        [Tooltip("Segundos que se muestra el resultado antes de cerrar.")]
        [SerializeField] private float _resultSeconds = 2.5f;

        [Header("Mensajes ({0} = precisión actual, {1} = requerida, en metros)")]
        [SerializeField, TextArea] private string _msgWaiting =
            "Apunta la cámara a fachadas y calles cercanas y muévela despacio.\nPrecisión: {0:F1} m (se necesitan {1:F0} m)";
        [SerializeField, TextArea] private string _msgWaitingNoTracking =
            "Buscando tu ubicación… apunta la cámara a fachadas y calles cercanas.";
        [SerializeField, TextArea] private string _msgAnchoring = "Reubicando los puntos…";
        [SerializeField, TextArea] private string _msgSuccess = "¡Listo! Los puntos se reubicaron.";
        [SerializeField, TextArea] private string _msgFailed =
            "No se logró suficiente precisión. Intenta de nuevo con más edificios a la vista.";
        [Tooltip("{0} = segundos restantes.")]
        [SerializeField, TextArea] private string _msgCooldown = "Podrás recalibrar de nuevo en {0:F0} s.";
        [SerializeField, TextArea] private string _msgInitialPending = "Los puntos aún se están ubicando por primera vez.";
        [SerializeField, TextArea] private string _msgUnavailable = "La recalibración no está disponible en esta escena.";

        // ── Estado interno ─────────────────────────────────────────────────────────
        private bool _waitingResult;

        // ── Lifecycle ──────────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            _recalibrateButton?.onClick.AddListener(OnRecalibrateClicked);
            _closeButton?.onClick.AddListener(Close);
            gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (GeoAnchorManager.Instance != null)
                GeoAnchorManager.Instance.RecalibrationFinished -= OnRecalibrationFinished;
        }

        private void Update()
        {
            GeoAnchorManager manager = GeoAnchorManager.Instance;

            if (_waitingResult)
            {
                if (manager == null) return;
                if (manager.CurrentState == GeoAnchorManager.State.Anchoring)
                    SetProgress(_msgAnchoring);
                else if (double.IsNaN(manager.CurrentHorizontalAccuracy))
                    SetProgress(_msgWaitingNoTracking);
                else
                    SetProgress(string.Format(_msgWaiting, manager.CurrentHorizontalAccuracy,
                                              manager.RequiredHorizontalAccuracy));
                return;
            }

            if (_introRoot != null && _introRoot.activeSelf)
                RefreshIntro(manager);
        }

        // ── API pública ────────────────────────────────────────────────────────────
        public void Open()
        {
            if (gameObject.activeSelf) return;

            gameObject.SetActive(true);
            HudHider.Hide(this, _hudElementsToHide);
            BlockInput(true);
            ShowIntro();
        }

        public void Close()
        {
            if (_waitingResult) return; // no se cierra a mitad de una recalibración
            StopAllCoroutines();
            BlockInput(false);
            HudHider.Restore(this);
            gameObject.SetActive(false);
        }

        // ── Vistas ─────────────────────────────────────────────────────────────────
        private void ShowIntro()
        {
            if (_introRoot != null) _introRoot.SetActive(true);
            if (_progressRoot != null) _progressRoot.SetActive(false);
            RefreshIntro(GeoAnchorManager.Instance);
        }

        private void ShowProgress()
        {
            if (_introRoot != null) _introRoot.SetActive(false);
            if (_progressRoot != null) _progressRoot.SetActive(true);
        }

        private void RefreshIntro(GeoAnchorManager manager)
        {
            string note;
            if (manager == null || !manager.isActiveAndEnabled) note = _msgUnavailable;
            else if (manager.IsWaitingForInitialAnchors) note = _msgInitialPending;
            else if (manager.CooldownRemaining > 0f) note = string.Format(_msgCooldown, Mathf.Ceil(manager.CooldownRemaining));
            else note = string.Empty;

            if (_recalibrateButton != null)
                _recalibrateButton.interactable = manager != null && manager.isActiveAndEnabled && manager.CanRecalibrate;

            if (_introNoteText != null)
            {
                _introNoteText.gameObject.SetActive(!string.IsNullOrEmpty(note));
                _introNoteText.text = note;
            }
        }

        private void SetProgress(string message)
        {
            if (_progressText != null && _progressText.text != message)
                _progressText.text = message;
        }

        // ── Recalibración ──────────────────────────────────────────────────────────
        private void OnRecalibrateClicked()
        {
            GeoAnchorManager manager = GeoAnchorManager.Instance;
            if (manager == null) return;

            manager.RecalibrationFinished -= OnRecalibrationFinished;
            manager.RecalibrationFinished += OnRecalibrationFinished;
            if (!manager.Recalibrate())
            {
                manager.RecalibrationFinished -= OnRecalibrationFinished;
                RefreshIntro(manager);
                return;
            }

            _waitingResult = true;
            ShowProgress();
        }

        private void OnRecalibrationFinished(bool success)
        {
            if (GeoAnchorManager.Instance != null)
                GeoAnchorManager.Instance.RecalibrationFinished -= OnRecalibrationFinished;

            _waitingResult = false;
            SetProgress(success ? _msgSuccess : _msgFailed);
            if (gameObject.activeInHierarchy)
                StartCoroutine(CloseAfterDelay());
        }

        private IEnumerator CloseAfterDelay()
        {
            yield return new WaitForSecondsRealtime(_resultSeconds);
            Close();
        }

        // ── Helpers ────────────────────────────────────────────────────────────────
        private static void BlockInput(bool block)
        {
            if (StageManager.Instance != null)
                StageManager.Instance.SetPlayerInputBlocked(block);
        }
    }
}
