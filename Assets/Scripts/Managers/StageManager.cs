namespace SIRMED.Managers
{
    using System;
    using UnityEngine;

    /// <summary>
    /// StageManager — Núcleo de progresión de la experiencia SIRMED AR.
    ///
    /// Responsabilidades:
    ///   - Mantener la etapa actual (Intro → Etapa1 … Etapa5).
    ///   - Activar/desactivar GameObjects de escena al cambiar de etapa.
    ///   - Disparar el evento OnStageChanged para que otros sistemas reaccionen.
    ///   - Exponer NextStage() y GoToStage() como API pública.
    ///
    /// Setup en editor:
    ///   1. Crear un GameObject vacío "StageManager" en la escena raíz.
    ///   2. Adjuntar este script.
    ///   3. En stageConfigs, añadir 6 entradas (índice 0=Intro … 5=Etapa5)
    ///      y arrastrar los GameObjects a activar/desactivar en cada etapa.
    ///   4. Elegir startStage (normalmente Intro).
    ///
    /// Nota de portado (SATCS → SIRMED_AR): en el proyecto WebGL original este
    /// manager también recibía una referencia a ARCameraController para bloquear
    /// input del jugador. En AR real la cámara la mueve la persona, no hay
    /// "input de jugador" que bloquear a nivel de movimiento — SetPlayerInputBlocked
    /// se conserva como gancho para que la UI (paneles/diálogos) ignore toques,
    /// pero ya no intenta congelar ninguna cámara.
    /// </summary>

    // ── Datos de configuración por etapa ─────────────────────────────────────────
    [Serializable]
    public class StageConfig
    {
        [Tooltip("Nombre descriptivo (solo para el editor, no afecta lógica)")]
        public string stageName;

        [Tooltip("GameObjects que se activan al entrar a esta etapa")]
        public GameObject[] objectsToActivate;

        [Tooltip("GameObjects que se desactivan al entrar a esta etapa")]
        public GameObject[] objectsToDeactivate;
    }

    // ── StageManager ──────────────────────────────────────────────────────────────
    public class StageManager : MonoBehaviour
    {
        // ── Singleton ─────────────────────────────────────────────────────────────
        public static StageManager Instance { get; private set; }

        // ── Enum de etapas ────────────────────────────────────────────────────────
        public enum Stage
        {
            Intro = 0,
            Etapa1 = 1,
            Etapa2 = 2,
            Etapa3 = 3,
            Etapa4 = 4,
            Etapa5 = 5
        }

        // ── Evento público ────────────────────────────────────────────────────────
        /// <summary>
        /// Se dispara cada vez que la etapa cambia.
        /// Firma: (Stage etapaAnterior, Stage etapaNueva)
        /// Suscribirse desde cualquier sistema: AudioStageManager, UI, etc.
        /// </summary>
        public event Action<Stage, Stage> OnStageChanged;

        // ── Propiedades públicas ──────────────────────────────────────────────────
        public Stage CurrentStage { get; private set; } = Stage.Intro;

        /// <summary>True mientras el input de UI (paneles/diálogos) debe ignorarse.</summary>
        public bool IsPlayerInputBlocked { get; private set; }

        // ── Inspector ─────────────────────────────────────────────────────────────
        [Header("Configuración por etapa")]
        [Tooltip("6 entradas: índice 0=Intro, 1=Etapa1, 2=Etapa2, 3=Etapa3, 4=Etapa4, 5=Etapa5")]
        public StageConfig[] stageConfigs = new StageConfig[6];

        [Header("Debug")]
        [Tooltip("Etapa con la que arranca la escena al presionar Play.")]
        public Stage startStage = Stage.Intro;

        [Tooltip("Muestra en consola cada transición de etapa.")]
        public bool debugLogs = true;

        // ── Lifecycle ─────────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            // Forzar la etapa inicial aunque sea la misma que CurrentStage por defecto
            Stage initial = startStage;
            CurrentStage = (Stage)(((int)initial - 1 + 6) % 6); // valor diferente para forzar el cambio
            GoToStage(initial);
        }

        // ── API pública ───────────────────────────────────────────────────────────

        /// <summary>
        /// Avanza una etapa hacia adelante.
        /// Llamar desde MultipleChoicePanel al responder correctamente.
        /// </summary>
        public void NextStage()
        {
            int next = (int)CurrentStage + 1;
            if (next > (int)Stage.Etapa5)
            {
                if (debugLogs) Debug.Log("[StageManager] La experiencia ha finalizado (Etapa5 completada).");
                return;
            }

            GoToStage((Stage)next);
        }

        public void PreviousStage()
        {
            int prev = (int)CurrentStage - 1;
            if (prev < 0)
            {
                if (debugLogs) Debug.Log("[StageManager] Ya está en la primera etapa.");
                return;
            }

            GoToStage((Stage)prev);
        }

        /// <summary>
        /// Salta directamente a la etapa indicada.
        /// Útil para debugging o para que un panel de bienvenida inicie en Etapa1.
        /// </summary>
        public void GoToStage(Stage target)
        {
            if (target == CurrentStage) return;

            Stage previous = CurrentStage;
            CurrentStage = target;

            if (debugLogs)
                Debug.Log($"[StageManager] {previous} → {target}");

            // OnStageChanged primero: sistemas reaccionan (audio, visuals, hotspots).
            // ApplyStageConfig después: StageManager tiene la última palabra sobre SetActive.
            OnStageChanged?.Invoke(previous, target);
            ApplyStageConfig(target);
        }

        /// <summary>
        /// Bloquea o desbloquea el input de UI (paneles/diálogos).
        /// Llamar desde WelcomePanel, CinematicManager, NpcDialoguePanel, etc.
        /// </summary>
        public void SetPlayerInputBlocked(bool blocked)
        {
            IsPlayerInputBlocked = blocked;
        }

        // ── Privados ──────────────────────────────────────────────────────────────
        private void ApplyStageConfig(Stage stage)
        {
            int index = (int)stage;
            if (stageConfigs == null || index >= stageConfigs.Length) return;

            StageConfig config = stageConfigs[index];
            if (config == null) return;

            if (config.objectsToActivate != null)
                foreach (var go in config.objectsToActivate)
                    if (go != null) go.SetActive(true);

            if (config.objectsToDeactivate != null)
                foreach (var go in config.objectsToDeactivate)
                    if (go != null) go.SetActive(false);
        }
    }
}
