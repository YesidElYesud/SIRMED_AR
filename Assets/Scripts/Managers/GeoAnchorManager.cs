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

        // ── Internos ───────────────────────────────────────────────────────────────
        private class Entry
        {
            public ARGeospatialCreatorAnchor creator;
            public Quaternion rotation;
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private bool _initialDone;
        private bool _autoReanchorPending;
        private double _anchoredScore = double.PositiveInfinity; // PoseErrorScore al crear las anclas actuales

        // Calidad "VPS ubicado": siempre se acepta recalibrar con ella, aunque el puntaje no baje 20 %.
        private const double ExcellentHorizontal = 1.5, ExcellentVertical = 1.5, ExcellentYaw = 5;
        // Metros de error lateral por grado de rumbo a ~20 m (distancia típica a un hotspot).
        private const double MetersPerYawDegree = 0.35;
        private float _nextRecalibrationTime;

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
                _entries.Add(new Entry { creator = creator, rotation = creator.transform.rotation });
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
            int ok = 0, failed = 0;

            foreach (Entry entry in _entries)
            {
                if (entry.creator == null) continue; // destruido durante la sesión

                ARGeospatialCreatorAnchor c = entry.creator;
                ARGeospatialAnchor anchor = null;

                switch (c.AltitudeType)
                {
                    case AnchorAltitudeType.WGS84:
                        anchor = _anchorManager.AddAnchor(c.Latitude, c.Longitude, c.Altitude, entry.rotation);
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
