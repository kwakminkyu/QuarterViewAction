using System.Collections.Generic;
using UnityEngine;

// Plays a character's motion from what its gameplay components report, so the
// same component drives the player and any monster. Sits on the model next to
// the Animator; the gameplay components live on a parent.
//
// Each skill action names its own animator state (SkillAction.animationState),
// so a character plays an action only if its controller has a state of that
// name. Every action returns to locomotion through the ActionEnd trigger.
[RequireComponent(typeof(Animator))]
public sealed class CharacterAnimator : MonoBehaviour
{
    private static readonly int MoveHash = Animator.StringToHash("Move");
    private static readonly int ActionEndHash = Animator.StringToHash("ActionEnd");

    // Optional: played when health runs out, if the controller has the state.
    private static readonly int DeathHash = Animator.StringToHash("Death");

    private readonly HashSet<int> reportedMissingStates = new();

    private Animator animator;
    private SkillController skillController;
    private CharacterMovement movement;
    private Health health;
    private bool isPlayingAction;
    private bool isDead;

    private void Awake()
    {
        animator = GetComponent<Animator>();

        // All optional: a character without one simply never sends those
        // events - a training dummy has no skills, a turret never moves.
        skillController = GetComponentInParent<SkillController>();
        movement = GetComponentInParent<CharacterMovement>();
        health = GetComponentInParent<Health>();
    }

    private void OnEnable()
    {
        if (skillController != null)
        {
            skillController.ActionStarted += OnActionStarted;
            skillController.SkillEnded += OnSkillEnded;
        }

        if (movement != null)
        {
            movement.MovingChanged += OnMovingChanged;
            OnMovingChanged(movement.IsMoving);
        }

        if (health != null)
        {
            health.Depleted += OnDepleted;
        }
    }

    private void OnDisable()
    {
        if (skillController != null)
        {
            skillController.ActionStarted -= OnActionStarted;
            skillController.SkillEnded -= OnSkillEnded;
        }

        if (movement != null)
        {
            movement.MovingChanged -= OnMovingChanged;
        }

        if (health != null)
        {
            health.Depleted -= OnDepleted;
        }
    }

    // Entered by name rather than chained through triggers, so the motion
    // always matches the hitbox no matter what state the animator believes it
    // is in. Entry is an instant cut by design: combo input must respond on
    // the same frame.
    private void OnActionStarted(SkillAction action)
    {
        if (isDead || !action.HasAnimation)
        {
            return;
        }

        int stateHash = action.AnimationStateHash;

        if (!animator.HasState(0, stateHash))
        {
            if (reportedMissingStates.Add(stateHash))
            {
                Debug.LogError(
                    $"Animator has no state '{action.animationState}' " +
                    $"for action '{action.name}'.",
                    this);
            }

            return;
        }

        // A pending ActionEnd would pull the new action straight back out
        // through the AnyState return transition.
        animator.ResetTrigger(ActionEndHash);
        animator.Play(stateHash, 0, 0f);
        isPlayingAction = true;
    }

    private void OnSkillEnded()
    {
        // A skill that played nothing has nothing to return from.
        if (isDead || !isPlayingAction)
        {
            return;
        }

        isPlayingAction = false;
        animator.SetTrigger(ActionEndHash);
    }

    private void OnMovingChanged(bool moving)
    {
        animator.SetBool(MoveHash, moving);
    }

    private void OnDepleted()
    {
        // Death cancels the running skill, and that SkillEnded may arrive
        // after this; isDead keeps it from pulling the body back to idle.
        isDead = true;
        isPlayingAction = false;
        animator.ResetTrigger(ActionEndHash);

        if (animator.HasState(0, DeathHash))
        {
            animator.Play(DeathHash, 0, 0f);
        }
    }
}
