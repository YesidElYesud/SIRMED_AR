namespace SIRMED.UI
{
    using System.Text;
    using Google.XR.ARCoreExtensions;
    using SIRMED.Gameplay.Hotspots;
    using SIRMED.Managers;
    using TMPro;
    using UnityEngine;
    using UnityEngine.UI;
    using UnityEngine.XR.ARFoundation;
    using UnityEngine.XR.ARSubsystems;

    /// <summary>
    /// DebugOverlay — modo debug mínimo para pruebas en campo (checklist, Bloque 0):
    /// SAT activo, lat/lon/alt, precisión, estado de tracking, hotspot más cercano,
    /// etapa y nivel de riesgo. Solo texto, se refresca unas veces por segundo.
    ///
    /// Se muestra/oculta con un botón (p. ej. uno pequeño y discreto en una esquina, o
    /// desde SettingsPanel llamando Toggle()). Sin AREarthManager (Editor) muestra lo
    /// que pueda.
    ///
    /// Jerarquía sugerida:
    ///   DebugOverlay           [este script]                (activo, sin Image)
    ///   ├── Panel [INACTIVO]   [Image semitransparente]     ← panelRoot
    ///   │   └── Text (TMP)     (alineado arriba-izquierda)  ← text
    ///   └── ToggleButton       [Button, pequeño]            ← toggleButton (opcional)
    /// </summary>
    public class DebugOverlay : MonoBehaviour
    {
        [Header("Identificación del SAT")]
        [Tooltip("Nombre del sitio SAT de esta escena. Vacío = nombre de la escena.")]
        [SerializeField] private string satName = "";

        [Header("UI")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private TextMeshProUGUI text;
        [SerializeField] private Button toggleButton;
        [SerializeField] private bool startVisible = false;

        [Header("Referencias (vacío = se buscan solas)")]
        [SerializeField] private AREarthManager earthManager;

        [SerializeField] private float refreshInterval = 0.5f;

        private float _nextRefresh;
        private readonly StringBuilder _sb = new StringBuilder(512);

        private void Awake()
        {
            if (toggleButton != null) toggleButton.onClick.AddListener(Toggle);
            if (panelRoot != null) panelRoot.SetActive(startVisible);
            if (string.IsNullOrEmpty(satName))
                satName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        }

        public void Toggle()
        {
            if (panelRoot != null) panelRoot.SetActive(!panelRoot.activeSelf);
            _nextRefresh = 0f;
        }

        private void Update()
        {
            if (panelRoot == null || !panelRoot.activeSelf || text == null) return;
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + refreshInterval;

            if (earthManager == null) earthManager = FindAnyObjectByType<AREarthManager>();

            _sb.Clear();
            _sb.Append("SAT: ").Append(satName).Append('\n');
            _sb.Append("Sesión AR: ").Append(ARSession.state).Append('\n');

            if (earthManager != null)
            {
                _sb.Append("Earth: ").Append(earthManager.EarthState)
                   .Append(" / ").Append(earthManager.EarthTrackingState).Append('\n');

                if (earthManager.EarthTrackingState == TrackingState.Tracking)
                {
                    GeospatialPose pose = earthManager.CameraGeospatialPose;
                    _sb.AppendFormat("Lat {0:F6}  Lon {1:F6}\n", pose.Latitude, pose.Longitude);
                    _sb.AppendFormat("Alt {0:F1} m (±{1:F1})\n", pose.Altitude, pose.VerticalAccuracy);
                    _sb.AppendFormat("Precisión H ±{0:F1} m  Yaw ±{1:F1}°\n", pose.HorizontalAccuracy, pose.OrientationYawAccuracy);
                }
            }
            else
            {
                _sb.Append("Earth: sin AREarthManager\n");
            }

            _sb.Append("Posición confiable: ").Append(TrackingStatusBanner.IsPositionReliable ? "sí" : "no").Append('\n');

            AppendNearestHotspot();

            if (StageManager.Instance != null)
                _sb.Append("Etapa: ").Append(StageManager.Instance.CurrentStage);
            if (RiskLevelIndicator.Instance != null)
                _sb.Append("  Nivel: ").Append(RiskLevelIndicator.Instance.CurrentLevel);

            text.text = _sb.ToString();
        }

        private void AppendNearestHotspot()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            HotspotController nearest = null;
            float best = float.PositiveInfinity;
            int anchored = 0;
            foreach (HotspotController h in HotspotController.ActiveHotspots)
            {
                if (h == null || !h.IsAnchorReady()) continue;
                anchored++;
                Vector3 d = h.transform.position - cam.transform.position;
                d.y = 0f;
                float dist = d.magnitude;
                if (dist < best) { best = dist; nearest = h; }
            }

            _sb.Append("Hotspots anclados: ").Append(anchored).Append('/').Append(HotspotController.ActiveHotspots.Count).Append('\n');
            if (nearest != null)
                _sb.AppendFormat("Más cercano: {0} a {1:F1} m\n", nearest.name, best);
        }
    }
}
