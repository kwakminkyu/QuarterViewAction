using UnityEngine;

[CreateAssetMenu(fileName = "ProjectileSkillAction", menuName = "Skills/Actions/Projectile")]
public sealed class ProjectileSkillAction : SkillAction
{
    public ProjectileAttackData attackData;
    public Vector3 spawnOffset =
        new Vector3(0f, 1f, 0.5f);

    private ProjectileAttack projectileAttack;

    public override void OnActiveEnter(
        in SkillActionContext context)
    {
        if (attackData == null)
        {
            Debug.LogError(
                $"{name} has no projectile attack data.",
                this);
            return;
        }

        Vector3 direction = ResolveDirection(in context);
        Quaternion rotation = Quaternion.LookRotation(
            direction,
            Vector3.up);
        Vector3 spawnPosition = context.User.transform.position +
            rotation * spawnOffset;
        var attackContext = new AttackContext(
            context.User,
            attackData,
            attackData.payload);

        projectileAttack ??= new ProjectileAttack();
        projectileAttack.Execute(
            in attackContext,
            attackData,
            spawnPosition,
            direction);
    }

    private static Vector3 ResolveDirection(
        in SkillActionContext context)
    {
        Vector3 direction = context.Direction;

        if (direction.sqrMagnitude <= Mathf.Epsilon &&
            context.Target != null)
        {
            direction = context.Target.position -
                context.User.transform.position;
        }

        if (direction.sqrMagnitude <= Mathf.Epsilon)
        {
            direction = context.User.transform.forward;
        }

        return direction.sqrMagnitude <= Mathf.Epsilon
            ? Vector3.forward
            : direction.normalized;
    }
}
