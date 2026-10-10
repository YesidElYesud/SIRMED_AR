namespace SIRMED.Managers
{
    using SIRMED.Utils;
    using System;
    using System.Collections;
    using UnityEngine;
    using UnityEngine.Rendering;
    using UnityEngine.Rendering.Universal;

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
    ///   5. (Post-processing) Crear un Volume Profile por etapa
    ///      (Assets > Create > Rendering > Volume Profile) y asignarlo en
    ///      postProcessProfile. No hace falta poner Volumes en la escena: esta
    ///      clase crea un Volume global por perfil como hijo suyo (así funciona
    ///      desde el prefab Managers sin referencias de escena) y hace crossfade
    ///      de weight entre la etapa anterior y la nueva.
    ///
    /// Post-processing en AR: el fondo de cámara lo dibuja la misma cámara, así
    /// que el post afecta TODO el frame (calle real + objetos virtuales); la UI
    /// en Screen Space Overlay no se ve afectada. El URP Asset está en
    /// "Volume Update Mode = Via Scripting" y sin HDR (ajustes de rendimiento),
    /// por eso esta clase llama a UpdateVolumeStack() solo al aplicar/transicionar
    /// y el post de la cámara se enciende solo en las etapas que tienen perfil
    /// (un perfil vacío, sin overrides, cuenta como "sin perfil").
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

        [Serializable]
        public class ParticleSystems
        {
            public ParticleSystem particle;
            public float rateOverTime;
            public float simulationSpeed;
            public ParticleSystemStopBehavior stopBehavior;
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
            [Header("Post-processing (URP Volume)")]
            [Tooltip("Volume Profile de esta etapa (Color Adjustments, Vignette, White Balance…). " +
                     "Vacío = sin post en esta etapa. Al activar la etapa su weight sube a 1 y el " +
                     "de la anterior baja a 0. Es un asset: se puede compartir entre escenas/sitios.")]
            public VolumeProfile postProcessProfile;
            [Tooltip("Duración del crossfade de post-processing (segundos).")]
            [Range(0f, 3f)]
            public float ppFadeDuration = 1.0f;

            // ── Camera ────────────────────────────────────────────────────────────
            public float cameraFarPlane = 125;

            // ── Partículas ────────────────────────────────────────────────────────
            [Header("Partículas")]
            [Tooltip("Sistemas de partículas a ACTIVAR en esta etapa (p.ej. lluvia, chispas).")]
            public ParticleSystems[] particlesToPlay;

            [Tooltip("Sistemas de partículas a DETENER al entrar a esta etapa.")]
            public ParticleSystems[] particlesToStop;

            // ── Clouds ────────────────────────────────────────────────────────────
            [Header("Clouds")]
            public float cloudsAlpha = 0;
            public float cloudsColor = 1;
            public float cloudLayerR;
            public float cloudLayerG;
            public float cloudLayerB;
            public float cloudLayerA;

            // ── Wet Floor ─────────────────────────────────────────────────────────
            [Header("Wet Floot")]
            public float wetFloorRipples;
            public float wetFloorWaves;
            public Color wetFlootColor;

            // ── Transición ────────────────────────────────────────────────────────
            [Header("Transición")]
            [Tooltip("Duración del fade de iluminación y niebla al entrar a esta etapa (segundos).")]
            [Range(0f, 5f)]
            public float transitionDuration = 1.5f;
        }

        // ── Inspector ─────────────────────────────────────────────────────────────
        [Header("Configuración por etapa")]
        [Tooltip("5 entradas: índice 0=Intro, 1=Etapa1 … 4=Etapa4.")]
        public StageVisualConfig[] stageConfigs = new StageVisualConfig[]
        {
            new StageVisualConfig { stageName = "Intro",  ambientIntensity = 1.0f, sunIntensity = 1.0f,  sunColor = new Color(1.0f, 0.95f, 0.85f), transitionDuration = 1.0f },
            new StageVisualConfig { stageName = "Etapa1", ambientIntensity = 1.0f, sunIntensity = 1.0f,  sunColor = new Color(1.0f, 0.95f, 0.85f), transitionDuration = 1.5f },
            new StageVisualConfig { stageName = "Etapa2", ambientIntensity = 0.8f, sunIntensity = 0.7f,  sunColor = new Color(0.9f, 0.9f, 0.95f),  enableFog = true, fogDensity = 0.01f, transitionDuration = 2.0f },
            new StageVisualConfig { stageName = "Etapa3", ambientIntensity = 0.5f, sunIntensity = 0.4f,  sunColor = new Color(0.7f, 0.75f, 0.9f),  enableFog = true, fogDensity = 0.03f, transitionDuration = 1.0f },
            new StageVisualConfig { stageName = "Etapa4", ambientIntensity = 0.3f, sunIntensity = 0.25f, sunColor = new Color(0.5f, 0.55f, 0.7f),  enableFog = true, fogDensity = 0.05f, transitionDuration = 0.8f }
        };

        [Header("Referencias")]
        [Tooltip("Directional Light de la escena. Se busca automáticamente si queda vacío.")]
        public Light sunLight;
        public Material clouds;
        public Material wetFloor;

        [Header("Post-processing")]
        [Tooltip("Enciende 'Post Processing' en la cámara principal en las etapas con perfil. " +
                 "Apagar para medir rendimiento sin post sin tener que vaciar los perfiles.")]
        public bool enablePostProcessing = true;

        [Tooltip("Prioridad de los Volumes por etapa (por encima de cualquier Volume global de la escena).")]
        public float volumePriority = 10f;

        [Header("Debug")]
        public bool debugLogs = false;

        // ── Internos ──────────────────────────────────────────────────────────────
        private Coroutine _lightTransitionRoutine;
        private Coroutine _ppTransitionRoutine;
        private int _currentStageIndex = -1;

        // Un Volume global por etapa (índice = etapa), creado en Awake a partir
        // de stageConfigs[i].postProcessProfile. null si la etapa no tiene perfil.
        private Volume[] _stageVolumes;
        private Camera _postCamera;
        private UniversalAdditionalCameraData _postCameraData;

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

            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            CreateStageVolumes();
        }

        private void Start()
        {
            if (sunLight == null)
                sunLight = FindAnyObjectByType<Light>();

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

            // ── Camera ────────────────────────────────────────────────────────────
            StopCoroutine(CameraFarPlaneRoutine(config));
            StartCoroutine(CameraFarPlaneRoutine(config));

            // ── Iluminación y niebla: con o sin fade ──────────────────────────────
            if (_lightTransitionRoutine != null)
                StopCoroutine(_lightTransitionRoutine);

            if (fade && config.transitionDuration > 0f)
                _lightTransitionRoutine = StartCoroutine(LightTransitionRoutine(config));
            else
                ApplyImmediateLightValues(config);

            // ── Clouds ────────────────────────────────────────────────────────────
            StopCoroutine(CloudsRoutine(config));
            StartCoroutine(CloudsRoutine(config));

            // ── Wet Floor ─────────────────────────────────────────────────────────
            StopCoroutine(WetFloorRoutine(config));
            StartCoroutine(WetFloorRoutine(config));

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

        // ── Post-processing (URP Volumes) ─────────────────────────────────────────
        private void CreateStageVolumes()
        {
            if (stageConfigs == null) return;
            _stageVolumes = new Volume[stageConfigs.Length];

            for (int i = 0; i < stageConfigs.Length; i++)
            {
                // Un perfil sin overrides cuenta como "sin post": no enciende nada.
                var profile = stageConfigs[i]?.postProcessProfile;
                if (profile == null || profile.components.Count == 0) continue;

                var go = new GameObject($"PP_{i}_{stageConfigs[i].stageName}");
                go.transform.SetParent(transform, false);
                go.layer = gameObject.layer; // debe estar en el Volume Mask de la cámara (Default)

                var vol = go.AddComponent<Volume>();
                vol.isGlobal = true;
                vol.priority = volumePriority;
                vol.sharedProfile = profile; // sharedProfile: no clona el asset
                vol.weight = 0f;
                go.SetActive(false);
                _stageVolumes[i] = vol;
            }
        }

        private Volume GetStageVolume(int index)
        {
            return (_stageVolumes != null && index >= 0 && index < _stageVolumes.Length)
                ? _stageVolumes[index]
                : null;
        }

        /// <summary>
        /// Busca la cámara principal (cambia tras LoadScene porque este objeto es
        /// DontDestroyOnLoad).
        /// </summary>
        private bool EnsurePostCamera()
        {
            if (_postCamera == null)
            {
                _postCamera = Camera.main;
                _postCameraData = _postCamera != null ? _postCamera.GetUniversalAdditionalCameraData() : null;
            }
            return _postCameraData != null;
        }

        /// <summary>
        /// El post de la cámara solo está encendido mientras la etapa actual (o un
        /// fade) tiene un Volume: en etapas sin perfil el coste es cero.
        /// </summary>
        private void SetCameraPost(bool on)
        {
            if (_postCameraData != null)
                _postCameraData.renderPostProcessing = enablePostProcessing && on;
        }

        /// <summary>Con Volume Update Mode = Via Scripting los cambios de weight no se ven sin esto.</summary>
        private void RefreshVolumeStack()
        {
            if (_postCameraData == null || !_postCameraData.renderPostProcessing) return;

            // En Start() URP puede no haber inicializado aún el VolumeManager
            // (lanza error). Se reintenta cuando esté listo.
            if (!VolumeManager.instance.isInitialized)
            {
                if (_deferredRefreshRoutine == null)
                    _deferredRefreshRoutine = StartCoroutine(RefreshWhenVolumeManagerReady());
                return;
            }
            _postCamera.UpdateVolumeStack(_postCameraData);
        }

        private Coroutine _deferredRefreshRoutine;

        private IEnumerator RefreshWhenVolumeManagerReady()
        {
            while (!VolumeManager.instance.isInitialized)
                yield return null;
            _deferredRefreshRoutine = null;
            RefreshVolumeStack();
        }

        private void SetOnlyVolumeActive(Volume target)
        {
            if (_stageVolumes == null) return;
            foreach (var v in _stageVolumes)
            {
                if (v == null) continue;
                bool on = v == target;
                v.weight = on ? 1f : 0f;
                v.gameObject.SetActive(on);
            }
        }

        private void CrossfadePostProcessVolume(int targetIndex, float duration, bool fade)
        {
            if (_ppTransitionRoutine != null)
            {
                StopCoroutine(_ppTransitionRoutine);
                _ppTransitionRoutine = null;
            }

            if (!EnsurePostCamera()) return;

            Volume targetVol = GetStageVolume(targetIndex);
            Volume prevVol = GetStageVolume(_currentStageIndex);

            if (!fade || duration <= 0f || prevVol == targetVol)
            {
                SetOnlyVolumeActive(targetVol);
                //SetCameraPost(targetVol != null);
                RefreshVolumeStack();
                return;
            }

            //SetCameraPost(true);
            _ppTransitionRoutine = StartCoroutine(PPFadeRoutine(prevVol, targetVol, duration));
        }

        private IEnumerator PPFadeRoutine(Volume from, Volume to, float duration)
        {
            // Si se interrumpió un fade anterior puede quedar un tercer volumen a medias.
            float fromStart = from != null && from.gameObject.activeSelf ? from.weight : 0f;
            float toStart = to != null && to.gameObject.activeSelf ? to.weight : 0f;
            foreach (var v in _stageVolumes)
            {
                if (v == null || v == from || v == to) continue;
                v.weight = 0f;
                v.gameObject.SetActive(false);
            }
            if (to != null) to.gameObject.SetActive(true);

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));

                if (from != null) from.weight = Mathf.Lerp(fromStart, 0f, t);
                if (to != null) to.weight = Mathf.Lerp(toStart, 1f, t);
                RefreshVolumeStack();

                yield return null;
            }

            SetOnlyVolumeActive(to);
            RefreshVolumeStack();
            //SetCameraPost(to != null);
            _ppTransitionRoutine = null;
        }

        // ── Partículas ────────────────────────────────────────────────────────────
        private void ApplyParticles(StageVisualConfig config)
        {
            if (config.particlesToStop != null)
            {
                foreach (var ps in config.particlesToStop)
                {
                    if (ps.particle == null) continue;
                    ps.particle.Stop(true, ps.stopBehavior);
                }
            }

            if (config.particlesToPlay != null)
            {
                foreach (var ps in config.particlesToPlay)
                {
                    if (ps.particle == null) continue;
                    ps.particle.gameObject.SetActive(true);

                    var mainModule = ps.particle.main;
                    mainModule.simulationSpeed = ps.simulationSpeed;

                    var emissionModule = ps.particle.emission;
                    emissionModule.rateOverTime = ps.rateOverTime;

                    if (!ps.particle.isPlaying) ps.particle.Play();
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

        private IEnumerator CameraFarPlaneRoutine(StageVisualConfig target)
        {
            float startFarPlane = _postCamera.farClipPlane;

            float elapsed = 0f;
            while (elapsed < target.transitionDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / target.transitionDuration));

                _postCamera.farClipPlane = Mathf.Lerp(startFarPlane, target.cameraFarPlane, t);

                yield return null;
            }
        }

        private IEnumerator CloudsRoutine(StageVisualConfig target)
        {
            float startCloudsAlpha = clouds.GetFloat("_Alpha_Power");
            float startCloudsColor = clouds.GetFloat("_Color");
            float startCloudLayerR = clouds.GetFloat("_Layer_R");
            float startCloudLayerG = clouds.GetFloat("_Layer_G");
            float startCloudLayerB = clouds.GetFloat("_Layer_B");
            float startCloudLayerA = clouds.GetFloat("_Layer_A");

            float elapsed = 0f;
            while (elapsed < target.transitionDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / target.transitionDuration));

                clouds.SetFloat("_Alpha_Power", Mathf.Lerp(startCloudsAlpha, target.cloudsAlpha, t));
                clouds.SetFloat("_Color", Mathf.Lerp(startCloudsColor, target.cloudsColor, t));
                clouds.SetFloat("_Layer_R", Mathf.Lerp(startCloudLayerR, target.cloudLayerR, t));
                clouds.SetFloat("_Layer_G", Mathf.Lerp(startCloudLayerG, target.cloudLayerG, t));
                clouds.SetFloat("_Layer_B", Mathf.Lerp(startCloudLayerB, target.cloudLayerB, t));
                clouds.SetFloat("_Layer_A", Mathf.Lerp(startCloudLayerA, target.cloudLayerA, t));

                yield return null;
            }
        }

        private IEnumerator WetFloorRoutine(StageVisualConfig target)
        {
            float startRipples = wetFloor.GetFloat("_Ripples_Strength");
            float startWaves = wetFloor.GetFloat("_Waves_Strength");
            Color startColor = wetFloor.GetColor("_Color");

            float elapsed = 0f;
            while (elapsed < target.transitionDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / target.transitionDuration));

                wetFloor.SetFloat("_Ripples_Strength", Mathf.Lerp(startRipples, target.wetFloorRipples, t));
                wetFloor.SetFloat("_Waves_Strength", Mathf.Lerp(startWaves, target.wetFloorWaves, t));
                wetFloor.SetColor("_Color", Color.Lerp(startColor, target.wetFlootColor, t));

                yield return null;
            }
        }
    }
}
