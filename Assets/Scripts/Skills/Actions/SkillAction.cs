using System;
using UnityEngine;

[Flags]
public enum SkillMovementPhases
{
    None = 0,
    Startup = 1 << 0,
    Active = 1 << 1,
    Recovery = 1 << 2,
}

public enum SkillMovementMode
{
    FixedLunge,
    SpeedMultiplier,
}

[Serializable]
public struct SkillMovementSettings
{
    public SkillMovementPhases phases;
    public SkillMovementMode mode;
    [Min(0f)] public float lungeDistance;
    [Min(0f)] public float moveSpeedMultiplier;
}

public abstract class SkillAction : ScriptableObject
{
    [Min(0f)] public float startupDuration;
    [Min(0f)] public float activeDuration;
    [Min(0f)] public float recoveryDuration;
    [Min(0f)] public float recoveryCancelDelay;

    public SkillMovementSettings movementSettings;

    public void ApplyPhaseMovement(
        SkillPhase phase,
        in SkillActionContext context,
        float phaseDuration)
    {
        SkillMovementPhases flag = phase switch
        {
            SkillPhase.Startup => SkillMovementPhases.Startup,
            SkillPhase.Active => SkillMovementPhases.Active,
            SkillPhase.Recovery => SkillMovementPhases.Recovery,
            _ => SkillMovementPhases.None,
        };

        if ((movementSettings.phases & flag) == 0)
        {
            return;
        }

        switch (movementSettings.mode)
        {
            case SkillMovementMode.FixedLunge:
                ApplyLunge(
                    in context,
                    ResolveDirection(in context),
                    movementSettings.lungeDistance,
                    phaseDuration);
                break;

            case SkillMovementMode.SpeedMultiplier:
                context.Movement.MoveBySkillFollowingInput(
                    movementSettings.moveSpeedMultiplier);
                break;
        }
    }

    public virtual void OnActiveEnter(in SkillActionContext context)
    {
    }

    public virtual void OnActiveUpdate(in SkillActionContext context)
    {
    }

    public virtual void OnActiveExit(in SkillActionContext context)
    {
    }

    public virtual void OnSkillEnd(in SkillActionContext context)
    {
    }

    protected static Vector3 ResolveDirection(in SkillActionContext context)
    {
        Vector3 direction = context.Direction;
        direction.y = 0f;

        if (direction.sqrMagnitude <= Mathf.Epsilon &&
            context.Target != null)
        {
            direction = context.Target.position -
                context.User.transform.position;
            direction.y = 0f;
        }

        if (direction.sqrMagnitude <= Mathf.Epsilon)
        {
            direction = context.User.transform.forward;
            direction.y = 0f;
        }

        return direction.sqrMagnitude <= Mathf.Epsilon
            ? Vector3.forward
            : direction.normalized;
    }

    protected void ApplyLunge(
        in SkillActionContext context,
        Vector3 direction,
        float distance,
        float phaseDuration)
    {
        if (phaseDuration <= Mathf.Epsilon ||
            distance <= Mathf.Epsilon ||
            context.DeltaTime <= Mathf.Epsilon ||
            Time.deltaTime <= Mathf.Epsilon)
        {
            return;
        }

        float phaseDistance =
            distance * context.DeltaTime / phaseDuration;
        float frameSpeed = phaseDistance / Time.deltaTime;

        context.Movement.MoveBySkill(direction.normalized * frameSpeed);
    }
}
