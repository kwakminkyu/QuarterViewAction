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
        Vector3 direction = ResolveDirection(in context);
        ApplyLunge(in context, direction, distance);
    }

    public override void OnSkillEnd(
        in SkillActionContext context)
    {
        context.DamageReceiver.SetInvulnerable(false);
    }
}
