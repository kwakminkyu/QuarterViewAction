using System;
using UnityEngine;

[RequireComponent(typeof(Health))]
public sealed class DamageReceiver : MonoBehaviour, IDamageable
{
    public Health health;

    [Tooltip("Seconds this can't be hit again after taking a hit. The " +
        "player's grace against being worn down by several attackers at " +
        "once; left at zero on monsters so every hit of a combo lands.")]
    [SerializeField, Min(0f)] private float invulnerableAfterHit;

    // Two separate sources, so neither can end the other's: a skill that
    // makes its user untouchable (only one skill runs at a time, so a flag is
    // enough), and a timed grace such as the one after a hit.
    private bool isInvulnerable;
    private float invulnerableUntil = float.NegativeInfinity;

    public bool IsInvulnerable =>
        isInvulnerable || Time.time < invulnerableUntil;
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
        invulnerableUntil = float.NegativeInfinity;
    }

    // For skills: on for as long as the skill says so.
    public void SetInvulnerable(bool value)
    {
        isInvulnerable = value;
    }

    // For a set time from now; never shortens a grace already running.
    public void GrantInvulnerability(float duration)
    {
        invulnerableUntil = Mathf.Max(invulnerableUntil, Time.time + duration);
    }

    public void TakeDamage(in DamageInfo info)
    {
        if (IsInvulnerable || health.IsDepleted)
        {
            return;
        }

        health.Decrease(info.Payload.damage);

        if (invulnerableAfterHit > 0f)
        {
            GrantInvulnerability(invulnerableAfterHit);
        }

        DamageReceived?.Invoke(info);
    }
}
