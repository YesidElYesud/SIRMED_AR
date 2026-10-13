namespace SIRMED.Gameplay.NPCs
{
    using System.Collections;
    using Google.XR.ARCoreExtensions;
    using SIRMED.Gameplay.Hotspots;
    using SIRMED.Managers;
    using SIRMED.Utils;
    using UnityEngine;

    /// <summary>
    /// LeaderNpcController — la Líder comunitaria como personaje 3D que reemplaza a la
    /// moneda de sus hotspots (comportamiento del proyecto WebGL: objectsToHideOnClose /
    /// objectsToShowOnClose + NPCWaypointWalker).
    ///
    /// Flujo:
    ///   1. Al inicio el modelo (visualRoot) está oculto y cada parada muestra su moneda.
    ///      Cada parada es un HotspotController normal con requiredStageOverride = su nivel,
    ///      así que solo se ve la del nivel activo.
    ///   2. Al activar cualquier parada (HotspotController.Activated, antes de abrir el panel) la líder aparece
    ///      en ese punto y la moneda de esa parada se oculta.
    ///   3. Al cambiar de nivel, si ya apareció, camina en línea recta hasta la parada del
    ///      nuevo nivel (cuya moneda se oculta: el jugador habla con ella ahí).
    ///
    /// Movimiento: mismo principio que DirectorController — cada parada ya trae altitud
    /// real del Geospatial API, así que no hace falta detectar el suelo. Mientras está
    /// quieta, el modelo se reparenta bajo el ARGeospatialAnchor de la parada para que
    /// siga al anchor si este se corrige; mientras camina queda en espacio de mundo.
    ///
    /// SETUP EN EDITOR:
    ///   LiderNPC                 [LeaderNpcController]   (raíz de escena, activo)
    ///   └── Lider                [Animator]              ← visualRoot (puede empezar activo; se oculta en Awake)
    /// </summary>
    public class LeaderNpcController : MonoBehaviour
    {
        [Header("Paradas")]
        [Tooltip("Hotspots del Líder, en orden. Cada uno con requiredStageOverride = su nivel (1 = N1 … 4 = N4)\n" +
                 "y sin sequenceId (si no, la parada de un nivel no aparece cuando se saltó la anterior).")]
        public HotspotController[] stops;

        [Header("Visual")]
        [Tooltip("Raíz del modelo 3D de la líder (hijo de este GameObject).")]
        public Transform visualRoot;

        [Tooltip("Animator del modelo. Opcional.")]
        public Animator animator;

        [Tooltip("DissolveFader en la raíz del modelo (aparición suave). Sin él, aparece con SetActive.")]
        public DissolveFader fader;

        [Tooltip("Parámetro Bool del Animator que indica 'caminando'. Vacío = no se toca el Animator.")]
        public string walkingBoolParam = "Caminando";

        [Tooltip("Desplazamiento vertical (m) respecto a la posición del hotspot. Negativo si la moneda flota\n" +
                 "sobre el suelo y la líder queda en el aire.")]
        public float heightOffset = 0f;

        [Header("Movimiento")]
        [Tooltip("Velocidad de caminata en metros/segundo.")]
        public float moveSpeed = 1.2f;

        [Tooltip("Distancia (m) para considerar que ya llegó a la parada.")]
        public float arrivalThreshold = 0.05f;

        [Tooltip("Grados por segundo al girar (hacia la parada o hacia el jugador).")]
        public float turnSpeed = 240f;

        [Tooltip("Si true, cuando está quieta gira para mirar al jugador.")]
        public bool facePlayerWhenIdle = true;

        // ── Internos ──────────────────────────────────────────────────────────────
        private HotspotController _currentStop;
        private Coroutine _moveRoutine;
        private Transform _playerCamera;
        private bool _spawned;

        public bool IsSpawned => _spawned;

        // ── Lifecycle ─────────────────────────────────────────────────────────────
        private void Awake()
        {
            if (animator != null) animator.applyRootMotion = false; // clips Mixamo: el script mueve, no la animación
            if (visualRoot != null)
            {
                if (fader != null) fader.SetVisibleImmediate(false);
                else visualRoot.gameObject.SetActive(false);
            }
        }

        private void OnEnable()
        {
            if (stops == null) return;
            foreach (HotspotController stop in stops)
                if (stop != null) stop.Activated += OnStopActivated;
        }

        private void OnDisable()
        {
            if (stops == null) return;
            foreach (HotspotController stop in stops)
                if (stop != null) stop.Activated -= OnStopActivated;
        }

        private void Start()
        {
            if (Camera.main != null) _playerCamera = Camera.main.transform;
            if (StageManager.Instance != null)
                StageManager.Instance.OnStageChanged += OnStageChanged;
        }

        private void OnDestroy()
        {
            if (StageManager.Instance != null)
                StageManager.Instance.OnStageChanged -= OnStageChanged;
        }

        private void Update()
        {
            if (!_spawned || _moveRoutine != null || !facePlayerWhenIdle || _playerCamera == null) return;
            TurnTowards(_playerCamera.position);
        }

        // ── Eventos ───────────────────────────────────────────────────────────────
        private void OnStopActivated(HotspotController stop)
        {
            if (_spawned) return;
            Spawn(stop);
        }

        private void OnStageChanged(StageManager.Stage previous, StageManager.Stage current)
        {
            if (!_spawned) return;
            // Los hotspots refrescan su visibilidad en este mismo evento; esperar un frame
            // para que ya esté activa la parada del nuevo nivel.
            StartCoroutine(MoveToActiveStopNextFrame());
        }

        private IEnumerator MoveToActiveStopNextFrame()
        {
            yield return null;
            if (stops == null) yield break;
            foreach (HotspotController stop in stops)
            {
                if (stop != null && stop != _currentStop && stop.gameObject.activeInHierarchy)
                {
                    MoveTo(stop);
                    yield break;
                }
            }
        }

        // ── Aparición ─────────────────────────────────────────────────────────────
        private void Spawn(HotspotController stop)
        {
            if (visualRoot == null) return;

            _spawned = true;
            _currentStop = stop;
            stop.SetMarkerVisible(false);

            visualRoot.SetParent(null, true);
            visualRoot.position = StopPosition(stop);
            if (_playerCamera != null) FaceInstantly(_playerCamera.position);
            AttachToAnchor(stop);
            if (fader != null) fader.FadeIn();
            else visualRoot.gameObject.SetActive(true);
        }

        // ── Movimiento ────────────────────────────────────────────────────────────
        private void MoveTo(HotspotController target)
        {
            if (target == null || visualRoot == null) return;
            if (_moveRoutine != null) StopCoroutine(_moveRoutine);
            _moveRoutine = StartCoroutine(MoveRoutine(target));
        }

        private IEnumerator MoveRoutine(HotspotController target)
        {
            _currentStop = target;
            target.SetMarkerVisible(false);

            // Igual que el Director: no confiar en transform.position hasta que el anchor resuelva.
            while (!target.IsAnchorReady())
                yield return null;

            visualRoot.SetParent(null, true);
            SetWalking(true);

            // Se relee la posición cada frame: el anchor de destino puede corregirse mientras camina.
            while (Vector3.Distance(visualRoot.position, StopPosition(target)) > arrivalThreshold)
            {
                Vector3 targetPos = StopPosition(target);
                TurnTowards(targetPos);
                visualRoot.position = Vector3.MoveTowards(visualRoot.position, targetPos, moveSpeed * Time.deltaTime);
                yield return null;
            }
            visualRoot.position = StopPosition(target);

            SetWalking(false);
            AttachToAnchor(target);
            _moveRoutine = null;
        }

        // ── Helpers ───────────────────────────────────────────────────────────────
        private Vector3 StopPosition(HotspotController stop) =>
            stop.transform.position + Vector3.up * heightOffset;

        /// <summary>Cuelga el modelo del ARGeospatialAnchor de la parada (no del hotspot, que se desactiva al cambiar de nivel).</summary>
        private void AttachToAnchor(HotspotController stop)
        {
            Transform parent = stop.transform.parent;
            if (parent != null && parent.GetComponent<ARGeospatialAnchor>() != null)
                visualRoot.SetParent(parent, true);
        }

        private void TurnTowards(Vector3 worldPoint)
        {
            Vector3 flat = worldPoint - visualRoot.position;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.0001f) return;
            visualRoot.rotation = Quaternion.RotateTowards(
                visualRoot.rotation, Quaternion.LookRotation(flat, Vector3.up), turnSpeed * Time.deltaTime);
        }

        private void FaceInstantly(Vector3 worldPoint)
        {
            Vector3 flat = worldPoint - visualRoot.position;
            flat.y = 0f;
            if (flat.sqrMagnitude > 0.0001f)
                visualRoot.rotation = Quaternion.LookRotation(flat, Vector3.up);
        }

        private void SetWalking(bool walking)
        {
            if (animator != null && !string.IsNullOrEmpty(walkingBoolParam))
                animator.SetBool(walkingBoolParam, walking);
        }
    }
}
