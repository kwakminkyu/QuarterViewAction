using UnityEngine;

[CreateAssetMenu(fileName = "OverlapSkillAction", menuName = "Skills/Actions/Overlap")]
public sealed class OverlapSkillAction : SkillAction
{
    public OverlapAttackData attackData;
    public Vector3 centerOffset =
        new Vector3(0f, 1f, 1f);
    public bool invulnerableUntilSkillEnds;

    public override void OnActiveEnter(
        in SkillActionContext context)
    {
        if (attackData == null)
        {
            Debug.LogError(
                $"{name} has no overlap attack data.",
                this);
            return;
        }

        if (invulnerableUntilSkillEnds)
        {
            context.DamageReceiver.SetInvulnerable(true);
        }

        Vector3 direction = ResolveDirection(in context);
        Quaternion rotation = Quaternion.LookRotation(
            direction,
            Vector3.up);
        Vector3 position = context.User.transform.position +
            rotation * centerOffset;

        var attackContext = new AttackContext(
            context.User,
            attackData,
            attackData.payload);

        int hitCount = context.OverlapAttack.Execute(
            in attackContext,
            attackData,
            position,
            rotation);

        if (context.User.TryGetComponent(
                out SkillDebugView debugView))
        {
            debugView.ReportOverlap(
                context.Skill,
                context.ActionIndex,
                attackData,
                position,
                rotation,
                hitCount);
        }
    }

    public override void OnSkillEnd(
        in SkillActionContext context)
    {
        if (invulnerableUntilSkillEnds)
        {
            context.DamageReceiver.SetInvulnerable(false);
        }
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
