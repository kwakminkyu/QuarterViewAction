using UnityEngine;

public readonly struct AttackContext
{
    public GameObject Attacker { get; }
    public AttackData AttackData { get; }
    public DamagePayload Payload { get; }

    public AttackContext(
        GameObject attacker,
        AttackData attackData,
        DamagePayload payload)
    {
        Attacker = attacker;
        AttackData = attackData;
        Payload = payload;
    }
}
