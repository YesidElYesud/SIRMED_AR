using SIRMED.Gameplay.Dialogue;
using SIRMED.Gameplay.Hotspots;
using SIRMED.Gameplay.NPCs;
using SIRMED.UI;
using UnityEngine;

/// <summary>
/// Samuel (niño separado de su acompañante, N3). Flujo del documento de diálogos:
///   1. En el hotspot, dos preguntas encadenadas (NpcDialogueData.nextOnCorrect).
///   2. Respondidas bien → el niño se alegra y llega la líder: si está cerca (≤ runIfCloserThan)
///      corre hasta él; si está lejos (en varias escenas a 40–200 m) desaparece con un fundido
///      y reaparece junto al niño, del lado visible para el jugador.
///   3. En cuanto el panel queda libre, la líder dice leaderDialogue ("Yo me encargo…"); al
///      cerrarlo el Director da el consejo S5.
///   4. Niño y líder caminan juntos por la ruta hasta el punto de encuentro.
/// </summary>
[RequireComponent(typeof(HotspotController))]
public class ChildSeparatedNPC : MonoBehaviour
{

    [Header("References")]
    [SerializeField] private HotspotController controller;
    [SerializeField] private Animator animator;

    [Header("Leader")]
    [SerializeField] private LeaderNpcController leader;
    [Tooltip("Si la líder está a más de esta distancia horizontal (m) del niño, llega con un fundido\n" +
             "en vez de correr (no atraviesa casas ni hace esperar al jugador).")]
    [SerializeField] private float runIfCloserThan = 8f;
    [Tooltip("Distancia (m) al lado del niño a la que reaparece la líder tras el fundido.")]
    [SerializeField] private float leaderSideOffset = 1f;

    [Header("Cierre y camino al punto de encuentro")]
    [Tooltip("Línea de la líder al llegar (\"Yo me encargo, ya estamos ubicando a tu mamá…\").\n" +
             "Sale apenas se cierra el diálogo del niño, sin importar la distancia: desde el 29-oct los\n" +
             "hotspots se activan tocándolos hasta a 25 m (HotspotsActivator), no por cercanía.")]
    [SerializeField] private NpcDialogueData leaderDialogue;
    [SerializeField] private float walkSpeed = 1.1f;
    [Tooltip("Distancia (m) a la que la líder camina detrás del niño.")]
    [SerializeField] private float followDistance = 1.2f;
    [SerializeField] private float turnSpeed = 240f;

    private enum State { Idle, LeaderComing, WaitingToTalk, Talking, Walking, Done }
    private State _state = State.Idle;

    private readonly MeetingPointPath _path = new MeetingPointPath();
    private Transform _camera;

    private readonly int _Happy = Animator.StringToHash("Happy");
    private readonly int _Sad = Animator.StringToHash("Sad");
    private readonly int _Speed = Animator.StringToHash("Speed");
    private readonly int _Talk1 = Animator.StringToHash("Talk1");
    private readonly int _Talk2 = Animator.StringToHash("Talk2");

    private void OnEnable()
    {
        controller.correctNpcDialogue += OnCorrectDialogue;
        controller.incorrectNpcDialogue += OnIncorrectDialogue;
    }

    private void OnDisable()
    {
        controller.correctNpcDialogue -= OnCorrectDialogue;
        controller.incorrectNpcDialogue -= OnIncorrectDialogue;
        _path.Unsubscribe();
    }

    private void OnCorrectDialogue()
    {
        // Ya ayudado: tocarlo otra vez reabría el diálogo y reiniciaba todo el flujo.
        if (_state != State.Idle) return;

        controller.enabled = false;
        controller.KeepVisible = true;
        // HotspotsActivator activa por raycast contra el Collider, aunque el controller esté deshabilitado.
        if (controller.TryGetComponent(out Collider tapCollider)) tapCollider.enabled = false;

        animator.SetTrigger(_Happy);

        if (leader == null) { _state = State.WaitingToTalk; return; }

        _state = State.LeaderComing;
        leader.ExternallyControlled = true;
        // Si el jugador no la había llamado, desde ahora cuenta como aparecida: su moneda no
        // debe seguir activa (al tocarla se teletransportaba a la parada y dejaba solo al niño).
        leader.MarkSpawnedByEscort();

        Transform root = leader.visualRoot;
        bool visible = root.gameObject.activeInHierarchy;
        float distance = Flat(animator.transform.position - root.position).magnitude;

        if (visible && distance <= runIfCloserThan)
        {
            // Cerca y a la vista: llega corriendo.
            leader.animator.SetFloat(_Speed, 2);
            root.rotation = Quaternion.LookRotation(Flat(animator.transform.position - root.position));
            return;
        }

        // Lejos u oculta: fundido de salida (si se veía) → reaparece junto al niño.
        Debug.Log($"[Samuel] La líder está a {distance:F0} m (visible: {visible}): llega con fundido.", this);
        if (leader.fader != null)
        {
            if (visible) leader.fader.FadeOut(PlaceLeaderNextToChild);
            else PlaceLeaderNextToChild();
        }
        else
        {
            PlaceLeaderNextToChild();
        }
    }

