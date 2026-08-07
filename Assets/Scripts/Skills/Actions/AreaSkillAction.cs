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

    private static Vector3 ResolveDirection(
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

        return direction.sqrMagnitude <= Mathf.Epsilon
            ? Vector3.forward
            : direction.normalized;
    }
}
