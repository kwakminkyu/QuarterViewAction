using UnityEngine;

[CreateAssetMenu(fileName = "DashSkillAction", menuName = "Skills/Actions/Dash")]
public sealed class DashSkillAction : SkillAction
{
    public override void OnActiveEnter(
        in SkillActionContext context)
    {
        context.DamageReceiver.SetInvulnerable(true);
    }

    public override void OnSkillEnd(
        in SkillActionContext context)
    {
        context.DamageReceiver.SetInvulnerable(false);
    }
}
