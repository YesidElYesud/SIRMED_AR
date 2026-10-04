namespace SIRMED.Gameplay.Director
{
    using System.Collections.Generic;
    using SIRMED.Gameplay.Environment;
    using SIRMED.Gameplay.Hotspots;
    using SIRMED.Managers;
    using SIRMED.UI;
    using UnityEngine;

    /// <summary>
    /// DirectorAdviceController — motor de reglas del Director DAGRD (GDD pág. 28,
    /// Tabla 15). Decide CUÁNDO mostrar un consejo; el CÓMO (banner + gestos +
    /// movimiento del personaje) vive en DirectorController/DirectorAdvicePanel —
    /// este script solo llama DirectorController.Instance.ShowAdvice().
    ///
    /// Situaciones del "Documento de Validación de Contenidos" del DAGRD (S1..S10):
    ///   S1  N4 + usuario inmóvil un tiempo definido.
    ///   S2  N4 + usuario alejándose de la ruta.
    ///   S3  N3 + usuario entra en una QuebradaZone.
    ///   S4  Sube a N3 (o lleva un rato en N2) sin haber visitado el hotspot Sirena.
    ///   S5  Ayuda comunitaria correcta → NotifyCommunityHelpCorrect().
    ///   S6  N4 dentro de una QuebradaZone, o N3 quedándose dentro.
    ///   S7  Llegada al punto de encuentro.
    ///   S8  Salto a N4 directo desde N1/N2.
    ///   S9  Justo después de S7 (esperar autorización para volver a casa).
    ///   S10 El usuario decide cargar objetos → NotifyObjectsDecision().
    ///
    /// Regla de diseño del GDD (pág. 28): cooldown entre consejos y nunca repetir
    /// el mismo mensaje seguido (TryTrigger). Los consejos urgentes (S6, S7, S8,
    /// S9, S10) se saltan el cooldown y, si el banner está ocupado, esperan en
    /// cola a que se cierre en vez de perderse.
    /// </summary>
    public class DirectorAdviceController : MonoBehaviour
    {
        public static DirectorAdviceController Instance { get; private set; }

        [Header("S1 — N4 inmóvil")]
        public DirectorAdviceData adviceN4Inactivo;
        [Tooltip("Segundos sin moverse más de 'inactivityMoveThreshold' antes de disparar el consejo.")]
        public float inactivityWindow = 12f;
        [Tooltip("Metros de movimiento que reinician el contador de inactividad.")]
        public float inactivityMoveThreshold = 1f;

        [Header("S2 — N4 desvío de ruta")]
        public DirectorAdviceData adviceN4Desvio;
        [Tooltip("Cuánto debe crecer la distancia a la ruta (respecto al mínimo visto recientemente) para considerarlo un desvío, en metros.")]
        public float desvioDeltaThreshold = 4f;
        [Tooltip("Cada cuántos segundos se revisa la distancia a la ruta (no hace falta cada frame).")]
        public float desvioCheckInterval = 1f;

        [Header("S3 / S6 — Quebrada (requiere QuebradaZone en la escena)")]
        [Tooltip("S3: N3 y el usuario entra en una QuebradaZone.")]
        public DirectorAdviceData adviceN3Quebrada;
        [Tooltip("S6: N4 dentro de una QuebradaZone, o N3 quedándose dentro más de 'quebradaLingerSeconds'.")]
        public DirectorAdviceData adviceCaudal;
        [Tooltip("Segundos dentro de una QuebradaZone en N3 (después de S3) antes de escalar a S6.")]
        public float quebradaLingerSeconds = 10f;

        [Header("S4 — Sirena no visitada")]
        public DirectorAdviceData adviceSirenaNoVisitada;
        [Tooltip("Segundos en N2 sin visitar la sirena antes del recordatorio. Al subir a N3 se avisa siempre si sigue sin visitarse.")]
        public float sirenaReminderDelayN2 = 90f;

        [Header("S5 / S10 — Ayuda comunitaria (los llaman los diálogos)")]
        public DirectorAdviceData adviceAyudaCorrecta;
        public DirectorAdviceData adviceObjetos;

        [Header("S7 / S9 — Llegada al punto de encuentro")]
        public DirectorAdviceData adviceLlegada;
        [Tooltip("S9: se muestra justo después de S7.")]
        public DirectorAdviceData adviceZonaSegura;

        [Header("S8 — Salto rápido a N4")]
        public DirectorAdviceData adviceSaltoN4;

        [Header("Cooldown")]
        [Tooltip("Segundos mínimos entre dos consejos no urgentes (GDD: evitar interrupciones constantes).")]
        public float cooldownSeconds = 15f;

        // ── Internos ──────────────────────────────────────────────────────────────
        private Transform _playerCamera;

        private float _lastAdviceTime = -999f;
        private DirectorAdviceData _lastAdviceData;
        private readonly Queue<DirectorAdviceData> _urgentQueue = new Queue<DirectorAdviceData>();

        private Vector3 _lastCheckedPosition;
        private float _stillTimer;

        private float _minRouteDistanceSeen = float.PositiveInfinity;
        private float _desvioTimer;

        private bool _wasInsideQuebrada;
        private float _insideQuebradaTimer;
        private bool _caudalShownThisVisit;

        private float _n2Timer;
        private bool _sirenaRemindedInN2;

        private bool _subscribedToArrival;
        private bool _subscribedToStage;

        // ── Lifecycle ─────────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Unsubscribe();
        }

        private void OnEnable() => Subscribe();
        private void OnDisable() => Unsubscribe();

        private void Subscribe()
        {
            // Los singletons pueden no existir todavía por orden de carga de escena —
            // Update() reintenta hasta que aparezcan.
            if (!_subscribedToArrival && EvacuationRouteController.Instance != null)
            {
                EvacuationRouteController.Instance.OnArrivalAtPuntoDeEncuentro += HandleArrival;
                _subscribedToArrival = true;
            }
            if (!_subscribedToStage && StageManager.Instance != null)
            {
                StageManager.Instance.OnStageChanged += HandleStageChanged;
                _subscribedToStage = true;
            }
        }

        private void Unsubscribe()
        {
            if (_subscribedToArrival && EvacuationRouteController.Instance != null)
                EvacuationRouteController.Instance.OnArrivalAtPuntoDeEncuentro -= HandleArrival;
            _subscribedToArrival = false;

            if (_subscribedToStage && StageManager.Instance != null)
                StageManager.Instance.OnStageChanged -= HandleStageChanged;
            _subscribedToStage = false;
        }

        private void Update()
        {
            if (!_subscribedToArrival || !_subscribedToStage) Subscribe();

            FlushUrgentQueue();

            if (_playerCamera == null)
            {
                if (Camera.main == null) return;
                _playerCamera = Camera.main.transform;
                _lastCheckedPosition = _playerCamera.position;
            }

            RiskLevel level = RiskLevelIndicator.Instance != null
                ? RiskLevelIndicator.Instance.CurrentLevel
                : RiskLevel.None;

            // GDD §24: sin posición confiable no se da orientación basada en posición.
            // Pasar false reinicia los temporizadores, así que al recuperar el tracking
            // se vuelve a medir desde cero en vez de reaccionar al salto.
            bool reliable = TrackingStatusBanner.IsPositionReliable;
            bool isN4 = level == RiskLevel.N4;
            // Leyendo un panel modal (diálogo, trivia, info) no cuenta como "inmóvil".
            CheckInactivity(isN4 && reliable && !IsModalOpen());
            CheckDesvio(isN4 && reliable);
            CheckQuebrada(level, reliable);
            CheckSirenaInN2(level);
        }

        /// <summary>True si hay consejos urgentes esperando turno (RouteProgressHud espera a que salgan antes del cierre).</summary>
        public bool HasPendingAdvice => _urgentQueue.Count > 0;

        // ── API pública (la llaman los diálogos de ayuda comunitaria) ─────────────
        /// <summary>S5: el usuario resolvió bien una situación de ayuda comunitaria.</summary>
        public void NotifyCommunityHelpCorrect() => TryTrigger(adviceAyudaCorrecta);

        /// <summary>S10: el usuario eligió cargar/regresar por objetos no vitales.</summary>
        public void NotifyObjectsDecision() => TryTrigger(adviceObjetos, urgent: true);

        // ── S1: N4 + inmóvil ──────────────────────────────────────────────────────
        private void CheckInactivity(bool evaluate)
        {
            if (!evaluate)
            {
                _stillTimer = 0f;
                _lastCheckedPosition = _playerCamera.position;
                return;
            }

            float moved = Vector3.Distance(_playerCamera.position, _lastCheckedPosition);
            if (moved > inactivityMoveThreshold)
            {
                _stillTimer = 0f;
                _lastCheckedPosition = _playerCamera.position;
                return;
            }

            _stillTimer += Time.deltaTime;
            if (_stillTimer < inactivityWindow) return;

            _stillTimer = 0f;
            TryTrigger(adviceN4Inactivo);
        }

        // ── S2: N4 + desvío de ruta ───────────────────────────────────────────────
        // Aproximación deliberada: en vez de un chequeo estricto "a la izquierda/
        // derecha del camino", se compara la distancia perpendicular actual a la
        // ruta contra el mínimo visto recientemente — si crece de forma sostenida,
        // el jugador se está alejando en vez de progresar.
        private void CheckDesvio(bool evaluate)
        {
            EvacuationRouteController route = EvacuationRouteController.Instance;
            if (!evaluate || route == null || !route.IsVisible)
            {
                _minRouteDistanceSeen = float.PositiveInfinity;
                _desvioTimer = 0f;
                return;
            }

            _desvioTimer += Time.deltaTime;
            if (_desvioTimer < desvioCheckInterval) return;
            _desvioTimer = 0f;

            if (!route.TryGetDistanceToRoute(_playerCamera.position, out float distance)) return;

            if (distance < _minRouteDistanceSeen)
            {
                _minRouteDistanceSeen = distance;
                return;
            }

            if (distance - _minRouteDistanceSeen < desvioDeltaThreshold) return;

            _minRouteDistanceSeen = distance; // evita re-disparar en el próximo chequeo con la misma medición
            TryTrigger(adviceN4Desvio);
        }

        // ── S3 / S6: cerca del cauce ──────────────────────────────────────────────
        private void CheckQuebrada(RiskLevel level, bool reliable)
        {
            bool relevant = reliable && (level == RiskLevel.N3 || level == RiskLevel.N4);
            bool inside = relevant && QuebradaZone.IsInsideAny(_playerCamera.position);

            if (!inside)
            {
                _wasInsideQuebrada = false;
                _insideQuebradaTimer = 0f;
                _caudalShownThisVisit = false;
                return;
            }

            bool justEntered = !_wasInsideQuebrada;
            _wasInsideQuebrada = true;
            _insideQuebradaTimer += Time.deltaTime;

            if (level == RiskLevel.N4)
            {
                if (!_caudalShownThisVisit)
                {
                    _caudalShownThisVisit = true;
                    TryTrigger(adviceCaudal, urgent: true);
                }
                return;
            }

            // N3: primero el aviso preventivo (S3); si se queda, escalar a S6.
            if (justEntered)
                TryTrigger(adviceN3Quebrada);
            else if (!_caudalShownThisVisit && _insideQuebradaTimer >= quebradaLingerSeconds)
            {
                _caudalShownThisVisit = true;
                TryTrigger(adviceCaudal, urgent: true);
            }
        }

        // ── S4: sirena no visitada ────────────────────────────────────────────────
        private void CheckSirenaInN2(RiskLevel level)
        {
            if (level != RiskLevel.N2 || _sirenaRemindedInN2) return;

            _n2Timer += Time.deltaTime;
            if (_n2Timer < sirenaReminderDelayN2 || !IsSirenaPending()) return;

            _sirenaRemindedInN2 = true;
            TryTrigger(adviceSirenaNoVisitada);
        }

        /// <summary>True si hay un hotspot Sirena activo y todavía no se ha visitado ninguno.</summary>
        private static bool IsSirenaPending()
        {
            bool anySirena = false;
            foreach (HotspotController h in HotspotController.ActiveHotspots)
            {
                if (h == null || h.ResolvedCategory != HotspotCategory.Sirena) continue;
                if (h.HasBeenVisited) return false;
                anySirena = true;
            }
            return anySirena;
        }

        // ── Cambios de etapa: S4 al subir a N3, S8 salto a N4 ─────────────────────
        private void HandleStageChanged(StageManager.Stage previous, StageManager.Stage current)
        {
            _n2Timer = 0f;
            _sirenaRemindedInN2 = false;

            if (current == StageManager.Stage.Etapa4 &&
                (previous == StageManager.Stage.Etapa1 || previous == StageManager.Stage.Etapa2))
            {
                TryTrigger(adviceSaltoN4, urgent: true);
                return;
            }

            if (current == StageManager.Stage.Etapa3 && previous < StageManager.Stage.Etapa3 && IsSirenaPending())
                TryTrigger(adviceSirenaNoVisitada);
        }

        // ── S7 + S9: llegada al punto de encuentro ────────────────────────────────
        private void HandleArrival()
        {
            TryTrigger(adviceLlegada, urgent: true);
            TryTrigger(adviceZonaSegura, urgent: true);
        }

        // ── Disparo con cooldown + no-repetir (GDD pág. 28) ──────────────────────
        private void TryTrigger(DirectorAdviceData advice, bool urgent = false)
        {
            if (advice == null) return;

            if (urgent)
            {
                if (!_urgentQueue.Contains(advice)) _urgentQueue.Enqueue(advice);
                FlushUrgentQueue();
                return;
            }

            if (IsPanelBusy()) return;
            if (advice == _lastAdviceData) return;
            if (Time.time - _lastAdviceTime < cooldownSeconds) return;

            Show(advice);
        }

        private void FlushUrgentQueue()
        {
            if (_urgentQueue.Count == 0 || IsPanelBusy()) return;
            Show(_urgentQueue.Dequeue());
        }

        // Ocupado = banner del Director visible o un panel modal abierto (NpcDialoguePanel,
        // TriviaPanel… bloquean el input vía StageManager). Así el consejo no queda tapado
        // detrás del diálogo: los urgentes esperan en cola, los demás se descartan.
        private static bool IsPanelBusy() =>
            (DirectorAdvicePanel.Instance != null && DirectorAdvicePanel.Instance.IsShowing) || IsModalOpen();

        private static bool IsModalOpen() =>
            StageManager.Instance != null && StageManager.Instance.IsPlayerInputBlocked;

        private void Show(DirectorAdviceData advice)
        {
            _lastAdviceTime = Time.time;
            _lastAdviceData = advice;
            DirectorController.Instance?.ShowAdvice(advice);
        }
    }
}
