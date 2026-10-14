using SIRMED.Gameplay.Hotspots;
using SIRMED.Gameplay.NPCs;
using UnityEngine;

[RequireComponent(typeof(HotspotController))]
public class ChildSeparatedNPC : MonoBehaviour
{

    [Header("References")]
    [SerializeField] private HotspotController controller;
    [SerializeField] private Animator animator;

    [Header("Leader")]
    [SerializeField] private LeaderNpcController leader;

    private bool start = false;

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
    }

    private void OnCorrectDialogue()
    {
        start = true;
        controller.enabled = false;

        animator.SetTrigger(_Happy);

        leader.fader.FadeIn();
        leader.animator.SetFloat(_Speed, 2);
        leader.visualRoot.rotation = Quaternion.LookRotation(animator.transform.position - leader.visualRoot.position);
    }

    private void OnIncorrectDialogue()
    {
        animator.SetTrigger(_Sad);
    }

    private void Update()
    {
        if (!start)
            return;

        leader.visualRoot.position = Vector3.MoveTowards(
            leader.visualRoot.position,
            animator.transform.position,
            leader.moveSpeed * 2 * Time.deltaTime
        );

        if (Vector3.Distance(leader.visualRoot.position , animator.transform.position) < 1)
        {
            start = false;

            animator.SetTrigger(_Talk1);
            leader.animator.SetTrigger(_Talk2);
            leader.animator.SetFloat(_Speed, 0);
        }
    }
}