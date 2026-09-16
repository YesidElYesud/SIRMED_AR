namespace SIRMED.UI
{
    using System.Collections.Generic;
    using SIRMED.Gameplay.Hotspots;
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>
    /// MinimapController — radar simple del HUD: el jugador queda fijo en el centro
    /// y los hotspots activos y anclados aparecen como puntos alrededor, a escala,
    /// hasta un radio configurable (GDD §7.2, fase "Minimapa").
    ///
    /// Nota de diseño: NO usa lat/lon/AREarthManager. Los hotspots ya quedan
    /// anclados en el mismo espacio-mundo de Unity que la cámara (ver
    /// HotspotController.IsAnchorReady/CheckProximity, que hacen lo mismo con
    /// Vector3.Distance). El minimapa reutiliza esa misma posición mundial en vez
    /// de recalcular distancias geodésicas — es la fuente de verdad más simple y ya
    /// validada en el proyecto. La orientación es "world-up" (los ejes X/Z de la
    /// sesión AR, fijos durante toda la partida): el ícono del jugador rota para
    /// indicar hacia dónde mira la cámara, los puntos NO rotan con él.
    ///
    /// Estructura esperada en escena:
    ///
    ///   MinimapController         ← este script
    ///   └── MinimapRoot           ← minimapRoot (se activa/desactiva con SetVisible)
    ///       └── RadarArea         ← radarArea (RectTransform circular; su ancho define el radio en px)
    ///           ├── PlayerIcon    ← playerIcon (fijo en el centro, rota con la cámara)
    ///           └── DotsContainer ← dotsContainer (vacío; acá se instancian los DotPrefab)
    ///   DotPrefab (Image)         ← dotPrefab, prefab asignado en el Inspector (no vive en la escena)
    /// </summary>
    public class MinimapController : MonoBehaviour
    {
        // ── Singleton ─────────────────────────────────────────────────────────────
        public static MinimapController Instance { get; private set; }

        [Header("Referencias UI")]
        [Tooltip("Raíz que se activa/desactiva con SetVisible.")]
        public GameObject minimapRoot;

        [Tooltip("RectTransform circular del radar. Su ancho (rect.width) define el radio en píxeles.")]
        public RectTransform radarArea;

        [Tooltip("Ícono del jugador, fijo en el centro del radar. Rota sobre Z para indicar hacia dónde mira la cámara.")]
        public RectTransform playerIcon;

        [Tooltip("Contenedor vacío donde se instancian los puntos de hotspot (DotPrefab).")]
        public RectTransform dotsContainer;

        [Tooltip("Prefab del punto de hotspot. Debe tener una Image en la raíz.")]
        public RectTransform dotPrefab;

        [Header("Rango")]
        [Tooltip("Distancia en metros que representa el borde del radar.")]
        public float worldRange = 60f;

        [Tooltip("Si true, un hotspot más allá de worldRange se muestra pegado al borde apuntando hacia él.\n" +
                 "Si false, simplemente no se dibuja (solo 'POIs cercanos', como pide el GDD).")]
        public bool clampToEdge = false;

        [Tooltip("Margen en píxeles para que el punto no quede pegado justo al borde del radar.")]
        public float edgePaddingPx = 6f;

        [Header("Colores por nivel de riesgo (opcional)")]
        [Tooltip("Color de respaldo cuando el hotspot no tiene riskLevel o no hay color asignado.")]
        public Color defaultDotColor = Color.white;
        public Color colorN1 = new Color(0.30f, 0.80f, 0.35f);
        public Color colorN2 = new Color(0.95f, 0.85f, 0.25f);
        public Color colorN3 = new Color(0.95f, 0.55f, 0.15f);
        public Color colorN4 = new Color(0.90f, 0.20f, 0.20f);

        // ── Internos ──────────────────────────────────────────────────────────────
        private Transform _playerCamera;
        private readonly Dictionary<HotspotController, RectTransform> _dots =
            new Dictionary<HotspotController, RectTransform>();
        private readonly List<HotspotController> _staleBuffer = new List<HotspotController>();
        private bool _visible = true;

        // ── Lifecycle ─────────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            if (dotPrefab != null)
                dotPrefab.gameObject.SetActive(false);
        }

        private void Start()
        {
            if (Camera.main != null)
                _playerCamera = Camera.main.transform;

            SetVisible(_visible);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (!_visible || _playerCamera == null || radarArea == null) return;

            RotatePlayerIcon();
            RefreshDots();
        }

        // ── API pública ───────────────────────────────────────────────────────────

        /// <summary>Muestra u oculta el widget completo (p.ej. durante una cinemática).</summary>
        public void SetVisible(bool visible)
        {
            _visible = visible;
            if (minimapRoot != null) minimapRoot.SetActive(visible);
        }

        // ── Orientación del jugador ───────────────────────────────────────────────
        private void RotatePlayerIcon()
        {
            if (playerIcon == null) return;
            float yaw = _playerCamera.eulerAngles.y;
            playerIcon.localEulerAngles = new Vector3(0f, 0f, -yaw);
        }

        // ── Puntos de hotspot ─────────────────────────────────────────────────────
        private void RefreshDots()
        {
            if (dotsContainer == null || dotPrefab == null) return;

            float radiusPx = radarArea.rect.width * 0.5f;
            Vector3 camPos = _playerCamera.position;

            // Soltar puntos de hotspots que ya no están activos (cambio de etapa, etc.)
            if (_dots.Count > 0)
            {
                _staleBuffer.Clear();
                foreach (var kv in _dots)
                    if (kv.Key == null || !kv.Key.isActiveAndEnabled)
                        _staleBuffer.Add(kv.Key);
                foreach (var h in _staleBuffer)
                {
                    if (_dots.TryGetValue(h, out var rt) && rt != null) Destroy(rt.gameObject);
                    _dots.Remove(h);
                }
            }

            foreach (var hotspot in HotspotController.ActiveHotspots)
            {
                if (hotspot == null || !hotspot.IsAnchorReady()) continue;

                Vector3 offset = hotspot.transform.position - camPos;
                Vector2 flat = new Vector2(offset.x, offset.z);
                float dist = flat.magnitude;

                bool withinRange = dist <= worldRange;
                if (!withinRange && !clampToEdge)
                {
                    HideDot(hotspot);
                    continue;
                }

                Vector2 pointPx = flat * (radiusPx / worldRange);
                float maxPx = radiusPx - edgePaddingPx;
                if (pointPx.magnitude > maxPx)
                    pointPx = pointPx.normalized * maxPx;

                RectTransform dot = GetOrCreateDot(hotspot);
                dot.anchoredPosition = pointPx;
            }
        }

        private RectTransform GetOrCreateDot(HotspotController hotspot)
        {
            if (_dots.TryGetValue(hotspot, out var existing) && existing != null)
            {
                if (!existing.gameObject.activeSelf) existing.gameObject.SetActive(true);
                return existing;
            }

            RectTransform dot = Instantiate(dotPrefab, dotsContainer);
            dot.gameObject.SetActive(true);
            dot.gameObject.name = $"Dot_{hotspot.name}";

            Image img = dot.GetComponent<Image>();
            if (img != null)
            {
                // Si el hotspot no trae ícono propio, se conserva el sprite por defecto del prefab.
                if (hotspot.data != null && hotspot.data.icon != null)
                    img.sprite = hotspot.data.icon;
                img.color = hotspot.data != null ? GetRiskColor(hotspot.data.riskLevel) : defaultDotColor;
            }

            _dots[hotspot] = dot;
            return dot;
        }

        private void HideDot(HotspotController hotspot)
        {
            if (_dots.TryGetValue(hotspot, out var dot) && dot != null && dot.gameObject.activeSelf)
                dot.gameObject.SetActive(false);
        }

        private Color GetRiskColor(RiskLevel level) => level switch
        {
            RiskLevel.N1 => colorN1,
            RiskLevel.N2 => colorN2,
            RiskLevel.N3 => colorN3,
            RiskLevel.N4 => colorN4,
            _ => defaultDotColor,
        };
    }
}
