namespace SIRMED.Gameplay.Director
{
    using UnityEngine;

    /// <summary>
    /// Una línea de un consejo del Director: texto + voz opcional + gesto opcional.
    /// Deliberadamente separada de SIRMED.Gameplay.Dialogue.DialogueLine (que usan
    /// NpcDialoguePanel/SiataCallPanel) porque el Director necesita un campo extra
    /// (gestureTrigger) que esos otros paneles no usan.
    /// </summary>
    [System.Serializable]
    public class DirectorAdviceLine
    {
        [TextArea(2, 4)]
        [Tooltip("Texto que aparece en el banner del Director.")]
        public string text;

        [Tooltip("Clip de voz reproducido al mostrar esta línea. Si el jugador avanza antes de que termine, se corta. Vacío = línea sin narración.")]
        public AudioClip audio;

        [Tooltip("Nombre del parámetro Trigger del Animator del Director a disparar al mostrar esta línea (gesto/animación puntual). Vacío = no se toca el Animator.")]
        public string gestureTrigger;
    }

    /// <summary>
    /// DirectorAdviceData — ScriptableObject con un consejo del Director DAGRD
    /// (GDD pág. 28, Tabla 15; banco de mensajes en Anexo A). Consumido por
    /// DirectorController.ShowAdvice() vía DirectorAdvicePanel.
    ///
    /// Crear via: Assets > Create > AR > Director Advice Data
    /// </summary>
    [CreateAssetMenu(fileName = "NewDirectorAdvice", menuName = "AR/Director Advice Data", order = 5)]
    public class DirectorAdviceData : ScriptableObject
    {
        [Tooltip("Solo para identificar el asset en el Inspector/Project — no se muestra al jugador.")]
        public string debugLabel;

        [Tooltip("Líneas paginadas del consejo (texto + audio + gesto opcional cada una).")]
        public DirectorAdviceLine[] lines;

        [Tooltip("Si se asigna, al terminar de mostrar este consejo el Director camina hacia el DirectorWaypoint cuyo pointId coincida (ver DirectorController.waypoints). Vacío = el Director se queda donde está.")]
        public string moveToPointId;
    }
}
