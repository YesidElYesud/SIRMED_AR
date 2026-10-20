namespace SIRMED.UI
{
    using SIRMED.Managers;
    using TMPro;
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>
    /// SettingsPanel — Botón SONIDOS del HUD + panel compacto de ajustes de sonido.
    ///
    ///   - Desactivar todo el sonido   (Toggle, marcado = silenciado; voces incluidas)
    ///   - Desactivar solo efectos     (Toggle, marcado = silenciado; ambiente, sirena, obturador)
    ///   - Volumen del sonido          (Slider 0–1)
    ///
    /// El estado y la persistencia viven en SoundSettings (estático); este script solo
    /// es la UI. Con "todo" silenciado, el toggle de efectos y el slider quedan no
    /// interactuables (no tendrían efecto audible).
    ///
    /// Va en un GameObject SIEMPRE ACTIVO (p. ej. un contenedor en Safe Area) que tenga
    /// como hijos el botón y el panel: el panel se muestra/oculta con _panelRoot, no con
    /// este GameObject, para que Awake() corra aunque el panel arranque oculto.
    ///
    /// Jerarquía sugerida:
    ///   SoundSettings              [este script]
    ///   ├── SoundButton            [Button, Image]  ← _openButton (mismo tamaño que ScreenshotButton)
    ///   └── SoundPanel             ← _panelRoot
    ///       ├── MuteAllToggle      [Toggle]  ← _muteAllToggle
    ///       ├── MuteEffectsToggle  [Toggle]  ← _muteEffectsToggle
    ///       ├── VolumeSlider       [Slider]  ← _volumeSlider
    ///       │   └── ValueText      [TMP]     ← _volumeValueText (opcional, "80%")
    ///       └── CloseButton        [Button]  ← _closeButton (opcional)
    /// </summary>
    public class SettingsPanel : MonoBehaviour
    {
        public static SettingsPanel Instance { get; private set; }

        // ── Referencias ────────────────────────────────────────────────────────────
        [Header("Botón del HUD")]
        [Tooltip("Abre/cierra el panel.")]
        [SerializeField] private Button _openButton;

        [Header("Panel")]
        [Tooltip("Raíz visual del panel; se activa/desactiva al abrir/cerrar.")]
        [SerializeField] private GameObject _panelRoot;
        [SerializeField] private Button _closeButton;

        [Header("Controles")]
        [Tooltip("Marcado = todo el sonido desactivado.")]
        [SerializeField] private Toggle _muteAllToggle;
        [Tooltip("Marcado = solo los efectos desactivados (las voces siguen).")]
        [SerializeField] private Toggle _muteEffectsToggle;
        [Tooltip("Volumen general, rango 0–1.")]
        [SerializeField] private Slider _volumeSlider;
        [Tooltip("Opcional: muestra el volumen en porcentaje.")]
        [SerializeField] private TMP_Text _volumeValueText;

        // ── Elementos HUD a ocultar ────────────────────────────────────────────────
        [Header("HUD — ocultar mientras el panel esté abierto")]
        [SerializeField] private GameObject[] _hudElementsToHide;

        // ── Estado interno ─────────────────────────────────────────────────────────
        private bool[] _hudWasActive;

        public bool IsOpen => _panelRoot != null && _panelRoot.activeSelf;

        // ── Lifecycle ──────────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            if (_panelRoot == null)
                Debug.LogWarning("[SettingsPanel] _panelRoot no asignado en el Inspector.", this);
            else
                _panelRoot.SetActive(false);

            if (_volumeSlider != null)
            {
                _volumeSlider.minValue = 0f;
                _volumeSlider.maxValue = 1f;
            }

            WireControls();
            Refresh();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ── API pública ────────────────────────────────────────────────────────────
        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        /// <summary>Abre el panel y oculta los elementos del HUD.</summary>
        public void Open()
        {
            if (_panelRoot == null || IsOpen) return;

            _hudWasActive = new bool[_hudElementsToHide.Length];
            for (int i = 0; i < _hudElementsToHide.Length; i++)
            {
                if (_hudElementsToHide[i] == null) continue;
                _hudWasActive[i] = _hudElementsToHide[i].activeSelf;
                _hudElementsToHide[i].SetActive(false);
            }

            Refresh();
            _panelRoot.SetActive(true);
        }

        /// <summary>Cierra el panel y restaura el HUD.</summary>
        public void Close()
        {
            if (_panelRoot == null || !IsOpen) return;
            _panelRoot.SetActive(false);
            RestoreHUD();
        }

        // ── Controles ──────────────────────────────────────────────────────────────
        private void WireControls()
        {
            _openButton?.onClick.AddListener(Toggle);
            _closeButton?.onClick.AddListener(Close);

            _muteAllToggle?.onValueChanged.AddListener(muted =>
            {
                SoundSettings.SetAllMuted(muted);
                Refresh();
            });
            _muteEffectsToggle?.onValueChanged.AddListener(SoundSettings.SetEffectsMuted);
            _volumeSlider?.onValueChanged.AddListener(value =>
            {
                SoundSettings.SetVolume(value);
                RefreshVolumeText();
            });
        }

        /// <summary>Sincroniza los controles con SoundSettings sin disparar sus eventos.</summary>
        private void Refresh()
        {
            bool allMuted = SoundSettings.AllMuted;

            if (_muteAllToggle != null) _muteAllToggle.SetIsOnWithoutNotify(allMuted);

            if (_muteEffectsToggle != null)
            {
                _muteEffectsToggle.SetIsOnWithoutNotify(SoundSettings.EffectsMuted);
                _muteEffectsToggle.interactable = !allMuted;
            }

            if (_volumeSlider != null)
            {
                _volumeSlider.SetValueWithoutNotify(SoundSettings.Volume);
                _volumeSlider.interactable = !allMuted;
            }

            RefreshVolumeText();
        }

        private void RefreshVolumeText()
        {
            if (_volumeValueText != null)
                _volumeValueText.text = $"{Mathf.RoundToInt(SoundSettings.Volume * 100f)}%";
        }

        private void RestoreHUD()
        {
            if (_hudWasActive == null) return;
            for (int i = 0; i < _hudElementsToHide.Length; i++)
            {
                if (_hudElementsToHide[i] == null) continue;
                _hudElementsToHide[i].SetActive(_hudWasActive[i]);
            }
            _hudWasActive = null;
        }
    }
}
