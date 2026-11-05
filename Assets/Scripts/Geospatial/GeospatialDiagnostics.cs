namespace SIRMED.Geospatial
{
    using System.Collections;
    using Google.XR.ARCoreExtensions;
    using UnityEngine;
    using UnityEngine.XR.ARFoundation;
    using UnityEngine.XR.ARSubsystems;

    /// <summary>
    /// Diagnostic-only component, not part of gameplay: logs AREarthManager.EarthState /
    /// EarthTrackingState periodically and runs a one-shot CheckVpsAvailabilityAsync at the
    /// device's current GPS fix. Added to disambiguate, during field testing, "Earth tracking
    /// still converging" from a hard failure (wrong/unauthorized ARCore API key, Geospatial
    /// mode disabled, VPS not available at this location, etc.) instead of only seeing
    /// ARGeospatialCreatorAnchor's generic "Waiting for AR Session to become stable." spam.
    /// Safe to remove once Geospatial tracking is confirmed working in the field.
    /// </summary>
    public class GeospatialDiagnostics : MonoBehaviour
    {
        [SerializeField] private AREarthManager _earthManager;
        [SerializeField] private float _logInterval = 5f;
        [Tooltip("Reintentos del chequeo de VPS si la respuesta es Unknown o error de red.")]
        [SerializeField] private int _vpsCheckAttempts = 3;
        [Tooltip("Segundos entre reintentos del chequeo de VPS.")]
        [SerializeField] private float _vpsRetryDelay = 10f;

        private float _nextLogTime;

        private void Awake()
        {
            if (Application.isEditor)
                enabled = false;
        }

        private IEnumerator Start()
        {
            if (Input.location.status == LocationServiceStatus.Stopped)
                Input.location.Start(0, 0);

            float timeout = Time.time + 15f;
            while (Input.location.status == LocationServiceStatus.Initializing && Time.time < timeout)
                yield return null;

            Debug.Log($"[GeoDiag] Unity location service status: {Input.location.status}");

            if (Input.location.status != LocationServiceStatus.Running)
                yield break;

            Debug.Log($"[GeoDiag] [Input] Device location: {Input.location.lastData.latitude}, {Input.location.lastData.longitude} " +
                      $"(accuracy H:{Input.location.lastData.horizontalAccuracy}m V:{Input.location.lastData.verticalAccuracy}m)");

            if (_earthManager == null)
            {
                Debug.Log("[GeoDiag] No AREarthManager assigned, skipping VPS check.");
                yield break;
            }

            // El chequeo de VPS necesita la sesión ARCore lista: si se pide al arrancar
            // (como antes) devuelve Unknown y no dice nada. Se espera a que el Earth esté
            // en Tracking y se usa su lat/lon (double, más precisa que Input.location).
            float trackingTimeout = Time.time + 60f;
            while (!IsEarthTracking() && Time.time < trackingTimeout)
                yield return null;

            for (int attempt = 1; attempt <= _vpsCheckAttempts; attempt++)
            {
                double lat, lon;
                string source;
                if (IsEarthTracking())
                {
                    GeospatialPose pose = _earthManager.CameraGeospatialPose;
                    lat = pose.Latitude;
                    lon = pose.Longitude;
                    source = "Earth";
                }
                else
                {
                    lat = Input.location.lastData.latitude;
                    lon = Input.location.lastData.longitude;
                    source = "GPS";
                }

                VpsAvailabilityPromise promise = AREarthManager.CheckVpsAvailabilityAsync(lat, lon);
                yield return promise;
                VpsAvailability result = promise.Result;
                Debug.Log($"[GeoDiag] VPS availability at {lat:F6}, {lon:F6} ({source}, intento {attempt}/{_vpsCheckAttempts}): " +
                          $"{result} — {Explain(result)}");

                bool retry = result == VpsAvailability.Unknown || result == VpsAvailability.ErrorNetworkConnection;
                if (!retry) yield break;
                yield return new WaitForSeconds(_vpsRetryDelay);
            }
        }

        private bool IsEarthTracking() =>
            _earthManager != null &&
            ARSession.state == ARSessionState.SessionTracking &&
            _earthManager.EarthState == EarthState.Enabled &&
            _earthManager.EarthTrackingState == TrackingState.Tracking;

        private static string Explain(VpsAvailability v)
        {
            switch (v)
            {
                case VpsAvailability.Available: return "hay cobertura VPS: la precisión debería bajar de ~1 m al apuntar a fachadas.";
                case VpsAvailability.Unavailable: return "SIN cobertura VPS: solo GPS + brújula (precisión típica 3–10 m, rumbo 10–40°).";
                case VpsAvailability.ErrorNetworkConnection: return "sin conexión a internet para consultar VPS.";
                case VpsAvailability.ErrorNotAuthorized: return "API key / autenticación de ARCore rechazada.";
                case VpsAvailability.ErrorResourceExhausted: return "cuota de la ARCore API agotada.";
                case VpsAvailability.ErrorInternal: return "error interno de ARCore.";
                default: return "la consulta no se completó.";
            }
        }

        private void Update()
        {
            if (_earthManager == null || Time.time < _nextLogTime) return;
            _nextLogTime = Time.time + _logInterval;
            Debug.Log($"[GeoDiag] [Earth] EarthState={_earthManager.EarthState} EarthTrackingState={_earthManager.EarthTrackingState} " +
                $"(accuracy H:{_earthManager.CameraGeospatialPose.HorizontalAccuracy} V:{_earthManager.CameraGeospatialPose.VerticalAccuracy} Y:{_earthManager.CameraGeospatialPose.OrientationYawAccuracy})");
        }
    }
}