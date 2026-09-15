namespace SIRMED.UI
{
    using TMPro;
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>
    /// Panel de ajustes compacto — sonido global.
    ///
    /// Comportamiento:
    ///   - Abre/cierra desde un botón en HUD (no es fullscreen).
    ///   - Al abrir: oculta los elementos del HUD indicados en hudElementsToHide[].
    ///   - Al cerrar: restaura esos elementos a su estado previo.
    ///   - Persiste el estado en PlayerPrefs.
    ///
    /// Nota de portado (SATCS → SIRMED_AR): se eliminaron los botones de
    /// inversión de ejes del giroscopio (InvertPitch/Roll/Yaw) — dependían de
    /// GyroscopeManager, que no existe en AR real (el tracking de AR Foundation
    /// no necesita ni permite ese tipo de ajuste manual). Solo se conserva el
    /// control de sonido, que es independiente de la fuente de tracking.
    /// </summary>
    public class SettingsPanel : MonoBehaviour
    {
        public static SettingsPanel Instance { get; private set; }

        // ── Referencias de botones ─────────────────────────────────────────────────
        [Header("Botones del panel")]
        [SerializeField] private Button _closeButton;
        [SerializeField] private Button _soundOnButton;
        [SerializeField] private Button _soundOffButton;

        // ── Colores de estado ──────────────────────────────────────────────────────
        [Header("Colores — estado activo / inactivo")]
        [SerializeField] private Color _colorFondoActivo = new Color(1.00f, 0.80f, 0.00f, 1f); // amarillo
        [SerializeField] private Color _colorFondoInactivo = new Color(0.14f, 0.16f, 0.26f, 1f); // navy
        [SerializeField] private Color _colorTextoActivo = new Color(0.14f, 0.16f, 0.26f, 1f); // navy oscuro
        [SerializeField] private Color _colorTextoInactivo = new Color(1.00f, 0.80f, 0.00f, 1f); // amarillo

        // ── Elementos HUD a ocultar ────────────────────────────────────────────────
        [Header("HUD — ocultar mientras el panel esté abierto")]
        [SerializeField] private GameObject[] _hudElementsToHide;

        // ── PlayerPrefs keys ───────────────────────────────────────────────────────
        private const string KEY_SOUND = "settings_sound_on";

        // ── Estado interno ─────────────────────────────────────────────────────────
        private bool _soundOn;
        private bool[] _hudWasActive;

        // ── Lifecycle ──────────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            // NO DontDestroyOnLoad — panel de escena, no Singleton global
        }

        private void Start()
        {
            LoadPrefs();
            WireButtons();
            RefreshSoundButtons();
            gameObject.SetActive(false);
        }

        // ── API pública ────────────────────────────────────────────────────────────

        /// <summary>Abre el panel y oculta los elementos del HUD.</summary>
        public void Open()
        {
            // Guardar estado previo del HUD y ocultar
            _hudWasActive = new bool[_hudElementsToHide.Length];
            for (int i = 0; i < _hudElementsToHide.Length; i++)
            {
                if (_hudElementsToHide[i] == null) continue;
                _hudWasActive[i] = _hudElementsToHide[i].activeSelf;
                _hudElementsToHide[i].SetActive(false);
            }

            RefreshSoundButtons();
            gameObject.SetActive(true);
        }

        /// <summary>Cierra el panel y restaura el HUD.</summary>
        public void Close()
        {
            gameObject.SetActive(false);
            RestoreHUD();
        }

        // ── Sonido ─────────────────────────────────────────────────────────────────
        private void SetSound(bool on)
        {
            _soundOn = on;
            AudioListener.volume = on ? 1f : 0f;
            PlayerPrefs.SetInt(KEY_SOUND, on ? 1 : 0);
            PlayerPrefs.Save();
            RefreshSoundButtons();
        }

        // ── Feedback visual ────────────────────────────────────────────────────────
        private void RefreshSoundButtons()
        {
            SetButtonState(_soundOnButton, _soundOn);
            SetButtonState(_soundOffButton, !_soundOn);
        }

        /// <summary>Aplica color de fondo y texto al botón según si está "activo".</summary>
        private void SetButtonState(Button btn, bool active)
        {
            if (btn == null) return;

            var img = btn.GetComponent<Image>();
            if (img != null)
                img.color = active ? _colorFondoActivo : _colorFondoInactivo;

            var label = btn.GetComponentInChildren<TMP_Text>();
            if (label != null)
                label.color = active ? _colorTextoActivo : _colorTextoInactivo;
        }

        // ── Init ───────────────────────────────────────────────────────────────────
        private void LoadPrefs()
        {
            _soundOn = PlayerPrefs.GetInt(KEY_SOUND, 1) == 1;
            AudioListener.volume = _soundOn ? 1f : 0f;
        }

        private void WireButtons()
        {
            _closeButton?.onClick.AddListener(Close);
            _soundOnButton?.onClick.AddListener(() => SetSound(true));
            _soundOffButton?.onClick.AddListener(() => SetSound(false));
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
