using System;
using UnityEngine;

[Serializable]
public struct DamagePayload
{
    [Min(0f)] public float damage;
    [Min(0f)] public float stagger;
    [Min(0f)] public float knockback;

    public DamagePayload(float damage, float stagger, float knockback)
    {
        this.damage = Mathf.Max(0f, damage);
        this.stagger = Mathf.Max(0f, stagger);
        this.knockback = Mathf.Max(0f, knockback);
    }
}
