using UnityEngine;

public abstract class SkillAction : ScriptableObject
{
    [Min(0f)] public float startupDuration;
    [Min(0f)] public float activeDuration;
    [Min(0f)] public float recoveryDuration;

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
        float distance)
    {
        if (activeDuration <= Mathf.Epsilon ||
            distance <= Mathf.Epsilon ||
            context.DeltaTime <= Mathf.Epsilon ||
            Time.deltaTime <= Mathf.Epsilon)
        {
            return;
        }

        float activeDistance =
            distance * context.DeltaTime / activeDuration;
        float frameSpeed = activeDistance / Time.deltaTime;

        context.Movement.MoveBySkill(direction.normalized * frameSpeed);
    }
}
