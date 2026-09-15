namespace SIRMED.UI
{
    using SIRMED.Gameplay.Dialogue;
    using SIRMED.Gameplay.Hotspots;
    using SIRMED.Managers;
    using TMPro;
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>
    /// InfoSlidePanel — Panel de slides secuenciales para contenido educativo mid-game.
    ///
    /// Flujo:
    ///   HotspotController.DispatchAction() → InfoSlidePanel.Show(slides, caller, advancesStage)
    ///   → jugador navega slides → último slide → "Cerrar"
    ///   → si advancesStage: StageManager.NextStage()
    ///   → caller.ClosePanel() → desbloqueo de input
    ///
    /// Jerarquía UI recomendada:
    ///   InfoSlidePanel
    ///     Overlay (Image semi-transparente)
    ///       ContentBox (Image + VerticalLayoutGroup)
    ///         SlideImage (Image) ← opcional, se oculta si el slide no tiene imagen
    ///         TitleText (TMP)
    ///         BodyScrollRect (ScrollRect)
    ///           Viewport → Content → BodyText (TMP)
    ///         NavRow (HorizontalLayoutGroup)
    ///           PrevButton (Button) ← se oculta en slide 0
    ///           Counter (TMP)      ← "1 / 3"
    ///           NextButton (Button) ← texto cambia a "Cerrar" en el último slide
    /// </summary>
    public class InfoSlidePanel : MonoBehaviour
    {
        // ── Singleton ─────────────────────────────────────────────────────────────
        public static InfoSlidePanel Instance { get; private set; }

        // ── Inspector ─────────────────────────────────────────────────────────────
        [Header("Imagen del slide (opcional)")]
        public Image slideImage;

        [Header("Textos")]
        public TextMeshProUGUI titleText;
        public TextMeshProUGUI bodyText;

        [Header("Navegación")]
        public Button prevButton;
        public Button nextButton;
        [Tooltip("TextMeshPro DENTRO del nextButton para cambiar su texto a 'Cerrar'.")]
        public TextMeshProUGUI nextButtonLabel;
        public TextMeshProUGUI counterText;

        [Header("Textos de botones")]
        public string labelNext = "Siguiente ›";
        public string labelClose = "Cerrar";

        // ── Estado interno ────────────────────────────────────────────────────────
        private InfoSlideData[] _slides;
        private HotspotController _caller;
        private bool _advancesStage;
        private int _currentIndex;

        // ── Lifecycle ─────────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            gameObject.SetActive(false);
        }

        private void Start()
        {
            if (prevButton != null) prevButton.onClick.AddListener(OnPrev);
            if (nextButton != null) nextButton.onClick.AddListener(OnNext);
        }

        // ── API pública ────────────────────────────────────────────────────────────

        /// <summary>
        /// Muestra el panel con los slides dados.
        /// </summary>
        public void Show(InfoSlideData[] slides, HotspotController caller, bool advancesStage)
        {
            if (slides == null || slides.Length == 0)
            {
                Debug.LogWarning("[InfoSlidePanel] Show() llamado sin slides. Se ignora.");
                return;
            }

            _slides = slides;
            _caller = caller;
            _advancesStage = advancesStage;
            _currentIndex = 0;

            gameObject.SetActive(true);
            BlockInput(true);
            DisplaySlide(0);
        }

        // ── Navegación ─────────────────────────────────────────────────────────────
        private void OnPrev()
        {
            if (_currentIndex > 0) DisplaySlide(_currentIndex - 1);
        }

        private void OnNext()
        {
            if (_currentIndex < _slides.Length - 1)
            {
                DisplaySlide(_currentIndex + 1);
            }
            else
            {
                Close();
            }
        }

        // ── Visualización ──────────────────────────────────────────────────────────
        private void DisplaySlide(int index)
        {
            _currentIndex = index;
            InfoSlideData data = _slides[index];

            if (titleText != null) titleText.text = data.title;
            if (bodyText != null) bodyText.text = data.body;

            // Imagen: mostrar solo si el slide la tiene
            if (slideImage != null)
            {
                bool hasImg = data.image != null;
                slideImage.gameObject.SetActive(hasImg);
                if (hasImg) slideImage.sprite = data.image;
            }

            RefreshNav();
        }

        private void RefreshNav()
        {
            bool isFirst = _currentIndex == 0;
            bool isLast = _currentIndex == _slides.Length - 1;

            // Botón Anterior: oculto en el primer slide
            if (prevButton != null)
                prevButton.gameObject.SetActive(!isFirst);

            // Botón Siguiente: texto cambia a "Cerrar" en el último slide
            if (nextButtonLabel != null)
                nextButtonLabel.text = isLast ? labelClose : labelNext;

            // Contador
            if (counterText != null)
                counterText.text = $"{_currentIndex + 1} / {_slides.Length}";
        }

        // ── Cierre ─────────────────────────────────────────────────────────────────
        private void Close()
        {
            BlockInput(false);
            gameObject.SetActive(false);

            // Avisar al hotspot que el panel cerró
            _caller?.ClosePanel();

            // Avanzar etapa si corresponde
            if (_advancesStage && StageManager.Instance != null)
                StageManager.Instance.NextStage();

            _slides = null;
            _caller = null;
        }

        // ── Helpers ────────────────────────────────────────────────────────────────
        private void BlockInput(bool block)
        {
            if (StageManager.Instance != null)
                StageManager.Instance.SetPlayerInputBlocked(block);
        }
    }
}
