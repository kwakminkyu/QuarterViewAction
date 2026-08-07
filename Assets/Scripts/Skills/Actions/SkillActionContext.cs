using UnityEngine;

public readonly struct SkillActionContext
{
    public GameObject User { get; }
    public CharacterMovement Movement { get; }
    public DamageReceiver DamageReceiver { get; }
    public OverlapAttack OverlapAttack { get; }
    public SkillDefinition Skill { get; }
    public int ActionIndex { get; }
    public Vector3 Direction { get; }
    public Transform Target { get; }
    public float ActiveElapsedTime { get; }
    public float DeltaTime { get; }

    internal SkillActionContext(
        GameObject user,
        CharacterMovement movement,
        DamageReceiver damageReceiver,
        OverlapAttack overlapAttack,
        SkillDefinition skill,
        int actionIndex,
        Vector3 direction,
        Transform target,
        float activeElapsedTime,
        float deltaTime)
    {
        User = user;
        Movement = movement;
        DamageReceiver = damageReceiver;
        OverlapAttack = overlapAttack;
        Skill = skill;
        ActionIndex = actionIndex;
        Direction = direction;
        Target = target;
        ActiveElapsedTime = activeElapsedTime;
        DeltaTime = deltaTime;
    }
}
