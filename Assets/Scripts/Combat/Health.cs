using System;
using UnityEngine;

public sealed class Health : MonoBehaviour
{
    [Min(1f)] public float maxHealth = 100f;

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
        HealthChanged?.Invoke(CurrentHealth, maxHealth);

        if (IsDepleted)
        {
            Depleted?.Invoke();
        }

        Debug.Log("Current Health: " + CurrentHealth + "/" + maxHealth);
        return decreasedAmount;
    }
}
