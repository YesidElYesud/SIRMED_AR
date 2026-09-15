namespace SIRMED.Gameplay.Hotspots
{
    using System.Collections;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>
    /// Botón de acción del HUD para hotspots.
    ///
    /// Se muestra cuando el jugador entra en el radio de un hotspot y se oculta al salir.
    /// Pulsa (escala seno) para llamar la atención. Al pulsarlo dispara DispatchAction()
    /// sobre el hotspot activo (el más cercano si hay varios en rango).
    ///
    /// Setup en Unity:
    ///   1. Crea un GO hijo del HUD llamado "HotspotPromptButton".
    ///   2. Añade este script + un Button.
    ///   3. Añade una Image hija circular (círculo blanco/azul) — "ButtonVisual".
    ///   4. Añade un TextMeshProUGUI o Text hijo de ButtonVisual con la letra "i".
    ///   5. Asigna buttonRoot = el propio RectTransform del GO (o un hijo que contenga la visual).
    ///   6. Asigna button = el componente Button del GO.
    ///   7. Desactiva el GO en escena (inactivo por defecto).
    /// </summary>
    public class HotspotPromptButton : MonoBehaviour
    {
        public static HotspotPromptButton Instance { get; private set; }

        [Header("Referencias UI")]
        [Tooltip("GameObject raíz que se activa/desactiva (puede ser este mismo GO o un hijo visual)")]
        [SerializeField] private GameObject buttonRoot;
        [Tooltip("Componente Button que recibe el clic del jugador")]
        [SerializeField] private Button button;

        [Header("Animación de pulso")]
        [Tooltip("Escala mínima del pulso (contracción)")]
        [SerializeField] private float pulseMinScale = 0.85f;
        [Tooltip("Escala máxima del pulso (expansión)")]
        [SerializeField] private float pulseMaxScale = 1.18f;
        [Tooltip("Ciclos de pulso por segundo")]
        [SerializeField] private float pulseSpeed = 1.6f;

        // ── Estado interno ─────────────────────────────────────────────────────────
        private readonly List<IHotspotInteractable> _nearbyHotspots = new List<IHotspotInteractable>();
        private IHotspotInteractable _activeHotspot;
        private Coroutine _pulseRoutine;

        // ── Lifecycle ──────────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            if (buttonRoot != null) buttonRoot.SetActive(false);
            if (button != null) button.onClick.AddListener(OnButtonClicked);
        }

        private void OnDestroy()
        {
            if (button != null) button.onClick.RemoveListener(OnButtonClicked);
            if (Instance == this) Instance = null;
        }

        // ── API pública ────────────────────────────────────────────────────────────

        /// <summary>
        /// Registra un hotspot como "en rango". El botón se muestra apuntando al más cercano.
        /// Llamado por HotspotController al entrar en su radio (o al cerrarse su panel mientras
        /// el jugador sigue en rango).
        /// </summary>
        public void RegisterHotspot(IHotspotInteractable hotspot)
        {
            if (hotspot == null) return;
            if (!_nearbyHotspots.Contains(hotspot))
                _nearbyHotspots.Add(hotspot);
            RefreshActive();
        }

        /// <summary>
        /// Desregistra un hotspot. Si era el activo, el botón busca el siguiente más cercano
        /// o se oculta si no queda ninguno.
        /// Llamado al salir del radio, al abrir el panel o al desactivarse el hotspot.
        /// </summary>
        public void UnregisterHotspot(IHotspotInteractable hotspot)
        {
            _nearbyHotspots.Remove(hotspot);
            RefreshActive();
        }

        // ── Lógica interna ─────────────────────────────────────────────────────────
        private void RefreshActive()
        {
            if (_nearbyHotspots.Count == 0)
            {
                _activeHotspot = null;
                HideButton();
                return;
            }

            _activeHotspot = FindClosest();
            ShowButton();
        }

        private IHotspotInteractable FindClosest()
        {
            Transform cam = Camera.main != null ? Camera.main.transform : null;
            if (cam == null) return _nearbyHotspots[0];

            IHotspotInteractable best = null;
            float bestD = float.MaxValue;
            foreach (var h in _nearbyHotspots)
            {
                if (h == null) continue;
                float d = Vector3.Distance(cam.position, h.transform.position);
                if (d < bestD) { bestD = d; best = h; }
            }
            return best;
        }

        private void ShowButton()
        {
            if (buttonRoot == null) return;
            buttonRoot.SetActive(true);
            if (_pulseRoutine == null)
                _pulseRoutine = StartCoroutine(PulseRoutine());
        }

        private void HideButton()
        {
            if (_pulseRoutine != null)
            {
                StopCoroutine(_pulseRoutine);
                _pulseRoutine = null;
            }
            if (buttonRoot != null)
            {
                buttonRoot.transform.localScale = Vector3.one;
                buttonRoot.SetActive(false);
            }
        }

        private IEnumerator PulseRoutine()
        {
            float t = 0f;
            while (true)
            {
                t += Time.deltaTime * pulseSpeed * Mathf.PI * 2f;
                float s = Mathf.Lerp(pulseMinScale, pulseMaxScale, (Mathf.Sin(t) + 1f) * 0.5f);
                if (buttonRoot != null)
                    buttonRoot.transform.localScale = new Vector3(s, s, 1f);
                yield return null;
            }
        }

        private void OnButtonClicked()
        {
            if (_activeHotspot != null)
                _activeHotspot.DispatchAction();
        }
    }
}
