using UnityEngine;

[CreateAssetMenu(fileName = "RaycastSkillAction", menuName = "Skills/Actions/Raycast")]
public sealed class RaycastSkillAction : SkillAction
{
    public RaycastAttackData attackData;
    public Vector3 originOffset =
        new Vector3(0f, 1f, 0.5f);

    private RaycastAttack raycastAttack;

    public override void OnActiveEnter(
        in SkillActionContext context)
    {
        if (attackData == null)
        {
            Debug.LogError(
                $"{name} has no raycast attack data.",
                this);
            return;
        }

        Vector3 direction = ResolveDirection(in context);
        Quaternion rotation = Quaternion.LookRotation(
            direction,
            Vector3.up);
        Vector3 origin = context.User.transform.position +
            rotation * originOffset;
        var attackContext = new AttackContext(
            context.User,
            attackData,
            attackData.payload);

        raycastAttack ??= new RaycastAttack();
        bool damageApplied = raycastAttack.Execute(
            in attackContext,
            attackData,
            origin,
            direction,
            out RaycastHit hit);

        if (context.User.TryGetComponent(
                out SkillDebugView debugView))
        {
            debugView.ReportRaycast(
                context.Skill,
                context.ActionIndex,
                attackData,
                origin,
                direction,
                hit,
                damageApplied);
        }
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
