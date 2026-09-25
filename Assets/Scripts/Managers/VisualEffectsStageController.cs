namespace SIRMED.Managers
{
    using System;
    using System.Collections;
    using UnityEngine;
    using UnityEngine.Rendering;
    using SIRMED.Utils;

#if UNITY_POST_PROCESSING_STACK_V2
    using UnityEngine.Rendering.PostProcessing;
#endif

    /// <summary>
    /// VisualEffectsStageController — Iluminación/niebla/partículas por etapa.
    ///
    /// Patrón idéntico a AudioStageManager:
    ///   - Singleton + DontDestroyOnLoad
    ///   - Se suscribe a StageManager.OnStageChanged
    ///   - Configuración serializada por etapa (6 entradas: Intro → Etapa5)
    ///
    /// Nota de portado (SATCS → SIRMED_AR): en el proyecto WebGL original esta
    /// clase también cambiaba el material del skybox por etapa. En AR real el
    /// fondo de cámara se renderiza antes que los objetos opacos
    /// (CameraBackgroundRenderingMode.BeforeOpaques, ver HotspotSurveyController),
    /// así que el skybox queda completamente tapado por el feed de la cámara real
    /// y cambiarlo no tiene ningún efecto visible. Por eso se eliminó el campo
    /// skyboxMaterial y el modo ambiente "Skybox" (que dependía de él); los
    /// presets por etapa usan FlatColor, que sí afecta la iluminación de los
    /// objetos 3D virtuales (hotspots, marcadores, NPCs) aunque el cielo real
    /// nunca cambie. sunLight (color/intensidad) y niebla se conservan igual
    /// porque también afectan a los objetos virtuales.
    ///
    /// Setup en escena:
    ///   1. Crear GameObject vacío "VisualEffectsStageController" en la raíz.
    ///   2. Adjuntar este script.
    ///   3. Asignar stageConfigs con 6 entradas en el Inspector.
    ///   4. (Opcional) Arrastrar la Directional Light a sunLight.
    ///   5. (Post-processing) Crear volúmenes globales en escena y asignarlos
    ///      en el campo postProcessVolume de cada etapa. Solo el volumen
    ///      de la etapa activa tendrá weight=1; los demás weight=0.
    ///
    /// Convivencia con HDRLightEstimation: si hay uno activo en escena y el
    /// dispositivo ya entrega estimación de luz real, esta clase deja de
    /// escribir el sol y el ambiente directamente — en su lugar le pasa
    /// sunColor/sunIntensity/ambientIntensity como tinte y multiplicadores
    /// sobre la luz real (SetStageModulation). Intro/Etapa1 (1.0) quedan como
    /// base neutra = luz real tal cual. Sin estimación, comportamiento de siempre.
    /// </summary>
    public class VisualEffectsStageController : MonoBehaviour
    {
        // ── Singleton ─────────────────────────────────────────────────────────────
        public static VisualEffectsStageController Instance { get; private set; }

        // ── Modo de ambiente ──────────────────────────────────────────────────────
        public enum AmbientSourceMode
        {
            /// <summary>Color plano. Usa ambientColor × ambientIntensity. Afecta objetos 3D virtuales.</summary>
            FlatColor,
            /// <summary>Trilight: sky/equator/ground por separado.</summary>
            Trilight
        }

        // ── Datos visuales por etapa ──────────────────────────────────────────────
        [Serializable]
        public class StageVisualConfig
        {
            [Tooltip("Nombre descriptivo (solo visual en el Inspector)")]
            public string stageName;

            // ── Iluminación Ambiental ─────────────────────────────────────────────
            [Header("Iluminación Ambiental (afecta objetos 3D virtuales)")]
            [Tooltip("FlatColor: color × intensidad. Trilight: sky/equator/ground.")]
            public AmbientSourceMode ambientMode = AmbientSourceMode.FlatColor;

            [Tooltip("Color de luz ambiental (solo modo FlatColor).")]
            public Color ambientColor = new Color(0.2f, 0.2f, 0.2f);

            [Tooltip("Intensidad de la luz ambiental (0–8).")]
            [Range(0f, 8f)]
            public float ambientIntensity = 1f;

            [Tooltip("Sky color — solo modo Trilight.")]
            public Color ambientSkyColor = new Color(0.5f, 0.7f, 1.0f);
            [Tooltip("Equator color — solo modo Trilight.")]
            public Color ambientEquatorColor = new Color(0.4f, 0.4f, 0.4f);
            [Tooltip("Ground color — solo modo Trilight.")]
            public Color ambientGroundColor = new Color(0.2f, 0.2f, 0.1f);

            // ── Sol (Directional Light) ───────────────────────────────────────────
            [Header("Sol (Directional Light) — afecta objetos 3D virtuales")]
            [Tooltip("Color del sol para esta etapa. Alpha ignorado.")]
            public Color sunColor = Color.white;

            [Tooltip("Intensidad del sol (0–2).")]
            [Range(0f, 2f)]
            public float sunIntensity = 1f;

            // ── Niebla ────────────────────────────────────────────────────────────
            [Header("Niebla (afecta objetos 3D virtuales, no el fondo de cámara real)")]
            [Tooltip("Activar niebla en esta etapa.")]
            public bool enableFog = false;

            [Tooltip("Color de la niebla.")]
            public Color fogColor = new Color(0.5f, 0.5f, 0.5f);

            [Tooltip("Densidad de la niebla (modo Exponential).")]
            [Range(0f, 0.1f)]
            public float fogDensity = 0.02f;

            // ── Post-processing ───────────────────────────────────────────────────
            [Header("Post-processing (requiere Post Processing Stack v2)")]
            [Tooltip("Volumen de post-processing exclusivo de esta etapa. " +
                     "Al activar la etapa su weight sube a 1; el de la etapa anterior baja a 0. " +
                     "Crear GameObjects con PostProcessVolume en escena y arrastrarlo aquí.")]
#if UNITY_POST_PROCESSING_STACK_V2
            public PostProcessVolume postProcessVolume;
#else
            public UnityEngine.Object postProcessVolume;   // placeholder si el paquete no está
#endif
            [Tooltip("Duración del crossfade de post-processing (segundos).")]
            [Range(0f, 3f)]
            public float ppFadeDuration = 1.0f;

            // ── Partículas ────────────────────────────────────────────────────────
            [Header("Partículas")]
            [Tooltip("Sistemas de partículas a ACTIVAR en esta etapa (p.ej. lluvia, chispas).")]
            public ParticleSystem[] particlesToPlay;

            [Tooltip("Sistemas de partículas a DETENER al entrar a esta etapa.")]
            public ParticleSystem[] particlesToStop;

            // ── Transición ────────────────────────────────────────────────────────
            [Header("Transición")]
            [Tooltip("Duración del fade de iluminación y niebla al entrar a esta etapa (segundos).")]
            [Range(0f, 5f)]
            public float transitionDuration = 1.5f;
        }

        // ── Inspector ─────────────────────────────────────────────────────────────
        [Header("Configuración por etapa")]
        [Tooltip("6 entradas: índice 0=Intro, 1=Etapa1 … 5=Etapa5.")]
        public StageVisualConfig[] stageConfigs = new StageVisualConfig[]
        {
            new StageVisualConfig { stageName = "Intro",  ambientIntensity = 1.0f, sunIntensity = 1.0f,  sunColor = new Color(1.0f, 0.95f, 0.85f), transitionDuration = 1.0f },
            new StageVisualConfig { stageName = "Etapa1", ambientIntensity = 1.0f, sunIntensity = 1.0f,  sunColor = new Color(1.0f, 0.95f, 0.85f), transitionDuration = 1.5f },
            new StageVisualConfig { stageName = "Etapa2", ambientIntensity = 0.8f, sunIntensity = 0.7f,  sunColor = new Color(0.9f, 0.9f, 0.95f),  enableFog = true, fogDensity = 0.01f, transitionDuration = 2.0f },
            new StageVisualConfig { stageName = "Etapa3", ambientIntensity = 0.5f, sunIntensity = 0.4f,  sunColor = new Color(0.7f, 0.75f, 0.9f),  enableFog = true, fogDensity = 0.03f, transitionDuration = 1.0f },
            new StageVisualConfig { stageName = "Etapa4", ambientIntensity = 0.3f, sunIntensity = 0.25f, sunColor = new Color(0.5f, 0.55f, 0.7f),  enableFog = true, fogDensity = 0.05f, transitionDuration = 0.8f },
            new StageVisualConfig { stageName = "Etapa5", ambientIntensity = 0.9f, sunIntensity = 0.8f,  sunColor = new Color(0.9f, 0.85f, 0.75f), enableFog = false, transitionDuration = 2.5f },
        };

        [Header("Referencias")]
        [Tooltip("Directional Light de la escena. Se busca automáticamente si queda vacío.")]
        public Light sunLight;

        [Header("Debug")]
        public bool debugLogs = false;

        // ── Internos ──────────────────────────────────────────────────────────────
        private Coroutine _lightTransitionRoutine;
        private Coroutine _ppTransitionRoutine;
        private int _currentStageIndex = -1;

        // Valores de sol/ambiente de la etapa actual (lo que se interpola en las
        // transiciones). No se leen de sunLight porque con HDRLightEstimation
        // activo la luz contiene estimación real × estos valores.
        private Color _stageSunColor = Color.white;
        private float _stageSunIntensity = 1f;
        private float _stageAmbientIntensity = 1f;

        // ── Lifecycle ─────────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            if (sunLight == null)
                sunLight = FindAnyObjectByType<Light>();

            // Desactivar todos los volúmenes de PP al inicio
            SetAllPostProcessVolumesOff();

            if (StageManager.Instance != null)
                StageManager.Instance.OnStageChanged += OnStageChanged;

            // Aplicar visuals de la etapa inicial sin transición
            if (StageManager.Instance != null)
                ApplyVisuals((int)StageManager.Instance.CurrentStage, fade: false);
        }

        private void OnDestroy()
        {
            if (StageManager.Instance != null)
                StageManager.Instance.OnStageChanged -= OnStageChanged;
        }

        // ── Reacción al cambio de etapa ───────────────────────────────────────────
        private void OnStageChanged(StageManager.Stage previous, StageManager.Stage current)
        {
            ApplyVisuals((int)current, fade: true);
        }

        // ── API pública ───────────────────────────────────────────────────────────
        /// <summary>Fuerza un cambio de visuals a una etapa concreta (sin necesidad de cambiar de Stage).</summary>
        public void ForceApplyStage(int stageIndex, bool fade = true)
        {
            ApplyVisuals(stageIndex, fade);
        }

        // ── Lógica principal ──────────────────────────────────────────────────────
        private void ApplyVisuals(int stageIndex, bool fade)
        {
            if (stageConfigs == null || stageIndex < 0 || stageIndex >= stageConfigs.Length) return;

            StageVisualConfig config = stageConfigs[stageIndex];
            if (config == null) return;

            if (debugLogs)
                Debug.Log($"[VisualFX] Etapa {stageIndex}: {config.stageName} | fade={fade}");

            // ── Modo ambiente: flat o trilight ─────────────────────────────────────
            ApplyAmbientMode(config);

            // ── Partículas: inmediatas ────────────────────────────────────────────
            ApplyParticles(config);

            // ── Post-processing: fade entre volúmenes ─────────────────────────────
            CrossfadePostProcessVolume(stageIndex, config.ppFadeDuration, fade);

            // ── Iluminación y niebla: con o sin fade ──────────────────────────────
            if (_lightTransitionRoutine != null)
                StopCoroutine(_lightTransitionRoutine);

            if (fade && config.transitionDuration > 0f)
                _lightTransitionRoutine = StartCoroutine(LightTransitionRoutine(config));
            else
                ApplyImmediateLightValues(config);

            _currentStageIndex = stageIndex;
        }

        // ── Ambient mode ──────────────────────────────────────────────────────────
        private void ApplyAmbientMode(StageVisualConfig config)
        {
            // El ambiente real (armónicos esféricos) lo pone HDRLightEstimation,
            // modulado por ambientIntensity vía SetStageModulation.
            if (HDRLightEstimation.Instance != null && HDRLightEstimation.Instance.ProvidesAmbient)
                return;

            switch (config.ambientMode)
            {
                case AmbientSourceMode.Trilight:
                    RenderSettings.ambientMode = AmbientMode.Trilight;
                    RenderSettings.ambientSkyColor = config.ambientSkyColor * config.ambientIntensity;
                    RenderSettings.ambientEquatorColor = config.ambientEquatorColor * config.ambientIntensity;
                    RenderSettings.ambientGroundColor = config.ambientGroundColor * config.ambientIntensity;
                    break;

                default: // FlatColor
                    RenderSettings.ambientMode = AmbientMode.Flat;
                    RenderSettings.ambientLight = config.ambientColor * config.ambientIntensity;
                    break;
            }
        }

        // ── Post-processing crossfade ─────────────────────────────────────────────
        private void SetAllPostProcessVolumesOff()
        {
            if (stageConfigs == null) return;
            foreach (var cfg in stageConfigs)
            {
#if UNITY_POST_PROCESSING_STACK_V2
                if (cfg?.postProcessVolume != null)
                    cfg.postProcessVolume.weight = 0f;
#endif
            }
        }

        private void CrossfadePostProcessVolume(int targetIndex, float duration, bool fade)
        {
#if UNITY_POST_PROCESSING_STACK_V2
            if (_ppTransitionRoutine != null)
                StopCoroutine(_ppTransitionRoutine);

            var targetVol = stageConfigs[targetIndex]?.postProcessVolume;

            if (!fade || duration <= 0f)
            {
                SetAllPostProcessVolumesOff();
                if (targetVol != null) targetVol.weight = 1f;
                return;
            }

            // Obtener volumen anterior
            PostProcessVolume prevVol = (_currentStageIndex >= 0 && _currentStageIndex < stageConfigs.Length)
                ? stageConfigs[_currentStageIndex]?.postProcessVolume
                : null;

            _ppTransitionRoutine = StartCoroutine(PPFadeRoutine(prevVol, targetVol, duration));
#endif
        }

#if UNITY_POST_PROCESSING_STACK_V2
        private IEnumerator PPFadeRoutine(PostProcessVolume from, PostProcessVolume to, float duration)
        {
            float elapsed = 0f;
            if (to != null) to.gameObject.SetActive(true);

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));

                if (from != null) from.weight = 1f - t;
                if (to != null) to.weight = t;

                yield return null;
            }

            // Valores finales exactos
            if (from != null) { from.weight = 0f; }
            if (to != null) { to.weight = 1f; }

            // Desactivar volúmenes inactivos para ahorrar GPU
            foreach (var cfg in stageConfigs)
            {
                if (cfg?.postProcessVolume == null) continue;
                if (cfg.postProcessVolume != to)
                    cfg.postProcessVolume.gameObject.SetActive(false);
            }

            _ppTransitionRoutine = null;
        }
