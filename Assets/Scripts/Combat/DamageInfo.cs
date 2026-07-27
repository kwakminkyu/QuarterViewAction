using UnityEngine;

public readonly struct DamageInfo
{
    public GameObject Attacker { get; }
    public DamagePayload Payload { get; }
    public Vector3 HitPoint { get; }
    public Vector3 HitDirection { get; }

    public DamageInfo(
        GameObject attacker,
        DamagePayload payload,
        Vector3 hitPoint,
        Vector3 hitDirection)
    {
        Attacker = attacker;
        Payload = payload;
        HitPoint = hitPoint;
        HitDirection = hitDirection.normalized;
    }

    public DamageInfo(
        in AttackContext context,
        Vector3 hitPoint,
        Vector3 hitDirection)
        : this(
            context.Attacker,
            context.Payload,
            hitPoint,
            hitDirection)
    {
    }
}
