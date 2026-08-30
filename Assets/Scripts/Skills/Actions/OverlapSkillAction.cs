using UnityEngine;

[CreateAssetMenu(fileName = "OverlapSkillAction", menuName = "Skills/Actions/Overlap")]
public sealed class OverlapSkillAction : SkillAction
{
    public OverlapAttackData attackData;
    public Vector3 centerOffset =
        new Vector3(0f, 1f, 1f);
    public bool invulnerableUntilSkillEnds;
    [Min(0f)] public float lungeDistance;

    public override void OnActiveEnter(
        in SkillActionContext context)
    {
        if (invulnerableUntilSkillEnds)
        {
            context.DamageReceiver.SetInvulnerable(true);
        }

        ExecuteOverlapAttack(in context);
    }

    public override void OnActiveUpdate(
        in SkillActionContext context)
    {
        ExecuteOverlapAttack(in context);

        if (lungeDistance <= Mathf.Epsilon)
        {
            return;
        }

        Vector3 direction = ResolveDirection(in context);
        ApplyLunge(in context, direction, lungeDistance);
    }

    private void ExecuteOverlapAttack(in SkillActionContext context)
    {
        if (attackData == null)
        {
            Debug.LogError(
                $"{name} has no overlap attack data.",
                this);
            return;
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
}
