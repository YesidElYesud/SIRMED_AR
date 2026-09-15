namespace SIRMED.Managers
{
    using System;
    using System.Collections;
    using UnityEngine;

    /// <summary>
    /// AudioStageManager — Gestión de audio ambiental por etapa con crossfade.
    ///
    /// Responsabilidades:
    ///   - Reproducir un AudioClip diferente por etapa (ambiente, alarma, lluvia, etc.).
    ///   - Hacer crossfade suave entre etapas usando dos AudioSources alternos.
    ///   - Exponer PlaySFX() para sonidos puntuales (truenos, alertas, UI).
    ///   - Permitir silenciar/restaurar audio durante diálogos u otros sistemas.
    ///
    /// Setup en escena:
    ///   1. Crear GameObject vacío "AudioStageManager" en la raíz.
    ///   2. Adjuntar este script.
    ///   3. Agregar 2 componentes AudioSource al mismo GameObject
    ///      (se asignan automáticamente si se dejan vacíos).
    ///   4. Configurar stageAudios con 6 entradas (índice = etapa).
    ///   5. Asignar los AudioClips en el Inspector.
    /// </summary>
    public class AudioStageManager : MonoBehaviour
    {
        // ── Singleton ─────────────────────────────────────────────────────────────
        public static AudioStageManager Instance { get; private set; }

        // ── Datos de audio por etapa ──────────────────────────────────────────────
        [Serializable]
        public class StageAudio
        {
            [Tooltip("Nombre descriptivo (solo visual en el Inspector)")]
            public string stageName;

            [Tooltip("Música o ambiente para esta etapa. Null = silencio.")]
            public AudioClip clip;

            [Tooltip("Volumen objetivo para esta etapa (0–1).")]
            [Range(0f, 1f)]
            public float volume = 0.5f;

            [Tooltip("Tiempo de crossfade al entrar a esta etapa (segundos).")]
            [Range(0f, 5f)]
            public float fadeTime = 1.5f;
        }

        // ── Inspector ─────────────────────────────────────────────────────────────
        [Header("Audio por etapa")]
        [Tooltip("6 entradas: índice 0=Intro, 1=Etapa1 … 5=Etapa5.\n" +
                 "Deja el clip en null para silencio en esa etapa.")]
        public StageAudio[] stageAudios = new StageAudio[]
        {
            new StageAudio { stageName = "Intro",  volume = 0.4f, fadeTime = 1f },
            new StageAudio { stageName = "Etapa1", volume = 0.5f, fadeTime = 1.5f },
            new StageAudio { stageName = "Etapa2", volume = 0.5f, fadeTime = 1.5f },
            new StageAudio { stageName = "Etapa3", volume = 0.7f, fadeTime = 0.8f },
            new StageAudio { stageName = "Etapa4", volume = 0.8f, fadeTime = 0.5f },
            new StageAudio { stageName = "Etapa5", volume = 0.5f, fadeTime = 2f },
        };

        [Header("AudioSources (se crean automáticamente si quedan vacíos)")]
        [Tooltip("Canal A de audio ambiental.")]
        public AudioSource sourceA;

        [Tooltip("Canal B de audio ambiental (crossfade).")]
        public AudioSource sourceB;

        [Tooltip("Canal para efectos de sonido puntuales (SFX).")]
        public AudioSource sourceSFX;

        [Header("Comportamiento")]
        [Tooltip("Volumen maestro global (multiplica el volumen por etapa).")]
        [Range(0f, 1f)]
        public float masterVolume = 1f;

        // ── Internos ──────────────────────────────────────────────────────────────
        private AudioSource _active;   // fuente que suena actualmente
        private AudioSource _inactive; // fuente que entra con fade-in
        private Coroutine _fadeRoutine;
        private float _preMuteVolume = -1f; // para restaurar tras mute

        // ── Lifecycle ─────────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            EnsureAudioSources();
        }

        private void Start()
        {
            if (StageManager.Instance != null)
                StageManager.Instance.OnStageChanged += OnStageChanged;

            // No reproducir audio en Start(): iOS suspende el AudioContext hasta
            // recibir un gesto del usuario. El audio arranca cuando StageManager
            // pasa de Intro a Etapa1 (botón "Comenzar"), momento en que
            // OnStageChanged ya puede llamar Play() con el contexto activo.
        }

        private void OnDestroy()
        {
            if (StageManager.Instance != null)
                StageManager.Instance.OnStageChanged -= OnStageChanged;
        }

        // ── Reacción al cambio de etapa ───────────────────────────────────────────
        private void OnStageChanged(StageManager.Stage previous, StageManager.Stage current)
        {
            ApplyStageAudio((int)current, fade: true);
        }

        // ── API pública ───────────────────────────────────────────────────────────

        /// <summary>
        /// Reproduce un sonido puntual (trueno, alerta, UI) sin interrumpir el ambiente.
        /// </summary>
        public void PlaySFX(AudioClip clip, float volume = 1f)
        {
            if (clip == null || sourceSFX == null) return;
            sourceSFX.PlayOneShot(clip, volume * masterVolume);
        }

        /// <summary>
        /// Baja gradualmente el volumen del ambiente a cero.
        /// Llama RestoreVolume() para volver al nivel normal.
        /// </summary>
        public void MuteAmbient(float fadeDuration = 0.5f)
        {
            if (_preMuteVolume >= 0f) return; // ya está silenciado / duckeado
            _preMuteVolume = _active != null ? _active.volume : 0f;
            StartCoroutine(FadeSource(_active, 0f, fadeDuration));
        }

        /// <summary>
        /// Baja gradualmente el volumen del ambiente a un nivel reducido (duck).
        /// Úsalo durante diálogos de NPC para que la voz se oiga sobre el ambiente.
        /// Llama RestoreVolume() para volver al nivel normal al cerrar el diálogo.
        /// </summary>
        /// <param name="targetVolume">Volumen duckeado (0–1). Por defecto 0.15.</param>
        /// <param name="fadeDuration">Segundos del fade-down.</param>
        public void DuckAmbient(float targetVolume = 0.15f, float fadeDuration = 0.4f)
        {
            if (_preMuteVolume >= 0f) return; // ya está duckeado o silenciado — no re-entrar
            _preMuteVolume = _active != null ? _active.volume : 0f;
            if (_active != null)
                StartCoroutine(FadeSource(_active, targetVolume * masterVolume, fadeDuration));
        }

        /// <summary>Restaura el volumen del ambiente tras un MuteAmbient() o DuckAmbient().</summary>
        public void RestoreVolume(float fadeDuration = 0.5f)
        {
            if (_preMuteVolume < 0f) return;
            float target = _preMuteVolume;
            _preMuteVolume = -1f;
            StartCoroutine(FadeSource(_active, target, fadeDuration));
        }

        // ── Lógica interna ────────────────────────────────────────────────────────
        private void ApplyStageAudio(int stageIndex, bool fade)
        {
            if (stageAudios == null || stageIndex >= stageAudios.Length) return;

            StageAudio config = stageAudios[stageIndex];
            float targetVolume = config.volume * masterVolume;

            // Si el clip es el mismo que ya está sonando, solo ajustamos el volumen
            if (_active != null && _active.clip == config.clip && _active.isPlaying)
            {
                if (fade)
                    StartCoroutine(FadeSource(_active, targetVolume, config.fadeTime));
                else
                    _active.volume = targetVolume;
                return;
            }

            if (_fadeRoutine != null)
                StopCoroutine(_fadeRoutine);

            _fadeRoutine = fade
                ? StartCoroutine(CrossfadeTo(config.clip, targetVolume, config.fadeTime))
                : null;

            if (!fade)
                PlayImmediate(config.clip, targetVolume);
        }

        private void PlayImmediate(AudioClip clip, float volume)
        {
            if (_active == null) return;
            _active.clip = clip;
            _active.volume = volume;
            _active.loop = true;

            if (clip != null) _active.Play();
            else _active.Stop();
        }

        private IEnumerator CrossfadeTo(AudioClip newClip, float targetVolume, float duration)
        {
            // Intercambiar fuentes: la inactiva entra, la activa sale
            (_active, _inactive) = (_inactive, _active);

            // Preparar la nueva fuente (entra)
            _active.clip = newClip;
            _active.volume = 0f;
            _active.loop = true;
            if (newClip != null) _active.Play();

            float elapsed = 0f;
            float startVolumeOut = _inactive != null ? _inactive.volume : 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                if (_active != null) _active.volume = Mathf.Lerp(0f, targetVolume, t);
                if (_inactive != null) _inactive.volume = Mathf.Lerp(startVolumeOut, 0f, t);

                yield return null;
            }

            // Finalizar: detener la fuente saliente
            if (_inactive != null)
            {
                _inactive.Stop();
                _inactive.clip = null;
            }

            _fadeRoutine = null;
        }

        private IEnumerator FadeSource(AudioSource source, float targetVolume, float duration)
        {
            if (source == null) yield break;

            float start = source.volume;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                source.volume = Mathf.Lerp(start, targetVolume, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }

            source.volume = targetVolume;
        }

        // ── Inicialización de AudioSources ────────────────────────────────────────
        private void EnsureAudioSources()
        {
            AudioSource[] existing = GetComponents<AudioSource>();

            sourceA = GetOrCreate(existing, 0, "AmbientA");
            sourceB = GetOrCreate(existing, 1, "AmbientB");
            sourceSFX = GetOrCreate(existing, 2, "SFX");

            ConfigureAmbientSource(sourceA);
            ConfigureAmbientSource(sourceB);

            sourceSFX.playOnAwake = false;
            sourceSFX.loop = false;

            _active = sourceA;
            _inactive = sourceB;
        }

        private AudioSource GetOrCreate(AudioSource[] existing, int index, string label)
        {
            if (existing.Length > index && existing[index] != null)
                return existing[index];

            AudioSource s = gameObject.AddComponent<AudioSource>();
            s.name = label; // solo informativo
            return s;
        }

        private void ConfigureAmbientSource(AudioSource s)
        {
            s.playOnAwake = false;
            s.loop = true;
            s.spatialBlend = 0f; // audio 2D (ambiente global)
            s.volume = 0f;
        }
    }
}
