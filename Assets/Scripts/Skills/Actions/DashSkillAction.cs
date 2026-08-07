using UnityEngine;

[CreateAssetMenu(fileName = "DashSkillAction", menuName = "Skills/Actions/Dash")]
public sealed class DashSkillAction : SkillAction
{
    [Min(0f)] public float distance = 5f;

    public override void OnActiveEnter(
        in SkillActionContext context)
    {
        context.DamageReceiver.SetInvulnerable(true);
    }

    public override void OnActiveUpdate(
        in SkillActionContext context)
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

        if (direction.sqrMagnitude <= Mathf.Epsilon)
        {
            return;
        }

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

        context.Movement.MoveBySkill(
            direction.normalized * frameSpeed);
    }

    public override void OnSkillEnd(
        in SkillActionContext context)
    {
        context.DamageReceiver.SetInvulnerable(false);
    }
}
