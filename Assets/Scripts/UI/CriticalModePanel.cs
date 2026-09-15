namespace SIRMED.UI
{
    using System;
    using SIRMED.Managers;
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>
    /// CriticalModePanel — Modal automático de alerta para etapas críticas.
    /// Muestra una imagen de fondo por etapa (sin textos) + botón Continuar.
    ///
    /// Setup en escena:
    ///   CriticalModePanel
    ///   ├── Overlay        (Image negro semitransparente, fullscreen stretch)
    ///   ├── ContentBox     (Image panel centrado — puede ser solo stretch)
    ///   │   ├── PanelImage (Image — fullscreen o centrada, aquí se proyecta la imagen)
    ///   │   └── ContinueButton (Button — "Continuar")
    /// </summary>
    public class CriticalModePanel : MonoBehaviour
    {
        // ── Singleton ─────────────────────────────────────────────────────────────
        public static CriticalModePanel Instance { get; private set; }

        // ── Datos de contenido por etapa ──────────────────────────────────────────
        [Serializable]
        public class StageContent
        {
            [Tooltip("Etapa que dispara este modal")]
            public StageManager.Stage stage;

            [Tooltip("Imagen que se muestra en el panel para esta etapa")]
            public Sprite image;
        }

        // ── Inspector ─────────────────────────────────────────────────────────────
        [Header("Contenido por etapa")]
        [Tooltip("Define qué etapas disparan el modal y qué imagen mostrar.")]
        public StageContent[] stageContents = new StageContent[]
        {
            new StageContent { stage = StageManager.Stage.Etapa3 },
            new StageContent { stage = StageManager.Stage.Etapa4 }
        };

        [Header("UI")]
        public Image panelImage;
        public Button continueButton;

        [Header("Comportamiento")]
        [Tooltip("Segundos de espera antes de mostrar el modal al entrar a la etapa.")]
        [Range(0f, 5f)]
        public float showDelay = 1f;

        [Tooltip("Segundos hasta que el modal se cierra automáticamente.\n" +
                 "0 = el jugador debe pulsar el botón 'Entendido'.")]
        [Range(0f, 10f)]
        public float autoDismissDelay = 0f;

        // ── Internos ──────────────────────────────────────────────────────────────
        private Coroutine _autoDismissRoutine;

        // ── Lifecycle ─────────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            if (continueButton != null)
                continueButton.onClick.AddListener(Hide);
            // NO llamar SetActive(false) aquí: CriticalModePanel está antes que StageManager
            // en la jerarquía, así que su Awake() corre primero y StageManager.Instance es null.
        }

        private void Start()
        {
            // En Start() todos los Awake() ya corrieron → StageManager.Instance está garantizado.
            if (StageManager.Instance != null)
            {
                StageManager.Instance.OnStageChanged += OnStageChanged;
            }
            else
            {
                Debug.LogError("[CriticalModePanel] StageManager.Instance es null en Start() — el panel NO se mostrará automáticamente.");
            }

            gameObject.SetActive(false); // Ocultar DESPUÉS de suscribirse
        }

        // ── Test en Editor ────────────────────────────────────────────────────────
        [ContextMenu("Test: Mostrar Etapa4")]
        private void TestShowEtapa4()
        {
            StageContent content = FindContent(StageManager.Stage.Etapa4);
            if (content == null)
            {
                Debug.LogWarning("[CriticalModePanel] No hay StageContent para Etapa4 en stageContents[].");
                return;
            }
            Show(content);
        }

        [ContextMenu("Test: Mostrar Etapa3")]
        private void TestShowEtapa3()
        {
            StageContent content = FindContent(StageManager.Stage.Etapa3);
            if (content == null)
            {
                Debug.LogWarning("[CriticalModePanel] No hay StageContent para Etapa3 en stageContents[].");
                return;
            }
            Show(content);
        }

        private void OnDestroy()
        {
            if (StageManager.Instance != null)
                StageManager.Instance.OnStageChanged -= OnStageChanged;
        }

        // ── Reacción al cambio de etapa ───────────────────────────────────────────
        private void OnStageChanged(StageManager.Stage previous, StageManager.Stage current)
        {
            StageContent content = FindContent(current);
            if (content == null) return;
            // Show() activa el GO antes de cualquier otra operación, por lo que
            // es seguro llamarlo directamente — StartCoroutine en un GO inactivo falla silenciosamente.
            Show(content);
        }

        // ── API pública ───────────────────────────────────────────────────────────

        /// <summary>
        /// Muestra el modal con el contenido de la etapa actual.
        /// </summary>
        public void Show(StageContent content)
        {
            if (content == null) return;

            PopulateUI(content);
            gameObject.SetActive(true);
            BlockInput(true);

            if (autoDismissDelay > 0f)
            {
                if (_autoDismissRoutine != null) StopCoroutine(_autoDismissRoutine);
                _autoDismissRoutine = StartCoroutine(AutoDismissRoutine());
            }
        }

        /// <summary>Cierra el modal y desbloquea el input. Llamado por el botón "Entendido" o por auto-dismiss.</summary>
        public void Hide()
        {
            if (_autoDismissRoutine != null) { StopCoroutine(_autoDismissRoutine); _autoDismissRoutine = null; }

            BlockInput(false);
            gameObject.SetActive(false);
        }

        private System.Collections.IEnumerator AutoDismissRoutine()
        {
            yield return new WaitForSeconds(autoDismissDelay);
            Hide();
        }

        // ── Helpers ───────────────────────────────────────────────────────────────
        private StageContent FindContent(StageManager.Stage stage)
        {
            if (stageContents == null) return null;
            foreach (var content in stageContents)
                if (content.stage == stage) return content;
            return null;
        }

        private void PopulateUI(StageContent content)
        {
            if (panelImage == null) return;
            if (content.image != null)
            {
                panelImage.sprite = content.image;
                panelImage.gameObject.SetActive(true);
            }
            else
            {
                panelImage.gameObject.SetActive(false);
                Debug.LogWarning($"[CriticalModePanel] StageContent para {content.stage} no tiene imagen asignada.");
            }
        }

        private void BlockInput(bool block)
        {
            if (StageManager.Instance != null)
                StageManager.Instance.SetPlayerInputBlocked(block);
        }
    }
}
