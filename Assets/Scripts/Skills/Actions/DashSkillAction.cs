using UnityEngine;

[CreateAssetMenu(fileName = "DashSkillAction",menuName = "Skills/Actions/Dash")]
public sealed class DashSkillAction : SkillAction
{
    [SerializeField, Min(0f)] private float distance = 5f;

    public float Distance => Mathf.Max(0f, distance);

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

        if (ActiveDuration <= Mathf.Epsilon ||
            Distance <= Mathf.Epsilon ||
            context.DeltaTime <= Mathf.Epsilon ||
            Time.deltaTime <= Mathf.Epsilon)
        {
            return;
        }

        float activeDistance =
            Distance * context.DeltaTime / ActiveDuration;
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