    private void PlaceLeaderNextToChild()
    {
        Transform root = leader.visualRoot;
        Transform child = animator.transform;

        // Al lado del niño, perpendicular a la línea niño→jugador: los dos quedan a la vista.
        if (_camera == null && Camera.main != null) _camera = Camera.main.transform;
        Vector3 toPlayer = _camera != null ? Flat(_camera.position - child.position) : Vector3.zero;
        Vector3 side = toPlayer.sqrMagnitude > 0.0001f
            ? Vector3.Cross(Vector3.up, toPlayer.normalized)
            : child.right;

        // Colgarla del ancla del niño para que se mueva con él si ARCore corrige la pose.
        Transform anchor = controller.transform.parent;
        root.SetParent(anchor != null ? anchor : null, true);
        root.position = child.position + side * leaderSideOffset;
        root.rotation = Quaternion.LookRotation(Flat(child.position - root.position).sqrMagnitude > 0.0001f
            ? Flat(child.position - root.position)
            : child.forward);

        leader.animator.SetFloat(_Speed, 0);
        if (leader.fader != null) leader.fader.FadeIn();
        else root.gameObject.SetActive(true);
        OnLeaderArrived();
    }

    private void OnLeaderArrived()
    {
        animator.SetTrigger(_Talk1);
        leader.animator.SetTrigger(_Talk2);
        leader.animator.SetFloat(_Speed, 0);
        _state = State.WaitingToTalk;
    }

    private void OnIncorrectDialogue()
    {
        animator.SetTrigger(_Sad);
    }

    private void Update()
    {
        switch (_state)
        {
            case State.LeaderComing: UpdateLeaderComing(); break;
            case State.WaitingToTalk: UpdateWaitingToTalk(); break;
            case State.Walking: UpdateWalking(); break;
        }
    }

    private void UpdateLeaderComing()
    {
        leader.visualRoot.position = Vector3.MoveTowards(
            leader.visualRoot.position,
            animator.transform.position,
            leader.moveSpeed * 2 * Time.deltaTime
        );

        if (Vector3.Distance(leader.visualRoot.position, animator.transform.position) < 1)
            OnLeaderArrived();
    }

    private void UpdateWaitingToTalk()
    {
        NpcDialoguePanel panel = NpcDialoguePanel.Instance;
        if (panel != null && !MeetingPointPath.CanTalkNow(panel)) return;

        _state = State.Talking;
        if (panel != null && leaderDialogue != null)
            panel.Show(leaderDialogue, null, onClosed: OnLeaderDialogueClosed);
        else
            OnLeaderDialogueClosed();
    }

    private void OnLeaderDialogueClosed()
    {
        SIRMED.Gameplay.Director.DirectorAdviceController.Instance?.NotifyCommunityHelpCorrect();
        StartWalkingToMeetingPoint();
    }

    // ── Camino al punto de encuentro ────────────────────────────────────────────
    private void StartWalkingToMeetingPoint()
    {
        float groundOffset = animator.transform.position.y - controller.transform.position.y;
        if (!_path.Build(animator.transform.position, groundOffset))
        {
            Debug.Log("[Samuel] Sin ruta ni punto de encuentro anclados: se queda donde está.", this);
            EndWalk();
            return;
        }

        _state = State.Walking;
        animator.applyRootMotion = false;
        animator.SetFloat(_Speed, 1);
        if (leader != null) leader.animator.SetFloat(_Speed, 1);
        Debug.Log($"[Samuel] Camino al punto de encuentro: {_path.Count} puntos.", this);
    }

    private void UpdateWalking()
    {
        Transform child = animator.transform;
        _path.Step(child, walkSpeed, turnSpeed);

        if (leader != null)
        {
            // La líder va detrás del niño, a la misma altura (los dos al piso).
            Transform lider = leader.visualRoot;
            Vector3 toChild = child.position - lider.position;
            if (Flat(toChild).magnitude > followDistance)
            {
                Vector3 goal = child.position - Flat(toChild).normalized * followDistance;
                lider.position = Vector3.MoveTowards(lider.position, goal, walkSpeed * 1.2f * Time.deltaTime);
            }
            Turn(lider, toChild);
        }

        if (_path.Finished) EndWalk();
    }

    private void EndWalk()
    {
        _state = State.Done;
        _path.Unsubscribe();
        animator.SetFloat(_Speed, 0);
        if (leader != null)
        {
            leader.animator.SetFloat(_Speed, 0);
            leader.ExternallyControlled = false;
        }
    }

    private void Turn(Transform t, Vector3 dir) => MeetingPointPath.Turn(t, dir, turnSpeed);

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
