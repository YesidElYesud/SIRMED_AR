using SIRMED.Gameplay.Hotspots;
using System;
using UnityEngine;

public class DogNPC : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private HotspotController controller;
    [SerializeField] private Animator animator;
    [SerializeField] private Transform mainCamera;

    [Space]
    [SerializeField] private Transform safeSpace;

    private bool start = false;
    private Vector3 positionOffset;

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

        animator.transform.SetParent(null);
        positionOffset = animator.transform.position - mainCamera.position;
    }

    private void OnIncorrectDialogue()
    {
        //throw new NotImplementedException();
    }

    private void Update()
    {
        if (!start)
            return;

        animator.transform.position = Vector3.Lerp(animator.transform.position, mainCamera.position + positionOffset, Time.deltaTime * 3);

        Vector3 euler = animator.transform.eulerAngles;
        euler.y = mainCamera.eulerAngles.y;
        animator.transform.eulerAngles = Vector3.Lerp(animator.transform.eulerAngles, euler, Time.deltaTime * 2);

        if (Vector3.Distance(animator.transform.position, safeSpace.position) < 3)
        {
            start = false;
        }
    }
}