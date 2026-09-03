namespace SIRMED.Geospatial
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using Google.XR.ARCoreExtensions;
    using UnityEngine;
    using UnityEngine.EventSystems;
    using UnityEngine.UI;
    using UnityEngine.XR.ARFoundation;
    using UnityEngine.XR.ARSubsystems;
#if UNITY_ANDROID
    using UnityEngine.Android;
#endif

    /// <summary>
    /// Site-survey tool: walk to a physical location, tap the screen to capture the device's
    /// current ARCore Geospatial pose (fused GPS+VPS, not raw GPS), confirm it, and export the
    /// captured points as text/CSV to later place hotspots/3D elements at the same coordinates.
    /// </summary>
    public class HotspotSurveyController : MonoBehaviour
    {
        [Header("AR Components")]
        public ARSession Session;
        public ARCoreExtensions ArCoreExtensions;
        public ARAnchorManager AnchorManager;
        public AREarthManager EarthManager;

        [Header("Marker")]
        public GameObject HotspotMarkerPrefab;

        [Header("Live Status UI")]
        public Text StatusText;
        public Text HintText;

        [Header("Confirmation UI")]
        public GameObject ConfirmPanel;
        public Text ConfirmDetailsText;
        public InputField ConfirmLabelInput;
        public Button ConfirmAcceptButton;
        public Button ConfirmCancelButton;

        [Header("Export UI")]
        public GameObject ExportPanel;
        public InputField ExportTextField;
        public Text ExportSummaryText;
        public Button ShowExportButton;
        public Button CloseExportButton;
        public Button ClearAllButton;

        // Same thresholds used by the Geospatial sample: below these, EarthManager's pose is
        // considered too unreliable to record.
        private const double HorizontalAccuracyThresholdMeters = 20;
        private const double OrientationYawAccuracyThresholdDegrees = 25;

        private const string PersistentFileName = "hotspot_survey.csv";

        private readonly List<HotspotRecord> _records = new List<HotspotRecord>();
        private readonly List<GameObject> _anchorObjects = new List<GameObject>();

        private ARAnchor _pendingAnchor;
        private GeospatialPose _pendingPose;
        private bool _hasPendingCapture;

        private bool _isLocalizing = true;
        private bool _enablingGeospatial;
        private float _configurePrepareTime = 3f;
        private IEnumerator _startLocationService;

        private string PersistentFilePath =>
            Path.Combine(Application.persistentDataPath, PersistentFileName);

        public void Awake()
        {
            Screen.autorotateToLandscapeLeft = false;
            Screen.autorotateToLandscapeRight = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.orientation = ScreenOrientation.Portrait;
            Application.targetFrameRate = 60;
        }

        public void OnEnable()
        {
            StartCoroutine(EnsureArCoreAvailable());

            _startLocationService = StartLocationService();
            StartCoroutine(_startLocationService);

            ConfirmPanel.SetActive(false);
            ExportPanel.SetActive(false);
            ClearAllButton.gameObject.SetActive(false);

            ConfirmAcceptButton.onClick.AddListener(OnConfirmAcceptClicked);
            ConfirmCancelButton.onClick.AddListener(OnConfirmCancelClicked);
            ShowExportButton.onClick.AddListener(OnShowExportClicked);
            CloseExportButton.onClick.AddListener(() => ExportPanel.SetActive(false));
            ClearAllButton.onClick.AddListener(OnClearAllClicked);

            _isLocalizing = true;
            HintText.text = "Iniciando servicios de ubicación...";
            RefreshExportSummary();
        }

        public void OnDisable()
        {
            if (_startLocationService != null)
            {
                StopCoroutine(_startLocationService);
                _startLocationService = null;
            }

            Input.location.Stop();
        }

        public void Update()
        {
            if (Session == null || EarthManager == null || ArCoreExtensions == null)
            {
                return;
            }

            if (ARSession.state != ARSessionState.SessionInitializing &&
                ARSession.state != ARSessionState.SessionTracking)
            {
                return;
            }

            var featureSupport = EarthManager.IsGeospatialModeSupported(GeospatialMode.Enabled);
            if (featureSupport == FeatureSupported.Unsupported)
            {
                HintText.text = "El API Geospatial no es compatible con este dispositivo.";
                return;
            }

            if (featureSupport == FeatureSupported.Supported &&
                ArCoreExtensions.ARCoreExtensionsConfig.GeospatialMode == GeospatialMode.Disabled)
            {
                ArCoreExtensions.ARCoreExtensionsConfig.GeospatialMode = GeospatialMode.Enabled;
                _configurePrepareTime = 3f;
                _enablingGeospatial = true;
                return;
            }

            if (_enablingGeospatial)
            {
                _configurePrepareTime -= Time.deltaTime;
                if (_configurePrepareTime > 0)
                {
                    return;
                }

                _enablingGeospatial = false;
            }

            if (EarthManager.EarthState != EarthState.Enabled)
            {
                HintText.text = "Inicializando Geospatial...";
                return;
            }

            var earthTrackingState = EarthManager.EarthTrackingState;
            var pose = earthTrackingState == TrackingState.Tracking ?
                EarthManager.CameraGeospatialPose : new GeospatialPose();

            bool isSessionReady = ARSession.state == ARSessionState.SessionTracking &&
                Input.location.status == LocationServiceStatus.Running;
            _isLocalizing = !isSessionReady ||
                earthTrackingState != TrackingState.Tracking ||
                pose.OrientationYawAccuracy > OrientationYawAccuracyThresholdDegrees ||
                pose.HorizontalAccuracy > HorizontalAccuracyThresholdMeters;

            if (_isLocalizing)
            {
                HintText.text = "Apunta la cámara a edificios o puntos de referencia cercanos " +
                    "para localizar el dispositivo.";
                StatusText.text = string.Empty;
                return;
            }

            HintText.text = _hasPendingCapture ?
                "Confirma o descarta el punto antes de tocar de nuevo." :
                "Toca la pantalla para capturar un hotspot en tu ubicación actual.";

            StatusText.text = FormatPose(pose);

            if (!_hasPendingCapture &&
                Input.touchCount > 0 &&
                Input.GetTouch(0).phase == TouchPhase.Began &&
                !EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId))
            {
                CapturePendingHotspot(pose);
            }
        }

        private void CapturePendingHotspot(GeospatialPose pose)
        {
            ARGeospatialAnchor anchor = AnchorManager.AddAnchor(
                pose.Latitude, pose.Longitude, pose.Altitude, pose.EunRotation);
            if (anchor == null)
            {
                HintText.text = "No se pudo colocar el ancla, intenta de nuevo.";
                return;
            }

            _pendingAnchor = anchor;
            _pendingPose = pose;
            _hasPendingCapture = true;

            if (HotspotMarkerPrefab != null)
            {
                Instantiate(HotspotMarkerPrefab, anchor.transform);
            }

            ConfirmLabelInput.text = $"Hotspot {_records.Count + 1:00}";
            ConfirmDetailsText.text = FormatPose(pose);
            ConfirmPanel.SetActive(true);
        }

        private void OnConfirmAcceptClicked()
        {
            if (!_hasPendingCapture)
            {
                return;
            }

            var record = new HotspotRecord
            {
                Label = string.IsNullOrWhiteSpace(ConfirmLabelInput.text) ?
                    $"Hotspot {_records.Count + 1:00}" : ConfirmLabelInput.text.Trim(),
                Latitude = _pendingPose.Latitude,
                Longitude = _pendingPose.Longitude,
                Altitude = _pendingPose.Altitude,
                HorizontalAccuracy = _pendingPose.HorizontalAccuracy,
                VerticalAccuracy = _pendingPose.VerticalAccuracy,
                OrientationYawAccuracy = _pendingPose.OrientationYawAccuracy,
                CapturedAtUtc = DateTime.UtcNow.ToString("O"),
            };

            _records.Add(record);
            _anchorObjects.Add(_pendingAnchor.gameObject);
            AppendRecordToDisk(record);

            ClearAllButton.gameObject.SetActive(true);
            RefreshExportSummary();
            ClearPendingCapture(destroyAnchor: false);
        }

        private void OnConfirmCancelClicked()
        {
            ClearPendingCapture(destroyAnchor: true);
        }

        private void ClearPendingCapture(bool destroyAnchor)
        {
            if (destroyAnchor && _pendingAnchor != null)
            {
                Destroy(_pendingAnchor.gameObject);
            }

            _pendingAnchor = null;
            _pendingMarker = null;
            _hasPendingCapture = false;
            ConfirmPanel.SetActive(false);
        }

        private void OnShowExportClicked()
        {
            ExportTextField.text = BuildExportText();
            ExportPanel.SetActive(true);
            ExportTextField.Select();
            ExportTextField.caretPosition = 0;
        }

        private void OnClearAllClicked()
        {
            foreach (var anchorObject in _anchorObjects)
            {
                Destroy(anchorObject);
            }

            _anchorObjects.Clear();
            _records.Clear();

            if (File.Exists(PersistentFilePath))
            {
                File.Delete(PersistentFilePath);
            }

            ClearAllButton.gameObject.SetActive(false);
            RefreshExportSummary();
        }

        private void RefreshExportSummary()
        {
            ExportSummaryText.text = $"{_records.Count} hotspot(s) capturado(s)";
        }

        private string BuildExportText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("SIRMED_AR - Hotspot Survey Export");
            sb.AppendLine($"Generado: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Total: {_records.Count}");
            sb.AppendLine();
            sb.AppendLine(HotspotRecord.CsvHeader);
            foreach (var record in _records)
            {
                sb.AppendLine(record.ToCsvRow());
            }

            return sb.ToString();
        }

        private void AppendRecordToDisk(HotspotRecord record)
        {
            try
            {
                if (!File.Exists(PersistentFilePath))
                {
                    File.WriteAllText(PersistentFilePath, HotspotRecord.CsvHeader + "\n");
                }

                File.AppendAllText(PersistentFilePath, record.ToCsvRow() + "\n");
            }
            catch (IOException e)
            {
                Debug.LogWarning($"No se pudo guardar el hotspot en disco: {e.Message}");
            }
        }

        private static string FormatPose(GeospatialPose pose)
        {
            return string.Format(
                "Latitud/Longitud: {0}°, {1}°\n" +
                "Precisión horizontal: {2} m\n" +
                "Altitud: {3} m\n" +
                "Precisión vertical: {4} m\n" +
                "Precisión de orientación (yaw): {5}°",
                pose.Latitude.ToString("F6"),
                pose.Longitude.ToString("F6"),
                pose.HorizontalAccuracy.ToString("F2"),
                pose.Altitude.ToString("F2"),
                pose.VerticalAccuracy.ToString("F2"),
                pose.OrientationYawAccuracy.ToString("F1"));
        }

        private IEnumerator EnsureArCoreAvailable()
        {
            if (ARSession.state == ARSessionState.None)
            {
                yield return ARSession.CheckAvailability();
            }

            yield return null;

            if (ARSession.state == ARSessionState.NeedsInstall)
            {
                yield return ARSession.Install();
            }
        }

        private IEnumerator StartLocationService()
        {
#if UNITY_ANDROID
            if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
            {
                Permission.RequestUserPermission(Permission.FineLocation);
                yield return new WaitForSeconds(3.0f);
            }
#endif

            if (!Input.location.isEnabledByUser)
            {
                HintText.text = "Activa el servicio de ubicación del teléfono para continuar.";
                yield break;
            }

            Input.location.Start();

            while (Input.location.status == LocationServiceStatus.Initializing)
            {
                yield return null;
            }

            if (Input.location.status != LocationServiceStatus.Running)
            {
                HintText.text = "No se pudo iniciar el servicio de ubicación.";
                Input.location.Stop();
            }
        }
    }
}
