using System;
using UnityEngine;

public sealed class AreaAttack
{
    public Area Execute(
        in AttackContext context,
        AreaAttackData data,
        Vector3 position,
        Quaternion rotation)
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        if (data.areaPrefab == null)
        {
            Debug.LogError(
                $"{data.name} has no area prefab.",
                data);
            return null;
        }

        Area area = UnityEngine.Object.Instantiate(
            data.areaPrefab,
            position,
            rotation);
        area.Initialize(in context, data);
        return area;
    }
}
