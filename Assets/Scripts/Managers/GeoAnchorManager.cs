namespace SIRMED.Managers
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using Google.XR.ARCoreExtensions;
    using Google.XR.ARCoreExtensions.GeospatialCreator;
    using UnityEngine;
    using UnityEngine.XR.ARFoundation;
    using UnityEngine.XR.ARSubsystems;

    /// <summary>
    /// GeoAnchorManager — Toma el control del anclaje de TODOS los ARGeospatialCreatorAnchor
    /// de la escena (hotspots, RouteWaypoint, DirectorWaypoint, QuebradaZone, FX...).
    ///
    /// Por qué existe: ARGeospatialCreatorAnchor (Google) crea su ancla apenas el Earth pasa
    /// a Tracking, sin mirar la precisión, y solo lo intenta UNA vez (estado privado, sin
    /// API para repetir). Si el teléfono arranca con 20–30 m de error, los hotspots quedan
    /// corridos para toda la sesión. Este manager:
    ///   1. En Awake desactiva esos componentes (su Update ya no corre → no anclan solos).
    ///   2. Ancla inicial: espera precisión ≤ _maxHorizontalAccuracy (8 m) sostenida
    ///      _stableSeconds; si no llega en _initialFallbackSeconds, ancla igual con lo que
    ///      haya (mejor hotspots algo corridos que ningún hotspot).
    ///   2b. Reanclaje automático: si el ancla inicial salió por fallback, sigue esperando
    ///      la precisión en segundo plano y, apenas se alcanza, recrea todas las anclas UNA
    ///      vez (igual que Recalibrate, sin que el jugador abra Ajustes). Sin esto, anclar
    ///      con rumbo/altitud malos deja los hotspots elevados o corridos toda la sesión.
    ///      Se cancela si el jugador recalibra a mano con éxito.
    ///   3. Recalibrate(): misma espera (sin fallback, con timeout) y vuelve a crear todas
    ///      las anclas SOLO si la precisión es mejor que la de las anclas actuales (puntaje
    ///      combinado H + V + rumbo, ver PoseErrorScore); si no mejora, se conservan. Mueve
    ///      cada objeto (y cualquier otro hijo del ancla vieja, p. ej.
    ///      el modelo del Líder) al ancla nueva y destruyendo la vieja.
    ///   4. Corrección de altura (en cada anclaje de 2, 2b y 3): compara la altitud del
    ///      teléfono con la de los hotspots a ≤ 20 m y sube/baja todas las anclas WGS84 la
    ///      diferencia, para que el error vertical de la sesión no deje los puntos elevados
    ///      o hundidos. Ver ComputeAltitudeCorrection.
    ///
    /// El resultado es idéntico a ARGeospatialCreatorAnchor.FinishAnchor: el objeto queda
    /// hijo directo de un ARGeospatialAnchor con pose local cero, así que todos los
    /// IsAnchorReady() existentes siguen funcionando sin cambios.
    ///
    /// NO reinicia la sesión AR (ARSession.Reset): los ARGeospatialAnchor guardan el handle
    /// nativo de la sesión y quedarían inválidos. Costo de API: las anclas WGS84 se calculan
    /// en el dispositivo con la pose Earth ya disponible (sin requests extra); solo las de
    /// terreno/azotea hacen una consulta cada una.
    ///
    /// En el Editor no hace nada (allí Google tampoco ancla y IsAnchorReady() devuelve true).
    /// Va en el prefab Managers; busca solo AREarthManager y ARAnchorManager.
    /// </summary>
    public class GeoAnchorManager : MonoBehaviour
    {
        public enum State
        {
            Idle,               // Sin trabajo pendiente
            WaitingAccuracy,    // Esperando precisión suficiente
            Anchoring,          // Creando anclas
        }

        public static GeoAnchorManager Instance { get; private set; }

        // ── Referencias ────────────────────────────────────────────────────────────
        [Header("Referencias (vacío = se buscan en la escena)")]
        [SerializeField] private AREarthManager _earthManager;
        [SerializeField] private ARAnchorManager _anchorManager;

        // ── Umbrales ───────────────────────────────────────────────────────────────
        [Header("Precisión requerida para anclar")]
        [Tooltip("Precisión horizontal máxima, en metros.")]
        [SerializeField] private double _maxHorizontalAccuracy = 8;
        [Tooltip("Precisión de orientación máxima, en grados (mismo criterio que TrackingStatusBanner).")]
        [SerializeField] private double _maxYawAccuracy = 25;
        [Tooltip("Precisión vertical máxima, en metros. Sin VPS puede llegar a 10 m y deja los hotspots elevados o hundidos.")]
        [SerializeField] private double _maxVerticalAccuracy = 5;
        [Tooltip("Recalibrar solo reemplaza las anclas si el error combinado baja al menos esta fracción (0.2 = 20 %).")]
        [Range(0f, 0.9f)]
        [SerializeField] private float _recalibrationMinImprovement = 0.2f;
        [Tooltip("Segundos seguidos dentro del umbral antes de anclar (evita anclar en un pico bueno aislado).")]
        [SerializeField] private float _stableSeconds = 1f;

        [Header("Tiempos")]
        [Tooltip("Ancla inicial: si tras estos segundos con Earth en Tracking no se alcanzó la precisión, se ancla igual.")]
        [SerializeField] private float _initialFallbackSeconds = 30f;
        [Tooltip("Recalibración: si no se alcanza la precisión en estos segundos, se cancela y se conservan las anclas actuales.")]
        [SerializeField] private float _recalibrationTimeout = 60f;
        [Tooltip("Segundos mínimos entre recalibraciones.")]
        [SerializeField] private float _recalibrationCooldown = 20f;
        [Tooltip("Si el ancla inicial salió por fallback, reanclar solo una vez al alcanzar la precisión.")]
        [SerializeField] private bool _autoReanchorAfterFallback = true;

        [Header("Corrección de altura (ver ComputeAltitudeCorrection)")]
        [Tooltip("Al anclar, compara la altitud del teléfono con la de los hotspots cercanos y sube/baja TODAS las anclas WGS84 esa diferencia.")]
        [SerializeField] private bool _altitudeCorrection = true;
        [Tooltip("Solo se usan hotspots a esta distancia horizontal o menos, en metros. Sin ninguno cerca no se corrige.")]
        [SerializeField] private float _altitudeReferenceRadius = 20f;
        [Tooltip("Si la diferencia supera este valor, en metros, se considera lectura mala y no se corrige.")]
        [SerializeField] private float _maxAltitudeCorrection = 10f;
        [Tooltip("Fija la altura de cada ancla al crearla: ARCore sigue ajustando la posición horizontal, pero sus cambios de altitud estimada ya no suben/bajan los hotspots.")]
        [SerializeField] private bool _lockAnchorHeight = true;
        [Tooltip("Cada cuántos segundos escribir en el log la línea '[GeoAnchor] Diag' (0 = nunca).")]
        [SerializeField] private float _diagnosticLogInterval = 5f;

        // ── Eventos ────────────────────────────────────────────────────────────────
        /// <summary>Se dispara al terminar el anclaje inicial.</summary>
        public event Action InitialAnchoringCompleted;
        public event Action RecalibrationStart;

        /// <summary>Se dispara al terminar una recalibración: true = anclas renovadas, false = cancelada (timeout).</summary>
        public event Action<bool> RecalibrationFinished;

        /// <summary>Se dispara cuando el reanclaje automático (tras un fallback) recrea las anclas.</summary>
        public event Action AutoReanchored;

        // ── Estado público ─────────────────────────────────────────────────────────
        public State CurrentState { get; private set; } = State.Idle;

        /// <summary>True mientras el ancla inicial todavía no se colocó (los hotspots aún no aparecen).</summary>
        public bool IsWaitingForInitialAnchors => !_initialDone && enabled;

        public bool IsRecalibrating { get; private set; }

        /// <summary>True si las anclas actuales salieron por fallback y se espera precisión para reanclar solas.</summary>
        public bool IsAutoReanchorPending => _autoReanchorPending;

        /// <summary>Precisión horizontal actual en metros (NaN si el Earth no está en Tracking).</summary>
        public double CurrentHorizontalAccuracy { get; private set; } = double.NaN;

        public double RequiredHorizontalAccuracy => _maxHorizontalAccuracy;

        /// <summary>Segundos que faltan para poder recalibrar de nuevo (0 = disponible).</summary>
        public float CooldownRemaining => Mathf.Max(0f, _nextRecalibrationTime - Time.time);

        public bool CanRecalibrate => _initialDone && !IsRecalibrating && CooldownRemaining <= 0f;

        /// <summary>Metros que se sumaron a la altitud de las anclas WGS84 actuales (0 = sin corrección).</summary>
        public double AltitudeCorrection { get; private set; }

        // ── Internos ───────────────────────────────────────────────────────────────
        private class Entry
        {
            public ARGeospatialCreatorAnchor creator;
            public Quaternion rotation;
            public bool isHotspot; // punto levantado en campo: sirve de referencia de altura
        }

        private readonly List<Entry> _entries = new List<Entry>();

        // Altura fijada por ancla (ver LateUpdate). captured = ya se leyó su primera pose válida.
        private class HeightLock
        {
            public bool captured;
            public float lockedY;   // localPosition.y fijada (espacio de sesión AR)
            public float arcoreY;   // última localPosition.y que puso ARCore (para el diagnóstico)
        }

        private readonly Dictionary<ARGeospatialAnchor, HeightLock> _heightLocks =
            new Dictionary<ARGeospatialAnchor, HeightLock>();
        private readonly List<ARGeospatialAnchor> _deadLocks = new List<ARGeospatialAnchor>();
        private bool _initialDone;
        private bool _autoReanchorPending;
        private double _anchoredScore = double.PositiveInfinity; // PoseErrorScore al crear las anclas actuales

        // Calidad "VPS ubicado": siempre se acepta recalibrar con ella, aunque el puntaje no baje 20 %.
        private const double ExcellentHorizontal = 1.5, ExcellentVertical = 1.5, ExcellentYaw = 5;
        // Metros de error lateral por grado de rumbo a ~20 m (distancia típica a un hotspot).
        private const double MetersPerYawDegree = 0.35;
        private float _nextRecalibrationTime;
        private float _nextDiagnosticTime;

        // ── Lifecycle ──────────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;

            if (Application.isEditor)
            {
                // Sin sesión AR real: dejamos el comportamiento de siempre.
                _initialDone = true;
                enabled = false;
                return;
            }

            // Todos los Awake corren antes del primer Update, así que ningún
            // ARGeospatialCreatorAnchor alcanza a anclar por su cuenta.
            var creators = FindObjectsByType<ARGeospatialCreatorAnchor>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var creator in creators)
            {
                creator.enabled = false;
                _entries.Add(new Entry
                {
                    creator = creator,
                    rotation = creator.transform.rotation,
                    isHotspot = creator.GetComponent<SIRMED.Gameplay.Hotspots.HotspotController>() != null,
                });
            }
            Debug.Log($"[GeoAnchor] {_entries.Count} anclas bajo control; se anclarán con precisión ≤ {_maxHorizontalAccuracy} m.");
        }

        private void Start()
        {
            if (_earthManager == null) _earthManager = FindAnyObjectByType<AREarthManager>();
            if (_anchorManager == null) _anchorManager = FindAnyObjectByType<ARAnchorManager>();

            if (_earthManager == null || _anchorManager == null)
                Debug.LogError("[GeoAnchor] Falta AREarthManager o ARAnchorManager en la escena: no se puede anclar.", this);

            StartCoroutine(InitialAnchoringRoutine());
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            CurrentHorizontalAccuracy = IsEarthTracking()
                ? _earthManager.CameraGeospatialPose.HorizontalAccuracy
                : double.NaN;

            if (_diagnosticLogInterval > 0f && _initialDone && Time.time >= _nextDiagnosticTime)
            {
                _nextDiagnosticTime = Time.time + _diagnosticLogInterval;
                LogHeightDiagnostic();
            }
        }

        /// <summary>
        /// Fija la altura de las anclas. ARGeospatialAnchor.Update() copia en cada frame la pose
        /// nativa a localPosition, y ARCore la recalcula con cada nueva estimación de la pose
        /// Earth: si su altitud estimada salta 1–2 m (pasa cuando la precisión vertical
        /// empeora), todos los hotspots suben o bajan con ella aunque el teléfono no se mueva
        /// (medido en campo, 9-nov). Aquí, después de ese Update, se conserva la x/z de ARCore y
        /// se reemplaza la y por la que tenía el ancla en su primer frame válido, cuando se
        /// calculó la corrección de altura. La altura queda en el espacio de la sesión AR
        /// (tracking visual del teléfono, que casi no deriva en vertical). Recalibrar crea
        /// anclas nuevas y vuelve a fijar.
        /// </summary>
        private void LateUpdate()
        {
            if (!_lockAnchorHeight || _heightLocks.Count == 0) return;

            foreach (KeyValuePair<ARGeospatialAnchor, HeightLock> kv in _heightLocks)
            {
                ARGeospatialAnchor anchor = kv.Key;
                if (anchor == null) { _deadLocks.Add(anchor); continue; } // destruida al recalibrar

                HeightLock hl = kv.Value;
                Vector3 p = anchor.transform.localPosition;
                if (!hl.captured)
                {
                    if (p == Vector3.zero) continue; // aún sin pose (los primeros frames leen 0,0,0)
                    hl.captured = true;
                    hl.lockedY = p.y;
                }
                hl.arcoreY = p.y;
                if (p.y != hl.lockedY)
                    anchor.transform.localPosition = new Vector3(p.x, hl.lockedY, p.z);
            }

            if (_deadLocks.Count > 0)
            {
                foreach (ARGeospatialAnchor dead in _deadLocks) _heightLocks.Remove(dead);
                _deadLocks.Clear();
            }
        }

        /// <summary>
        /// Solo diagnóstico (no cambia nada): cada _diagnosticLogInterval s escribe la pose Earth y
        /// cuánto más arriba (+) o abajo (−) que el teléfono está el hotspot anclado más cercano.
        /// Los hotspots se levantaron a la altura del teléfono, así que lo esperado es ≈ 0 más la
        /// pendiente. Si ese valor cambia en la misma sesión sin recalibrar, ARCore está moviendo
        /// las anclas por su cuenta.
        /// </summary>
        private void LogHeightDiagnostic()
        {
            Camera cam = Camera.main;
            if (cam == null || !IsEarthTracking()) return;

            Vector3 camPos = cam.transform.position;
            ARGeospatialCreatorAnchor nearest = null;
            float best = float.PositiveInfinity;
            foreach (Entry e in _entries)
            {
                ARGeospatialCreatorAnchor c = e.creator;
                if (c == null || !e.isHotspot) continue;
                Transform parent = c.transform.parent;
                if (parent == null || parent.GetComponent<ARGeospatialAnchor>() == null) continue;
                Vector3 d = c.transform.position - camPos;
                d.y = 0f;
                if (d.magnitude < best) { best = d.magnitude; nearest = c; }
            }
            if (nearest == null) return;

            GeospatialPose p = _earthManager.CameraGeospatialPose;
            float dy = nearest.transform.position.y - camPos.y;
            string drift = _heightLocks.TryGetValue(nearest.transform.parent.GetComponent<ARGeospatialAnchor>(), out HeightLock hl) && hl.captured
                ? $"{hl.arcoreY - hl.lockedY:+0.0;-0.0} m" : "sin fijar";
            Debug.Log($"[GeoAnchor] Diag: alt teléfono {p.Altitude:F1} m (V {p.VerticalAccuracy:F1}, H {p.HorizontalAccuracy:F1}, " +
                      $"rumbo {p.OrientationYawAccuracy:F1}°) | '{nearest.name}' a {best:F1} m, {dy:+0.0;-0.0} m respecto al teléfono " +
                      $"(guardada {nearest.Altitude - p.Altitude:+0.0;-0.0} m) | corrección {AltitudeCorrection:+0.0;-0.0} m | " +
                      $"ARCore la movería {drift} (altura {(_lockAnchorHeight ? "fija" : "libre")})");
        }

        // ── API pública ────────────────────────────────────────────────────────────

        /// <summary>
        /// Vuelve a crear todas las anclas cuando la precisión sea suficiente. Devuelve false
        /// si no se puede ahora (ancla inicial pendiente, otra recalibración en curso o cooldown).
        /// </summary>
        public bool Recalibrate()
        {
            if (!enabled || !CanRecalibrate) return false;
            StartCoroutine(RecalibrationRoutine());
            return true;
        }

        // ── Rutinas ────────────────────────────────────────────────────────────────
        private IEnumerator InitialAnchoringRoutine()
        {
            CurrentState = State.WaitingAccuracy;

            // El fallback cuenta desde que el Earth está en Tracking, no desde el arranque
            // (permisos, cámara y primer VPS pueden tardar).
            while (!IsEarthTracking()) yield return null;

            bool accurate = false;
            yield return WaitForAccuracy(_initialFallbackSeconds, ok => accurate = ok);
            if (!accurate)
                Debug.LogWarning($"[GeoAnchor] Sin precisión ≤ {_maxHorizontalAccuracy} m tras {_initialFallbackSeconds} s " +
                                 $"(actual {CurrentHorizontalAccuracy:F1} m): se ancla igual. Se puede recalibrar luego.");

            yield return AnchorAll();

            _initialDone = true;
            CurrentState = State.Idle;
            InitialAnchoringCompleted?.Invoke();

            if (!accurate && _autoReanchorAfterFallback)
                StartCoroutine(AutoReanchorRoutine());
        }

        /// <summary>
        /// Tras un ancla inicial por fallback: espera (sin límite) la precisión requerida
        /// sostenida _stableSeconds y recrea todas las anclas una vez. Si mientras tanto el
        /// jugador recalibra a mano, espera a que termine; si esa recalibración tuvo éxito,
        /// RecalibrationRoutine apaga _autoReanchorPending y esta rutina se retira.
        /// </summary>
        private IEnumerator AutoReanchorRoutine()
        {
            _autoReanchorPending = true;
            Debug.Log("[GeoAnchor] Anclas colocadas por fallback: se reanclarán solas al alcanzar la precisión.");

            float goodSince = -1f;
            while (_autoReanchorPending)
            {
                if (IsRecalibrating || !IsAccurateEnough())
                {
                    goodSince = -1f;
                }
                else
                {
                    if (goodSince < 0f) goodSince = Time.time;
                    if (Time.time - goodSince >= _stableSeconds) break;
                }
                yield return null;
            }
            if (!_autoReanchorPending) yield break; // una recalibración manual ya lo resolvió

            _autoReanchorPending = false;
            IsRecalibrating = true; // bloquea Recalibrate() mientras se crean las anclas
            yield return AnchorAll();

            // Igual que en RecalibrationRoutine: un par de frames para que las anclas nuevas
            // tengan su pose real antes de reconstruir la cinta de evacuación.
            yield return null;
            yield return null;
            RecalibrationStart?.Invoke();

            IsRecalibrating = false;
            CurrentState = State.Idle;
            Debug.Log($"[GeoAnchor] Reanclaje automático completo (precisión {CurrentHorizontalAccuracy:F1} m, " +
                      $"rumbo {_earthManager.CameraGeospatialPose.OrientationYawAccuracy:F1}°).");
            AutoReanchored?.Invoke();
        }

        private IEnumerator RecalibrationRoutine()
        {
            IsRecalibrating = true;
            CurrentState = State.WaitingAccuracy;

            bool accurate = false;
            yield return WaitForAccuracy(_recalibrationTimeout, ok => accurate = ok, IsBetterThanCurrentAnchors);

            if (accurate)
            {
                yield return AnchorAll();

                // La cinta de evacuación guarda las posiciones de los waypoints al construirse.
                // Se espera un par de frames para que las anclas nuevas reciban su pose real
                // (antes leen 0,0,0) y se le pide reconstruirse.
                yield return null;
                yield return null;
                RecalibrationStart?.Invoke();
                _autoReanchorPending = false;

                Debug.Log($"[GeoAnchor] Recalibración completa (precisión {CurrentHorizontalAccuracy:F1} m).");
            }
            else
            {
                Debug.LogWarning($"[GeoAnchor] Recalibración cancelada: en {_recalibrationTimeout} s no se logró precisión " +
                                 $"dentro del umbral y mejor que la actual (error anclas {_anchoredScore:F1}, " +
                                 $"ahora {CurrentScoreText()}). Se conservan las anclas actuales.");
            }

            _nextRecalibrationTime = Time.time + _recalibrationCooldown;
            IsRecalibrating = false;
            CurrentState = State.Idle;
            RecalibrationFinished?.Invoke(accurate);
        }

        /// <summary>
        /// Espera hasta que condition (por defecto IsAccurateEnough) se cumpla sostenida
        /// _stableSeconds, o hasta timeout (result false).
        /// </summary>
        private IEnumerator WaitForAccuracy(float timeout, Action<bool> result, Func<bool> condition = null)
        {
            if (condition == null) condition = IsAccurateEnough;
            float deadline = Time.time + timeout;
            float goodSince = -1f;

            while (Time.time < deadline)
            {
                if (condition())
                {
                    if (goodSince < 0f) goodSince = Time.time;
                    if (Time.time - goodSince >= _stableSeconds)
                    {
                        result(true);
                        yield break;
                    }
                }
                else
                {
                    goodSince = -1f;
                }
                yield return null;
            }

            result(false);
        }

        private IEnumerator AnchorAll()
        {
            CurrentState = State.Anchoring;
            _anchoredScore = IsEarthTracking() ? PoseErrorScore(_earthManager.CameraGeospatialPose) : double.PositiveInfinity;
            if (IsEarthTracking())
            {
                GeospatialPose p = _earthManager.CameraGeospatialPose;
                Debug.Log($"[GeoAnchor] Anclando con H {p.HorizontalAccuracy:F1} m, V {p.VerticalAccuracy:F1} m, " +
                          $"rumbo {p.OrientationYawAccuracy:F1}° (error combinado {_anchoredScore:F1}).");
            }
            AltitudeCorrection = ComputeAltitudeCorrection();
            int ok = 0, failed = 0;

            foreach (Entry entry in _entries)
            {
                if (entry.creator == null) continue; // destruido durante la sesión

                ARGeospatialCreatorAnchor c = entry.creator;
                ARGeospatialAnchor anchor = null;

                switch (c.AltitudeType)
                {
                    case AnchorAltitudeType.WGS84:
                        anchor = _anchorManager.AddAnchor(c.Latitude, c.Longitude, c.Altitude + AltitudeCorrection, entry.rotation);
                        break;

                    case AnchorAltitudeType.Terrain:
                    {
                        var promise = _anchorManager.ResolveAnchorOnTerrainAsync(
                            c.Latitude, c.Longitude, c.Altitude, entry.rotation);
                        yield return promise;
                        if (promise.Result.TerrainAnchorState == TerrainAnchorState.Success)
                            anchor = promise.Result.Anchor;
                        break;
                    }

                    case AnchorAltitudeType.Rooftop:
                    {
                        var promise = _anchorManager.ResolveAnchorOnRooftopAsync(
                            c.Latitude, c.Longitude, c.Altitude, entry.rotation);
                        yield return promise;
                        if (promise.Result.RooftopAnchorState == RooftopAnchorState.Success)
                            anchor = promise.Result.Anchor;
                        break;
                    }
                }

                if (c == null) continue; // destruido mientras se resolvía
                if (anchor == null)
                {
                    // Se conserva el ancla anterior (si había) en vez de dejar el objeto suelto.
                    failed++;
                    Debug.LogError($"[GeoAnchor] No se pudo crear el ancla de '{c.name}'.", c);
                    continue;
                }

                Attach(c.transform, anchor);
                _heightLocks[anchor] = new HeightLock();
                ok++;
            }

            Debug.Log($"[GeoAnchor] Anclas creadas: {ok}, fallidas: {failed}.");
        }

        /// <summary>
        /// Cuelga target del ancla nueva con pose local cero (igual que FinishAnchor de Google).
        /// Si estaba bajo un ancla anterior, mueve también los demás hijos de esa ancla
        /// (p. ej. el modelo del Líder, que se cuelga del ancla de su parada) conservando
        /// su pose local, y destruye el ancla vieja.
        /// </summary>
        private static void Attach(Transform target, ARGeospatialAnchor newAnchor)
        {
            Transform oldParent = target.parent;
            ARGeospatialAnchor oldAnchor = oldParent != null ? oldParent.GetComponent<ARGeospatialAnchor>() : null;

            target.SetParent(newAnchor.transform, false);
            target.localPosition = Vector3.zero;
            target.localRotation = Quaternion.identity;

            if (oldAnchor == null) return;

            for (int i = oldParent.childCount - 1; i >= 0; i--)
                oldParent.GetChild(i).SetParent(newAnchor.transform, false);

            Destroy(oldAnchor.gameObject);
        }

        /// <summary>
        /// Corrección de altura. Todas las anclas se crean con la misma pose Earth, así que su
        /// error vertical es el mismo para todas: si la altitud estimada del teléfono está b metros
        /// arriba, todas quedan b metros abajo (y al revés). Los hotspots se levantaron con el
        /// teléfono en la mano (HotspotSurveyController guarda la altitud de la cámara), así que
        /// parado cerca de ellos la altitud del teléfono debería ser la de los hotspots cercanos.
        /// La diferencia es b y se suma a todas las anclas WGS84 (Terrain/Rooftop no: su altura
        /// la pone Google). Se interpola entre los 3 hotspots más cercanos para no confundir la
        /// pendiente de la calle con error. Devuelve 0 (sin corrección) si está apagada, no hay
        /// hotspots a ≤ _altitudeReferenceRadius o la diferencia supera _maxAltitudeCorrection.
        /// </summary>
        private double ComputeAltitudeCorrection()
        {
            if (!_altitudeCorrection || !IsEarthTracking()) return 0;

            GeospatialPose cam = _earthManager.CameraGeospatialPose;
            double cosLat = Math.Cos(cam.Latitude * Math.PI / 180.0);
            const double metersPerDegree = 111320.0;

            var near = new List<(double dist, double alt, string name)>();
            foreach (Entry e in _entries)
            {
                ARGeospatialCreatorAnchor c = e.creator;
                if (c == null || !e.isHotspot || c.AltitudeType != AnchorAltitudeType.WGS84) continue;
                double north = (c.Latitude - cam.Latitude) * metersPerDegree;
                double east = (c.Longitude - cam.Longitude) * metersPerDegree * cosLat;
                double dist = Math.Sqrt(north * north + east * east);
                if (dist <= _altitudeReferenceRadius) near.Add((dist, c.Altitude, c.name));
            }

            if (near.Count == 0)
            {
                Debug.Log($"[GeoAnchor] Altura: sin hotspots a ≤ {_altitudeReferenceRadius} m; no se corrige.");
                return 0;
            }

            near.Sort((a, b) => a.dist.CompareTo(b.dist));
            int n = Math.Min(3, near.Count);
            double weightSum = 0, altSum = 0;
            for (int i = 0; i < n; i++)
            {
                double w = 1.0 / Math.Max(near[i].dist * near[i].dist, 1.0); // a menos de 1 m pesan igual
                weightSum += w;
                altSum += w * near[i].alt;
            }
            double expected = altSum / weightSum;
            double delta = cam.Altitude - expected;

            if (Math.Abs(delta) > _maxAltitudeCorrection)
            {
                Debug.LogWarning($"[GeoAnchor] Altura: diferencia {delta:+0.0;-0.0} m (> {_maxAltitudeCorrection} m) con " +
                                 $"'{near[0].name}' a {near[0].dist:F0} m; se descarta y no se corrige.");
                return 0;
            }

            Debug.Log($"[GeoAnchor] Altura: teléfono {cam.Altitude:F1} m, esperado {expected:F1} m ({n} hotspots, " +
                      $"más cercano '{near[0].name}' a {near[0].dist:F0} m) → anclas WGS84 {delta:+0.0;-0.0} m.");
            return delta;
        }

        // ── Helpers ────────────────────────────────────────────────────────────────
        private bool IsEarthTracking()
        {
            return _earthManager != null &&
                   ARSession.state == ARSessionState.SessionTracking &&
                   _earthManager.EarthState == EarthState.Enabled &&
                   _earthManager.EarthTrackingState == TrackingState.Tracking;
        }

        private bool IsAccurateEnough()
        {
            if (!IsEarthTracking()) return false;
            GeospatialPose pose = _earthManager.CameraGeospatialPose;
            return pose.HorizontalAccuracy <= _maxHorizontalAccuracy &&
                   pose.VerticalAccuracy <= _maxVerticalAccuracy &&
                   pose.OrientationYawAccuracy <= _maxYawAccuracy;
        }

        /// <summary>Dentro del umbral Y claramente mejor que la calidad con que se crearon las anclas actuales.</summary>
        private bool IsBetterThanCurrentAnchors()
        {
            if (!IsAccurateEnough()) return false;
            GeospatialPose pose = _earthManager.CameraGeospatialPose;
            if (pose.HorizontalAccuracy <= ExcellentHorizontal &&
                pose.VerticalAccuracy <= ExcellentVertical &&
                pose.OrientationYawAccuracy <= ExcellentYaw)
                return true;
            return PoseErrorScore(pose) <= _anchoredScore * (1.0 - _recalibrationMinImprovement);
        }

        /// <summary>Error combinado aproximado en metros: H + V + error lateral del rumbo a ~20 m.</summary>
        private static double PoseErrorScore(GeospatialPose pose) =>
            pose.HorizontalAccuracy + pose.VerticalAccuracy + pose.OrientationYawAccuracy * MetersPerYawDegree;

        private string CurrentScoreText() =>
            IsEarthTracking() ? PoseErrorScore(_earthManager.CameraGeospatialPose).ToString("F1") : "sin tracking";
    }
}
