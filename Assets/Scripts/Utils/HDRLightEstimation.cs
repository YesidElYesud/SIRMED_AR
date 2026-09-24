using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;

namespace Assets.Scripts.Utils
{
    /// <summary>
    /// A component that can be used to access the most recently received HDR light estimation information
    /// for the physical environment as observed by an AR device.
    /// </summary>
    public class HDRLightEstimation : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("The ARCameraManager which will produce frame events containing light estimation information.")]
        private ARCameraManager m_CameraManager;

        [SerializeField] private Light m_Light;

        void OnEnable()
        {
            if (m_CameraManager != null)
                m_CameraManager.frameReceived += FrameChanged;
        }

        void OnDisable()
        {
            if (m_CameraManager != null)
                m_CameraManager.frameReceived -= FrameChanged;
        }

        void FrameChanged(ARCameraFrameEventArgs args)
        {
            // The estimated brightness of the physical environment, if available.
            if (args.lightEstimation.averageBrightness.HasValue)
            {
                m_Light.intensity = args.lightEstimation.averageBrightness.Value;
            }

            // The estimated color temperature of the physical environment, if available.
            if (args.lightEstimation.averageColorTemperature.HasValue)
            {
                m_Light.colorTemperature = args.lightEstimation.averageColorTemperature.Value;
            }

            // The estimated color correction value of the physical environment, if available.
            if (args.lightEstimation.colorCorrection.HasValue)
            {
                m_Light.color = args.lightEstimation.colorCorrection.Value;
            }

            // The estimated direction of the main light of the physical environment, if available.
            if (args.lightEstimation.mainLightDirection.HasValue)
            {
                m_Light.transform.rotation = Quaternion.LookRotation(args.lightEstimation.mainLightDirection.Value);
            }

            // The estimated color of the main light of the physical environment, if available.
            if (args.lightEstimation.mainLightColor.HasValue)
            {
                m_Light.color = args.lightEstimation.mainLightColor.Value;
            }

            // The estimated intensity in lumens of main light of the physical environment, if available.
            if (args.lightEstimation.mainLightIntensityLumens.HasValue)
            {
                //mainLightIntensityLumens = args.lightEstimation.mainLightIntensityLumens;
                m_Light.intensity = args.lightEstimation.averageMainLightBrightness.Value;
            }

            // The estimated spherical harmonics coefficients of the physical environment, if available.
            if (args.lightEstimation.ambientSphericalHarmonics.HasValue)
            {
                RenderSettings.ambientMode = AmbientMode.Skybox;
                RenderSettings.ambientProbe = args.lightEstimation.ambientSphericalHarmonics.Value;
            }
        }
    }
}