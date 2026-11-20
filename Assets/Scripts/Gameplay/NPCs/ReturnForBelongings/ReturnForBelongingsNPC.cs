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

    private enum State { Idle, Agreeing, Walking, Arrived }
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
        _state = State.Agreeing;
        _timer = agreeDelay;
    }

    private void OnIncorrectDialogue()
    {
        if (_state == State.Idle) Trigger(IncorrectParam);
    }

    private void Update()
    {
        switch (_state)
        {
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
            Debug.Log("[DonaMaria] Sin ruta ni punto de encuentro anclados: se queda donde está.", this);
            _state = State.Arrived;
            return;
        }

        Debug.Log($"[DonaMaria] Camino al punto de encuentro: {_path.Count} puntos.", this);
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
