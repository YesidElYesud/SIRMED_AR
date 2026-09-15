namespace SIRMED.UI
{
    using System;
    using SIRMED.Gameplay.Hotspots;
    using SIRMED.Managers;
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>
    /// RiskLevelIndicator — HUD de nivel de riesgo N1–N4.
    ///
    /// Estructura esperada en escena:
    ///
    ///   RiskLevelIndicator          ← este script
    ///   └── IndicatorRoot           ← indicatorRoot
    ///       ├── FondoBtn            ← Button + Image; al pulsar abre RiskLevelPanel
    ///       │   └── LevelImage      ← Image que muestra el sprite del nivel (levelImage)
    ///       ├── BtnN1               ← Button; al pulsar → SetLevel(N1)
    ///       ├── BtnN2               ← Button; al pulsar → SetLevel(N2)
    ///       ├── BtnN3               ← Button; al pulsar → SetLevel(N3)
    ///       └── BtnN4               ← Button; al pulsar → SetLevel(N4)
    ///
    ///   RiskLevelPanel              ← panelRoot (puede vivir fuera de IndicatorRoot)
    ///       ├── PanelImage          ← Image de fondo (cambia con el nivel)
    ///       └── ExitButton          ← Button; cierra el panel
    /// </summary>
    public class RiskLevelIndicator : MonoBehaviour
    {
        // ── Singleton ─────────────────────────────────────────────────────────────
        public static RiskLevelIndicator Instance { get; private set; }

        // ── Nivel por defecto por etapa ───────────────────────────────────────────
        [Serializable]
        public class StageRiskConfig
        {
            [Tooltip("Etapa a la que aplica este nivel por defecto.")]
            public StageManager.Stage stage;

            [Tooltip("Nivel de riesgo a mostrar al entrar a esta etapa.\n" +
                     "None = limpiar el indicador al entrar a la etapa.")]
            public RiskLevel defaultRiskLevel = RiskLevel.None;
        }

        [Header("Nivel de riesgo por etapa (opcional)")]
        [Tooltip("Al entrar a cada etapa configurada, el indicador muestra el nivel asignado.")]
        public StageRiskConfig[] stageRiskDefaults = new StageRiskConfig[]
        {
            new StageRiskConfig { stage = StageManager.Stage.Intro,  defaultRiskLevel = RiskLevel.None },
            new StageRiskConfig { stage = StageManager.Stage.Etapa1, defaultRiskLevel = RiskLevel.N1 },
            new StageRiskConfig { stage = StageManager.Stage.Etapa2, defaultRiskLevel = RiskLevel.N2 },
            new StageRiskConfig { stage = StageManager.Stage.Etapa3, defaultRiskLevel = RiskLevel.N3 },
            new StageRiskConfig { stage = StageManager.Stage.Etapa4, defaultRiskLevel = RiskLevel.N4 },
            new StageRiskConfig { stage = StageManager.Stage.Etapa5, defaultRiskLevel = RiskLevel.N1 },
        };

        // ── Referencias UI — Indicador ────────────────────────────────────────────
        [Header("Indicador HUD")]
        [Tooltip("GameObject raíz del widget. Se activa con nivel ≠ None.")]
        public GameObject indicatorRoot;

        [Tooltip("Image del nivel activo dentro de FondoBtn.")]
        public Image levelImage;

        [Tooltip("Botón FondoBtn. Al pulsarlo abre el RiskLevelPanel.")]
        public Button fondoBtn;

        // ── Referencias UI — Botones de nivel ────────────────────────────────────
        [Header("Botones de nivel (junto a FondoBtn)")]
        public Button btnN1;
        public Button btnN2;
        public Button btnN3;
        public Button btnN4;

        // ── Referencias UI — Panel de riesgo ─────────────────────────────────────
        [Header("RiskLevelPanel")]
        [Tooltip("Panel que se abre al pulsar FondoBtn. Contiene PanelImage + ExitButton.")]
        public GameObject panelRoot;

        [Tooltip("Image dentro del panel que muestra la imagen del nivel actual.")]
        public Image panelImage;

        public Button panelExitButton;

        // ── Sprites indicador (pequeños, en HUD) ─────────────────────────────────
        [Header("Sprites indicador (HUD)")]
        public Sprite spriteN1;
        public Sprite spriteN2;
        public Sprite spriteN3;
        public Sprite spriteN4;

        // ── Sprites panel (tamaño completo) ───────────────────────────────────────
        [Header("Sprites panel (imagen fullscreen/grande)")]
        [Tooltip("Si quedan vacíos se usarán los sprites del indicador como fallback.")]
        public Sprite panelSpriteN1;
        public Sprite panelSpriteN2;
        public Sprite panelSpriteN3;
        public Sprite panelSpriteN4;

        // ── Animación ─────────────────────────────────────────────────────────────
        [Header("Animación")]
        [Range(0f, 5f)]
        public float pulseSpeedN3 = 1.5f;
        [Range(0f, 5f)]
        public float pulseSpeedN4 = 2.5f;
        [Range(0f, 0.2f)]
        public float pulseAmplitude = 0.07f;

        // ── Estado ────────────────────────────────────────────────────────────────
        public RiskLevel CurrentLevel { get; private set; } = RiskLevel.None;

        private Vector3 _baseScale;

        // ── Lifecycle ─────────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            if (indicatorRoot != null)
                _baseScale = indicatorRoot.transform.localScale;
        }

        private void Start()
        {
            if (StageManager.Instance != null)
                StageManager.Instance.OnStageChanged += OnStageChanged;

            // Auto-cablear si no están asignados en el Inspector
            AutoWireButtons();

            // Botón principal → abre el panel
            if (fondoBtn != null)
                fondoBtn.onClick.AddListener(OpenPanel);

            // Botón salir del panel
            if (panelExitButton != null)
                panelExitButton.onClick.AddListener(ClosePanel);

            // Botones de nivel → cambian la etapa completa del juego (dispara todos los efectos)
            if (btnN1 != null) btnN1.onClick.AddListener(() => GoToStageForLevel(RiskLevel.N1));
            if (btnN2 != null) btnN2.onClick.AddListener(() => GoToStageForLevel(RiskLevel.N2));
            if (btnN3 != null) btnN3.onClick.AddListener(() => GoToStageForLevel(RiskLevel.N3));
            if (btnN4 != null) btnN4.onClick.AddListener(() => GoToStageForLevel(RiskLevel.N4));

            // Ocultar indicador y panel al arrancar
            if (indicatorRoot != null) indicatorRoot.SetActive(false);
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        // Busca los botones por nombre en la jerarquía si el Inspector los dejó vacíos.
        private void AutoWireButtons()
        {
            if (indicatorRoot == null) return;
            var root = indicatorRoot.transform;

            if (fondoBtn == null)
                fondoBtn = root.Find("FondoBtn")?.GetComponent<Button>();

            // Los 4 botones de nivel viven dentro de un hijo llamado "Orden"
            Transform orden = root.Find("Orden");
            Transform search = orden != null ? orden : root;

            if (btnN1 == null) btnN1 = search.Find("N1_Btn")?.GetComponent<Button>();
            if (btnN2 == null) btnN2 = search.Find("N2_Btn")?.GetComponent<Button>();
            if (btnN3 == null) btnN3 = search.Find("N3_Btn")?.GetComponent<Button>();
            if (btnN4 == null) btnN4 = search.Find("N4_Btn")?.GetComponent<Button>();
        }

        // Llama GoToStage para el nivel solicitado y dispara todos los efectos del juego.
        private void GoToStageForLevel(RiskLevel level)
        {
            StageManager.Stage stage = level switch
            {
                RiskLevel.N1 => StageManager.Stage.Etapa1,
                RiskLevel.N2 => StageManager.Stage.Etapa2,
                RiskLevel.N3 => StageManager.Stage.Etapa3,
                RiskLevel.N4 => StageManager.Stage.Etapa4,
                _ => StageManager.Stage.Etapa1,
            };
            StageManager.Instance?.GoToStage(stage);
        }

        private void OnDestroy()
        {
            if (StageManager.Instance != null)
                StageManager.Instance.OnStageChanged -= OnStageChanged;
        }

        private void Update()
        {
            ApplyPulse();
        }

        // ── API pública ───────────────────────────────────────────────────────────

        /// <summary>
        /// Establece el nivel de riesgo activo.
        /// Actualiza el sprite del indicador y, si el panel está abierto, también la imagen del panel.
        /// None oculta el widget.
        /// </summary>
        public void SetLevel(RiskLevel level)
        {
            CurrentLevel = level;

            bool visible = level != RiskLevel.None;
            if (indicatorRoot != null) indicatorRoot.SetActive(visible);

            if (!visible)
            {
                ClosePanel();
                return;
            }

            if (levelImage != null) levelImage.sprite = GetIndicatorSprite(level);

            // Si el panel ya está abierto, actualizar su imagen en tiempo real
            if (panelRoot != null && panelRoot.activeSelf)
                RefreshPanelImage();
        }

        /// <summary>Oculta el widget. Equivale a SetLevel(None).</summary>
        public void ClearLevel() => SetLevel(RiskLevel.None);

        // ── Panel ─────────────────────────────────────────────────────────────────

        public void OpenPanel()
        {
            if (panelRoot == null || CurrentLevel == RiskLevel.None) return;
            RefreshPanelImage();
            panelRoot.SetActive(true);
        }

        public void ClosePanel()
        {
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        private void RefreshPanelImage()
        {
            if (panelImage == null) return;
            panelImage.sprite = GetPanelSprite(CurrentLevel);
        }

        // ── Reacción al cambio de etapa ───────────────────────────────────────────
        private void OnStageChanged(StageManager.Stage previous, StageManager.Stage current)
        {
            StageRiskConfig config = FindStageConfig(current);
            if (config != null)
                SetLevel(config.defaultRiskLevel);
        }

        // ── Restaurar sprite al reactivarse ──────────────────────────────────────
        private void OnEnable()
        {
            if (CurrentLevel == RiskLevel.None) return;
            if (indicatorRoot != null) indicatorRoot.SetActive(true);
            if (levelImage != null) levelImage.sprite = GetIndicatorSprite(CurrentLevel);
        }

        // ── Pulso de escala (N3 / N4) ─────────────────────────────────────────────
        private void ApplyPulse()
        {
            if (indicatorRoot == null || CurrentLevel < RiskLevel.N3) return;

            float speed = CurrentLevel == RiskLevel.N4 ? pulseSpeedN4 : pulseSpeedN3;
            float pulse = 1f + pulseAmplitude * Mathf.Sin(Time.time * speed * Mathf.PI * 2f);
            indicatorRoot.transform.localScale = _baseScale * pulse;
        }

        // ── Helpers ───────────────────────────────────────────────────────────────
        private Sprite GetIndicatorSprite(RiskLevel level) => level switch
        {
            RiskLevel.N1 => spriteN1,
            RiskLevel.N2 => spriteN2,
            RiskLevel.N3 => spriteN3,
            RiskLevel.N4 => spriteN4,
            _ => null,
        };

        // Si el sprite de panel no está asignado, usa el del indicador como fallback.
        private Sprite GetPanelSprite(RiskLevel level) => level switch
        {
            RiskLevel.N1 => panelSpriteN1 != null ? panelSpriteN1 : spriteN1,
            RiskLevel.N2 => panelSpriteN2 != null ? panelSpriteN2 : spriteN2,
            RiskLevel.N3 => panelSpriteN3 != null ? panelSpriteN3 : spriteN3,
            RiskLevel.N4 => panelSpriteN4 != null ? panelSpriteN4 : spriteN4,
            _ => null,
        };

        private StageRiskConfig FindStageConfig(StageManager.Stage stage)
        {
            if (stageRiskDefaults == null) return null;
            foreach (var c in stageRiskDefaults)
                if (c.stage == stage) return c;
            return null;
        }
    }
}
