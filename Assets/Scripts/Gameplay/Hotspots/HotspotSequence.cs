namespace SIRMED.Gameplay.Hotspots
{
    using System;
    using UnityEngine;
    using UnityEngine.SceneManagement;

    /// <summary>
    /// HotspotSequence — progreso de hotspots que deben hacerse en orden (checklist
    /// M05: Líder comunitario, parada 1 → 2 → 3). Cada HotspotController con
    /// sequenceId no vacío solo se muestra cuando ya se completaron los pasos
    /// anteriores de su secuencia.
    ///
    /// El progreso se guarda en PlayerPrefs por escena (cada SAT es una escena), así
    /// que sobrevive a cerrar la app; EndGamePanel.Restart() lo borra con ResetScene().
    /// </summary>
    public static class HotspotSequence
    {
        /// <summary>Se dispara al completar un paso o al reiniciar (los hotspots refrescan su visibilidad).</summary>
        public static event Action Changed;

        private const string KeyPrefix = "SIRMED_SEQ_";
        private const string IdsKeySuffix = "__ids";

        /// <summary>Cuántos pasos de la secuencia ya se completaron (0 = ninguno).</summary>
        public static int CompletedSteps(string sequenceId) =>
            PlayerPrefs.GetInt(Key(sequenceId), 0);

        /// <summary>El paso 'step' (0 = primero) está disponible si todos los anteriores se completaron.</summary>
        public static bool IsUnlocked(string sequenceId, int step) =>
            string.IsNullOrEmpty(sequenceId) || step <= CompletedSteps(sequenceId);

        public static void Complete(string sequenceId, int step)
        {
            if (string.IsNullOrEmpty(sequenceId) || step + 1 <= CompletedSteps(sequenceId)) return;

            PlayerPrefs.SetInt(Key(sequenceId), step + 1);
            RememberId(sequenceId);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>Borra el progreso de todas las secuencias de la escena activa.</summary>
        public static void ResetScene()
        {
            string idsKey = KeyPrefix + SceneName() + IdsKeySuffix;
            foreach (string id in PlayerPrefs.GetString(idsKey, "").Split('|'))
                if (!string.IsNullOrEmpty(id)) PlayerPrefs.DeleteKey(Key(id));
            PlayerPrefs.DeleteKey(idsKey);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        private static void RememberId(string sequenceId)
        {
            string idsKey = KeyPrefix + SceneName() + IdsKeySuffix;
            string ids = PlayerPrefs.GetString(idsKey, "");
            if (("|" + ids + "|").Contains("|" + sequenceId + "|")) return;
            PlayerPrefs.SetString(idsKey, string.IsNullOrEmpty(ids) ? sequenceId : ids + "|" + sequenceId);
        }

        private static string Key(string sequenceId) => KeyPrefix + SceneName() + "_" + sequenceId;

        private static string SceneName() => SceneManager.GetActiveScene().name;
    }
}
