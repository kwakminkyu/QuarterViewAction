using UnityEngine;

[CreateAssetMenu(
    fileName = "OverlapSkillAction",
    menuName = "Skills/Actions/Overlap")]
public sealed class OverlapSkillAction : SkillAction
{
    [SerializeField] private OverlapAttackData attackData;
    [SerializeField] private Vector3 centerOffset =
        new Vector3(0f, 1f, 1f);

    private OverlapAttack overlapAttack;

    public OverlapAttackData AttackData => attackData;
    public Vector3 CenterOffset => centerOffset;

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

        Vector3 direction = ResolveDirection(in context);
        Quaternion rotation = Quaternion.LookRotation(
            direction,
            Vector3.up);
        Vector3 position = context.User.transform.position +
            rotation * centerOffset;

        var attackContext = new AttackContext(
            context.User,
            attackData,
            attackData.Payload);

        overlapAttack ??= new OverlapAttack();
        int hitCount = overlapAttack.Execute(
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
