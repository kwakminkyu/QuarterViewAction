using System;
using UnityEngine;

public sealed class Health : MonoBehaviour
{
    [SerializeField, Min(1f)] private float maxHealth = 100f;

    public float MaxHealth => maxHealth;
    public float CurrentHealth { get; private set; }
    public bool IsDepleted => CurrentHealth <= 0f;

    public event Action<float> HealthDecreased;
    public event Action<float, float> HealthChanged;
    public event Action Depleted;

    private void Awake()
    {
        CurrentHealth = maxHealth;
    }

    public float Decrease(float amount)
    {
        if (IsDepleted || amount <= 0f)
        {
            return 0f;
        }

        float previousHealth = CurrentHealth;
        CurrentHealth = Mathf.Max(CurrentHealth - amount, 0f);
        float decreasedAmount = previousHealth - CurrentHealth;

        HealthDecreased?.Invoke(decreasedAmount);
        HealthChanged?.Invoke(CurrentHealth, MaxHealth);

        if (IsDepleted)
        {
            Depleted?.Invoke();
        }

        return decreasedAmount;
    }
}
