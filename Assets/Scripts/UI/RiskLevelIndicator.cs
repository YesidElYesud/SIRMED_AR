namespace SIRMED.UI
{
    using SIRMED.Gameplay.Hotspots;
    using SIRMED.Managers;
    using System;
    using System.Collections;
    using TMPro;
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
            new StageRiskConfig { stage = StageManager.Stage.Etapa4, defaultRiskLevel = RiskLevel.N4 }
        };

        // ── Referencias UI — Indicador ────────────────────────────────────────────
        [Header("Indicador HUD")]
        [Tooltip("GameObject raíz del widget. Se activa con nivel ≠ None.")]
        public GameObject indicatorRoot;

        // ── Referencias UI — Botones de nivel ─────────────────────────────────────
        [Header("Botones de nivel")]
        public Button[] btnsN;
        public RectTransform[] btnsPanel;

        // ── Referencias UI — Fondo y texto ────────────────────────────────────────
        [Header("Fondo y texto")]
        public RectTransform bg;
        public CanvasRenderer description;
        public TextMeshProUGUI descriptionText;

        // ── Referencias UI — Descripciones ────────────────────────────────────────
        [Header("Descripciones")]
        [TextArea] public string[] riskTexts;

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
        private bool firstBtn = false;

        // ── Lifecycle ─────────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            if (indicatorRoot != null)
                _baseScale = indicatorRoot.transform.localScale;

            btnsPanel[0].sizeDelta = new Vector2(btnsPanel[0].sizeDelta.x, 250);
            for (int i = 1; i < btnsPanel.Length; i++)
            {
                btnsPanel[i].sizeDelta = new Vector2(btnsPanel[i].sizeDelta.x, 100);
            }
            bg.sizeDelta = new Vector2(0, 355); //160
            descriptionText.SetText(string.Empty);
        }

        private void Start()
        {
            if (StageManager.Instance != null)
                StageManager.Instance.OnStageChanged += OnStageChanged;

            // Botones de nivel → cambian la etapa completa del juego (dispara todos los efectos)
            if (btnsN[0] != null) btnsN[0].onClick.AddListener(() => GoToStageForLevel(RiskLevel.N1));
            if (btnsN[1] != null) btnsN[1].onClick.AddListener(() => GoToStageForLevel(RiskLevel.N2));
            if (btnsN[2] != null) btnsN[2].onClick.AddListener(() => GoToStageForLevel(RiskLevel.N3));
            if (btnsN[3] != null) btnsN[3].onClick.AddListener(() => GoToStageForLevel(RiskLevel.N4));

            // Ocultar indicador y panel al arrancar
            if (indicatorRoot != null) indicatorRoot.SetActive(false);
        }

        // Llama GoToStage para el nivel solicitado y dispara todos los efectos del juego.
        private void GoToStageForLevel(RiskLevel level)
        {
            StageManager.Stage stage = (StageManager.Stage)(int)level;
            int index = (int)level - 1;

            descriptionText.SetText(riskTexts[index]);

            if (StageManager.Instance?.CurrentStage != stage)
            {
                CoroutineRunner.Instance.StartCoroutine(AnimatePanels(index));

                StageManager.Instance?.GoToStage(stage);
            }
            else
            {
                if (!firstBtn || Mathf.Approximately(btnsPanel[index].sizeDelta.y, 100f))
                {
                    firstBtn = true;
                    CoroutineRunner.Instance.StartCoroutine(AnimatePanels(index));
                }
                else
                {
                    CoroutineRunner.Instance.StartCoroutine(ClosePanels(index));
                }
            }
        }

        private IEnumerator AnimatePanels(int index)
        {
            float lerp = 0;

            while (lerp < 1)
            {
                lerp += Time.deltaTime * 3;

                for (int i = 0; i < btnsPanel.Length; i++)
                {
                    if (i == index)
                        continue;

                    btnsPanel[i].sizeDelta = new Vector2(btnsPanel[i].sizeDelta.x, Mathf.Lerp(btnsPanel[i].sizeDelta.y, 100, lerp));
                }

                description.SetAlpha(lerp);

                btnsPanel[index].sizeDelta = new Vector2(btnsPanel[index].sizeDelta.x, Mathf.Lerp(btnsPanel[index].sizeDelta.y, 250, lerp));

                float height = descriptionText.GetPreferredValues(descriptionText.text, descriptionText.rectTransform.rect.width, Mathf.Infinity).y;
                bg.sizeDelta = new Vector2(0, Mathf.Lerp(bg.sizeDelta.y, 365 + height, lerp));

                yield return null;
            }
        }

        private IEnumerator ClosePanels(int index)
        {
            float lerp = 0;

            while (lerp < 1)
            {
                lerp += Time.deltaTime * 3;

                btnsPanel[index].sizeDelta = new Vector2(btnsPanel[index].sizeDelta.x, Mathf.Lerp(btnsPanel[index].sizeDelta.y, 100, lerp));
                bg.sizeDelta = new Vector2(0, Mathf.Lerp(bg.sizeDelta.y, 160, lerp));

                yield return null;
            }
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
        }

        // ── Pulso de escala (N3 / N4) ─────────────────────────────────────────────
        private void ApplyPulse()
        {
            if (indicatorRoot == null || CurrentLevel < RiskLevel.N3) return;

            float speed = CurrentLevel == RiskLevel.N4 ? pulseSpeedN4 : pulseSpeedN3;
            float pulse = 1f + pulseAmplitude * Mathf.Sin(Time.time * speed * Mathf.PI * 2f);
            indicatorRoot.transform.localScale = _baseScale * pulse;
        }

        private StageRiskConfig FindStageConfig(StageManager.Stage stage)
        {
            if (stageRiskDefaults == null) return null;
            foreach (var c in stageRiskDefaults)
                if (c.stage == stage) return c;
            return null;
        }
    }
}