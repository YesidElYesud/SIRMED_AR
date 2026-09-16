namespace SIRMED.Gameplay.Dialogue
{
    using UnityEngine;

    /// <summary>
    /// TriviaData — Microtrivia de refuerzo asociada a un HotspotData.
    ///
    /// Se muestra automáticamente al cerrar el panel principal del hotspot
    /// (InfoPanel, diálogo NPC, SIATA o slides) — nunca antes del contenido,
    /// tal como pide el GDD (Mecánica 3: Minijuegos de trivia). Ver
    /// HotspotController.ClosePanel() y TriviaPanel.
    ///
    /// Pura data — reutiliza DialogueOption (misma clase que NpcDialogueData y
    /// SiataDialogueSequence) para no duplicar la estructura de opciones.
    ///
    /// Crear via: Assets > Create > AR > Trivia Data
    /// </summary>
    [CreateAssetMenu(fileName = "NewTrivia", menuName = "AR/Trivia Data", order = 4)]
    public class TriviaData : ScriptableObject
    {
        [Header("Pregunta")]
        [TextArea(2, 4)]
        public string question = "¿Pregunta de la trivia?";

        [Tooltip("Entre 2 y 4 opciones. Solo una debe tener isCorrect = true.")]
        public DialogueOption[] options;

        [Header("Comportamiento")]
        [Tooltip("Segundos de pausa tras responder (correcto o incorrecto) antes de continuar.")]
        [Range(0.5f, 4f)]
        public float answerDelay = 1.5f;
    }
}
