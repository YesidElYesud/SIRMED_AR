namespace SIRMED.UI
{
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>
    /// Fondo de pantalla completa para el layout "phone frame".
    /// Solo es visible cuando al menos uno de los paneles rastreados está activo.
    /// Durante el juego (sin paneles abiertos) se oculta para mostrar el feed de
    /// cámara AR real. Se puede suprimir vía SetSuppressed(true) para paneles que
    /// gestionan su propio HUD (p.ej. NpcDialoguePanel) y necesitan que el fondo
    /// no se muestre.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class ScreenBackground : MonoBehaviour
    {
        public static ScreenBackground Instance { get; private set; }

        private bool _suppressed;
        [Header("Apariencia")]
        [SerializeField] Color backgroundColor = new Color(0.90f, 0.87f, 0.82f, 1f);
        [SerializeField] Sprite backgroundSprite;
        [SerializeField] Image.Type spriteType = Image.Type.Sliced;

        [Header("Paneles que activan el fondo")]
        [Tooltip("El fondo se muestra solo cuando alguno de estos paneles está activo.\n" +
                 "Asignar: WelcomePanel, HotspotPanel, NpcDialoguePanel, SiataCallPanel, InfoSlidePanel.")]
        [SerializeField] GameObject[] trackedPanels;

        [Header("HUD")]
        [Tooltip("El HUD se oculta mientras el fondo esté visible y se restaura al cerrarse.")]
        [SerializeField] GameObject hud;

        Image _img;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            _img = GetComponent<Image>();
            FillScreen();
            Apply();
        }

        void FillScreen()
        {
            var rt = GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        void Apply()
        {
            _img.raycastTarget = false;
            _img.color = backgroundColor;
            if (backgroundSprite != null)
            {
                _img.sprite = backgroundSprite;
                _img.type = spriteType;
            }
        }

        void LateUpdate()
        {
            bool panelOpen = AnyPanelActive();

            // El HUD se gestiona SIEMPRE según si algún panel está abierto,
            // sin importar si estamos suprimidos.
            if (hud != null) hud.SetActive(!panelOpen);

            // La imagen de fondo solo se muestra si hay un panel abierto
            // Y no estamos suprimidos (p.ej. NpcDialoguePanel quiere transparencia).
            _img.enabled = panelOpen && !_suppressed;
        }

        /// <summary>
        /// Suprime la imagen de fondo sin afectar la gestión del HUD.
        /// Usar cuando el panel activo quiere mostrar la escena AR en lugar del fondo sólido.
        /// </summary>
        public void SetSuppressed(bool suppressed) => _suppressed = suppressed;

        bool AnyPanelActive()
        {
            if (trackedPanels == null) return false;
            for (int i = 0; i < trackedPanels.Length; i++)
                if (trackedPanels[i] != null && trackedPanels[i].activeSelf) return true;
            return false;
        }

        // ── API pública ────────────────────────────────────────────────────────────
        public void SetColor(Color c)
        {
            backgroundColor = c;
            if (_img != null) _img.color = c;
        }

        public void SetSprite(Sprite s)
        {
            if (_img == null) return;
            _img.sprite = s;
            _img.type = spriteType;
        }

        public void ClearSprite()
        {
            if (_img == null) return;
            _img.sprite = null;
            _img.color = backgroundColor;
        }
    }
}
