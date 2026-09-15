namespace SIRMED.UI
{
    using SIRMED.Managers;
    using TMPro;
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>
    /// WelcomePanel — Panel de bienvenida con onboarding.
    ///
    /// Dos modos de slides (se puede usar uno o ambos en el mismo panel):
    ///
    ///   MODO A — Data-driven (recomendado):
    ///     Rellena el array "slideData" con título + cuerpo + imágenes opcionales,
    ///     o asigna un WelcomeSlideSet específico de la escena en "Slide Set".
    ///     El script reutiliza un único "slideTemplate" y lo puebla con cada entrada.
    ///
    ///   MODO B — GameObjects hijos (retrocompatible):
    ///     Llena el array "slides" con GameObjects existentes.
    ///     slideTemplate debe quedar vacío para que no haya conflicto.
    ///
    /// Flujo:
    ///   Intro → onboarding → "Comenzar" → GoToStage(Etapa1)
    ///
    /// Nota de portado (SATCS → SIRMED_AR): el contenido por defecto de "Controles"
    /// describía joystick/WASD/mouse-look, que no existen en AR real — se reemplazó
    /// por instrucciones de uso físico del teléfono. La recalibración manual del
    /// giroscopio al presionar "Comenzar" también se eliminó: el tracking de
    /// AR Foundation + Geospatial no necesita ese paso.
    /// </summary>
    public class WelcomePanel : MonoBehaviour
    {
        // ── Slide data (Modo A) ───────────────────────────────────────────────────
        [System.Serializable]
        public class WelcomeSlideData
        {
            [Tooltip("Título del slide.")]
            public string title;

            [TextArea(3, 8)]
            [Tooltip("Texto principal del slide.")]
            public string body;

            [Tooltip("Imagen ilustrativa (diagrama de controles, mapa, etc.). Opcional.")]
            public Sprite image;
        }

        // ── Slide Set (override por escena) ──────────────────────────────────────
        [Header("Slide Set — override por escena")]
        [Tooltip(
            "ScriptableObject con los slides de esta escena. " +
            "Si está asignado, REEMPLAZA el array 'slideData' de abajo. " +
            "Crear via Assets > Create > AR > Welcome Slide Set. " +
            "Dejar vacío para usar el array inline (retrocompat).")]
        [SerializeField] private WelcomeSlideSet _slideSet;

        // ── Propiedad interna: devuelve el array activo según si hay SlideSet asignado ──
        private WelcomeSlideData[] ActiveSlideData =>
            (_slideSet != null && _slideSet.slides != null && _slideSet.slides.Length > 0)
                ? _slideSet.slides
                : slideData;

        [Header("Slides — Modo A: datos en Inspector")]
        [Tooltip("Define aquí el contenido de cada slide. Solo se usa si 'Slide Set' está vacío.")]
        public WelcomeSlideData[] slideData = new WelcomeSlideData[]
        {
            new WelcomeSlideData
            {
                title = "Controles",
                body =
                    "Esta experiencia usa Realidad Aumentada real:\n\n" +
                    "· Camina físicamente por el sector para moverte — no hay joystick.\n" +
                    "· Apunta la cámara del teléfono al entorno para mirar alrededor.\n" +
                    "· Toca el botón que aparece en pantalla cuando estés cerca de un punto de interés.\n\n" +
                    "📱 Antes de presionar COMENZAR:\n" +
                    "Sostén el teléfono frente a ti, en posición vertical, apuntando hacia el entorno."
            },
            new WelcomeSlideData
            {
                title = "¿Qué es el SATC?",
                body =
                    "El Sistema de Alerta Temprana Comunitaria (SATC) " +
                    "te permite conocer el nivel de riesgo de tu barrio " +
                    "ante crecientes súbitas de la quebrada.\n\n" +
                    "En esta experiencia aprenderás a reconocer las señales " +
                    "de alerta, a comunicarte con el SIATA y a actuar como " +
                    "líder comunitario ante una emergencia hidrológica."
            }
        };

        [Header("Plantilla UI (Modo A)")]
        [Tooltip("GameObject que contiene los elementos de texto/imagen del slide. Se puebla con cada entrada de slideData.")]
        public GameObject slideTemplate;

        public TextMeshProUGUI slideTitle;
        public TextMeshProUGUI slideBody;
        public Image slideImage;

        // ── Slides — Modo B (retrocompatible) ────────────────────────────────────
        [Header("Slides — Modo B: GameObjects hijos")]
        [Tooltip("Array de GameObjects ya diseñados. Solo se usa si slideTemplate está vacío.")]
        public GameObject[] slides;

        // ── Navegación ────────────────────────────────────────────────────────────
        [Header("Botones de navegación")]
        public Button previousButton;
        public Button nextButton;

        [Tooltip("Solo visible en el último slide.")]
        public Button startButton;

        [Header("Indicador de progreso (opcional)")]
        public TextMeshProUGUI slideCounterText;

        [Header("Debug")]
        [Tooltip("Salta el onboarding directamente. Útil en editor.")]
        public Button skipButton;

        // ── Internos ──────────────────────────────────────────────────────────────
        private int _currentSlide;
        private bool _usingDataMode;   // true = Modo A, false = Modo B
        private int SlideCount
        {
            get
            {
                if (_usingDataMode)
                    return (ActiveSlideData != null && ActiveSlideData.Length > 0) ? ActiveSlideData.Length : 1;
                return (slides != null) ? slides.Length : 0;
            }
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────────
        private void Start()
        {
            // Modo A si hay plantilla asignada, independiente de si slideData tiene contenido.
            // (Unity no aplica defaults de código a componentes ya serializados, por eso
            //  no se puede depender de slideData.Length > 0 como condición.)
            _usingDataMode = (slideTemplate != null);

            BlockPlayerInput(true);

            if (previousButton != null) previousButton.onClick.AddListener(OnPrevious);
            if (nextButton != null) nextButton.onClick.AddListener(OnNext);
            if (startButton != null) startButton.onClick.AddListener(OnStart);
            if (skipButton != null) skipButton.onClick.AddListener(OnStart);

            ShowSlide(0);
        }

        // ── Navegación ────────────────────────────────────────────────────────────
        private void OnPrevious()
        {
            if (_currentSlide > 0) ShowSlide(_currentSlide - 1);
        }

        private void OnNext()
        {
            if (_currentSlide < SlideCount - 1) ShowSlide(_currentSlide + 1);
        }

        private void OnStart()
        {
            BlockPlayerInput(false);

            if (StageManager.Instance != null)
                StageManager.Instance.GoToStage(StageManager.Stage.Etapa1);

            gameObject.SetActive(false);
        }

        // ── Visualización ─────────────────────────────────────────────────────────
        private void ShowSlide(int index)
        {
            if (SlideCount == 0) return;

            _currentSlide = Mathf.Clamp(index, 0, SlideCount - 1);

            if (_usingDataMode)
                PopulateTemplate(_currentSlide);
            else
                ActivateSlideObject(_currentSlide);

            RefreshButtons();
            UpdateCounter();
        }

        /// <summary>Modo A — puebla la plantilla con los datos del slide.</summary>
        private void PopulateTemplate(int index)
        {
            // Asegurar que la plantilla esté visible
            slideTemplate.SetActive(true);

            // Desactivar slides del Modo B para que no se solapen
            if (slides != null)
                foreach (var s in slides)
                    if (s != null && s != slideTemplate) s.SetActive(false);

            WelcomeSlideData[] active = ActiveSlideData;
            if (active == null || index >= active.Length) return;

            WelcomeSlideData data = active[index];

            if (slideTitle != null) slideTitle.text = data.title;
            if (slideBody != null) slideBody.text = data.body;

            if (slideImage != null)
            {
                bool hasImage = data.image != null;
                slideImage.gameObject.SetActive(hasImage);
                if (hasImage) slideImage.sprite = data.image;
            }
        }

        /// <summary>Modo B — activa solo el GameObject correspondiente.</summary>
        private void ActivateSlideObject(int index)
        {
            for (int i = 0; i < slides.Length; i++)
                if (slides[i] != null) slides[i].SetActive(i == index);
        }

        private void RefreshButtons()
        {
            bool isFirst = _currentSlide == 0;
            bool isLast = _currentSlide >= SlideCount - 1;

            if (previousButton != null) previousButton.gameObject.SetActive(!isFirst);
            if (nextButton != null) nextButton.gameObject.SetActive(!isLast);
            if (startButton != null) startButton.gameObject.SetActive(isLast);
        }

        private void UpdateCounter()
        {
            if (slideCounterText == null) return;
            slideCounterText.text = $"{_currentSlide + 1} / {SlideCount}";
        }

        // ── Bloqueo de input ──────────────────────────────────────────────────────
        private void BlockPlayerInput(bool block)
        {
            StageManager.Instance?.SetPlayerInputBlocked(block);
        }

        // ── API pública ───────────────────────────────────────────────────────────
        /// <summary>Muestra el panel desde código (ej: botón "Ver controles" en pausa).</summary>
        public void Show()
        {
            gameObject.SetActive(true);
            BlockPlayerInput(true);
            ShowSlide(0);
        }
    }
}
