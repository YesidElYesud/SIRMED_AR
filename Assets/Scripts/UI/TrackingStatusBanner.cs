namespace SIRMED.UI
{
    using Google.XR.ARCoreExtensions;
    using TMPro;
    using UnityEngine;
    using UnityEngine.XR.ARFoundation;
    using UnityEngine.XR.ARSubsystems;

    /// <summary>
    /// TrackingStatusBanner — aviso no bloqueante de "posición no confiable" (GDD §6
    /// "Continuidad ante pérdida de tracking", §9.3.1 calibración, §24 "La posición RA
    /// debe comunicar incertidumbre" y §15 "Recalibración RA").
    ///
    /// Sigue los mismos chequeos que GeospatialController del sample de Google:
    /// sesión AR trackeando, EarthState Enabled, EarthTrackingState Tracking, y
    /// precisión horizontal / de orientación por debajo de un umbral. Mientras alguno
    /// falla muestra un mensaje con qué hacer (en Geospatial "recalibrar" es apuntar
    /// la cámara a fachadas y calles para que el VPS vuelva a localizar).
    ///
    /// IsPositionReliable lo consultan otros sistemas (DirectorAdviceController) para
    /// no dar orientación crítica con una posición mala. Sin este componente en la
    /// escena vale true (comportamiento de antes).
    ///
    /// Estructura esperada en escena (raíz ACTIVA; el script oculta bannerRoot solo):
    ///   TrackingStatusBanner      [este script]
    ///   └── BannerRoot            [Image semitransparente]   ← bannerRoot
    ///       └── MessageText       [TextMeshProUGUI]          ← messageText
    /// </summary>
    public class TrackingStatusBanner : MonoBehaviour
    {
        public static TrackingStatusBanner Instance { get; private set; }

        /// <summary>True si la posición geoespacial es confiable (o si no hay banner en la escena).</summary>
        public static bool IsPositionReliable => Instance == null || Instance._reliable;

        [Header("Referencias")]
        [Tooltip("Vacío = se busca el AREarthManager de la escena (XR Origin).")]
        [SerializeField] private AREarthManager _earthManager;
        [SerializeField] private GameObject _bannerRoot;
        [SerializeField] private TMP_Text _messageText;

        [Header("Umbrales (mismos criterios que el sample Geospatial)")]
        [Tooltip("Precisión horizontal máxima aceptable, en metros.")]
        [SerializeField] private double _maxHorizontalAccuracy = 15;
        [Tooltip("Precisión de orientación máxima aceptable, en grados.")]
        [SerializeField] private double _maxYawAccuracy = 25;

        [Header("Anti-parpadeo")]
        [Tooltip("Segundos seguidos con posición mala antes de mostrar el aviso.")]
        [SerializeField] private float _showDelay = 1.5f;
        [Tooltip("Segundos seguidos con posición buena antes de ocultarlo.")]
        [SerializeField] private float _hideDelay = 1f;

        [Header("Editor")]
        [Tooltip("En el Editor no hay sesión AR real: sin esto el aviso quedaría siempre visible.")]
        [SerializeField] private bool _hideInEditor = true;

        [Header("Mensajes")]
        [SerializeField, TextArea] private string _msgStartingCamera =
            "Iniciando la cámara… mueve el teléfono lentamente.";
        [SerializeField, TextArea] private string _msgLocating =
            "Buscando tu ubicación. Apunta la cámara a fachadas y calles cercanas.";
        [SerializeField, TextArea] private string _msgLowAccuracy =
            "Ubicación imprecisa. Detente y apunta a los edificios cercanos para mejorarla.";
        [SerializeField, TextArea] private string _msgUnavailable =
            "La ubicación RA no está disponible. Revisa tu conexión a internet y el GPS.";

        private bool _reliable;
        private float _goodTimer;
        private float _badTimer;
        private string _pendingMessage;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            if (_earthManager == null) _earthManager = FindAnyObjectByType<AREarthManager>();
            if (_bannerRoot != null) _bannerRoot.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (Application.isEditor && _hideInEditor)
            {
                _reliable = true;
                SetBannerVisible(false);
                return;
            }

            string problem = Evaluate();
            float dt = Time.unscaledDeltaTime;

            if (problem == null)
            {
                _badTimer = 0f;
                _goodTimer += dt;
                if (_goodTimer >= _hideDelay)
                {
                    _reliable = true;
                    SetBannerVisible(false);
                }
                return;
            }

            // Para los demás sistemas deja de ser confiable al instante (criterio
            // conservador); el aviso visible sí espera _showDelay para no parpadear.
            _reliable = false;
            _goodTimer = 0f;
            _badTimer += dt;
            SetMessage(problem);
            if (_badTimer >= _showDelay) SetBannerVisible(true);
        }

        private void SetBannerVisible(bool visible)
        {
            if (_bannerRoot != null && _bannerRoot.activeSelf != visible)
                _bannerRoot.SetActive(visible);
        }

        // Devuelve el mensaje del problema actual, o null si la posición es buena.
        private string Evaluate()
        {
            if (ARSession.state != ARSessionState.SessionTracking)
                return _msgStartingCamera;

            if (_earthManager == null)
                return _msgUnavailable;

            EarthState earthState = _earthManager.EarthState;
            if (earthState == EarthState.ErrorEarthNotReady)
                return _msgLocating;
            if (earthState != EarthState.Enabled)
                return _msgUnavailable;

            if (_earthManager.EarthTrackingState != TrackingState.Tracking)
                return _msgLocating;

            GeospatialPose pose = _earthManager.CameraGeospatialPose;
            if (pose.HorizontalAccuracy > _maxHorizontalAccuracy ||
                pose.OrientationYawAccuracy > _maxYawAccuracy)
                return _msgLowAccuracy;

            return null;
        }

        private void SetMessage(string message)
        {
            if (message == _pendingMessage) return;
            _pendingMessage = message;
            if (_messageText != null) _messageText.text = message;
        }
    }
}
