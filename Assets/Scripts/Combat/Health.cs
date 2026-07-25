using System;
using UnityEngine;

public sealed class Health : MonoBehaviour, IDamageable
{
    [SerializeField, Min(1f)] private float maxHealth = 100f;

    public float MaxHealth => maxHealth;
    public float CurrentHealth { get; private set; }
    public bool IsAlive => CurrentHealth > 0f;

    public event Action<float> DamageTaken;
    public event Action<float, float> HealthChanged;
    public event Action Died;

    private void Awake()
    {
        CurrentHealth = maxHealth;
    }

    public void TakeDamage(float amount)
    {
        if (!IsAlive || amount <= 0f)
        {
            return;
        }

        float previousHealth = CurrentHealth;
        CurrentHealth = Mathf.Max(CurrentHealth - amount, 0f);
        float appliedDamage = previousHealth - CurrentHealth;

        DamageTaken?.Invoke(appliedDamage);
        HealthChanged?.Invoke(CurrentHealth, MaxHealth);

        if (!IsAlive)
        {
            Died?.Invoke();
        }
    }
}
