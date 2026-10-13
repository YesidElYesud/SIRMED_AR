using SIRMED.Gameplay.Hotspots;
using UnityEngine;

[RequireComponent(typeof(HotspotController))]
public class DisorientedOlderNPC : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private HotspotController controller;
    [SerializeField] private Animator animator;
    [SerializeField] private Transform[] poins;

    [Space]
    [SerializeField] private float walkSpeed;

    private int pointIndex;
    private Transform target;
    private Vector3 direction;
    private Quaternion targetRotation;
    private bool start = false;
    private bool startRotation = false;
    private float rotationLerp;
    private float distance;
    private Vector3 targetEuler;

    private readonly int _correct = Animator.StringToHash("correct");
    private readonly int _idle = Animator.StringToHash("idle");

    private void OnEnable()
    {
        controller.correctNpcDialogue += OnCorrectDialogue;
        controller.incorrectNpcDialogue += OnIncorrectDialogue;
        DisorientedOlderNPC_SMB.OnStartWalking += StartWalking;
    }

    private void OnDisable()
    {
        controller.correctNpcDialogue -= OnCorrectDialogue;
        controller.incorrectNpcDialogue -= OnIncorrectDialogue;
        DisorientedOlderNPC_SMB.OnStartWalking -= StartWalking;
    }

    private void OnCorrectDialogue()
    {
        //Debug.LogWarning("correct Dialogue");
        animator.SetTrigger(_correct);
    }

    private void OnIncorrectDialogue()
    {
        //Debug.LogWarning("Incorrect Dialogue");
    }

    private void StartWalking()
    {
        start = true;
        startRotation = true;
        pointIndex = 0;
        rotationLerp = 0;
    }

    private void Update()
    {
        if (!start || poins == null || pointIndex >= poins.Length)
            return;

        target = poins[pointIndex];

        animator.transform.position = Vector3.MoveTowards(
            animator.transform.position,
            target.position,
            walkSpeed * Time.deltaTime
        );

        direction = poins[pointIndex].position - transform.position;
        targetRotation = Quaternion.LookRotation(direction);

        if (startRotation)
        {
            rotationLerp += Time.deltaTime * 0.06f;
            animator.transform.rotation = Quaternion.Slerp(animator.transform.rotation, targetRotation, rotationLerp);

            if (Quaternion.Angle(animator.transform.rotation, targetRotation) < 0.25f)
            {
                startRotation = false;
            }
        }

        distance = Vector3.Distance(animator.transform.position, target.position);

        if (distance <= 2)
        {
            startRotation = false;

            targetEuler = targetRotation.eulerAngles;
            targetEuler.x = 0f;
            targetEuler.z = 0f;

            animator.transform.rotation = Quaternion.Slerp(animator.transform.rotation, Quaternion.Euler(targetEuler), 1 - (distance * 0.5f));
        }

        if (distance < 0.001f)
        {
            start = false;
            animator.SetTrigger(_idle);

            pointIndex++;
            rotationLerp = 0;
            startRotation = true;
        }
    }
}