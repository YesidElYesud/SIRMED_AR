namespace SIRMED.Gameplay.Hotspots
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.SceneManagement;

    /// <summary>
    /// HotspotSequence — progreso de hotspots que deben hacerse en orden (checklist
    /// M05: Líder comunitario, parada 1 → 2 → 3). Cada HotspotController con
    /// sequenceId no vacío solo se muestra cuando ya se completaron los pasos
    /// anteriores de su secuencia.
    ///
    /// El progreso vive solo en memoria: se reinicia al (re)cargar la escena y al cerrar
    /// la app. No se guarda en el teléfono.
    /// </summary>
    public static class HotspotSequence
    {
        /// <summary>Se dispara al completar un paso o al reiniciar (los hotspots refrescan su visibilidad).</summary>
        public static event Action Changed;

        private static readonly Dictionary<string, int> _completed = new Dictionary<string, int>();

        /// <summary>Cuántos pasos de la secuencia ya se completaron (0 = ninguno).</summary>
        public static int CompletedSteps(string sequenceId) =>
            !string.IsNullOrEmpty(sequenceId) && _completed.TryGetValue(sequenceId, out int n) ? n : 0;

        /// <summary>El paso 'step' (0 = primero) está disponible si todos los anteriores se completaron.</summary>
        public static bool IsUnlocked(string sequenceId, int step) =>
            string.IsNullOrEmpty(sequenceId) || step <= CompletedSteps(sequenceId);

        public static void Complete(string sequenceId, int step)
        {
            if (string.IsNullOrEmpty(sequenceId) || step + 1 <= CompletedSteps(sequenceId)) return;

            _completed[sequenceId] = step + 1;
            Changed?.Invoke();
        }

        /// <summary>Borra el progreso de todas las secuencias.</summary>
        public static void ResetScene()
        {
            _completed.Clear();
            Changed?.Invoke();
        }

        // Estado estático: se limpia al arrancar (también en el Editor con Domain Reload
        // desactivado) y cada vez que se carga una escena en modo Single.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
            _completed.Clear();
            Changed = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Single) _completed.Clear();
        }
    }
}
