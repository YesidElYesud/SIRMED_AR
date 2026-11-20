using SIRMED.Gameplay.Hotspots;
using SIRMED.Gameplay.NPCs;
using UnityEngine;

/// <summary>
/// Doña María (vecina que insiste en devolverse a la casa por sus cosas, N4; modelo VecinoUno).
/// Flujo del documento de diálogos (personaje 4, allí "Don Hernán"):
///   1. En el hotspot, dos preguntas encadenadas (NpcDialogueData.nextOnCorrect): "Se me quedó el
///      televisor…" y "Pero es el esfuerzo de años…". Al responder bien la segunda, el Director da el
///      consejo S5 (isCommunityHelp en el primer diálogo, sin deferDirectorAdvice).
///   2. Ella asiente (trigger "Yes") y, pasados agreeDelay segundos, camina sola por la ruta de
///      evacuación hasta el punto de encuentro (MeetingPointPath); allí se queda mirando al jugador.
///   Respuesta incorrecta: trigger "Incorrect" (se escabulle hacia la casa y vuelve a su sitio).
///
/// Animator (AC_DonaMaria): Float "Speed" (1 caminando, 0 detenida), Trigger "Yes", Trigger "Incorrect".
/// Sin controller asignado no anima, pero el flujo funciona igual.
///
/// También lo usa Carlos (vecino que se acerca a la quebrada, N3; modelo VecinoDos): mismo flujo con tres
/// preguntas encadenadas ("Ayuda Carlos" → 2 → 3) y su propio controller (AC_Carlos) con los mismos parámetros.
///
/// Con escort asignado (Doña María → bombero): al responder bien la cadena el bombero llega a su lado
/// (FirefighterNPC.BeginEscort), dice "Hizo lo correcto quedándose en la ruta…", y al cerrarlo el Director
/// da S5 (el primer diálogo debe tener deferDirectorAdvice) y ella camina al punto de encuentro con el
/// bombero detrás. Sin escort (Carlos) el flujo es el de arriba.
/// </summary>
[RequireComponent(typeof(HotspotController))]
public class ReturnForBelongingsNPC : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private HotspotController controller;
    [SerializeField] private Animator animator;

    [Header("Movimiento")]
    [SerializeField] private float walkSpeed = 1.2f;
    [SerializeField] private float turnSpeed = 200f;
    [Tooltip("Segundos entre el gesto de \"sí\" y el primer paso hacia el punto de encuentro.")]
    [SerializeField] private float agreeDelay = 2f;

    [Header("Bombero (opcional)")]
    [Tooltip("Si se asigna, el bombero llega, dice su línea y la acompaña al punto de encuentro.\n" +
             "El primer diálogo debe tener deferDirectorAdvice: S5 sale después de la línea del bombero.")]
    [SerializeField] private FirefighterNPC escort;
    [Tooltip("Segundos entre la línea del bombero y el primer paso.")]
    [SerializeField] private float afterEscortDelay = 0.5f;

    private enum State { Idle, WaitingEscort, Agreeing, Walking, Arrived }
    private State _state = State.Idle;
    private float _timer;
    private Transform _camera;

    private readonly MeetingPointPath _path = new MeetingPointPath();
    private static readonly int SpeedParam = Animator.StringToHash("Speed");
    private static readonly int YesParam = Animator.StringToHash("Yes");
    private static readonly int IncorrectParam = Animator.StringToHash("Incorrect");

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
        if (_state != State.Idle) return;
        controller.enabled = false;
        controller.KeepVisible = true; // el hotspot es solo N4: que no se oculte a mitad de camino
        // HotspotsActivator activa por raycast contra el Collider aunque el controller esté deshabilitado.
        if (controller.TryGetComponent(out Collider tapCollider)) tapCollider.enabled = false;

        Trigger(YesParam);
        if (escort != null)
        {
            _state = State.WaitingEscort;
            escort.BeginEscort(animator.transform, controller.transform.parent, OnEscortLineDone);
            return;
        }
        _state = State.Agreeing;
        _timer = agreeDelay;
    }

    private void OnEscortLineDone()
    {
        SIRMED.Gameplay.Director.DirectorAdviceController.Instance?.NotifyCommunityHelpCorrect();
        _state = State.Agreeing;
        _timer = afterEscortDelay;
    }

    private void OnIncorrectDialogue()
    {
        if (_state == State.Idle) Trigger(IncorrectParam);
    }

    private void Update()
    {
        switch (_state)
        {
            case State.WaitingEscort:
                FacePlayer();
                break;

            case State.Agreeing:
                FacePlayer();
                _timer -= Time.deltaTime;
                if (_timer <= 0f) StartWalking();
                break;

            case State.Walking:
                _path.Step(animator.transform, walkSpeed, turnSpeed);
                if (_path.Finished)
                {
                    _state = State.Arrived;
                    _path.Unsubscribe();
                    SetSpeed(0f);
                    if (escort != null) escort.EndEscort();
                }
                break;

            case State.Arrived:
                FacePlayer();
                break;
        }
    }

    private void StartWalking()
    {
        Transform model = animator.transform;
        float groundOffset = model.position.y - controller.transform.position.y;
        if (!_path.Build(model.position, groundOffset))
        {
            Debug.Log($"[{name}] Sin ruta ni punto de encuentro anclados: se queda donde está.", this);
            _state = State.Arrived;
            if (escort != null) escort.EndEscort();
            return;
        }

        Debug.Log($"[{name}] Camino al punto de encuentro: {_path.Count} puntos.", this);
        _state = State.Walking;
        SetSpeed(1f);
    }

    private void FacePlayer()
    {
        if (_camera == null && Camera.main != null) _camera = Camera.main.transform;
        if (_camera != null)
            MeetingPointPath.Turn(animator.transform, _camera.position - animator.transform.position, turnSpeed);
    }

    // Sin controller (o sin el parámetro) no se toca el Animator: evita avisos en consola.
    private bool HasParam(int hash, AnimatorControllerParameterType type)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return false;
        foreach (AnimatorControllerParameter p in animator.parameters)
            if (p.nameHash == hash && p.type == type) return true;
        return false;
    }

    private void SetSpeed(float value)
    {
        if (HasParam(SpeedParam, AnimatorControllerParameterType.Float)) animator.SetFloat(SpeedParam, value);
    }

    private void Trigger(int hash)
    {
        if (HasParam(hash, AnimatorControllerParameterType.Trigger)) animator.SetTrigger(hash);
    }
}
