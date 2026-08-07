using System;
using UnityEngine;

public sealed class ProjectileAttack
{
    public Projectile Execute(
        in AttackContext context,
        ProjectileAttackData data,
        Vector3 position,
        Vector3 direction)
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        if (data.projectilePrefab == null)
        {
            Debug.LogError(
                $"{data.name} has no projectile prefab.",
                data);
            return null;
        }

        direction = direction.normalized;

        if (direction.sqrMagnitude <= Mathf.Epsilon)
        {
            return null;
        }

        Projectile projectile = UnityEngine.Object.Instantiate(
            data.projectilePrefab,
            position,
            Quaternion.LookRotation(direction, Vector3.up));
        projectile.Initialize(in context, data, direction);
        return projectile;
    }
}
