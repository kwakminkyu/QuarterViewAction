using System;
using UnityEngine;

[Serializable]
public struct DamagePayload
{
    [SerializeField] private float damage;
    [SerializeField] private float stagger;
    [SerializeField] private float knockback;

    public float Damage => damage;
    public float Stagger => stagger;
    public float Knockback => knockback;

    public DamagePayload(float damage, float stagger, float knockback)
    {
        this.damage = Mathf.Max(0f, damage);
        this.stagger = Mathf.Max(0f, stagger);
        this.knockback = Mathf.Max(0f, knockback);
    }
}
