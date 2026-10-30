namespace SIRMED.Gameplay.Environment
{
    using System.Collections;
    using System.Collections.Generic;
    using Google.XR.ARCoreExtensions;
    using Google.XR.ARCoreExtensions.GeospatialCreator;
    using SIRMED.Gameplay.Hotspots;
    using SIRMED.Managers;
    using SIRMED.UI;
    using Unity.Mathematics;
    using UnityEngine;
    using UnityEngine.Splines;
    using UnityEngine.XR.ARSubsystems;

    /// <summary>
    /// EvacuationRouteController — cinta plana geo-anclada que marca la ruta de
    /// evacuación (GDD; roadmap de MIGRATION_CONTEXT.md §7.2 fase 3 / §11).
    ///
    /// Reemplaza el enfoque del proyecto original (LineRenderer + curva
    /// Catmull-Rom escrita a mano + SetTextureOffset en Update, ver
    /// GPS_AR_Test_game/Assets/_Shared/Scripts/Environment/EvacuationRouteController.cs)
    /// por una malla real (una cinta plana, no una línea delgada):
    ///
    ///   • Los waypoints son GameObjects con RouteWaypoint + su propio
    ///     ARGeospatialCreatorAnchor, igual que un hotspot — no existe un
    ///     espacio de Editor fijo en runtime, cada punto solo es confiable una
    ///     vez que su anchor resuelve (ver MIGRATION_CONTEXT.md §3 y §11).
    ///   • Una vez todos los waypoints están anclados, se construye un
    ///     UnityEngine.Splines.Spline con esas posiciones de mundo
    ///     (TangentMode.AutoSmooth reemplaza el Catmull-Rom manual del original).
    ///   • La malla de la cinta se genera muestreando ese spline a mano (no con
    ///     SplineExtrude): en com.unity.splines 2.9.1 la forma de tubo por
    ///     defecto de SplineExtrude fuerza Sides >= 3 (ver Sides setter en
    ///     SplineExtrude.cs) y el corte transversal personalizado
    ///     (IExtrudeShape) es 'internal' al paquete, así que no hay forma de
    ///     pedirle desde este código un corte plano de 2 lados. Un builder
    ///     propio da control total sobre ancho, flotado sobre el suelo y el
    ///     UV.y en metros que necesita el shader para tilear la flecha.
    ///   • El material (shader "SIRMED/EvacuationRibbon") anima el flujo de
    ///     flechas en el propio shader vía _Time, sin tocar el material desde
    ///     C# cada frame — solo el fade in/out toca _Color (alpha) al mostrar
    ///     u ocultar la ruta.
    ///
    /// API pública: Show()/Hide(), igual firma que el original — se invoca
    /// desde HotspotController.ClosePanel() cuando data.activatesEvacuationRoute
    /// es true.
    ///
    /// Además la ruta se muestra sola desde autoShowFromLevel (GDD §13, Tabla 11:
    /// "se prioriza en N3 y se hace obligatoriamente visible en N4"). Si se baja de
    /// ese nivel, se oculta salvo que la haya abierto un hotspot. Tras llegar al
    /// punto de encuentro no se vuelve a mostrar sola hasta bajar de nivel.
    ///
    /// Validación de waypoints (prueba en Olaya 2026-10-29: la cinta se doblaba en U,
    /// flotaba y tenía flechas en reversa): un ARGeospatialAnchor recién creado lee
    /// (0,0,0) hasta su primer Update y luego ARCore sigue corrigiendo su pose. Por eso
    /// en dispositivo un waypoint solo entra a la cinta si su ancla está en Tracking,
    /// lleva stableSeconds sin moverse, y su distancia al waypoint anterior coincide con
    /// la que dan sus lat/lon/alt. Los que fallan se omiten (queda un tramo recto entre
    /// sus vecinos) y los tramos de menos de minSegmentLength se descartan. Después de
    /// construida, la cinta se rearma cuando las anclas se corren (deriva o
    /// recalibración); si el rearme no valida, se conserva la malla anterior.
    /// En el Editor nada de esto aplica: se usan todos los waypoints como antes.
    ///
    /// Varias rutas por escena: cada instancia valida y construye la suya; Instance es
    /// la primera registrada (la que usan hotspots, minimapa y Director) y All las lista.
    /// </summary>
    [RequireComponent(typeof(SplineContainer))]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class EvacuationRouteController : MonoBehaviour
    {
        public static EvacuationRouteController Instance { get; private set; }

        private static readonly List<EvacuationRouteController> _all = new List<EvacuationRouteController>();

        /// <summary>Todas las rutas de la escena (una por cada cinta).</summary>
        public static IReadOnlyList<EvacuationRouteController> All => _all;

        /// <summary>Se dispara justo antes del auto-ocultamiento por proximidad al punto de encuentro (ver Update). Usado por DirectorAdviceController para la regla "llegada" (GDD Tabla 15/Anexo A).</summary>
        public event System.Action OnArrivalAtPuntoDeEncuentro;

        [Header("Ruta — Waypoints")]
        [Tooltip("GameObjects con RouteWaypoint + ARGeospatialCreatorAnchor, en orden desde el origen hasta el Punto de Encuentro.")]
        public RouteWaypoint[] waypoints;

        [Tooltip("Muestras de la cinta por metro de longitud del spline. Más = curva más suave, más triángulos.")]
        [Range(0.2f, 5f)]
        public float samplesPerMeter = 1f;

        [Header("Punto de Encuentro")]
        [Tooltip("Al acercarse a este Transform la cinta se oculta automáticamente. Normalmente el mismo GameObject del hotspot 'PUNTO DE ENCUENTRO'.")]
        public Transform puntoDeEncuentro;

        [Tooltip("Radio (m) de llegada: alrededor del punto de encuentro o del último waypoint (final de la cinta).")]
        public float hideRadius = 4f;

        [Header("Puntos de control (checklist M07/M08)")]
        [Tooltip("Radio (m) para dar por alcanzado un waypoint intermedio mientras la ruta está visible.")]
        public float checkpointRadius = 6f;
        [Tooltip("Puntos de control intermedios que hay que pasar antes de que cuente la llegada al punto de encuentro " +
                 "(condición mínima de ruta). Se limita a los que existan; 0 = sin condición.")]
        public int minCheckpointsForArrival = 1;

        /// <summary>(alcanzados, total) cada vez que se pasa un punto de control intermedio.</summary>
        public event System.Action<int, int> OnCheckpointReached;

        /// <summary>Puntos de control intermedios (waypoints sin contar el primero ni el último).</summary>
        public int CheckpointCount => waypoints == null ? 0 : Mathf.Max(0, waypoints.Length - 2);
        public int CheckpointsReached { get; private set; }

        [Header("Visibilidad por nivel de riesgo (GDD §13)")]
        [Tooltip("Desde este nivel la ruta aparece sola. None = solo la abren los hotspots con activatesEvacuationRoute.")]
        public RiskLevel autoShowFromLevel = RiskLevel.N3;

        [Header("Apariencia")]
        [Tooltip("Ancho de la cinta en metros.")]
        public float ribbonWidth = 0.5f;

        [Tooltip("Altura (m) sobre cada waypoint para que la cinta 'flote' un poco sobre el piso real en vez de enterrarse en terreno irregular.")]
        public float heightOffset = 0.15f;

        [Tooltip("Material con shader 'SIRMED/EvacuationRibbon' (o compatible, con propiedad _Color). Se instancia en runtime, no se modifica el asset compartido.")]
        public Material ribbonMaterial;

        [Tooltip("Color/alpha máximo de la cinta una vez visible. El shader multiplica esto por su propia textura de flechas.")]
        public Color tint = new Color(0.15f, 0.95f, 0.35f, 0.85f);

        [Header("Fade")]
        public float fadeInDuration = 0.8f;
        public float fadeOutDuration = 0.5f;

        [Header("Validación de waypoints (solo dispositivo)")]
        [Tooltip("Tramos más cortos que esto (m) se descartan: la spline hace bucles con puntos muy juntos.")]
        public float minSegmentLength = 2f;

        [Tooltip("Segundos que el ancla de un waypoint debe quedarse quieta antes de usarla.")]
        public float stableSeconds = 1f;

        [Tooltip("Movimiento (m) por debajo del cual se considera quieta el ancla.")]
        public float stableTolerance = 0.3f;

        [Tooltip("Error horizontal (m) aceptado entre la distancia real de dos waypoints y la de sus lat/lon.")]
        public float distanceTolerance = 2f;

        [Tooltip("Error horizontal aceptado como fracción de la distancia esperada (se usa el mayor de los dos).")]
        [Range(0f, 1f)]
        public float distanceToleranceRatio = 0.25f;

        [Tooltip("Error vertical (m) aceptado frente a la diferencia de altitud (solo anclas WGS84).")]
        public float altitudeTolerance = 2f;

        [Tooltip("Segundos máximos esperando a que todos los waypoints validen; después se arma con los que sí.")]
        public float maxWaitForAllWaypoints = 20f;

        [Tooltip("Si un waypoint usado se corre más que esto (m), la cinta se vuelve a armar.")]
        public float followAnchorsThreshold = 0.5f;

        // ── Internos ──────────────────────────────────────────────────────────────
        private SplineContainer _container;
        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private Mesh _mesh;
        private Material _materialInstance;
        private Coroutine _fadeRoutine;
        private float _currentAlpha;
        private bool _isVisible;
        private bool _built;
        private bool _shownByHotspot;
        private bool _arrived;
        private readonly List<Vector3> _sampledPoints = new List<Vector3>();
        private float _sampledSpacing = -1f;

        // Validación de waypoints
        private const float _evalInterval = 0.25f;
        private float _nextEval;
        private ARGeospatialCreatorAnchor[] _geo;
        private Vector3[] _lastSample;
        private float[] _stableSince;
        private readonly List<int> _candidate = new List<int>();
        private readonly List<int> _ready = new List<int>();
        private readonly List<int> _builtIndices = new List<int>();
        private readonly List<Vector3> _builtPositions = new List<Vector3>();
        private int _rejectedCount;
        private int _notReadyCount;
        private float _waitStart = -1f;
        private float _refreshPendingSince = -1f;
        private bool _refreshRequested;

        // ── Lifecycle ─────────────────────────────────────────────────────────────
        private void Awake()
        {
            _all.Add(this);
            if (Instance == null) Instance = this;

            _container = GetComponent<SplineContainer>();
            _meshFilter = GetComponent<MeshFilter>();
            _meshRenderer = GetComponent<MeshRenderer>();

            _mesh = new Mesh { name = "EvacuationRibbon" };
            _meshFilter.sharedMesh = _mesh;

            if (ribbonMaterial != null)
                _meshRenderer.material = ribbonMaterial; // instancia propia, no toca el asset compartido
            _materialInstance = _meshRenderer.material;

            ApplyAlpha(0f);
            _meshRenderer.enabled = false;

            int n = waypoints != null ? waypoints.Length : 0;
            _geo = new ARGeospatialCreatorAnchor[n];
            _lastSample = new Vector3[n];
            _stableSince = new float[n];
            for (int i = 0; i < n; i++)
            {
                _geo[i] = waypoints[i] != null ? waypoints[i].GetComponent<ARGeospatialCreatorAnchor>() : null;
                _stableSince[i] = -1f;
            }
        }

        private void OnEnable()
        {
            if (GeoAnchorManager.Instance != null)
                GeoAnchorManager.Instance.RecalibrationStart += Rebuild;
        }

        private void OnDisable()
        {
            if (GeoAnchorManager.Instance != null)
                GeoAnchorManager.Instance.RecalibrationStart -= Rebuild;
        }

        private void OnDestroy()
        {
            _all.Remove(this);
            if (Instance == this) Instance = _all.Count > 0 ? _all[0] : null;
        }

        // Longitud mínima (m) para aceptar la spline como válida. Justo después de que
        // ARCore reparenta un waypoint bajo su ARGeospatialAnchor recién resuelto, el
        // Transform de ese anchor puede tardar uno o más frames en recibir su pose real
        // trackeada — durante esa ventana wp.transform.position lee (0,0,0). Si son todos,
        // la spline colapsa a un punto (lo atrapa esta longitud); si son solo algunos, la
        // cinta se dobla hacia el origen de la sesión (lo atrapa SelectValidWaypoints).
        private const float _minValidSplineLength = 1f;

        // Diagnóstico (logcat: filtrar por "[EvacRoute]"): cada cuánto se reporta qué falta para construir.
        private const float _waitLogInterval = 5f;
        private float _nextWaitLog;

        private void Update()
        {
            if (!_built)
            {
                if (Application.isEditor) TryBuildInEditor();
                else TryInitialBuild();
                return;
            }

            if (!Application.isEditor) TryRefreshFromAnchors();

            UpdateLevelVisibility();

            if (!_isVisible || Camera.main == null) return;

            Vector3 player = Camera.main.transform.position;
            CheckCheckpoints(player);

            // Llegada: cerca del punto de encuentro O del final de la cinta (último waypoint).
            // Ambos cuentan porque el jugador sigue la cinta, y su final puede no coincidir
            // exactamente con el hotspot del punto de encuentro (en YesidCasa están a ~7 m).
            bool nearMeetingPoint = puntoDeEncuentro != null && IsWithinHideRadius(puntoDeEncuentro.position, player);
            RouteWaypoint last = waypoints[waypoints.Length - 1];
            bool nearRouteEnd = last != null && IsWithinHideRadius(last.transform.position, player);

            int required = Mathf.Min(minCheckpointsForArrival, CheckpointCount);
            if ((nearMeetingPoint || nearRouteEnd) && CheckpointsReached >= required)
            {
                _arrived = true;
                Debug.Log($"[EvacRoute] Llegada al punto de encuentro ({CheckpointsReached} checkpoints): se oculta la cinta.", this);
                OnArrivalAtPuntoDeEncuentro?.Invoke();
                Hide();
            }
        }

        private bool IsWithinHideRadius(Vector3 target, Vector3 player)
        {
            Vector3 flat = target - player;
            flat.y = 0f;
            return flat.sqrMagnitude <= hideRadius * hideRadius;
        }

        // ── API pública ───────────────────────────────────────────────────────────
        /// <summary>True mientras la cinta está visible (o apareciendo/desapareciendo). Usado por DirectorAdviceController para solo evaluar "desvío" mientras la ruta está activa.</summary>
        public bool IsVisible => _isVisible;

        /// <summary>True una vez que todos los waypoints anclaron y la cinta está construida.</summary>
        public bool IsBuilt => _built;

        /// <summary>
        /// Pide volver a armar la cinta con las posiciones actuales de los waypoints
        /// (GeoAnchorManager tras recalibrar: la spline guarda posiciones, no referencias).
        /// La malla vieja se sigue viendo, y solo se reemplaza cuando las anclas nuevas
        /// validan; la visibilidad y los checkpoints no se tocan.
        /// </summary>
        public void Rebuild()
        {
            if (!_built) return; // la construcción inicial ya está en curso
            if (Application.isEditor)
            {
                _built = false;
                return;
            }
            _refreshRequested = true;
        }

        /// <summary>
        /// Puntos de mundo a lo largo de la ruta, uno cada spacingMeters. Usado por
        /// MinimapController para dibujar la ruta punteada. false si aún no se construyó.
        /// La lista devuelta es interna: no modificarla.
        /// </summary>
        public bool TryGetSampledPoints(float spacingMeters, out IReadOnlyList<Vector3> points)
        {
            points = _sampledPoints;
            if (!_built) return false;

            spacingMeters = Mathf.Max(0.5f, spacingMeters);
            if (!Mathf.Approximately(spacingMeters, _sampledSpacing))
            {
                _sampledSpacing = spacingMeters;
                _sampledPoints.Clear();
                Spline spline = _container.Spline;
                int count = Mathf.Max(2, Mathf.CeilToInt(spline.GetLength() / spacingMeters) + 1);
                for (int i = 0; i < count; i++)
                {
                    float3 p = spline.EvaluatePosition(i / (float)(count - 1));
                    _sampledPoints.Add(transform.TransformPoint(new Vector3(p.x, p.y, p.z)));
                }
            }
            return true;
        }

        /// <summary>
        /// Distancia (m) del punto de mundo dado al punto más cercano sobre la ruta ya
        /// construida. false si la ruta todavía no se construyó (waypoints sin anclar).
        /// Usado por DirectorAdviceController para detectar que el jugador se aleja de
        /// la ruta (GDD Tabla 15: "N4 + usuario se aleja de la ruta").
        /// </summary>
        public bool TryGetDistanceToRoute(Vector3 worldPosition, out float distance)
        {
            if (!_built)
            {
                distance = 0f;
                return false;
            }

            float3 localPoint = transform.InverseTransformPoint(worldPosition);
            distance = SplineUtility.GetNearestPoint(_container.Spline, localPoint, out _, out _);
            return true;
        }

        public void Show()
        {
            _shownByHotspot = true;
            ShowInternal();
        }

        private void ShowInternal()
        {
            if (!_built)
            {
                Debug.Log("[EvacRoute] Show() pedido pero la cinta aún no está construida (faltan anclas).", this);
                return;
            }
            if (_isVisible) return;
            _isVisible = true;
            Debug.Log($"[EvacRoute] Mostrando cinta (por hotspot: {_shownByHotspot}).", this);
            _meshRenderer.enabled = true;
            if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
            _fadeRoutine = StartCoroutine(FadeRoutine(_currentAlpha, 1f, fadeInDuration));
        }

        public void Hide()
        {
            _shownByHotspot = false;
            if (!_isVisible) return;
            _isVisible = false;
            if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
            _fadeRoutine = StartCoroutine(FadeRoutine(_currentAlpha, 0f, fadeOutDuration));
        }

        // ── Visibilidad por nivel ─────────────────────────────────────────────────
        private void UpdateLevelVisibility()
        {
            if (autoShowFromLevel == RiskLevel.None) return;

            RiskLevel level = RiskLevelIndicator.Instance != null
                ? RiskLevelIndicator.Instance.CurrentLevel
                : RiskLevel.None;
            bool wanted = level != RiskLevel.None && level >= autoShowFromLevel;

            if (!wanted)
            {
                _arrived = false;
                if (!_shownByHotspot) ResetCheckpoints(); // ruta abierta por hotspot en N1/N2: conservar progreso
                if (_isVisible && !_shownByHotspot) Hide();
                return;
            }

            if (!_isVisible && !_arrived) ShowInternal();
        }

        // ── Puntos de control ─────────────────────────────────────────────────────
        // Cada waypoint intermedio cuenta una vez; no hace falta pasarlos en orden
        // (en la calle real el GPS puede "saltarse" uno), solo cuántos se alcanzaron.
        private bool[] _checkpointHit;

        private void CheckCheckpoints(Vector3 player)
        {
            int count = CheckpointCount;
            if (count == 0) return;
            if (_checkpointHit == null || _checkpointHit.Length != count) _checkpointHit = new bool[count];

            for (int i = 0; i < count; i++)
            {
                if (_checkpointHit[i]) continue;
                RouteWaypoint wp = waypoints[i + 1];
                if (wp == null) continue;

                Vector3 d = wp.transform.position - player;
                d.y = 0f;
                if (d.sqrMagnitude > checkpointRadius * checkpointRadius) continue;

                _checkpointHit[i] = true;
                CheckpointsReached++;
                OnCheckpointReached?.Invoke(CheckpointsReached, count);
            }
        }

        private void ResetCheckpoints()
        {
            if (CheckpointsReached == 0) return;
            CheckpointsReached = 0;
            _checkpointHit = null;
        }

        // ── Construcción ──────────────────────────────────────────────────────────
        // Editor: todos los waypoints con su posición autorada, igual que antes.
        private void TryBuildInEditor()
        {
            if (waypoints == null || waypoints.Length < 2) return;
            _candidate.Clear();
            for (int i = 0; i < waypoints.Length; i++)
                if (waypoints[i] != null) _candidate.Add(i);

            if (TryBuild(_candidate))
            {
                _built = true;
                Debug.Log($"[EvacRoute] Cinta construida: {_candidate.Count} waypoints, {_container.Spline.GetLength():F1} m.", this);
            }
        }

        // Dispositivo: espera a que todos los waypoints validen, o hasta maxWaitForAllWaypoints
        // y entonces arma con los que sí.
        private void TryInitialBuild()
        {
            if (waypoints == null || waypoints.Length < 2) return;
            if (Time.time < _nextEval) return;
            _nextEval = Time.time + _evalInterval;

            SampleWaypoints();
            SelectValidWaypoints(_candidate);

            if (_candidate.Count >= 2 && _waitStart < 0f) _waitStart = Time.time;
            bool complete = _rejectedCount == 0 && _notReadyCount == 0;
            bool timedOut = _waitStart >= 0f && Time.time - _waitStart >= maxWaitForAllWaypoints;

            if (_candidate.Count >= 2 && (complete || timedOut) && TryBuild(_candidate))
            {
                _built = true;
                Debug.Log($"[EvacRoute] Cinta construida: {_candidate.Count}/{waypoints.Length} waypoints, " +
                          $"{_container.Spline.GetLength():F1} m{DescribeOmitted()}.", this);
                return;
            }

            if (Time.time >= _nextWaitLog)
            {
                _nextWaitLog = Time.time + _waitLogInterval;
                Debug.Log($"[EvacRoute] Esperando waypoints: {_candidate.Count} válidos, {_notReadyCount} sin ancla estable, " +
                          $"{_rejectedCount} con posición inconsistente.", this);
            }
        }

        // Tras construida: rearma si las anclas usadas se corrieron (deriva/recalibración) o si
        // un waypoint omitido ya valida. Si el rearme no valida, queda la malla anterior.
        private void TryRefreshFromAnchors()
        {
            if (Time.time < _nextEval) return;
            _nextEval = Time.time + _evalInterval;

            SampleWaypoints();

            bool moved = _refreshRequested;
            for (int k = 0; k < _builtIndices.Count && !moved; k++)
            {
                int i = _builtIndices[k];
                if (!IsWaypointUsable(i)) continue;
                if ((waypoints[i].transform.position - _builtPositions[k]).sqrMagnitude >
                    followAnchorsThreshold * followAnchorsThreshold)
                    moved = true;
            }

            if (!moved && _builtIndices.Count == waypoints.Length) return;

            SelectValidWaypoints(_candidate);

            if (!moved)
            {
                if (_candidate.Count > _builtIndices.Count && TryBuild(_candidate))
                    Debug.Log($"[EvacRoute] Cinta ampliada: {_candidate.Count}/{waypoints.Length} waypoints.", this);
                return;
            }

            if (_refreshPendingSince < 0f) _refreshPendingSince = Time.time;
            bool timedOut = Time.time - _refreshPendingSince >= maxWaitForAllWaypoints;
            if ((_candidate.Count >= _builtIndices.Count || timedOut) && TryBuild(_candidate))
            {
                _refreshRequested = false;
                _refreshPendingSince = -1f;
                Debug.Log($"[EvacRoute] Cinta reajustada a las anclas: {_candidate.Count}/{waypoints.Length} waypoints, " +
                          $"{_container.Spline.GetLength():F1} m{DescribeOmitted()}.", this);
            }
        }

        /// <summary>Arma spline + malla con esos waypoints. false (y no toca nada) si la spline sale degenerada.</summary>
        private bool TryBuild(List<int> indices)
        {
            if (indices.Count < 2) return false;

            var positions = new List<float3>(indices.Count);
            foreach (int i in indices)
            {
                Vector3 local = transform.InverseTransformPoint(waypoints[i].transform.position);
                positions.Add(new float3(local.x, local.y, local.z));
            }

            var spline = new Spline(positions, TangentMode.AutoSmooth);
            if (spline.GetLength() < _minValidSplineLength) return false;

            _container.Spline = spline;
            BuildRibbonMesh();

            _builtIndices.Clear();
            _builtPositions.Clear();
            foreach (int i in indices)
            {
                _builtIndices.Add(i);
                _builtPositions.Add(waypoints[i].transform.position);
            }

            _sampledSpacing = -1f; // el minimapa vuelve a muestrear
            _sampledPoints.Clear();
            return true;
        }

        // ── Validación de waypoints ───────────────────────────────────────────────
        private bool IsWaypointUsable(int i)
        {
            RouteWaypoint wp = waypoints[i];
            if (wp == null || !wp.IsAnchorReady()) return false;
            ARGeospatialAnchor anchor = wp.transform.parent.GetComponent<ARGeospatialAnchor>();
            return anchor != null && anchor.trackingState == TrackingState.Tracking;
        }

        // Marca desde cuándo está quieta cada ancla (un ancla nueva lee (0,0,0) y luego salta).
        private void SampleWaypoints()
        {
            for (int i = 0; i < waypoints.Length; i++)
            {
                if (!IsWaypointUsable(i))
                {
                    _stableSince[i] = -1f;
                    continue;
                }

                Vector3 pos = waypoints[i].transform.position;
                if (_stableSince[i] < 0f ||
                    (pos - _lastSample[i]).sqrMagnitude > stableTolerance * stableTolerance)
                {
                    _stableSince[i] = Time.time;
                    _lastSample[i] = pos;
                }
            }
        }

        private bool IsStable(int i) => _stableSince[i] >= 0f && Time.time - _stableSince[i] >= stableSeconds;

        /// <summary>
        /// Recorre los waypoints en orden y deja los que cuadran con el anterior aceptado
        /// según sus lat/lon/alt. Un waypoint mal puesto rompe sus dos tramos, pero el tramo
        /// directo entre sus vecinos sí cuadra, así que queda omitido sin tumbar la ruta.
        /// </summary>
        private void SelectValidWaypoints(List<int> result)
        {
            result.Clear();
            _rejectedCount = 0;
            _notReadyCount = 0;

            List<int> ready = _ready;
            ready.Clear();
            for (int i = 0; i < waypoints.Length; i++)
            {
                if (IsStable(i)) ready.Add(i);
                else _notReadyCount++;
            }

            // Inicio: el primer waypoint que cuadra con alguno de los dos siguientes.
            int startPos = -1;
            for (int s = 0; s < ready.Count - 1 && startPos < 0; s++)
            {
                if (IsConsistent(ready[s], ready[s + 1]) ||
                    (s + 2 < ready.Count && IsConsistent(ready[s], ready[s + 2])))
                    startPos = s;
                else
                    _rejectedCount++;
            }
            if (startPos < 0)
            {
                if (ready.Count > 0) _rejectedCount = ready.Count;
                return;
            }

            result.Add(ready[startPos]);
            for (int s = startPos + 1; s < ready.Count; s++)
            {
                int i = ready[s];
                int last = result[result.Count - 1];
                if (!IsConsistent(last, i))
                {
                    _rejectedCount++;
                    continue;
                }

                if (HorizontalDistance(last, i) < minSegmentLength)
                {
                    // El final de la ruta manda: reemplaza al anterior si quedó pegado a él.
                    if (s == ready.Count - 1 && result.Count > 1) result[result.Count - 1] = i;
                    continue;
                }

                result.Add(i);
            }
        }

        private float HorizontalDistance(int a, int b)
        {
            Vector3 d = waypoints[b].transform.position - waypoints[a].transform.position;
            return new Vector2(d.x, d.z).magnitude;
        }

        // Distancia real entre dos waypoints vs la que dan sus coordenadas.
        private bool IsConsistent(int a, int b)
        {
            ARGeospatialCreatorAnchor ga = _geo[a], gb = _geo[b];
            if (ga == null || gb == null) return true; // sin coordenadas no hay contra qué comparar

            const double earthRadius = 6378137.0;
            double meanLat = (ga.Latitude + gb.Latitude) * 0.5 * Mathf.Deg2Rad;
            double north = (gb.Latitude - ga.Latitude) * Mathf.Deg2Rad * earthRadius;
            double east = (gb.Longitude - ga.Longitude) * Mathf.Deg2Rad * earthRadius * System.Math.Cos(meanLat);
            float expected = (float)System.Math.Sqrt(north * north + east * east);

            float tolerance = Mathf.Max(distanceTolerance, expected * distanceToleranceRatio);
            if (Mathf.Abs(HorizontalDistance(a, b) - expected) > tolerance) return false;

            if (ga.AltitudeType != AnchorAltitudeType.WGS84 || gb.AltitudeType != AnchorAltitudeType.WGS84)
                return true; // Terrain/Rooftop: la altura la decide Google, no se compara

            float dy = waypoints[b].transform.position.y - waypoints[a].transform.position.y;
            return Mathf.Abs(dy - (float)(gb.Altitude - ga.Altitude)) <= altitudeTolerance;
        }

        private string DescribeOmitted()
        {
            var omitted = new List<string>();
            for (int i = 0; i < waypoints.Length; i++)
                if (waypoints[i] != null && !_builtIndices.Contains(i)) omitted.Add(waypoints[i].name);
            return omitted.Count == 0 ? "" : " (omitidos: " + string.Join(", ", omitted) + ")";
        }

        // ── Malla de la cinta ─────────────────────────────────────────────────────
        private void BuildRibbonMesh()
        {
            Spline spline = _container.Spline;
            float length = spline.GetLength();
            int sampleCount = Mathf.Max(2, Mathf.CeilToInt(length * samplesPerMeter) + 1);

            var vertices = new List<Vector3>(sampleCount * 2);
            var uvs = new List<Vector2>(sampleCount * 2);
            var triangles = new List<int>((sampleCount - 1) * 6);

            float half = ribbonWidth * 0.5f;
            float distance = 0f;
            Vector3 prevPos = Vector3.zero;

            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)(sampleCount - 1);
                spline.Evaluate(t, out float3 posLocal, out float3 tanLocal, out _);

                Vector3 pos = new Vector3(posLocal.x, posLocal.y, posLocal.z) + Vector3.up * heightOffset;
                Vector3 tangent = new Vector3(tanLocal.x, tanLocal.y, tanLocal.z).normalized;
                if (tangent.sqrMagnitude < 0.0001f) tangent = Vector3.forward;
                Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized;

                if (i > 0) distance += Vector3.Distance(pos, prevPos);
                prevPos = pos;

                vertices.Add(pos - right * half);
                vertices.Add(pos + right * half);
                uvs.Add(new Vector2(0f, distance));
                uvs.Add(new Vector2(1f, distance));

                if (i > 0)
                {
                    int a = (i - 1) * 2;
                    int b = a + 1;
                    int c = i * 2;
                    int d = c + 1;

                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
            }

            _mesh.Clear();
            _mesh.SetVertices(vertices);
            _mesh.SetUVs(0, uvs);
            _mesh.SetTriangles(triangles, 0);
            _mesh.RecalculateBounds();
        }

        // ── Fade ──────────────────────────────────────────────────────────────────
        private IEnumerator FadeRoutine(float from, float to, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                ApplyAlpha(Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration)));
                yield return null;
            }
            ApplyAlpha(to);

            if (to <= 0f) _meshRenderer.enabled = false;
        }

        private void ApplyAlpha(float alpha)
        {
            _currentAlpha = alpha;
            if (_materialInstance == null) return;

            Color c = tint;
            c.a = tint.a * alpha;
            _materialInstance.SetColor("_Color", c);
        }
    }
}
