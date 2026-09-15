namespace SIRMED.UI
{
    using SIRMED.Gameplay.Hotspots;
    using SIRMED.Managers;
    using TMPro;
    using UnityEngine;
    using UnityEngine.UI;
    using UnityEngine.Video;

    /// <summary>
    /// CinematicManager — Reproductor de video en pantalla completa.
    ///
    /// Nota de portado (SATCS → SIRMED_AR): el proyecto WebGL original solo podía
    /// reproducir video por streaming de URL (VideoClip no es compatible en
    /// builds WebGL), lo que obligaba a un botón "Toca para reproducir" en iOS
    /// Safari (las políticas de autoplay del navegador bloquean Play() sin gesto
    /// del usuario). En una app nativa Android/iOS ninguna de las dos
    /// restricciones aplica: VideoClip funciona directamente y Play() no requiere
    /// gesto del usuario. Por eso se eliminó playButton/IsIOS() y se simplificó
    /// Play() a un solo camino (VideoClip si está asignado, si no cinematicUrl).
    ///
    /// Setup en escena:
    ///   CinematicPanel (inactivo por defecto)
    ///   ├── VideoRawImage   [RawImage — fullscreen stretch anchor 0,0→1,1]
    ///   ├── LoadingText     [TextMeshProUGUI]
    ///   └── SkipButton      [Button — esquina inferior derecha]
    /// </summary>
    public class CinematicManager : MonoBehaviour
    {
        // ── Singleton ─────────────────────────────────────────────────────────────
        public static CinematicManager Instance { get; private set; }

        // ── Inspector ─────────────────────────────────────────────────────────────
        [Header("Panel UI")]
        public GameObject cinematicPanel;
        public RawImage videoDisplay;
        public TextMeshProUGUI loadingText;
        public Button skipButton;

        [Header("VideoPlayer")]
        public VideoPlayer videoPlayer;

        [Header("RenderTexture")]
        [Tooltip("Resolución inicial del RT. Se redimensiona automáticamente al tamaño real del video en OnPrepared.")]
        public int renderWidth = 1280;
        public int renderHeight = 720;

        [Header("Comportamiento")]
        public bool skipIfNoContent = true;

        // ── Internos ──────────────────────────────────────────────────────────────
        private RenderTexture _rt;
        private HotspotController _sourceHotspot;
        private bool _advancesStage;

        // ── Lifecycle ─────────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            _rt = new RenderTexture(renderWidth, renderHeight, 0);
            _rt.name = "CinematicRT";

            if (videoPlayer != null)
            {
                videoPlayer.renderMode = VideoRenderMode.RenderTexture;
                videoPlayer.targetTexture = _rt;
                videoPlayer.skipOnDrop = true;
                videoPlayer.prepareCompleted += OnPrepared;
                videoPlayer.loopPointReached += OnFinished;
            }

            if (videoDisplay != null)
                videoDisplay.texture = _rt;

            if (skipButton != null)
                skipButton.onClick.AddListener(Skip);

            if (cinematicPanel != null)
                cinematicPanel.SetActive(false);
        }

        private void OnDestroy()
        {
            if (videoPlayer != null)
            {
                videoPlayer.prepareCompleted -= OnPrepared;
                videoPlayer.loopPointReached -= OnFinished;
            }
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
        }

        // ── API pública ───────────────────────────────────────────────────────────
        public void Play(HotspotData data, HotspotController source)
        {
            if (data == null) { Debug.LogWarning("[CinematicManager] HotspotData es null."); return; }

            bool hasClip = data.cinematicClip != null;
            bool hasUrl = !string.IsNullOrEmpty(data.cinematicUrl);

            if (!hasClip && !hasUrl)
            {
                Debug.LogWarning($"[CinematicManager] '{data.title}' sin video asignado.");
                if (skipIfNoContent)
                {
                    if (data.cinematicAdvancesStage && StageManager.Instance != null)
                        StageManager.Instance.NextStage();
                    source?.ClosePanel();
                }
                return;
            }

            _sourceHotspot = source;
            _advancesStage = data.cinematicAdvancesStage;

            ShowPanel(true);
            BlockInput(true);
            SetLoading(true);

            // Resetear uvRect por si el video anterior lo cambió
            if (videoDisplay != null)
                videoDisplay.uvRect = new Rect(0, 0, 1, 1);

            if (videoPlayer == null)
            {
                Debug.LogError("[CinematicManager] VideoPlayer no asignado.");
                return;
            }

            videoPlayer.Stop();

            if (hasClip)
            {
                videoPlayer.source = VideoSource.VideoClip;
                videoPlayer.clip = data.cinematicClip;
            }
            else
            {
                videoPlayer.source = VideoSource.Url;
                videoPlayer.url = ResolveUrl(data.cinematicUrl);
            }

            videoPlayer.Prepare();
        }

        public void Skip()
        {
            if (videoPlayer != null) videoPlayer.Stop();
            FinishCinematic();
        }

        // ── Callbacks VideoPlayer ─────────────────────────────────────────────────
        private void OnPrepared(VideoPlayer vp)
        {
            // Redimensionar RT a la resolución real del video
            ResizeRenderTexture((int)vp.width, (int)vp.height);

            // Ajustar uvRect para mantener aspect ratio sin deformar
            AdjustAspectRatio(vp);

            SetLoading(false);

            // App nativa: Play() no requiere gesto del usuario, arranca directo.
            vp.Play();
        }

        private void OnFinished(VideoPlayer vp) => FinishCinematic();

        // ── Redimensionar RenderTexture al tamaño real del video ──────────────────
        private void ResizeRenderTexture(int w, int h)
        {
            if (w == 0 || h == 0) return;
            if (_rt != null && _rt.width == w && _rt.height == h) return;

            if (_rt != null) { _rt.Release(); Destroy(_rt); }

            _rt = new RenderTexture(w, h, 0);
            _rt.name = "CinematicRT";

            videoPlayer.targetTexture = _rt;
            if (videoDisplay != null) videoDisplay.texture = _rt;
        }

        // ── Ajustar uvRect para relación de aspecto correcta ──────────────────────
        private void AdjustAspectRatio(VideoPlayer vp)
        {
            if (videoDisplay == null || vp.width == 0 || vp.height == 0) return;

            // Forzar recalculo del layout antes de leer rect
            Canvas.ForceUpdateCanvases();

            Rect panelRect = videoDisplay.rectTransform.rect;
            if (panelRect.width <= 0 || panelRect.height <= 0) return;

            float videoAspect = (float)vp.width / vp.height;
            float panelAspect = panelRect.width / panelRect.height;

            if (Mathf.Abs(videoAspect - panelAspect) < 0.01f)
            {
                videoDisplay.uvRect = new Rect(0, 0, 1, 1);
                return;
            }

            if (videoAspect > panelAspect)
            {
                // Video más ancho que el panel → letterbox (recorta arriba/abajo)
                float h = panelAspect / videoAspect;
                videoDisplay.uvRect = new Rect(0f, (1f - h) * 0.5f, 1f, h);
            }
            else
            {
                // Video más alto que el panel → pillarbox (recorta izquierda/derecha)
                float w = videoAspect / panelAspect;
                videoDisplay.uvRect = new Rect((1f - w) * 0.5f, 0f, w, 1f);
            }
        }

        // ── Resolución de URL ─────────────────────────────────────────────────────
        /// <summary>
        /// Resuelve una ruta relativa a StreamingAssets según la plataforma:
        /// Android necesita la ruta de streamingAssetsPath tal cual (jar:file://...),
        /// iOS/Editor/Standalone necesitan el prefijo file://.
        /// </summary>
        private string ResolveUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            if (url.StartsWith("http://") || url.StartsWith("https://") || url.StartsWith("file://")) return url;

            string relative = url.StartsWith("StreamingAssets/")
                ? url.Substring("StreamingAssets/".Length)
                : url;

            string basePath = Application.streamingAssetsPath;

#if UNITY_ANDROID && !UNITY_EDITOR
            return basePath + "/" + relative;
#else
            return "file://" + basePath + "/" + relative;
#endif
        }

        // ── Cierre y limpieza ─────────────────────────────────────────────────────
        private void FinishCinematic()
        {
            if (_advancesStage && StageManager.Instance != null)
                StageManager.Instance.NextStage();

            BlockInput(false);
            ShowPanel(false);

            if (_sourceHotspot != null) { _sourceHotspot.ClosePanel(); _sourceHotspot = null; }
        }

        // ── Helpers ───────────────────────────────────────────────────────────────
        private void ShowPanel(bool visible)
        {
            if (cinematicPanel != null) cinematicPanel.SetActive(visible);
        }

        private void SetLoading(bool loading)
        {
            if (loadingText != null) loadingText.gameObject.SetActive(loading);
            if (videoDisplay != null) videoDisplay.gameObject.SetActive(!loading);
        }

        private void BlockInput(bool block)
        {
            if (StageManager.Instance != null)
                StageManager.Instance.SetPlayerInputBlocked(block);
        }
    }
}
