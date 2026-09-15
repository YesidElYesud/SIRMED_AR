namespace SIRMED.Gameplay.Dialogue
{
    using UnityEngine;

    // ── Tipo de paso ──────────────────────────────────────────────────────────────
    public enum SiataStepType
    {
        /// <summary>Solo texto. El jugador pulsa "Continuar" para avanzar.</summary>
        Info = 0,

        /// <summary>Texto + opciones de selección múltiple. Debe responderse correctamente para avanzar.</summary>
        Question = 1,
    }

    // ── Paso individual ───────────────────────────────────────────────────────────
    [System.Serializable]
    public class SiataDialogueStep
    {
        [Tooltip("Info = texto + botón Continuar.\nQuestion = texto + opciones (requiere respuesta correcta para avanzar).")]
        public SiataStepType stepType = SiataStepType.Info;

        [Tooltip("Lo que dice el operador SIATA en este paso.")]
        [TextArea(3, 6)]
        public string npcText = "Texto del operador.";

        [Tooltip("Opciones de respuesta. Solo aplica cuando stepType = Question.\nUna sola opción debe tener isCorrect = true.")]
        public DialogueOption[] options;
    }

    // ── ScriptableObject ──────────────────────────────────────────────────────────
    /// <summary>
    /// SiataDialogueSequence — Conversación SIATA con pasos mixtos (Info + Question).
    ///
    /// Diferencia respecto a NpcDialogueData:
    ///   - Soporta múltiples pasos secuenciales en lugar de un solo texto + pregunta.
    ///   - Pasos Info: solo texto + botón Continuar (sin elección).
    ///   - Pasos Question: texto + opciones múltiples; retiene al jugador hasta respuesta correcta.
    ///
    /// Igual que NpcDialogueData, es pura data — el panel que la consume
    /// (SiataCallPanel) se implementa en una fase posterior.
    ///
    /// Crear via: Assets > Create > AR > SIATA Dialogue Sequence
    /// </summary>
    [CreateAssetMenu(fileName = "NewSiataSequence", menuName = "AR/SIATA Dialogue Sequence", order = 3)]
    public class SiataDialogueSequence : ScriptableObject
    {
        [Header("Identificación del operador")]
        [Tooltip("Nombre del operador SIATA (aparece en el encabezado de llamada).")]
        public string npcName = "SIATA — Sistema de Alerta Temprana";

        [Tooltip("Foto o avatar del operador SIATA.")]
        public Sprite npcPhoto;

        [Header("Pasos de la conversación")]
        [Tooltip("Lista de pasos secuenciales. Los pasos se muestran en orden:\n" +
                 "• Info → pulsar Continuar avanza al siguiente.\n" +
                 "• Question → debe responderse correctamente para avanzar.\n" +
                 "El primer paso se muestra tras el delay de conexión.")]
        public SiataDialogueStep[] steps;

        [Header("Comportamiento al completar")]
        [Tooltip("Si true, al completar el último paso se llama StageManager.NextStage().")]
        public bool advancesStageOnComplete = true;

        [Tooltip("Segundos de pausa tras una respuesta correcta antes de mostrar el siguiente paso.")]
        [Range(0.5f, 4f)]
        public float correctAnswerDelay = 1.5f;
    }
}
