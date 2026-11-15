using SIRMED.Gameplay.Hotspots;
using SIRMED.Gameplay.NPCs;
using UnityEngine;

/// <summary>
/// Perro (ayuda comunitaria). Al responder bien en su hotspot camina solo por la ruta de
/// evacuación hasta el punto de encuentro (MeetingPointPath) y allí se queda mirando al jugador.
///
/// Antes seguía al teléfono con un desfase fijo: subía y bajaba con el teléfono y giraba con
/// Lerp sobre ángulos de Euler (vuelta completa al pasar de 359° a 0°).
///
/// El modelo no tiene animaciones (Perro.fbx sin clips, Animator sin controller): si se le
/// asigna un controller con un float "Speed", se pone a 1 al caminar y a 0 al detenerse.
/// </summary>
public class DogNPC : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private HotspotController controller;
    [SerializeField] private Animator animator;
    [Tooltip("Cámara del jugador: el perro la mira al llegar. Si está vacío usa Camera.main.")]
    [SerializeField] private Transform mainCamera;

    [Space]
    [Tooltip("Solo si la escena no tiene ruta de evacuación anclada: destino directo.")]
    [SerializeField] private Transform safeSpace;

    [Header("Movimiento")]
    [SerializeField] private float walkSpeed = 1.3f;
    [SerializeField] private float turnSpeed = 240f;

    private enum State { Idle, Walking, Arrived }
    private State _state = State.Idle;

    private readonly MeetingPointPath _path = new MeetingPointPath();
    private static readonly int SpeedParam = Animator.StringToHash("Speed");

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
        controller.KeepVisible = true; // que el cambio de nivel no lo esconda a mitad de camino

        Transform model = animator.transform;
        float groundOffset = model.position.y - controller.transform.position.y;
        Vector3[] fallback = safeSpace != null ? new[] { safeSpace.position } : null;

        if (!_path.Build(model.position, groundOffset, fallback))
        {
            Debug.Log("[Perro] Sin ruta ni punto de encuentro anclados: se queda donde está.", this);
            _state = State.Arrived;
            return;
        }

        Debug.Log($"[Perro] Camino al punto de encuentro: {_path.Count} puntos.", this);
        _state = State.Walking;
        SetSpeed(1f);
    }

    private void OnIncorrectDialogue() { }

    private void Update()
    {
        switch (_state)
        {
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
                if (mainCamera == null && Camera.main != null) mainCamera = Camera.main.transform;
                if (mainCamera != null)
                    MeetingPointPath.Turn(animator.transform, mainCamera.position - animator.transform.position, turnSpeed);
                break;
        }
    }

    private void SetSpeed(float value)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        foreach (AnimatorControllerParameter p in animator.parameters)
            if (p.nameHash == SpeedParam && p.type == AnimatorControllerParameterType.Float)
            {
                animator.SetFloat(SpeedParam, value);
                return;
            }
    }
}
