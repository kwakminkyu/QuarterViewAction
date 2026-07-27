using System;
using UnityEngine;

[RequireComponent(typeof(Health))]
public sealed class DamageReceiver : MonoBehaviour, IDamageable
{
    [SerializeField] private Health health;

    public Health Health => health;
    public event Action<DamageInfo> DamageReceived;

    private void Reset()
    {
        health = GetComponent<Health>();
    }

    private void Awake()
    {
        if (health == null)
        {
            health = GetComponent<Health>();
        }
    }

    public void TakeDamage(in DamageInfo info)
    {
        if (health.IsDepleted)
        {
            return;
        }

        health.Decrease(info.Payload.Damage);
        DamageReceived?.Invoke(info);
    }
}
