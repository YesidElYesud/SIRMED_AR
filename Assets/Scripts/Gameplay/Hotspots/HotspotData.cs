namespace SIRMED.Gameplay.Hotspots
{
    using SIRMED.Gameplay.Dialogue;
    using UnityEngine;
    using UnityEngine.Video;

    // ── Nivel de riesgo ───────────────────────────────────────────────────────────
    /// <summary>
    /// Nivel de riesgo asociado a un hotspot o zona.
    /// Usado por HotspotUIPanel (badge de color) y, en una fase posterior,
    /// por el indicador de riesgo del HUD.
    /// </summary>
    public enum RiskLevel
    {
        None = 0,   // Sin nivel asignado — badge oculto
        N1 = 1,     // Bajo     — verde
        N2 = 2,     // Moderado — amarillo
        N3 = 3,     // Alto     — naranja
        N4 = 4,     // Crítico  — rojo
    }

    // ── Enum de tipo de acción ────────────────────────────────────────────────────
    public enum HotspotActionType
    {
        /// <summary>Muestra el panel informativo clásico.</summary>
        InfoPanel = 0,

        /// <summary>Reproduce una cinemática en pantalla completa.</summary>
        Cinematic = 1,

        /// <summary>
        /// Abre un diálogo con un NPC con opciones de respuesta.
        /// Pendiente: requiere NpcDialoguePanel (fase de diálogos/UI). Hasta entonces,
        /// HotspotController cae a InfoPanel con una advertencia en consola.
        /// </summary>
        NpcConversation = 2,

        /// <summary>
        /// Simula una llamada al SIATA con opciones de reporte.
        /// Pendiente: requiere SiataCallPanel (fase de diálogos/UI). Hasta entonces,
        /// HotspotController cae a InfoPanel con una advertencia en consola.
        /// </summary>
        SiataCall = 3,

        /// <summary>
        /// Muestra slides secuenciales de contenido educativo.
        /// Pendiente: requiere InfoSlidePanel (fase de diálogos/UI). Hasta entonces,
        /// HotspotController cae a InfoPanel con una advertencia en consola.
        /// </summary>
        InfoSlidePanel = 4,

        /// <summary>
        /// Solo actualiza el indicador de riesgo con el riskLevel asignado y cierra.
        /// No abre ningún panel. Usar riskLevel = None para que la interacción
        /// no cambie ningún indicador (marcador de zona sin contenido extra).
        /// </summary>
        RiskLevelOnly = 5,
    }

    // ── ScriptableObject ──────────────────────────────────────────────────────────
    /// <summary>
    /// HotspotData — contenido de un hotspot geo-anclado. Pura data: no sabe nada
    /// de dónde está anclado en el mundo real (eso lo resuelve el componente
    /// ARGeospatialCreatorAnchor del GameObject que lleva el HotspotController).
    ///
    /// Crear via: Assets > Create > AR > Hotspot Data
    /// </summary>
    [CreateAssetMenu(fileName = "NewHotspot", menuName = "AR/Hotspot Data", order = 1)]
    public class HotspotData : ScriptableObject
    {
        // ── Contenido base ────────────────────────────────────────────────────────
        [Header("Contenido")]
        [Tooltip("Título principal del hotspot")]
        public string title = "Hotspot";

        [Tooltip("Descripción o información a mostrar (usado en InfoPanel)")]
        [TextArea(3, 8)]
        public string description = "Información del hotspot.";

        [Tooltip("Ícono opcional para el panel informativo")]
        public Sprite icon;

        // ── Tipo de acción ────────────────────────────────────────────────────────
        [Header("Tipo de acción")]
        [Tooltip("Qué ocurre al activar este hotspot. Ver comentarios del enum " +
                 "HotspotActionType para el estado de cada tipo.")]
        public HotspotActionType actionType = HotspotActionType.InfoPanel;

        [Header("Datos según tipo de acción")]
        [Tooltip("VideoClip a reproducir. Solo se usa cuando actionType = Cinematic.")]
        public VideoClip cinematicClip;

        [Tooltip("URL del video (streaming remoto), alternativa a cinematicClip.\n" +
                 "Ejemplo: 'StreamingAssets/Videos/clip.mp4' o URL remota HTTPS.")]
        public string cinematicUrl = "";

        [Tooltip("Si es true, al terminar (o saltar) la cinemática se avanza a la siguiente etapa.")]
        public bool cinematicAdvancesStage = true;

        [Tooltip("Datos del diálogo. Solo se usa cuando actionType = NpcConversation o SiataCall (sin secuencia).")]
        public NpcDialogueData dialogueData;

        [Tooltip("Secuencia SIATA con pasos mixtos Info/Question.\n" +
                 "Si está asignada, tiene prioridad sobre dialogueData cuando actionType = SiataCall.")]
        public SiataDialogueSequence siataSequence;

        [Tooltip("Slides a mostrar cuando actionType = InfoSlidePanel.")]
        public InfoSlideData[] infoSlides;

        [Tooltip("Si true, al cerrar el último slide se avanza a la siguiente etapa (NextStage).")]
        public bool infoSlideAdvancesStage = true;

        // ── Activación ────────────────────────────────────────────────────────────
        [Header("Activación")]
        [Tooltip("Radio en metros para activación por proximidad respecto a la cámara del jugador.")]
        public float triggerRadius = 3f;

        [Tooltip("Etapa en que este hotspot es visible y activo.\n" +
                 "-1 = visible en todas las etapas.\n" +
                 " 0 = solo en Intro,  1 = Etapa1,  2 = Etapa2,  etc.")]
        public int requiredStage = -1;

        // ── Panel enriquecido ─────────────────────────────────────────────────────
        [Header("Panel Enriquecido")]
        [Tooltip("Imagen de cabecera mostrada en la parte superior del panel informativo.\n" +
                 "Opcional: si es null el header se oculta automáticamente.")]
        public Sprite headerImage;

        [Tooltip("Nivel de riesgo de esta zona. Controla el color del badge en el panel.\n" +
                 "None = badge oculto.")]
        public RiskLevel riskLevel = RiskLevel.None;

        // ── Avance de etapa al cerrar ─────────────────────────────────────────────
        [Header("Avance de Etapa")]
        [Tooltip("Si true, al cerrar este hotspot (o al interactuar con él si es RiskLevelOnly)\n" +
                 "se llama a StageManager.NextStage().")]
        public bool advancesStageOnClose = false;

        // ── Ruta de Evacuación ────────────────────────────────────────────────────
        [Header("Ruta de Evacuación")]
        [Tooltip("Si true, al cerrar este panel se debería mostrar la ruta de evacuación.\n" +
                 "Pendiente: requiere EvacuationRouteController (fase de ambiente). El flag ya\n" +
                 "queda guardado en el dato para no tener que retocar HotspotData más adelante.")]
        public bool activatesEvacuationRoute = false;

        // ── Fin del juego ─────────────────────────────────────────────────────────
        [Header("Fin del juego")]
        [Tooltip("Si true, al cerrar este panel se debería mostrar la pantalla de felicitación.\n" +
                 "Pendiente: requiere EndGamePanel (fase de diálogos/UI).")]
        public bool activatesEndGame = false;

        [Tooltip("Etapa en que se activa el EndGame.\n" +
                 "-1 = siempre que activatesEndGame sea true.\n" +
                 " 4 = solo en Etapa4.")]
        public int endGameRequiredStage = -1;

        // ── Visual ────────────────────────────────────────────────────────────────
        [Header("Visual")]
        [Tooltip("Si está marcado, el marcador visual del hotspot pulsa para llamar la atención.")]
        public bool isBlinking = true;

        [Tooltip("Velocidad del pulso visual (ciclos por segundo). Solo aplica si isBlinking = true.")]
        [Range(0.5f, 4f)]
        public float blinkSpeed = 1.5f;
    }
}
