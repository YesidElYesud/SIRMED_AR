namespace SIRMED.Gameplay.Hotspots
{
    using UnityEngine;

    /// <summary>
    /// Contrato mínimo que permite a HotspotPromptButton (y, en fases posteriores,
    /// a los paneles de diálogo) operar con cualquier fuente de interacción
    /// (HotspotController, un futuro NPC interactuable, etc.) sin acoplarse a un
    /// tipo concreto.
    ///
    /// Cualquier MonoBehaviour satisface 'Transform transform { get; }' automáticamente
    /// a través de Component.transform — no es necesario implementarlo explícitamente.
    /// </summary>
    public interface IHotspotInteractable
    {
        Transform transform { get; }

        /// <summary>Llamado por HotspotPromptButton cuando el jugador pulsa el botón HUD.</summary>
        void DispatchAction();

        /// <summary>Llamado por el panel de UI (HotspotUIPanel, etc.) al cerrarse.</summary>
        void ClosePanel();
    }
}
