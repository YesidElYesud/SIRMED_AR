using System;
using UnityEngine;

public class DisorientedOlderNPC_SMB : StateMachineBehaviour
{
    public static Action OnStartWalking;

    private readonly int _Walking = Animator.StringToHash("Walking");

    override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        if (_Walking == stateInfo.tagHash)
        {
            OnStartWalking?.Invoke();
        }
    }
}