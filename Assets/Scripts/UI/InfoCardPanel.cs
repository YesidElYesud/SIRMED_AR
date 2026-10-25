namespace SIRMED.UI
{
    using SIRMED.Gameplay.Dialogue;
    using SIRMED.Gameplay.Hotspots;
    using SIRMED.Managers;
    using TMPro;
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>
    /// InfoCardPanel — Tarjeta informativa compartida (título + intro + filas ícono/texto)
    /// que se intercala entre el contenido principal de un hotspot y su trivia.
    ///
    /// Flujo:
    ///   HotspotUIPanel (u otro panel) cierra → HotspotController.ClosePanel()
    ///   → si HotspotData.useInfoCard: InfoCardPanel.Show(data.infoCard, caller)
    ///   → "Siguiente" → caller.ClosePanel() → trivia (si hay) → cierre normal
    ///
    /// Un solo panel para todos los hotspots (Punto de encuentro, Ruta de evacuación...):
    /// solo cambia el texto/íconos, que viven en cada HotspotData.
    ///
    /// Jerarquía UI recomendada:
    ///   InfoCardPanel                     [InfoCardPanel.cs]  (ACTIVO en la escena)
    ///     Background (Image, stretch)     ← backgroundImage (opcional)
    ///     Card (Image)
    ///       TitleText (TMP)               ← titleText
    ///       IntroText (TMP)               ← introText
    ///       Row1..Row3                    ← rows[i].root
    ///         Icon (Image)                ← rows[i].icon
    ///         Text (TMP)                  ← rows[i].text
    ///     NextButton (Button)             ← nextButton
    ///
    /// IMPORTANTE: el GameObject debe empezar ACTIVO en la escena (Awake lo oculta);
    /// si empieza inactivo, Instance nunca se asigna y el hotspot salta directo a la trivia.
    /// </summary>
    public class InfoCardPanel : MonoBehaviour
    {
        [System.Serializable]
        public class RowSlot
        {
            [Tooltip("GameObject de la fila completa (se oculta si no hay dato para ella).")]
            public GameObject root;
            public Image icon;
            public TextMeshProUGUI text;
        }

        // ── Singleton ─────────────────────────────────────────────────────────────
        public static InfoCardPanel Instance { get; private set; }

        // ── Inspector ─────────────────────────────────────────────────────────────
        [Header("Fondo (opcional)")]
        public Image backgroundImage;

        [Header("Textos")]
        public TextMeshProUGUI titleText;
        public TextMeshProUGUI introText;

        [Header("Filas ícono + texto")]
        public RowSlot[] rows;

        [Header("Navegación")]
        public Button nextButton;

        [Header("HUD — ocultar mientras el panel está abierto")]
        [SerializeField] private GameObject[] _hudElementsToHide;

        // ── Estado interno ────────────────────────────────────────────────────────
        private HotspotController _caller;

        // ── Lifecycle ─────────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            gameObject.SetActive(false);
        }

        private void Start()
        {
            if (nextButton != null) nextButton.onClick.AddListener(Close);
        }

        // ── API pública ────────────────────────────────────────────────────────────

        /// <summary>Muestra la tarjeta. Al pulsar "Siguiente" llama caller.ClosePanel().</summary>
        public void Show(InfoCardData data, HotspotController caller)
        {
            if (data == null)
            {
                Debug.LogWarning("[InfoCardPanel] Show() llamado sin datos. Se ignora.");
                return;
            }

            _caller = caller;

            // Activar antes de asignar textos (TMP con auto-size inactivo a veces no se redibuja).
            gameObject.SetActive(true);
            HudHider.Hide(this, _hudElementsToHide);
            BlockInput(true);

            if (titleText != null) titleText.text = data.title;

            if (introText != null)
            {
                bool hasIntro = !string.IsNullOrWhiteSpace(data.intro);
                introText.gameObject.SetActive(hasIntro);
                introText.text = data.intro;
            }

            if (backgroundImage != null && data.background != null)
                backgroundImage.sprite = data.background;

            PopulateRows(data.rows);
        }

        // ── Visualización ──────────────────────────────────────────────────────────
        private void PopulateRows(InfoCardData.Row[] dataRows)
        {
            if (rows == null) return;

            int count = dataRows != null ? dataRows.Length : 0;
            if (count > rows.Length)
                Debug.LogWarning($"[InfoCardPanel] {count} filas en los datos pero solo {rows.Length} espacios en el panel; se ignoran las de más.");

            for (int i = 0; i < rows.Length; i++)
            {
                RowSlot slot = rows[i];
                if (slot == null) continue;

                bool used = i < count && dataRows[i] != null;
                if (slot.root != null) slot.root.SetActive(used);
                if (!used) continue;

                InfoCardData.Row row = dataRows[i];
                if (slot.text != null) slot.text.text = row.text;
                if (slot.icon != null)
                {
                    bool hasIcon = row.icon != null;
                    slot.icon.gameObject.SetActive(hasIcon);
                    if (hasIcon) slot.icon.sprite = row.icon;
                }
            }
        }

        // ── Cierre ─────────────────────────────────────────────────────────────────
        private void Close()
        {
            BlockInput(false);
            HudHider.Restore(this);
            gameObject.SetActive(false);

            HotspotController caller = _caller;
            _caller = null;

            // Continúa la cadena del hotspot: trivia (si hay) y luego el cierre normal
            caller?.ClosePanel();
        }

        // ── Helpers ────────────────────────────────────────────────────────────────
        private void BlockInput(bool block)
        {
            if (StageManager.Instance != null)
                StageManager.Instance.SetPlayerInputBlocked(block);
        }
    }
}
