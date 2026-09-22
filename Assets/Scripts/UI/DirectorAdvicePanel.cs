namespace SIRMED.UI
{
    using System;
    using SIRMED.Gameplay.Director;
    using SIRMED.Managers;
    using TMPro;
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>
    /// DirectorAdvicePanel — banner breve del Director DAGRD (GDD pág. 28; Anexo A).
    ///
    /// A diferencia de NpcDialoguePanel/SiataCallPanel, este panel NO oculta el HUD
    /// ni bloquea al jugador: el GDD exige que el consejo sea "breve, accionable y
    /// compatible con caminar de forma segura" (nunca debe competir con moverse
    /// durante una evacuación N4). Por eso vive como un banner superpuesto, no como
    /// un panel de pantalla completa.
    ///
    /// Paginado con el mismo patrón de NpcDialoguePanel (texto + audio opcional +
    /// botón "▶" que corta el audio en curso y avanza a la siguiente línea), pero
    /// sin opciones de respuesta — el Director da consejos, no hace preguntas.
    ///
    /// Jerarquía sugerida en escena:
    ///   DirectorAdvicePanel         [este script]
    ///   └── BannerRoot              [Image de fondo]              ← _root (oculto por defecto)
    ///       ├── AdviceText          [TMP]                          ← _text
    ///       └── NextButton          [Button] (▶)                  ← _nextButton
    /// </summary>
    public class DirectorAdvicePanel : MonoBehaviour
    {
        // ── Singleton ─────────────────────────────────────────────────────────────
        public static DirectorAdvicePanel Instance { get; private set; }

        [Header("UI")]
        [SerializeField] private GameObject _root;
        [SerializeField] private TextMeshProUGUI _text;
        [SerializeField] private Button _nextButton;

        [Header("Audio de voz")]
        [SerializeField] private AudioSource _voiceSource;
        [Tooltip("Volumen del ambiente mientras el banner está visible. Menos agresivo que NpcDialoguePanel porque este panel no toma control total de la pantalla.")]
        [Range(0f, 1f)] [SerializeField] private float _ambientDuckVolume = 0.4f;
        [Range(0f, 2f)] [SerializeField] private float _duckFadeDuration = 0.3f;

        /// <summary>True mientras el banner está visible (una línea en pantalla). Usado por DirectorAdviceController para no interrumpir un consejo en curso.</summary>
        public bool IsShowing { get; private set; }

        private DirectorAdviceData _current;
        private int _lineIndex;
        private Action<DirectorAdviceLine> _onLineShown;
        private Action _onFinished;

        // ── Lifecycle ─────────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            if (_voiceSource == null)
            {
                _voiceSource = gameObject.AddComponent<AudioSource>();
                _voiceSource.playOnAwake = false;
                _voiceSource.loop = false;
                _voiceSource.spatialBlend = 0f; // 2D — la voz del Director es siempre omnidireccional
            }

            if (_nextButton != null) _nextButton.onClick.AddListener(Advance);
            if (_root != null) _root.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ── API pública ───────────────────────────────────────────────────────────
        /// <param name="advice">Consejo a mostrar (líneas paginadas).</param>
        /// <param name="onLineShown">Invocado con cada línea al mostrarla — DirectorController lo usa para disparar el gesto asociado.</param>
        /// <param name="onFinished">Invocado una sola vez, al cerrar el banner tras la última línea.</param>
        public void Show(DirectorAdviceData advice, Action<DirectorAdviceLine> onLineShown, Action onFinished)
        {
            if (advice == null || advice.lines == null || advice.lines.Length == 0) return;

            _current = advice;
            _lineIndex = -1;
            _onLineShown = onLineShown;
            _onFinished = onFinished;
            IsShowing = true;

            if (_root != null) _root.SetActive(true);
            AudioStageManager.Instance?.DuckAmbient(_ambientDuckVolume, _duckFadeDuration);

            Advance();
        }

        // ── Paginado ──────────────────────────────────────────────────────────────
        private void Advance()
        {
            _lineIndex++;
            if (_current == null || _lineIndex >= _current.lines.Length)
            {
                Close();
                return;
            }

            DirectorAdviceLine line = _current.lines[_lineIndex];
            if (_text != null) _text.text = line.text;

            _voiceSource.Stop();
            if (line.audio != null)
            {
                _voiceSource.clip = line.audio;
                _voiceSource.Play();
            }

            _onLineShown?.Invoke(line);
        }

        private void Close()
        {
            IsShowing = false;
            if (_root != null) _root.SetActive(false);
            _voiceSource.Stop();
            AudioStageManager.Instance?.RestoreVolume(_duckFadeDuration);

            Action finished = _onFinished;
            _current = null;
            _onLineShown = null;
            _onFinished = null;
            finished?.Invoke();
        }
    }
}
