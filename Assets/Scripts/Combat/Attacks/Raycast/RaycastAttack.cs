using System;
using UnityEngine;

public sealed class RaycastAttack
{
    public bool Execute(
        in AttackContext context,
        RaycastAttackData data,
        Vector3 origin,
        Vector3 direction,
        out RaycastHit resolvedHit)
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        resolvedHit = default;
        direction = direction.normalized;

        if (direction.sqrMagnitude <= Mathf.Epsilon ||
            data.range <= Mathf.Epsilon)
        {
            return false;
        }

        if (!Physics.Raycast(
                origin,
                direction,
                out resolvedHit,
                data.range,
                data.targetMask,
                data.triggerInteraction))
        {
            return false;
        }

        DamageReceiver receiver = resolvedHit.collider
            .GetComponentInParent<DamageReceiver>();
        DamageReceiver attackerReceiver = context.Attacker == null
            ? null
            : context.Attacker.GetComponentInParent<DamageReceiver>();

        if (receiver == null ||
            !receiver.isActiveAndEnabled ||
            receiver == attackerReceiver)
        {
            return false;
        }

        var damageInfo = new DamageInfo(
            in context,
            resolvedHit.point,
            direction);
        float healthBeforeDamage = receiver.health.CurrentHealth;

        receiver.TakeDamage(in damageInfo);
        return receiver.health.CurrentHealth < healthBeforeDamage;
    }
}
