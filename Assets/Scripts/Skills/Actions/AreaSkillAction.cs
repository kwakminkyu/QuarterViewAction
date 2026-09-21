using UnityEngine;

[CreateAssetMenu(fileName = "AreaSkillAction", menuName = "Skills/Actions/Area")]
public sealed class AreaSkillAction : SkillAction
{
    public AreaAttackData attackData;
    public Vector3 spawnOffset;

    private AreaAttack areaAttack;

    public override void OnActiveEnter(
        in SkillActionContext context)
    {
        if (attackData == null)
        {
            Debug.LogError(
                $"{name} has no area attack data.",
                this);
            return;
        }

        Vector3 direction = ResolveDirection(in context);
        Quaternion rotation = Quaternion.LookRotation(
            direction,
            Vector3.up);
        Vector3 spawnOrigin = context.Target == null
            ? context.User.transform.position
            : context.Target.position;
        Vector3 position = spawnOrigin +
            rotation * spawnOffset;
        var attackContext = new AttackContext(
            context.User,
            attackData,
            attackData.payload);

        areaAttack ??= new AreaAttack();
        areaAttack.Execute(
            in attackContext,
            attackData,
            position,
            rotation);
    }
}
