using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;

namespace SIRMED.Utils
{
    /// <summary>
    /// Aplica la estimación de luz HDR de ARCore/ARKit (dirección, color e intensidad de la luz
    /// principal + armónicos esféricos del ambiente) a la Directional Light de la escena, para que
    /// los objetos virtuales se iluminen como el entorno real.
    ///
    /// Convive con <see cref="SIRMED.Managers.VisualEffectsStageController"/>: la estimación da la
    /// base real y la etapa narrativa la modula encima vía <see cref="SetStageModulation"/>
    /// (tinte + multiplicadores), así se conserva el oscurecimiento progresivo hacia las etapas de
    /// mayor riesgo. Mientras el dispositivo no entregue estimación (<see cref="HasLightEstimate"/> /
    /// <see cref="ProvidesAmbient"/> en false), este componente no toca la luz ni el ambiente y la
    /// etapa los controla directamente, como antes.
    ///
    /// Requiere que el ARCameraManager tenga "Light Estimation" activado (al menos
    /// Main Light Direction + Main Light Intensity + Ambient Spherical Harmonics).
    /// </summary>
    public class HDRLightEstimation : MonoBehaviour
    {
        public static HDRLightEstimation Instance { get; private set; }

        [SerializeField]
        [Tooltip("ARCameraManager que produce los frames con la estimación de luz.")]
        private ARCameraManager m_CameraManager;

        [SerializeField]
        [Tooltip("Directional Light a la que se aplica la estimación (la misma que usa VisualEffectsStageController).")]
        private Light m_Light;

        /// <summary>True desde que se recibió al menos una estimación de intensidad de la luz principal.</summary>
        public bool HasLightEstimate { get; private set; }

        /// <summary>True desde que se recibieron armónicos esféricos del ambiente.</summary>
        public bool ProvidesAmbient { get; private set; }

        public Light TargetLight => m_Light;

        // Última estimación cruda recibida
        private float _estimatedIntensity = 1f;
        private Color _estimatedColor = Color.white;
        private SphericalHarmonicsL2 _estimatedAmbient;

        // Modulación de la etapa actual (1 = neutra, igual que Intro/Etapa1)
        private Color _stageTint = Color.white;
        private float _stageSunMultiplier = 1f;
        private float _stageAmbientMultiplier = 1f;

        void OnEnable()
        {
            Instance = this;
            if (m_CameraManager != null)
                m_CameraManager.frameReceived += FrameChanged;
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
            if (m_CameraManager != null)
                m_CameraManager.frameReceived -= FrameChanged;
        }

        /// <summary>
        /// Modulación de la etapa narrativa sobre la luz real. La llama VisualEffectsStageController
        /// en cada paso de su transición.
        /// </summary>
        public void SetStageModulation(Color tint, float sunMultiplier, float ambientMultiplier)
        {
            _stageTint = tint;
            _stageSunMultiplier = sunMultiplier;
            _stageAmbientMultiplier = ambientMultiplier;
            Apply();
        }

        void FrameChanged(ARCameraFrameEventArgs args)
        {
            var estimate = args.lightEstimation;

            // Intensidad: preferir la de la luz principal (modo HDR); si no, el brillo promedio.
            if (estimate.averageMainLightBrightness.HasValue)
            {
                _estimatedIntensity = estimate.averageMainLightBrightness.Value;
                HasLightEstimate = true;
            }
            else if (estimate.averageBrightness.HasValue)
            {
                _estimatedIntensity = estimate.averageBrightness.Value;
                HasLightEstimate = true;
            }

            // Color: preferir el de la luz principal; si no, la corrección de color.
            if (estimate.mainLightColor.HasValue)
                _estimatedColor = estimate.mainLightColor.Value;
            else if (estimate.colorCorrection.HasValue)
                _estimatedColor = estimate.colorCorrection.Value;

            if (m_Light != null && estimate.mainLightDirection.HasValue)
                m_Light.transform.rotation = Quaternion.LookRotation(estimate.mainLightDirection.Value);

            if (estimate.ambientSphericalHarmonics.HasValue)
            {
                _estimatedAmbient = estimate.ambientSphericalHarmonics.Value;
                ProvidesAmbient = true;
            }

            Apply();
        }

        private void Apply()
        {
            if (m_Light != null && HasLightEstimate)
            {
                m_Light.color = _estimatedColor * _stageTint;
                m_Light.intensity = _estimatedIntensity * _stageSunMultiplier;
            }

            if (ProvidesAmbient)
            {
                RenderSettings.ambientMode = AmbientMode.Skybox;
                RenderSettings.ambientProbe = _estimatedAmbient * _stageAmbientMultiplier;
            }
        }
    }
}
