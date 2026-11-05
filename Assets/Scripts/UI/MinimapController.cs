namespace SIRMED.UI
{
    using System;
    using System.Collections.Generic;
    using Google.XR.ARCoreExtensions;
    using Google.XR.ARCoreExtensions.GeospatialCreator;
    using SIRMED.Gameplay.Environment;
    using SIRMED.Gameplay.Hotspots;
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>
    /// MinimapController — radar simple del HUD: el jugador queda fijo en el centro
    /// y los hotspots activos y anclados aparecen como puntos alrededor, a escala,
    /// hasta un radio configurable (GDD §7.2, fase "Minimapa").
    ///
    /// Nota de diseño: las distancias NO salen de lat/lon/AREarthManager. Los hotspots
    /// ya quedan anclados en el mismo espacio-mundo de Unity que la cámara (ver
    /// HotspotController.IsAnchorReady), y el minimapa reutiliza esa posición mundial.
    ///
    /// Orientación "norte arriba" (alineación por anclajes): los ejes X/Z de la sesión
    /// AR no apuntan al norte. Cada ARGeospatialCreatorAnchor ya anclado aporta un par
    /// (posición en el mundo AR, lat/lon conocida); con 2 o más pares separados se
    /// ajusta por mínimos cuadrados la rotación + traslación mundo AR -> mapa (escala 1,
    /// ambos en metros). Así puntos, ruta y fondo coinciden siempre entre sí, porque
    /// salen de la misma fuente. Se recalcula cada alignmentInterval s (cubre la
    /// recalibración de GeoAnchorManager). Mientras no haya ajuste, el radar cae al
    /// modo anterior ("world-up", sin fondo). El ícono del jugador rota con su rumbo;
    /// el mapa y los puntos no rotan.
    ///
    /// Fondo de mapa (opcional): mapData (MinimapMapData, PNG de OSM georreferenciado)
    /// + mapImage (RawImage hija de RadarArea, primera hija, anclas y pivote al centro).
    /// El Mask de RadarArea lo recorta en círculo.
    ///
    /// Estructura esperada en escena:
    ///
    ///   MinimapController         ← este script
    ///   └── MinimapRoot           ← minimapRoot (se activa/desactiva con SetVisible)
    ///       └── RadarArea         ← radarArea (RectTransform circular; su ancho define el radio en px)
    ///           ├── PlayerIcon    ← playerIcon (fijo en el centro, rota con la cámara)
    ///           └── DotsContainer ← dotsContainer (vacío; acá se instancian los DotPrefab)
    ///   DotPrefab (Image)         ← dotPrefab, prefab asignado en el Inspector (no vive en la escena)
    ///
    /// Ruta y modo evacuación (GDD Mecánica 1, Tabla 14) — todo opcional:
    ///   • routeDotPrefab: desde showRouteFromLevel la ruta de EvacuationRouteController
    ///     se dibuja punteada (un punto cada routeDotSpacing metros).
    ///   • meetingPointIcon: ícono del punto de encuentro (hijo de RadarArea); se
    ///     muestra con la ruta y queda pegado al borde cuando está lejos, así siempre
    ///     indica la dirección hacia la zona segura.
    ///   • Desde evacuationModeLevel (N4) se ocultan los puntos de hotspots y quedan
    ///     solo jugador, ruta y punto de encuentro.
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

        [Header("Mapa de fondo + norte arriba (opcional)")]
        [Tooltip("Mapa georreferenciado del sitio (Assets > Create > AR > Minimap Map Data). Vacío = sin fondo.")]
        public MinimapMapData mapData;

        [Tooltip("RawImage hija de RadarArea (primera hija, debajo de DotsContainer), anclas y pivote al centro.")]
        public RawImage mapImage;

        [Tooltip("Si true, el radar se orienta al norte con el ajuste por anclajes (requiere 2 o más anclas resueltas).")]
        public bool northUp = true;

        [Tooltip("Segundos entre recálculos del ajuste mundo AR -> mapa.")]
        public float alignmentInterval = 1f;

        [Tooltip("Separación mínima (m) entre anclas para considerar fiable la rotación.")]
        public float minAnchorSpread = 8f;

        [Tooltip("Anclas a más de esta distancia (m) del sitio se ignoran en el ajuste. Evita que puntos de otro " +
                 "sitio (p. ej. el río de prueba del prefab Weather System, con coordenadas de Olaya) dañen el norte.")]
        public float maxAnchorDistance = 2000f;

        [Header("Ruta de evacuación (opcional)")]
        [Tooltip("Prefab de los puntos que dibujan la ruta. Vacío = no se dibuja la ruta.")]
        public RectTransform routeDotPrefab;

        [Tooltip("Separación en metros entre puntos de la ruta.")]
        public float routeDotSpacing = 4f;

        [Tooltip("Desde este nivel se dibujan la ruta y el punto de encuentro (GDD: N2 como aprendizaje).")]
        public RiskLevel showRouteFromLevel = RiskLevel.N2;

        [Tooltip("Ícono del punto de encuentro, hijo de RadarArea (encima de DotsContainer).")]
        public RectTransform meetingPointIcon;

        [Tooltip("Desde este nivel se ocultan los puntos de hotspots (modo evacuación).")]
        public RiskLevel evacuationModeLevel = RiskLevel.N4;

        [Serializable]
        public class CategoryStyle
        {
            public HotspotCategory category;
            public Color color = Color.white;
            [Tooltip("Opcional: reemplaza el sprite del punto para este tipo.")]
            public Sprite sprite;
        }

        [Header("Colores por tipo de hotspot")]
        [Tooltip("Color (y sprite opcional) del punto según HotspotController.minimapCategory " +
                 "(Auto = deducido del ícono 3D hijo). Prioridad: tipo → riskLevel del HotspotData → defaultDotColor.")]
        public CategoryStyle[] categoryStyles =
        {
            new CategoryStyle { category = HotspotCategory.Comunitaria,    color = new Color(0.98f, 0.78f, 0.10f) }, // amarillo
            new CategoryStyle { category = HotspotCategory.Lider,          color = new Color(0.65f, 0.40f, 0.90f) }, // morado
            new CategoryStyle { category = HotspotCategory.RutaEvacuacion, color = new Color(1.00f, 0.55f, 0.10f) }, // naranja
            new CategoryStyle { category = HotspotCategory.PuntoEncuentro, color = new Color(0.15f, 0.85f, 0.35f) }, // verde
            new CategoryStyle { category = HotspotCategory.Clima,          color = new Color(0.30f, 0.85f, 0.95f) }, // cian
            new CategoryStyle { category = HotspotCategory.Documentos,     color = new Color(0.95f, 0.95f, 0.95f) }, // blanco
            new CategoryStyle { category = HotspotCategory.Trivia,         color = new Color(0.95f, 0.35f, 0.70f) }, // rosa
            new CategoryStyle { category = HotspotCategory.Sirena,         color = new Color(0.90f, 0.20f, 0.20f) }, // rojo
        };

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
        private readonly List<RectTransform> _routeDots = new List<RectTransform>();

        // Ajuste mundo AR (x, z) -> mapa (este, norte): m = R(theta)·w + t
        private ARGeospatialCreatorAnchor[] _geoAnchors = Array.Empty<ARGeospatialCreatorAnchor>();
        private bool _aligned;
        private float _alignCos = 1f, _alignSin;
        private float _alignDeg;
        private Vector2 _alignT;
        private float _nextAlignTime;
        private double _originLat, _originLon;
        private readonly List<Vector2> _worldPts = new List<Vector2>();
        private readonly List<Vector2> _mapPts = new List<Vector2>();

        // ── Lifecycle ─────────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            if (dotPrefab != null)
                dotPrefab.gameObject.SetActive(false);
            if (routeDotPrefab != null && routeDotPrefab.gameObject.scene.IsValid())
                routeDotPrefab.gameObject.SetActive(false);
            if (meetingPointIcon != null)
                meetingPointIcon.gameObject.SetActive(false);
        }

        private void Start()
        {
            if (Camera.main != null)
                _playerCamera = Camera.main.transform;

            var allAnchors = FindObjectsByType<ARGeospatialCreatorAnchor>(FindObjectsInactive.Include);
            if (mapData != null)
            {
                _originLat = mapData.centerLatitude;
                _originLon = mapData.centerLongitude;
            }
            else if (allAnchors.Length > 0)
            {
                // Mediana: robusta frente a unas pocas anclas de otro sitio.
                var lats = new List<double>(allAnchors.Length);
                var lons = new List<double>(allAnchors.Length);
                foreach (var a in allAnchors) { lats.Add(a.Latitude); lons.Add(a.Longitude); }
                lats.Sort(); lons.Sort();
                _originLat = lats[lats.Count / 2];
                _originLon = lons[lons.Count / 2];
            }

            var nearby = new List<ARGeospatialCreatorAnchor>(allAnchors.Length);
            foreach (var a in allAnchors)
            {
                if (MinimapMapData.LatLonToMeters(a.Latitude, a.Longitude, _originLat, _originLon).magnitude <= maxAnchorDistance)
                    nearby.Add(a);
            }
            if (nearby.Count < allAnchors.Length)
                Debug.Log($"[Minimap] {allAnchors.Length - nearby.Count} anclas a más de {maxAnchorDistance:F0} m del sitio se ignoran para orientar el mapa.");
            _geoAnchors = nearby.ToArray();

            if (mapImage != null)
            {
                mapImage.raycastTarget = false;
                if (mapData != null && mapData.texture != null) mapImage.texture = mapData.texture;
                mapImage.gameObject.SetActive(false);
            }

            SetVisible(_visible);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (!_visible || _playerCamera == null || radarArea == null) return;

            RiskLevel level = RiskLevelIndicator.Instance != null
                ? RiskLevelIndicator.Instance.CurrentLevel
                : RiskLevel.None;
            bool evacuationMode = level != RiskLevel.None && level >= evacuationModeLevel;
            bool showRoute = level != RiskLevel.None && level >= showRouteFromLevel;

            if (northUp && Time.time >= _nextAlignTime)
            {
                _nextAlignTime = Time.time + alignmentInterval;
                UpdateAlignment();
            }

            RotatePlayerIcon();
            RefreshMap();
            if (evacuationMode) HideAllDots();
            else RefreshDots();
            RefreshRoute(showRoute);
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
            // Rumbo respecto al norte: rotar el mundo theta (antihorario) resta theta al yaw (horario).
            float heading = _aligned ? yaw - _alignDeg : yaw;
            playerIcon.localEulerAngles = new Vector3(0f, 0f, -heading);
        }

        // ── Alineación mundo AR -> mapa (norte arriba) ────────────────────────────

        /// <summary>
        /// Ajuste rígido 2D por mínimos cuadrados (Procrustes sin escala) entre las
        /// posiciones AR de las anclas ya resueltas y sus lat/lon proyectadas.
        /// </summary>
        private void UpdateAlignment()
        {
            _worldPts.Clear();
            _mapPts.Clear();
            foreach (var anchor in _geoAnchors)
            {
                if (anchor == null) continue;
                Transform parent = anchor.transform.parent;
                if (parent == null || parent.GetComponent<ARGeospatialAnchor>() == null) continue;
                Vector3 w = anchor.transform.position;
                _worldPts.Add(new Vector2(w.x, w.z));
                _mapPts.Add(MinimapMapData.LatLonToMeters(anchor.Latitude, anchor.Longitude, _originLat, _originLon));
            }

            int n = _worldPts.Count;
            if (n < 2) { _aligned = false; return; }

            Vector2 wc = Vector2.zero, mc = Vector2.zero;
            for (int i = 0; i < n; i++) { wc += _worldPts[i]; mc += _mapPts[i]; }
            wc /= n; mc /= n;

            float sDot = 0f, sCross = 0f, maxSpread = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = _worldPts[i] - wc, b = _mapPts[i] - mc;
                sDot += a.x * b.x + a.y * b.y;
                sCross += a.x * b.y - a.y * b.x;
                maxSpread = Mathf.Max(maxSpread, b.magnitude);
            }
            if (maxSpread * 2f < minAnchorSpread) { _aligned = false; return; }

            float theta = Mathf.Atan2(sCross, sDot);
            _alignCos = Mathf.Cos(theta);
            _alignSin = Mathf.Sin(theta);
            _alignDeg = theta * Mathf.Rad2Deg;
            _alignT = mc - Rotate(wc);
            _aligned = true;
        }

        private Vector2 Rotate(Vector2 v) =>
            new Vector2(v.x * _alignCos - v.y * _alignSin, v.x * _alignSin + v.y * _alignCos);

        /// <summary>Offset mundo (x, z) -> marco del radar: (este, norte) si hay ajuste, ejes AR si no.</summary>
        private Vector2 WorldOffsetToMap(Vector3 worldOffset)
        {
            Vector2 v = new Vector2(worldOffset.x, worldOffset.z);
            return _aligned ? Rotate(v) : v;
        }

        // ── Fondo de mapa ─────────────────────────────────────────────────────────
        private void RefreshMap()
        {
            if (mapImage == null) return;
            bool show = _aligned && mapData != null && mapData.texture != null;
            if (mapImage.gameObject.activeSelf != show) mapImage.gameObject.SetActive(show);
            if (!show) return;

            float pxPerMeter = radarArea.rect.width * 0.5f / worldRange;
            Vector3 c = _playerCamera.position;
            Vector2 camMap = Rotate(new Vector2(c.x, c.z)) + _alignT; // metros respecto al centro del PNG

            RectTransform rt = mapImage.rectTransform;
            rt.sizeDelta = mapData.SizeMeters * pxPerMeter;
            rt.anchoredPosition = -camMap * pxPerMeter;
            rt.localRotation = Quaternion.identity;
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

                Vector2 flat = WorldOffsetToMap(hotspot.transform.position - camPos);
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
                CategoryStyle style = GetCategoryStyle(hotspot.ResolvedCategory);
                if (style != null && style.sprite != null)
                    img.sprite = style.sprite;
                else if (hotspot.data != null && hotspot.data.icon != null)
                    img.sprite = hotspot.data.icon;

                if (style != null)
                    img.color = style.color;
                else
                    img.color = hotspot.data != null ? GetRiskColor(hotspot.data.riskLevel) : defaultDotColor;
            }

            _dots[hotspot] = dot;
            return dot;
        }

        private void HideAllDots()
        {
            foreach (var kv in _dots)
                if (kv.Value != null && kv.Value.gameObject.activeSelf)
                    kv.Value.gameObject.SetActive(false);
        }

        // ── Ruta + punto de encuentro ─────────────────────────────────────────────
        private void RefreshRoute(bool show)
        {
            EvacuationRouteController route = EvacuationRouteController.Instance;
            IReadOnlyList<Vector3> points = null;
            bool hasRoute = show && route != null && routeDotPrefab != null && dotsContainer != null &&
                            route.TryGetSampledPoints(routeDotSpacing, out points);

            float radiusPx = radarArea.rect.width * 0.5f;
            float maxPx = radiusPx - edgePaddingPx;
            Vector3 camPos = _playerCamera.position;

            int used = 0;
            if (hasRoute)
            {
                foreach (Vector3 p in points)
                {
                    Vector2 px = ToRadar(p - camPos, radiusPx);
                    if (px.magnitude > maxPx) continue; // la ruta solo se dibuja dentro del radar
                    RectTransform dot = GetRouteDot(used++);
                    dot.anchoredPosition = px;
                }
            }
            for (int i = used; i < _routeDots.Count; i++)
                if (_routeDots[i].gameObject.activeSelf) _routeDots[i].gameObject.SetActive(false);

            if (meetingPointIcon == null) return;
            Transform meeting = route != null ? route.puntoDeEncuentro : null;
            bool meetingReady = show && meeting != null && IsAnchored(meeting);
            if (meetingPointIcon.gameObject.activeSelf != meetingReady)
                meetingPointIcon.gameObject.SetActive(meetingReady);
            if (!meetingReady) return;

            Vector2 mpx = ToRadar(meeting.position - camPos, radiusPx);
            if (mpx.magnitude > maxPx) mpx = mpx.normalized * maxPx; // siempre marca la dirección
            meetingPointIcon.anchoredPosition = mpx;
        }

        private Vector2 ToRadar(Vector3 worldOffset, float radiusPx) =>
            WorldOffsetToMap(worldOffset) * (radiusPx / worldRange);

        private RectTransform GetRouteDot(int index)
        {
            while (_routeDots.Count <= index)
            {
                RectTransform dot = Instantiate(routeDotPrefab, dotsContainer);
                dot.gameObject.name = $"RouteDot_{_routeDots.Count}";
                dot.SetAsFirstSibling(); // debajo de los puntos de hotspots
                _routeDots.Add(dot);
            }
            RectTransform d = _routeDots[index];
            if (!d.gameObject.activeSelf) d.gameObject.SetActive(true);
            return d;
        }

        private static bool IsAnchored(Transform t)
        {
            var hotspot = t.GetComponent<HotspotController>();
            if (hotspot != null) return hotspot.IsAnchorReady();
            return t.parent != null && t.parent.GetComponent<Google.XR.ARCoreExtensions.ARGeospatialAnchor>() != null;
        }

        private void HideDot(HotspotController hotspot)
        {
            if (_dots.TryGetValue(hotspot, out var dot) && dot != null && dot.gameObject.activeSelf)
                dot.gameObject.SetActive(false);
        }

        private CategoryStyle GetCategoryStyle(HotspotCategory category)
        {
            if (categoryStyles == null) return null;
            foreach (var s in categoryStyles)
                if (s != null && s.category == category) return s;
            return null;
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
