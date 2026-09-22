namespace SIRMED.Gameplay.Director
{
    using System.Collections;
    using SIRMED.UI;
    using UnityEngine;

    /// <summary>
    /// DirectorController — el Director DAGRD como personaje 3D anclado (GDD pág. 28).
    ///
    /// Diseño de movimiento (decisión de sesión — ver MIGRATION_CONTEXT.md): en vez del
    /// ground-snapping por raycast contra terreno modelado que bloquea la Fase 5 (NPCs;
    /// no hay terreno modelado en AR real), el Director camina entre DirectorWaypoint —
    /// GameObjects con su propio ARGeospatialCreatorAnchor, mismo patrón ya validado por
    /// hotspots/ruta de evacuación (ver RouteWaypoint/EvacuationRouteController). Cada
    /// waypoint ya trae su posición Y real (altitud del Geospatial API), así que
    /// interpolar en línea recta entre dos waypoints ya anclados no necesita ninguna
    /// detección de suelo — el mismo principio que ya usa el minimapa (MIGRATION_CONTEXT.md
    /// §9): el espacio de mundo de la sesión AR ya es métrico y consistente una vez
    /// resueltos los anchors.
    ///
    /// `visualRoot`/`animator` pueden quedar sin asignar hasta que exista la malla+rig
    /// del personaje — el resto del sistema (reglas, banner, movimiento entre puntos)
    /// funciona igual, solo sin representación visual todavía.
    /// </summary>
    public class DirectorController : MonoBehaviour
    {
        // ── Singleton ─────────────────────────────────────────────────────────────
        public static DirectorController Instance { get; private set; }

        [Header("Visual (puede quedar vacío hasta que exista la malla+rig)")]
        [Tooltip("Raíz del modelo 3D que efectivamente camina por el espacio de mundo AR.")]
        public Transform visualRoot;

        [Tooltip("Animator del rig. Opcional — si es null, los gestos/caminar simplemente no se reproducen.")]
        public Animator animator;

        [Tooltip("Nombre del parámetro Bool del Animator que indica 'caminando'. Vacío = no se toca el Animator durante el movimiento.")]
        public string walkingBoolParam = "Caminando";

        [Header("Movimiento")]
        [Tooltip("Todos los puntos por los que el Director puede moverse. DirectorAdviceData.moveToPointId busca aquí por pointId.")]
        public DirectorWaypoint[] waypoints;

        [Tooltip("Punto donde aparece el Director al arrancar la escena (antes de que cualquier regla lo mande a otro lado).")]
        public DirectorWaypoint homeWaypoint;

        [Tooltip("Velocidad de caminata en metros/segundo.")]
        public float moveSpeed = 1.2f;

        [Tooltip("Distancia (m) para considerar que ya llegó al punto objetivo.")]
        public float arrivalThreshold = 0.05f;

        // ── Internos ──────────────────────────────────────────────────────────────
        private Coroutine _moveRoutine;
        private bool _spawned;

        // ── Lifecycle ─────────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            // Spawn diferido: recién se ubica al Director en homeWaypoint cuando ese
            // anchor resuelve (mismo patrón de espera que EvacuationRouteController).
            if (_spawned || visualRoot == null || homeWaypoint == null) return;
            if (!homeWaypoint.IsAnchorReady()) return;

            visualRoot.position = homeWaypoint.transform.position;
            _spawned = true;
        }

        // ── API pública — consejos ───────────────────────────────────────────────
        /// <summary>Muestra un consejo (banner + gestos por línea); si el consejo define moveToPointId, camina hacia allá al terminar.</summary>
        public void ShowAdvice(DirectorAdviceData advice)
        {
            if (advice == null || advice.lines == null || advice.lines.Length == 0) return;
            DirectorAdvicePanel.Instance?.Show(advice, OnLineShown, () => OnAdviceFinished(advice));
        }

        private void OnLineShown(DirectorAdviceLine line)
        {
            if (animator != null && !string.IsNullOrEmpty(line.gestureTrigger))
                animator.SetTrigger(line.gestureTrigger);
        }

        private void OnAdviceFinished(DirectorAdviceData advice)
        {
            if (string.IsNullOrEmpty(advice.moveToPointId)) return;

            DirectorWaypoint target = FindWaypoint(advice.moveToPointId);
            if (target != null) MoveTo(target);
        }

        private DirectorWaypoint FindWaypoint(string pointId)
        {
            if (waypoints == null) return null;
            foreach (DirectorWaypoint wp in waypoints)
                if (wp != null && wp.pointId == pointId) return wp;
            return null;
        }

        // ── API pública — movimiento ──────────────────────────────────────────────
        public void MoveTo(DirectorWaypoint target)
        {
            if (target == null || visualRoot == null) return;
            if (_moveRoutine != null) StopCoroutine(_moveRoutine);
            _moveRoutine = StartCoroutine(MoveRoutine(target));
        }

        private IEnumerator MoveRoutine(DirectorWaypoint target)
        {
            // Esperar a que el anchor de destino haya resuelto — igual chequeo que
            // hotspots/RouteWaypoint antes de confiar en su transform.position.
            while (!target.IsAnchorReady())
                yield return null;

            if (animator != null && !string.IsNullOrEmpty(walkingBoolParam))
                animator.SetBool(walkingBoolParam, true);

            Vector3 targetPos = target.transform.position;
            while (Vector3.Distance(visualRoot.position, targetPos) > arrivalThreshold)
            {
                Vector3 toTarget = targetPos - visualRoot.position;
                Vector3 flat = new Vector3(toTarget.x, 0f, toTarget.z);
                if (flat.sqrMagnitude > 0.0001f)
                    visualRoot.rotation = Quaternion.LookRotation(flat, Vector3.up);

                visualRoot.position = Vector3.MoveTowards(visualRoot.position, targetPos, moveSpeed * Time.deltaTime);
                yield return null;
            }
            visualRoot.position = targetPos;

            if (animator != null && !string.IsNullOrEmpty(walkingBoolParam))
                animator.SetBool(walkingBoolParam, false);

            _moveRoutine = null;

            if (target.arrivalAdvice != null)
                ShowAdvice(target.arrivalAdvice);
        }
    }
}
