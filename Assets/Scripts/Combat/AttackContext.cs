using UnityEngine;

public readonly struct AttackContext
{
    public GameObject Attacker { get; }
    public DamagePayload Payload { get; }

    // TODO: AttackData와 AttackCategory가 정의되면 공격 출처 정보를 추가한다.
    // AttackCategory는 AttackData가 소유하고, Context에는 AttackData 참조만 둘 예정이다.
    // public AttackData AttackData { get; }

    public AttackContext(
        GameObject attacker,
        DamagePayload payload)
    {
        Attacker = attacker;
        Payload = payload;
    }
}
