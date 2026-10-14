namespace SIRMED.Gameplay.Director
{
    using System.Collections;
    using SIRMED.UI;
    using SIRMED.Utils;
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
    ///
    /// Cambio de arquitectura (sesión 2026-09-23): el spawn en `homeWaypoint` ya NO se
    /// hace copiando su posición por script en Update() — ese mecanismo nunca llegó a
    /// funcionar de forma confiable en dispositivo (Awake() ni siquiera llegaba a
    /// ejecutarse en varias pruebas, sin causa identificable pese a builds limpios).
    /// En su lugar se adopta el mismo patrón ya probado con los íconos de los hotspots:
    /// el GameObject `Director` debe ser hijo directo de `homeWaypoint` en la Jerarquía
    /// (posición local 0,0,0). Así, en cuanto `ARGeospatialCreatorAnchor` reparenta
    /// `homeWaypoint` bajo su `ARGeospatialAnchor` resuelto, el modelo se mueve solo con
    /// él — sin ningún polling. `homeWaypoint` se conserva solo por referencia/documentación,
    /// ya no participa en la lógica de spawn.
    ///
    /// Modo "frente al jugador" (appearInFrontOfPlayer, por defecto, sesión 2026-10-13): el
    /// modelo está oculto y, al abrirse un consejo, aparece a distanceInFront metros delante
    /// de la cámara (con DissolveFader, sin popping). Queda fijo en el mundo mientras el
    /// consejo está abierto (el jugador puede acercarse), mira al jugador, y desaparece al
    /// cerrarse. En este modo se ignora moveToPointId.
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

        [Tooltip("Punto donde arranca el Director (informativo — el spawn real ocurre por parentesco en la Jerarquía, ver comentario de clase).")]
        public DirectorWaypoint homeWaypoint;

        [Tooltip("Velocidad de caminata en metros/segundo.")]
        public float moveSpeed = 1.2f;

        [Tooltip("Distancia (m) para considerar que ya llegó al punto objetivo.")]
        public float arrivalThreshold = 0.05f;

        [Header("Aparición frente al jugador")]
        [Tooltip("Si true, el Director solo se ve mientras hay un consejo abierto: aparece delante de la cámara\n" +
                 "y desaparece al cerrarse (se ignora moveToPointId). Si false, comportamiento anterior (anclado).")]
        public bool appearInFrontOfPlayer = true;

        [Tooltip("Distancia (m) delante de la cámara a la que aparece.")]
        public float distanceInFront = 2.5f;

        [Tooltip("Altura aproximada del teléfono sobre el suelo (m): los pies quedan a esta distancia bajo la cámara.")]
        public float eyeHeight = 1.4f;

        [Tooltip("Grados por segundo al girar para mirar al jugador.")]
        public float turnSpeed = 240f;

        [Tooltip("DissolveFader en la raíz del modelo (aparición suave). Sin él, aparece/desaparece con SetActive.")]
        public DissolveFader fader;

        // ── Internos ──────────────────────────────────────────────────────────────
        private Coroutine _moveRoutine;
        private Transform _playerCamera;
        private bool _presenting;

        // ── Lifecycle ─────────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            if (appearInFrontOfPlayer) SetVisualVisible(false, immediate: true);
        }

        private void Update()
        {
            if (!_presenting || visualRoot == null) return;
            if (_playerCamera == null && Camera.main != null) _playerCamera = Camera.main.transform;
            if (_playerCamera == null) return;

            Vector3 flat = _playerCamera.position - visualRoot.position;
            flat.y = 0f;
            if (flat.sqrMagnitude > 0.0001f)
                visualRoot.rotation = Quaternion.RotateTowards(
                    visualRoot.rotation, Quaternion.LookRotation(flat, Vector3.up), turnSpeed * Time.deltaTime);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ── API pública — consejos ───────────────────────────────────────────────
        /// <summary>Muestra un consejo (banner + gestos por línea); si el consejo define moveToPointId, camina hacia allá al terminar.</summary>
        public void ShowAdvice(DirectorAdviceData advice)
        {
            if (advice == null || advice.lines == null || advice.lines.Length == 0) return;
            if (appearInFrontOfPlayer) Present();
            DirectorAdvicePanel.Instance?.Show(advice, OnLineShown, () => OnAdviceFinished(advice));
        }

        private void OnLineShown(DirectorAdviceLine line)
        {
            if (animator != null && !string.IsNullOrEmpty(line.gestureTrigger))
                animator.SetTrigger(line.gestureTrigger);
        }

        private void OnAdviceFinished(DirectorAdviceData advice)
        {
            if (appearInFrontOfPlayer)
            {
                _presenting = false;
                SetVisualVisible(false, immediate: false);
                return;
            }

            if (string.IsNullOrEmpty(advice.moveToPointId)) return;

            DirectorWaypoint target = FindWaypoint(advice.moveToPointId);
            if (target != null) MoveTo(target);
        }

        // ── Aparición frente al jugador ───────────────────────────────────────────
        /// <summary>Coloca el modelo delante de la cámara (si no estaba ya presente) y lo hace aparecer.</summary>
        private void Present()
        {
            if (visualRoot == null) return;
            if (_playerCamera == null && Camera.main != null) _playerCamera = Camera.main.transform;

            if (!_presenting && _playerCamera != null)
            {
                if (_moveRoutine != null) { StopCoroutine(_moveRoutine); _moveRoutine = null; }

                Vector3 forward = _playerCamera.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.0001f) forward = _playerCamera.up; // teléfono apuntando al suelo/cielo
                forward.y = 0f;
                forward.Normalize();

                Vector3 pos = _playerCamera.position + forward * distanceInFront;
                pos.y = _playerCamera.position.y - eyeHeight;

                // Fijo en el mundo (no sigue a la cámara): se suelta del waypoint.
                visualRoot.SetParent(null, true);
                visualRoot.position = pos;
                visualRoot.rotation = Quaternion.LookRotation(-forward, Vector3.up);
            }

            _presenting = true;
            SetVisualVisible(true, immediate: false);
        }

        private void SetVisualVisible(bool visible, bool immediate)
        {
            if (visualRoot == null) return;
            if (fader == null) { visualRoot.gameObject.SetActive(visible); return; }

            if (immediate) fader.SetVisibleImmediate(visible);
            else if (visible) fader.FadeIn();
            else fader.FadeOut();
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