#endif

        // ── Partículas ────────────────────────────────────────────────────────────
        private void ApplyParticles(StageVisualConfig config)
        {
            if (config.particlesToPlay != null)
            {
                foreach (var ps in config.particlesToPlay)
                {
                    if (ps == null) continue;
                    ps.gameObject.SetActive(true);
                    if (!ps.isPlaying) ps.Play();
                }
            }

            if (config.particlesToStop != null)
            {
                foreach (var ps in config.particlesToStop)
                {
                    if (ps == null) continue;
                    ps.Stop();
                    ps.gameObject.SetActive(false);
                }
            }
        }

        // ── Valores de luz / niebla ───────────────────────────────────────────────
        private void ApplyImmediateLightValues(StageVisualConfig config)
        {
            RenderSettings.fog = config.enableFog;
            RenderSettings.fogColor = config.fogColor;
            RenderSettings.fogDensity = config.fogDensity;
            RenderSettings.fogMode = FogMode.ExponentialSquared;

            ApplySunValues(config.sunColor, config.sunIntensity, config.ambientIntensity);
        }

        /// <summary>
        /// Aplica sol/ambiente de la etapa: como modulación sobre la luz real si
        /// HDRLightEstimation está entregando estimación, o directo a sunLight si no.
        /// </summary>
        private void ApplySunValues(Color sunColor, float sunIntensity, float ambientIntensity)
        {
            _stageSunColor = sunColor;
            _stageSunIntensity = sunIntensity;
            _stageAmbientIntensity = ambientIntensity;

            var hdr = HDRLightEstimation.Instance;
            if (hdr != null)
                hdr.SetStageModulation(sunColor, sunIntensity, ambientIntensity);

            bool lightDrivenByEstimate = hdr != null && hdr.HasLightEstimate && hdr.TargetLight == sunLight;
            if (sunLight != null && !lightDrivenByEstimate)
            {
                sunLight.color = sunColor;
                sunLight.intensity = sunIntensity;
            }
        }

        private IEnumerator LightTransitionRoutine(StageVisualConfig target)
        {
            float duration = target.transitionDuration;
            float elapsed = 0f;

            // Capturar valores de inicio
            Color startFogColor = RenderSettings.fogColor;
            float startFogDensity = RenderSettings.fogDensity;
            Color startSunColor = _stageSunColor;
            float startSunIntensity = _stageSunIntensity;
            float startAmbientIntensity = _stageAmbientIntensity;

            float endFogDensity = target.enableFog ? target.fogDensity : 0f;
            if (target.enableFog) RenderSettings.fog = true;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));

                RenderSettings.fogColor = Color.Lerp(startFogColor, target.fogColor, t);
                RenderSettings.fogDensity = Mathf.Lerp(startFogDensity, endFogDensity, t);

                ApplySunValues(
                    Color.Lerp(startSunColor, target.sunColor, t),
                    Mathf.Lerp(startSunIntensity, target.sunIntensity, t),
                    Mathf.Lerp(startAmbientIntensity, target.ambientIntensity, t));

                yield return null;
            }

            ApplyImmediateLightValues(target);
            if (!target.enableFog) RenderSettings.fog = false;

            _lightTransitionRoutine = null;
        }
    }
}
