using System.Collections.Generic;
using SIRMED.Gameplay.Dialogue;
using SIRMED.Gameplay.Hotspots;
using SIRMED.Gameplay.NPCs;
using SIRMED.UI;
using UnityEngine;

/// <summary>
/// Don Aurelio (persona mayor desorientada, N3). Flujo del documento de diálogos:
///   1. El jugador responde bien en el hotspot → el NPC hace el gesto "correct" y empieza a
///      caminar por la ruta de evacuación hacia el punto de encuentro.
///   2. A mitad de camino se detiene y, cuando el jugador está cerca, hace la segunda pregunta
///      (walkingDialogue: "¿Vamos despacio?"). Respondida bien, sigue caminando más despacio
///      ("Vamos a su ritmo, sin afán").
///   3. En el punto de encuentro espera al jugador, dice arrivalDialogue ("Llegamos…") y al
///      cerrarlo el Director da el consejo S5 de ayuda comunitaria.
///
/// El camino sale de MeetingPointPath (waypoints de la ruta + punto de encuentro); 'poins' solo
/// se usa si la escena no tiene ruta. No habla mientras el Director está hablando.
/// </summary>
[RequireComponent(typeof(HotspotController))]
public class DisorientedOlderNPC : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private HotspotController controller;
    [SerializeField] private Animator animator;
    [Tooltip("Solo si la escena no tiene EvacuationRouteController: puntos hasta el punto de encuentro.")]
    [SerializeField] private Transform[] poins;

    [Space]
    [SerializeField] private float walkSpeed = 1.15f;
    [Tooltip("Velocidad (m/s) tras responder bien \"¿Vamos despacio?\". La animación se frena en la misma proporción.")]
    [SerializeField] private float slowWalkSpeed = 0.75f;
    [SerializeField] private float turnSpeed = 180f;

    [Header("Acompañamiento")]
    [Tooltip("Segunda pregunta, a mitad de camino (\"¿Vamos despacio?\").")]
    [SerializeField] private NpcDialogueData walkingDialogue;
    [Tooltip("Línea al llegar al punto de encuentro (\"Llegamos…\").")]
    [SerializeField] private NpcDialogueData arrivalDialogue;
    [Tooltip("Distancia horizontal (m) a la que el jugador tiene que estar para que el NPC le hable.")]
    [SerializeField] private float talkDistance = 5f;

    private enum State { Idle, Starting, Walking, WaitingToTalk, Talking, Done }
    private State _state = State.Idle;

    private readonly MeetingPointPath _path = new MeetingPointPath();
    private bool _pathBuilt;
    private int _talkAtIndex = -1;
    private bool _walkingTalkDone;
    private bool _arrived;
    private Transform _camera;

    private readonly int _correct = Animator.StringToHash("correct");
    private readonly int _incorrect = Animator.StringToHash("incorrect");
    private readonly int _idle = Animator.StringToHash("idle");

    private void OnEnable()
    {
        controller.correctNpcDialogue += OnCorrectDialogue;
        controller.incorrectNpcDialogue += OnIncorrectDialogue;
        _path.Repathed += OnRepathed;
    }

    private void OnDisable()
    {
        controller.correctNpcDialogue -= OnCorrectDialogue;
        controller.incorrectNpcDialogue -= OnIncorrectDialogue;
        DisorientedOlderNPC_SMB.OnStartWalking -= StartWalking;
        _path.Repathed -= OnRepathed;
        _path.Unsubscribe();
    }

    // ── Respuestas en el hotspot ────────────────────────────────────────────────
    private void OnCorrectDialogue()
    {
        controller.enabled = false;
        controller.KeepVisible = true; // el hotspot es solo N3: que no se oculte a mitad de camino
        PlayCorrectThenWalk();
    }

    private void OnIncorrectDialogue()
    {
        animator.SetTrigger(_incorrect);
    }

    // "correct" → animación → estado con tag Walking → el SMB avisa y empieza a caminar.
    private void PlayCorrectThenWalk()
    {
        _state = State.Starting;
        DisorientedOlderNPC_SMB.OnStartWalking -= StartWalking;
        DisorientedOlderNPC_SMB.OnStartWalking += StartWalking;
        animator.SetTrigger(_correct);
    }

    private void StartWalking()
    {
        DisorientedOlderNPC_SMB.OnStartWalking -= StartWalking;
        if (!_pathBuilt) BuildPath();
        if (_path.Finished) { _arrived = true; StopAndWait(); return; }
        _state = State.Walking;
        animator.speed = _walkingTalkDone && walkSpeed > 0f ? slowWalkSpeed / walkSpeed : 1f;
    }

    private void BuildPath()
    {
        _pathBuilt = true;
        float groundOffset = animator.transform.position.y - controller.transform.position.y;

        List<Vector3> fallback = new List<Vector3>();
        if (poins != null)
            foreach (Transform p in poins)
                if (p != null) fallback.Add(p.position);

        _path.Build(animator.transform.position, groundOffset, fallback);
        SetTalkIndex();
        Debug.Log($"[DonAurelio] Camino al punto de encuentro: {_path.Count} puntos, pregunta en el {_talkAtIndex}.", this);
    }

    // La segunda pregunta va en el punto del medio (si hay al menos dos tramos).
    private void SetTalkIndex() =>
        _talkAtIndex = !_walkingTalkDone && walkingDialogue != null && _path.Count >= 2 ? _path.Count / 2 : -1;

    private void OnRepathed() => SetTalkIndex();

    // ── Movimiento y conversación ──────────────────────────────────────────────
    private void Update()
    {
        switch (_state)
        {
            case State.Walking: UpdateWalking(); break;
            case State.WaitingToTalk: UpdateWaitingToTalk(); break;
        }
    }

    private void UpdateWalking()
    {
        float speed = _walkingTalkDone ? slowWalkSpeed : walkSpeed;
        if (!_path.Step(animator.transform, speed, turnSpeed)) return;

        if (_path.Finished)
        {
            _arrived = true;
            StopAndWait();
        }
        else if (_path.Index == _talkAtIndex && !_walkingTalkDone)
        {
            StopAndWait();
        }
    }

    private void StopAndWait()
    {
        animator.speed = 1f;
        animator.SetTrigger(_idle);
        _state = State.WaitingToTalk;
    }

    private void UpdateWaitingToTalk()
    {
        if (_camera == null && Camera.main != null) _camera = Camera.main.transform;
        if (_camera == null) return;

        MeetingPointPath.Turn(animator.transform, _camera.position - animator.transform.position, turnSpeed);

        Vector3 d = _camera.position - animator.transform.position;
        if (new Vector2(d.x, d.z).magnitude > talkDistance) return;

        NpcDialoguePanel panel = NpcDialoguePanel.Instance;
        if (panel == null) { FinishWithoutPanel(); return; }
        if (!MeetingPointPath.CanTalkNow(panel)) return;

        _state = State.Talking;
        if (_arrived)
        {
            if (arrivalDialogue == null) { Finish(); return; }
            panel.Show(arrivalDialogue, null, onClosed: Finish);
        }
        else
        {
            panel.Show(walkingDialogue, null,
                onCorrect: () => _walkingTalkDone = true,
                onIncorrect: () => animator.SetTrigger(_incorrect),
                onClosed: OnWalkingDialogueClosed);
        }
    }

    private void OnWalkingDialogueClosed()
    {
        // Cerrado sin responder bien (p. ej. otro panel lo cerró): vuelve a esperar al jugador.
        if (!_walkingTalkDone) { _state = State.WaitingToTalk; return; }
        PlayCorrectThenWalk();
    }

    private void FinishWithoutPanel()
    {
        if (_arrived) Finish();
        else { _walkingTalkDone = true; PlayCorrectThenWalk(); }
    }

    private void Finish()
    {
        _state = State.Done;
        _path.Unsubscribe();
        SIRMED.Gameplay.Director.DirectorAdviceController.Instance?.NotifyCommunityHelpCorrect();
    }
}
