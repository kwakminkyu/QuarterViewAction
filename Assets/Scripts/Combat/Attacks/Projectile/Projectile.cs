using System;
using UnityEngine;

public sealed class Projectile : MonoBehaviour
{
    private AttackContext attackContext;
    private ProjectileAttackData attackData;
    private Vector3 direction;
    private float remainingLifetime;
    private bool isInitialized;
    private SkillDebugView debugView;

    public void Initialize(
        in AttackContext context,
        ProjectileAttackData data,
        Vector3 moveDirection)
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        attackContext = context;
        attackData = data;
        direction = moveDirection.normalized;
        remainingLifetime = data.lifetime;
        debugView = context.Attacker == null
            ? null
            : context.Attacker.GetComponent<SkillDebugView>();
        isInitialized = true;

        debugView?.ReportProjectileSpawn(
            data,
            transform.position);
    }

    private void Update()
    {
        if (!isInitialized)
        {
            return;
        }

        if (remainingLifetime <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        float movementDeltaTime = Mathf.Min(
            Time.deltaTime,
            remainingLifetime);
        float travelDistance = attackData.speed * movementDeltaTime;
        RaycastHit hit = default;
        bool collided = travelDistance > Mathf.Epsilon &&
            TryFindHit(travelDistance, out hit);

        debugView?.ReportProjectileCast(
            attackData,
            transform.position,
            direction,
            travelDistance,
            hit,
            collided);

        if (collided)
        {
            transform.position += direction * hit.distance;
            bool damageApplied = ApplyDamage(hit);
            debugView?.ReportProjectileHit(
                attackData,
                hit,
                damageApplied);
            Destroy(gameObject);
            return;
        }

        transform.position += direction * travelDistance;
        remainingLifetime -= movementDeltaTime;

        if (remainingLifetime <= 0f)
        {
            Destroy(gameObject);
        }
    }

    private bool TryFindHit(
        float travelDistance,
        out RaycastHit hit)
    {
        switch (attackData.shape)
        {
            case ProjectileCastShape.Sphere:
                return CastSphere(travelDistance, out hit);

            case ProjectileCastShape.Box:
                return CastBox(travelDistance, out hit);

            case ProjectileCastShape.Capsule:
                return CastCapsule(travelDistance, out hit);

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(attackData.shape),
                    attackData.shape,
                    null);
        }
    }

    private bool CastSphere(
        float travelDistance,
        out RaycastHit hit)
    {
        if (attackData.radius <= Mathf.Epsilon)
        {
            return CastRay(travelDistance, out hit);
        }

        return Physics.SphereCast(
            transform.position,
            attackData.radius,
            direction,
            out hit,
            travelDistance,
            attackData.targetMask,
            attackData.triggerInteraction);
    }

    private bool CastBox(
        float travelDistance,
        out RaycastHit hit)
    {
        Vector3 halfExtents = new Vector3(
            Mathf.Max(0f, attackData.boxSize.x),
            Mathf.Max(0f, attackData.boxSize.y),
            Mathf.Max(0f, attackData.boxSize.z)) * 0.5f;

        if (halfExtents.sqrMagnitude <= Mathf.Epsilon)
        {
            return CastRay(travelDistance, out hit);
        }

        return Physics.BoxCast(
            transform.position,
            halfExtents,
            direction,
            out hit,
            transform.rotation,
            travelDistance,
            attackData.targetMask,
            attackData.triggerInteraction);
    }

    private bool CastCapsule(
        float travelDistance,
        out RaycastHit hit)
    {
        float radius = Mathf.Max(0f, attackData.radius);

        if (radius <= Mathf.Epsilon)
        {
            return CastRay(travelDistance, out hit);
        }

        float height = Mathf.Max(
            attackData.capsuleHeight,
            radius * 2f);
        float halfSegment = height * 0.5f - radius;
        Vector3 point1 = transform.position +
            direction * halfSegment;
        Vector3 point2 = transform.position -
            direction * halfSegment;

        return Physics.CapsuleCast(
            point1,
            point2,
            radius,
            direction,
            out hit,
            travelDistance,
            attackData.targetMask,
            attackData.triggerInteraction);
    }

    private bool CastRay(
        float travelDistance,
        out RaycastHit hit)
    {

        return Physics.Raycast(
            transform.position,
            direction,
            out hit,
            travelDistance,
            attackData.targetMask,
            attackData.triggerInteraction);
    }

    private bool ApplyDamage(RaycastHit hit)
    {
        DamageReceiver receiver = hit.collider
            .GetComponentInParent<DamageReceiver>();
        DamageReceiver attackerReceiver =
            attackContext.Attacker == null
                ? null
                : attackContext.Attacker
                    .GetComponentInParent<DamageReceiver>();

        if (receiver == null ||
            !receiver.isActiveAndEnabled ||
            receiver == attackerReceiver)
        {
            return false;
        }

        var damageInfo = new DamageInfo(
            in attackContext,
            hit.point,
            direction);
        float healthBeforeDamage = receiver.health.CurrentHealth;

        receiver.TakeDamage(in damageInfo);
        return receiver.health.CurrentHealth < healthBeforeDamage;
    }
}
