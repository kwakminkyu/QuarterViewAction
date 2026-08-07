using System;
using UnityEngine;

[RequireComponent(typeof(Health))]
public sealed class DamageReceiver : MonoBehaviour, IDamageable
{
    public Health health;

    private bool isInvulnerable;

    public bool IsInvulnerable => isInvulnerable;
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

    private void OnDisable()
    {
        isInvulnerable = false;
    }

    public void SetInvulnerable(bool value)
    {
        isInvulnerable = value;
    }

    public void TakeDamage(in DamageInfo info)
    {
        if (isInvulnerable || health.IsDepleted)
        {
            return;
        }

        health.Decrease(info.Payload.damage);
        DamageReceived?.Invoke(info);
    }
}
