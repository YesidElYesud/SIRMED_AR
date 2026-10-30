namespace SIRMED.Geospatial
{
    using System.Collections;
    using Google.XR.ARCoreExtensions;
    using UnityEngine;

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

            float lat = Input.location.lastData.latitude;
            float lon = Input.location.lastData.longitude;
            Debug.Log($"[GeoDiag] [Input] Device location: {lat}, {lon} (accuracy H:{Input.location.lastData.horizontalAccuracy}m V:{Input.location.lastData.verticalAccuracy}m)");

            if (_earthManager == null)
            {
                Debug.Log("[GeoDiag] No AREarthManager assigned, skipping VPS check.");
                yield break;
            }

            VpsAvailabilityPromise promise = AREarthManager.CheckVpsAvailabilityAsync(lat, lon);
            yield return promise;
            Debug.Log($"[GeoDiag] VPS availability at device location: {promise.Result}");
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