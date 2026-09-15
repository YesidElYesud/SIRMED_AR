namespace SIRMED.UI
{
    using SIRMED.Managers;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using UnityEngine.UI;

    /// <summary>
    /// Panel final de la experiencia. Se muestra al cerrar el hotspot final
    /// (data.activatesEndGame = true en su HotspotData). Ocupa toda la pantalla,
    /// muestra la imagen de felicitación y ofrece un botón de reinicio.
    ///
    /// Nota de portado (SATCS → SIRMED_AR): se eliminó la rama
    /// Application.ExternalEval("location.reload()") — era el camino de reinicio
    /// para WebGL, que no existe en una app nativa. Restart() siempre destruye los
    /// singletons persistentes y recarga la escena. La lista de singletons se
    /// redujo a los que realmente existen en este proyecto (StageManager,
    /// AudioStageManager, VisualEffectsStageController, CinematicManager) — el
    /// resto (GPSManager, GyroscopeManager, AerialViewController,
    /// SceneOverviewController, CameraFeedManager) no se portaron.
    /// </summary>
    public class EndGamePanel : MonoBehaviour
    {
        public static EndGamePanel Instance { get; private set; }

        [Header("UI")]
        [Tooltip("Image donde se muestra la imagen de felicitación / slide final.")]
        [SerializeField] private Image panelImage;

        [Tooltip("Botón que reinicia la experiencia.")]
        [SerializeField] private Button restartButton;

        // ── Lifecycle ─────────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ── API pública ───────────────────────────────────────────────────────────
        public void Show()
        {
            gameObject.SetActive(true);
            StageManager.Instance?.SetPlayerInputBlocked(true);
        }

        /// <summary>
        /// Reinicia la experiencia: destruye los singletons persistentes y recarga
        /// la escena, evitando referencias obsoletas al StageManager, iluminación, etc.
        /// </summary>
        public void Restart()
        {
            StageManager.Instance?.SetPlayerInputBlocked(false);

            DestroyPersistentSingletons();
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        private static void DestroyPersistentSingletons()
        {
            if (StageManager.Instance != null) Destroy(StageManager.Instance.gameObject);
            if (AudioStageManager.Instance != null) Destroy(AudioStageManager.Instance.gameObject);
            if (VisualEffectsStageController.Instance != null) Destroy(VisualEffectsStageController.Instance.gameObject);
            if (CinematicManager.Instance != null) Destroy(CinematicManager.Instance.gameObject);
        }

        // ── Imagen configurable en runtime (opcional) ─────────────────────────────
        public void SetImage(Sprite sprite)
        {
            if (panelImage != null) panelImage.sprite = sprite;
        }
    }
}
