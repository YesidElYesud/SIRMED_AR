using System;
using SIRMED.Gameplay.Dialogue;
using SIRMED.Gameplay.Environment;
using SIRMED.Gameplay.NPCs;
using SIRMED.Managers;
using SIRMED.UI;
using SIRMED.Utils;
using UnityEngine;

/// <summary>
/// Bombero del punto de encuentro (checklist M08; línea del documento de diálogos, personaje 4).
/// Vive en un GameObject anclado (ARGeospatialCreatorAnchor, sin Collider ni HotspotData: no se toca)
/// junto al punto de encuentro; el modelo es hijo (prefab "bombero").
///
/// En el punto de encuentro:
///   - Oculto hasta visibleFromStage (por defecto N3, cuando aparece la cinta de la ruta); una vez visible
///     ya no se oculta. En N4 grita una vez (trigger "Yell"). Mira al jugador a menos de lookDistance.
///   - Al llegar el jugador al punto de encuentro (EvacuationRouteController.ArrivedAtAnyMeetingPoint, solo
///     si el punto de esa ruta está a menos de meetingPointRadius: con varias rutas hay un bombero por punto)
///     saluda ("Wave") y, cuando el Director termina S7/S9 y el panel está libre, dice line ("Hizo lo
///     correcto quedándose en la ruta…"). Al cerrarlo aplaude ("Clap"). Si ya la dijo con Doña María,
///     solo saluda.
///
/// Escolta de Doña María (ReturnForBelongingsNPC.escort → BeginEscort):
///   1. Llega junto a ella: camina si está a la vista y cerca (≤ walkIfCloserThan); si no, desaparece con
///      fundido (DissolveFader) y reaparece a su lado, del lado visible para el jugador. Queda colgado del
///      ancla de ella (GeoAnchorManager lo mueve si recrea anclas).
///   2. Cuando el panel queda libre dice line; al cerrarlo avisa a Doña María (onLineDone: Director S5 y
///      empiezan a caminar).
///   3. La sigue de cerca hasta el punto de encuentro; ella llama EndEscort al llegar.
///
/// Animator (AC_Bombero): Triggers "Wave", "Clap", "Yell" y Float "Speed" (1 caminando, 0 quieto).
/// Sin controller (o sin el parámetro) no anima, pero el flujo funciona igual.
/// </summary>
public class FirefighterNPC : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Modelo del bombero (hijo). Se oculta hasta visibleFromStage.")]
    [SerializeField] private Transform visualRoot;
    [SerializeField] private Animator animator;
    [Tooltip("Fundido al aparecer junto a Doña María. Sin él aparece de golpe.")]
    [SerializeField] private DissolveFader fader;

    [Header("Punto de encuentro")]
    [Tooltip("Etapa desde la que se ve (1 = N1 … 4 = N4).")]
    [SerializeField] private int visibleFromStage = 3;
    [Tooltip("Su línea: al llegar el jugador al punto de encuentro, o antes si acompaña a Doña María.")]
    [SerializeField] private NpcDialogueData arrivalDialogue;
    [Tooltip("Segundos que el Director y el panel deben estar libres antes de hablar (S9 sale justo tras S7).")]
    [SerializeField] private float freeSecondsBeforeTalking = 1f;
    [SerializeField] private float lookDistance = 30f;
    [SerializeField] private float turnSpeed = 180f;

    [Header("Escolta (Doña María)")]
    [Tooltip("Si está a la vista y a menos de esta distancia horizontal (m) de ella, llega caminando;\n" +
             "si no, con fundido.")]
    [SerializeField] private float walkIfCloserThan = 10f;
    [SerializeField] private float approachSpeed = 2f;
    [Tooltip("Distancia (m) al lado de ella a la que reaparece o se detiene.")]
    [SerializeField] private float sideOffset = 1f;
    [Tooltip("Distancia (m) a la que camina detrás de ella hacia el punto de encuentro.")]
    [SerializeField] private float followDistance = 1.3f;
    [SerializeField] private float followSpeed = 1.6f;

    private enum State { Waiting, WantsToTalk, Talking, Done, Approaching, Fading, EscortWantsToTalk, EscortTalking, Following }
    private State _state = State.Waiting;
    private bool _shown;
    private bool _yelled;
    private bool _lineSaid;
    private float _freeSince = -1f;
    private Transform _camera;
    private bool _subscribedToArrival;

    [Tooltip("Solo reacciona a la llegada a un punto de encuentro a menos de esta distancia (m) de él.")]
    [SerializeField] private float meetingPointRadius = 15f;

    private Transform _companion;
    private Transform _companionAnchor;
    private Action _onLineDone;

    private static readonly int WaveParam = Animator.StringToHash("Wave");
    private static readonly int ClapParam = Animator.StringToHash("Clap");
    private static readonly int YellParam = Animator.StringToHash("Yell");
    private static readonly int SpeedParam = Animator.StringToHash("Speed");

    private void Start()
    {
        if (StageManager.Instance != null)
        {
            StageManager.Instance.OnStageChanged += OnStageChanged;
            ApplyStage(StageManager.Instance.CurrentStage);
        }
        else
        {
            Show();
        }
    }

    private void OnDestroy()
    {
        if (StageManager.Instance != null) StageManager.Instance.OnStageChanged -= OnStageChanged;
        if (_subscribedToArrival) EvacuationRouteController.ArrivedAtAnyMeetingPoint -= OnArrival;
    }

    private void OnStageChanged(StageManager.Stage previous, StageManager.Stage current) => ApplyStage(current);

    private void ApplyStage(StageManager.Stage stage)
    {
        if ((int)stage >= visibleFromStage) Show();
        else if (!_shown && visualRoot != null) visualRoot.gameObject.SetActive(false);

        if ((int)stage >= 4 && _shown && !_yelled && _state == State.Waiting)
        {
            _yelled = true;
            Trigger(YellParam);
        }
    }

    private void Show()
    {
        if (_shown) return;
        _shown = true;
        if (visualRoot == null) return;
        if (fader != null) fader.SetVisibleImmediate(true);
        else visualRoot.gameObject.SetActive(true);
    }

    // ── Escolta de Doña María ──────────────────────────────────────────────────
    /// <summary>
    /// Llamado por ReturnForBelongingsNPC al ayudar a Doña María. companion = su modelo (al piso);
    /// companionAnchor = su ancla (padre de su hotspot). onLineDone se llama al cerrar la línea del bombero.
    /// </summary>
    public void BeginEscort(Transform companion, Transform companionAnchor, Action onLineDone)
    {
        if (visualRoot == null || companion == null)
        {
            onLineDone?.Invoke();
            return;
        }

        _companion = companion;
        _companionAnchor = companionAnchor;
        _onLineDone = onLineDone;

        bool visible = _shown && visualRoot.gameObject.activeInHierarchy;
        float distance = Flat(companion.position - visualRoot.position).magnitude;
        _shown = true; // desde aquí lo controla la escolta, no la etapa

        if (visible && distance <= walkIfCloserThan)
        {
            _state = State.Approaching;
            SetSpeed(1f);
            return;
        }

        Debug.Log($"[Bombero] Doña María está a {distance:F0} m (visible: {visible}): llega con fundido.", this);
        _state = State.Fading; // quieto e ignorando OnArrival hasta reaparecer junto a ella
        if (fader != null && visible) fader.FadeOut(PlaceNextToCompanion);
        else PlaceNextToCompanion();
    }

    /// <summary>Doña María llegó al punto de encuentro: el bombero se queda ahí mirando al jugador.</summary>
    public void EndEscort()
    {
        if (_state != State.Following) return;
        _state = State.Done;
        SetSpeed(0f);
        Trigger(ClapParam);
    }

    private void PlaceNextToCompanion()
    {
        if (_camera == null && Camera.main != null) _camera = Camera.main.transform;
        Vector3 toPlayer = _camera != null ? Flat(_camera.position - _companion.position) : Vector3.zero;
        Vector3 side = toPlayer.sqrMagnitude > 0.0001f
            ? Vector3.Cross(Vector3.up, toPlayer.normalized)
            : _companion.right;

        visualRoot.SetParent(_companionAnchor, true);
        visualRoot.position = _companion.position + side * sideOffset;
        Vector3 look = Flat(_companion.position - visualRoot.position);
        if (look.sqrMagnitude > 0.0001f) visualRoot.rotation = Quaternion.LookRotation(look);

        if (fader != null) fader.FadeIn();
        else visualRoot.gameObject.SetActive(true);
        OnReachedCompanion();
    }

    private void OnReachedCompanion()
    {
        SetSpeed(0f);
        Trigger(WaveParam);
        _state = State.EscortWantsToTalk;
        _freeSince = -1f;
    }

    private void UpdateApproaching()
    {
        Vector3 target = _companion.position;
        Vector3 to = Flat(target - visualRoot.position);
        if (to.magnitude <= sideOffset + 0.1f)
        {
            visualRoot.SetParent(_companionAnchor, true);
            OnReachedCompanion();
            return;
        }
        Vector3 goal = target - to.normalized * sideOffset;
        visualRoot.position = Vector3.MoveTowards(visualRoot.position, goal, approachSpeed * Time.deltaTime);
        MeetingPointPath.Turn(visualRoot, to, turnSpeed * 2f);
    }

    private void OnEscortLineClosed()
    {
        _lineSaid = true;
        _state = State.Following;
        Action done = _onLineDone;
        _onLineDone = null;
        done?.Invoke();
    }

    private void UpdateFollowing()
    {
        Vector3 to = Flat(_companion.position - visualRoot.position);
        if (to.magnitude > followDistance)
        {
            Vector3 goal = _companion.position - to.normalized * followDistance;
            visualRoot.position = Vector3.MoveTowards(visualRoot.position, goal, followSpeed * Time.deltaTime);
            SetSpeed(1f);
        }
        else
        {
            SetSpeed(0f);
        }
        MeetingPointPath.Turn(visualRoot, to, turnSpeed);
    }

    // ── Bucle ──────────────────────────────────────────────────────────────────
    private void Update()
    {
        if (!_subscribedToArrival)
        {
            _subscribedToArrival = true;
            EvacuationRouteController.ArrivedAtAnyMeetingPoint += OnArrival;
        }

        if (!_shown) return;

        switch (_state)
        {
            case State.Approaching: UpdateApproaching(); return;
            case State.Following: UpdateFollowing(); return;
            case State.Fading: return;
            case State.WantsToTalk:
                FacePlayer();
                if (ReadyToTalk()) { _state = State.Talking; NpcDialoguePanel.Instance.Show(arrivalDialogue, null, onClosed: OnArrivalLineClosed); }
                return;
            case State.EscortWantsToTalk:
                FacePlayer();
                if (arrivalDialogue == null || NpcDialoguePanel.Instance == null) { OnEscortLineClosed(); return; }
                if (ReadyToTalk()) { _state = State.EscortTalking; NpcDialoguePanel.Instance.Show(arrivalDialogue, null, onClosed: OnEscortLineClosed); }
                return;
            default:
                FacePlayer();
                return;
        }
    }

    private bool ReadyToTalk()
    {
        if (!MeetingPointPath.CanTalkNow(NpcDialoguePanel.Instance))
        {
            _freeSince = -1f;
            return false;
        }
        if (_freeSince < 0f) _freeSince = Time.time;
        return Time.time - _freeSince >= freeSecondsBeforeTalking;
    }

    private void OnArrival(EvacuationRouteController route)
    {
        if (_state != State.Waiting && _state != State.Done) return;
        // Otro punto de encuentro (escena con varias rutas): ese tiene su propio bombero.
        Transform meeting = route != null ? route.puntoDeEncuentro : null;
        if (meeting != null)
        {
            Vector3 d = meeting.position - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude > meetingPointRadius * meetingPointRadius) return;
        }
        Show();
        Trigger(WaveParam);
        if (!_lineSaid && arrivalDialogue != null && NpcDialoguePanel.Instance != null)
        {
            _state = State.WantsToTalk;
            _freeSince = -1f;
        }
        else
        {
            _state = State.Done;
        }
    }

    private void OnArrivalLineClosed()
    {
        _lineSaid = true;
        _state = State.Done;
        Trigger(ClapParam);
    }

    private void FacePlayer()
    {
        if (visualRoot == null) return;
        if (_camera == null && Camera.main != null) _camera = Camera.main.transform;
        if (_camera == null) return;
        Vector3 toPlayer = Flat(_camera.position - visualRoot.position);
        if (toPlayer.sqrMagnitude > lookDistance * lookDistance) return;
        MeetingPointPath.Turn(visualRoot, toPlayer, turnSpeed);
    }

    // Sin controller (o sin el parámetro) no se toca el Animator: evita avisos en consola.
    private bool HasParam(int hash, AnimatorControllerParameterType type)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return false;
        foreach (AnimatorControllerParameter p in animator.parameters)
            if (p.nameHash == hash && p.type == type) return true;
        return false;
    }

    private void Trigger(int hash)
    {
        if (HasParam(hash, AnimatorControllerParameterType.Trigger)) animator.SetTrigger(hash);
    }

    private void SetSpeed(float value)
    {
        if (HasParam(SpeedParam, AnimatorControllerParameterType.Float)) animator.SetFloat(SpeedParam, value);
    }

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
