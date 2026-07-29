using System;
using UnityEngine;

public abstract class CharacterStateMachine : MonoBehaviour
{
    public CharacterState CurrentState { get; private set; }

    protected virtual void Update()
    {
        CurrentState?.Update();
    }

    public void ChangeState(CharacterState nextState)
    {
        if (nextState == null)
        {
            throw new ArgumentNullException(nameof(nextState));
        }

        if (ReferenceEquals(CurrentState, nextState))
        {
            return;
        }

        CurrentState?.Exit();
        CurrentState = nextState;
        CurrentState.Enter();
    }

    public void OnAttackContact()
    {
        CurrentState?.OnAttackContact();
    }

    public void OnAnimationFinished()
    {
        CurrentState?.OnAnimationFinished();
    }
}
