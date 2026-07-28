using System;
using UnityEngine;

public sealed class OverlapAttack
{
    private Collider[] colliderBuffer;

    public OverlapAttack(int initialBufferCapacity = 32)
    {
        if (initialBufferCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialBufferCapacity));
        }

        colliderBuffer = new Collider[initialBufferCapacity];
    }

    public void Execute(
        in AttackContext context,
        OverlapAttackData data,
        Vector3 position,
        Quaternion rotation)
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        int colliderCount = FindColliders(
            data,
            position,
            rotation);

        DamageReceiver attackerReceiver = GetAttackerReceiver(
            context.Attacker);

        try
        {
            for (int i = 0; i < colliderCount; i++)
            {
                Collider targetCollider = colliderBuffer[i];
                DamageReceiver receiver =
                    targetCollider.GetComponentInParent<DamageReceiver>();

                if (receiver == null ||
                    !receiver.isActiveAndEnabled ||
                    receiver == attackerReceiver ||
                    IsAttackerCollider(context.Attacker, targetCollider))
                {
                    continue;
                }

                // A DamageReceiver is expected to have only one collider
                // included in the target layer mask.
                Vector3 hitPoint = targetCollider.ClosestPoint(position);
                Vector3 hitDirection = GetHitDirection(
                    context.Attacker,
                    receiver.transform,
                    position);

                var damageInfo = new DamageInfo(
                    in context,
                    hitPoint,
                    hitDirection);

                receiver.TakeDamage(in damageInfo);
            }
        }
        finally
        {
            Array.Clear(colliderBuffer, 0, colliderBuffer.Length);
        }
    }

    private int FindColliders(
        OverlapAttackData data,
        Vector3 position,
        Quaternion rotation)
    {
        while (true)
        {
            int colliderCount = FindCollidersNonAlloc(
                data,
                position,
                rotation);

            if (colliderCount < colliderBuffer.Length)
            {
                return colliderCount;
            }

            Array.Resize(
                ref colliderBuffer,
                colliderBuffer.Length * 2);
        }
    }

    private int FindCollidersNonAlloc(
        OverlapAttackData data,
        Vector3 position,
        Quaternion rotation)
    {
        switch (data.Shape)
        {
            case OverlapShape.Box:
                return Physics.OverlapBoxNonAlloc(
                    position,
                    data.BoxSize * 0.5f,
                    colliderBuffer,
                    rotation,
                    data.TargetMask,
                    data.TriggerInteraction);

            case OverlapShape.Sphere:
                return Physics.OverlapSphereNonAlloc(
                    position,
                    data.Radius,
                    colliderBuffer,
                    data.TargetMask,
                    data.TriggerInteraction);

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(data.Shape),
                    data.Shape,
                    null);
        }
    }

    private DamageReceiver GetAttackerReceiver(
        GameObject attacker)
    {
        return attacker == null
            ? null
            : attacker.GetComponentInParent<DamageReceiver>();
    }

    private bool IsAttackerCollider(
        GameObject attacker,
        Collider targetCollider)
    {
        return attacker != null &&
               targetCollider.transform.IsChildOf(attacker.transform);
    }

    private Vector3 GetHitDirection(
        GameObject attacker,
        Transform receiver,
        Vector3 attackPosition)
    {
        Vector3 direction = receiver.position - attackPosition;

        if (direction.sqrMagnitude > Mathf.Epsilon)
        {
            return direction.normalized;
        }

        return attacker == null
            ? Vector3.forward
            : attacker.transform.forward;
    }
}
