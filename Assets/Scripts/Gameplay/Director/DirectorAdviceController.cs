namespace SIRMED.Gameplay.Director
{
    using SIRMED.Gameplay.Environment;
    using SIRMED.Gameplay.Hotspots;
    using SIRMED.UI;
    using UnityEngine;

    /// <summary>
    /// DirectorAdviceController — motor de reglas del Director DAGRD (GDD pág. 28,
    /// Tabla 15; mensajes iniciales en Anexo A). Decide CUÁNDO mostrar un consejo;
    /// el CÓMO (banner + gestos + movimiento del personaje) vive en
    /// DirectorController/DirectorAdvicePanel — este script no los toca
    /// directamente, solo llama DirectorController.Instance.ShowAdvice().
    ///
    /// v1 implementa 3 de las condiciones de la Tabla 15/Anexo A — las que se
    /// pueden evaluar con estado que el resto del proyecto ya expone (nivel de
    /// riesgo, posición del jugador, ruta de evacuación):
    ///   • N4 + usuario inmóvil un tiempo definido.
    ///   • N4 + usuario alejándose de la ruta.
    ///   • Llegada al punto de encuentro.
    /// "Ayuda comunitaria correcta" y "hotspot crítico omitido" quedan pendientes
    /// de que existan esas mecánicas (Fase 5 del roadmap — MIGRATION_CONTEXT.md
    /// §7.2/§13) — agregar sus reglas aquí cuando existan sin tocar lo demás.
    ///
    /// Regla de diseño del GDD (pág. 28): cooldown entre consejos y nunca repetir
    /// el mismo mensaje seguido — implementado en TryTrigger().
    /// </summary>
    public class DirectorAdviceController : MonoBehaviour
    {
        public static DirectorAdviceController Instance { get; private set; }

        [Header("Consejo — N4 inmóvil (Tabla 15 / Anexo A \"N4 / inactividad\")")]
        public DirectorAdviceData adviceN4Inactivo;
        [Tooltip("Segundos sin moverse más de 'inactivityMoveThreshold' antes de disparar el consejo.")]
        public float inactivityWindow = 12f;
        [Tooltip("Metros de movimiento que reinician el contador de inactividad.")]
        public float inactivityMoveThreshold = 1f;

        [Header("Consejo — N4 desvío de ruta (Tabla 15 / Anexo A \"N4 / desvío\")")]
        public DirectorAdviceData adviceN4Desvio;
        [Tooltip("Cuánto debe crecer la distancia a la ruta (respecto al mínimo visto recientemente) para considerarlo un desvío, en metros.")]
        public float desvioDeltaThreshold = 4f;
        [Tooltip("Cada cuántos segundos se revisa la distancia a la ruta (no hace falta cada frame).")]
        public float desvioCheckInterval = 1f;

        [Header("Consejo — Llegada al punto de encuentro (Anexo A \"Llegada\")")]
        public DirectorAdviceData adviceLlegada;

        [Header("Cooldown")]
        [Tooltip("Segundos mínimos entre dos consejos cualquiera (GDD: evitar interrupciones constantes).")]
        public float cooldownSeconds = 15f;

        // ── Internos ──────────────────────────────────────────────────────────────
        private Transform _playerCamera;

        private float _lastAdviceTime = -999f;
        private DirectorAdviceData _lastAdviceData;

        private Vector3 _lastCheckedPosition;
        private float _stillTimer;

        private float _minRouteDistanceSeen = float.PositiveInfinity;
        private float _desvioTimer;

        private bool _subscribedToArrival;

        // ── Lifecycle ─────────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            UnsubscribeFromArrival();
        }

        private void OnEnable()
        {
            SubscribeToArrival();
        }

        private void OnDisable()
        {
            UnsubscribeFromArrival();
        }

        private void SubscribeToArrival()
        {
            // El singleton de la ruta puede no existir todavía por orden de carga de
            // escena — Update() reintenta suscribirse hasta que aparezca.
            if (_subscribedToArrival || EvacuationRouteController.Instance == null) return;
            EvacuationRouteController.Instance.OnArrivalAtPuntoDeEncuentro += HandleArrival;
            _subscribedToArrival = true;
        }

        private void UnsubscribeFromArrival()
        {
            if (!_subscribedToArrival || EvacuationRouteController.Instance == null) return;
            EvacuationRouteController.Instance.OnArrivalAtPuntoDeEncuentro -= HandleArrival;
            _subscribedToArrival = false;
        }

        private void Update()
        {
            if (!_subscribedToArrival) SubscribeToArrival();

            if (_playerCamera == null)
            {
                if (Camera.main == null) return;
                _playerCamera = Camera.main.transform;
                _lastCheckedPosition = _playerCamera.position;
            }

            bool isN4 = RiskLevelIndicator.Instance != null &&
                        RiskLevelIndicator.Instance.CurrentLevel == RiskLevel.N4;

            CheckInactivity(isN4);
            CheckDesvio(isN4);
        }

        // ── Regla: N4 + inmóvil ───────────────────────────────────────────────────
        private void CheckInactivity(bool isN4)
        {
            if (!isN4)
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

        // ── Regla: N4 + desvío de ruta ────────────────────────────────────────────
        // Aproximación deliberada: en vez de un chequeo estricto "a la izquierda/
        // derecha del camino", se compara la distancia perpendicular actual a la
        // ruta contra el mínimo visto recientemente — si crece de forma sostenida,
        // el jugador se está alejando en vez de progresar. Suficiente para el GDD
        // ("Corregir orientación") sin necesitar seguimiento de progreso por tramo.
        private void CheckDesvio(bool isN4)
        {
            EvacuationRouteController route = EvacuationRouteController.Instance;
            if (!isN4 || route == null || !route.IsVisible)
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

        // ── Regla: llegada al punto de encuentro ─────────────────────────────────
        private void HandleArrival()
        {
            TryTrigger(adviceLlegada);
        }

        // ── Disparo con cooldown + no-repetir (GDD pág. 28) ──────────────────────
        private void TryTrigger(DirectorAdviceData advice)
        {
            if (advice == null) return;
            if (DirectorAdvicePanel.Instance != null && DirectorAdvicePanel.Instance.IsShowing) return;
            if (advice == _lastAdviceData) return;
            if (Time.time - _lastAdviceTime < cooldownSeconds) return;

            _lastAdviceTime = Time.time;
            _lastAdviceData = advice;
            DirectorController.Instance?.ShowAdvice(advice);
        }
    }
}
