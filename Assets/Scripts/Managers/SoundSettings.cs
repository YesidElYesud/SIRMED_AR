namespace SIRMED.Managers
{
    using System;
    using UnityEngine;

    /// <summary>
    /// SoundSettings — Preferencias de sonido del jugador (botón SONIDOS del HUD).
    ///
    ///   - Volumen general (0–1) → AudioListener.volume.
    ///   - Silenciar todo        → AudioListener.volume = 0 (voces incluidas).
    ///   - Silenciar efectos     → AudioSource.mute en las fuentes de efectos: ambiente
    ///     por etapa (AudioStageManager), sirena (SirenController) y obturador
    ///     (ScreenshotButton). Las voces del Director y de los NPC siguen sonando.
    ///
    /// Estático y sin escena: se carga de PlayerPrefs antes de la primera escena, así
    /// el volumen guardado aplica aunque el panel de ajustes nunca se abra. Las fuentes
    /// de efectos se suscriben a Changed y llaman ApplyEffects() sobre sus AudioSource
    /// (mute no interfiere con los fades/ducking que esos scripts hacen con volume).
    /// La UI vive en SIRMED.UI.SettingsPanel.
    /// </summary>
    public static class SoundSettings
    {
        // "settings_sound_on" es la clave del SettingsPanel anterior (1 = sonido activo).
        private const string KEY_SOUND_ON = "settings_sound_on";
        private const string KEY_EFFECTS_ON = "settings_effects_on";
        private const string KEY_VOLUME = "settings_volume";

        /// <summary>Se dispara tras cualquier cambio de ajuste.</summary>
        public static event Action Changed;

        public static bool AllMuted { get; private set; }
        public static bool EffectsMuted { get; private set; }
        public static float Volume { get; private set; } = 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Load()
        {
            AllMuted = PlayerPrefs.GetInt(KEY_SOUND_ON, 1) == 0;
            EffectsMuted = PlayerPrefs.GetInt(KEY_EFFECTS_ON, 1) == 0;
            Volume = Mathf.Clamp01(PlayerPrefs.GetFloat(KEY_VOLUME, 1f));
            ApplyListener();
        }

        // ── API pública ───────────────────────────────────────────────────────────
        public static void SetAllMuted(bool muted)
        {
            if (AllMuted == muted) return;
            AllMuted = muted;
            PlayerPrefs.SetInt(KEY_SOUND_ON, muted ? 0 : 1);
            Commit();
        }

        public static void SetEffectsMuted(bool muted)
        {
            if (EffectsMuted == muted) return;
            EffectsMuted = muted;
            PlayerPrefs.SetInt(KEY_EFFECTS_ON, muted ? 0 : 1);
            Commit();
        }

        public static void SetVolume(float volume)
        {
            volume = Mathf.Clamp01(volume);
            if (Mathf.Approximately(Volume, volume)) return;
            Volume = volume;
            PlayerPrefs.SetFloat(KEY_VOLUME, volume);
            // Sin PlayerPrefs.Save(): el slider llama esto en cada frame del arrastre y
            // escribir a disco cada vez traba la UI en el teléfono. Se guarda con Save().
            Commit(save: false);
        }

        /// <summary>Escribe a disco los ajustes pendientes (p. ej. el volumen tras arrastrar).</summary>
        public static void Save() => PlayerPrefs.Save();

        /// <summary>Aplica "silenciar efectos" a una fuente de efectos (null se ignora).</summary>
        public static void ApplyEffects(AudioSource source)
        {
            if (source != null) source.mute = EffectsMuted;
        }

        // ── Privados ──────────────────────────────────────────────────────────────
        private static void Commit(bool save = true)
        {
            if (save) PlayerPrefs.Save();
            ApplyListener();
            Changed?.Invoke();
        }

        private static void ApplyListener() =>
            AudioListener.volume = AllMuted ? 0f : Volume;
    }
}
