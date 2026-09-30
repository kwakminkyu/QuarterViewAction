using System;
using UnityEngine;

// Lets hits break a character's attack. Each hit adds its payload's stagger;
// once the total passes maxPoise the character staggers - its skill is cut
// off and it can neither act nor walk for staggerDuration - and the total
// starts again from zero. A character that goes a while without being hit
// sheds what it has built up.
//
// Only characters that should be interruptible carry this; the player does
// not, by design.
[RequireComponent(typeof(DamageReceiver))]
public sealed class Poise : MonoBehaviour
{
    [Tooltip("Stagger a character can take before it breaks.")]
    [SerializeField, Min(0.01f)] private float maxPoise = 10f;
    // Above zero, or the lock taken on breaking would never be released.
    [SerializeField, Min(0.01f)] private float staggerDuration = 0.6f;

    [Header("Recovery")]
    [Tooltip("Seconds without a hit before built-up stagger starts to drain.")]
    [SerializeField, Min(0f)] private float recoveryDelay = 1.5f;
    [Tooltip("Stagger drained per second once recovery starts.")]
    [SerializeField, Min(0f)] private float recoveryRate = 10f;

    private DamageReceiver damageReceiver;
    private Health health;
    private SkillController skillController;
    private CharacterMovement movement;

    private float accumulated;
    private float timeSinceHit;
    private float staggerRemaining;

    public float Accumulated => accumulated;
    public bool IsStaggered => staggerRemaining > 0f;

    public event Action Staggered;
    public event Action StaggerEnded;

    private void Awake()
    {
        damageReceiver = GetComponent<DamageReceiver>();
        health = GetComponent<Health>();

        // Optional: a character that neither acts nor moves - a training
        // dummy - can still carry poise and report staggers.
        skillController = GetComponent<SkillController>();
        movement = GetComponent<CharacterMovement>();
    }

    private void OnEnable()
    {
        damageReceiver.DamageReceived += OnDamageReceived;
    }

    private void OnDisable()
    {
        damageReceiver.DamageReceived -= OnDamageReceived;

        // Never leave the character locked behind a disabled component.
        if (IsStaggered)
        {
            EndStagger();
        }
    }

    private void Update()
    {
        float deltaTime = Time.deltaTime;

        if (IsStaggered)
        {
            staggerRemaining -= deltaTime;

            if (staggerRemaining <= 0f)
            {
                EndStagger();
            }

            return;
        }

        timeSinceHit += deltaTime;

        if (timeSinceHit >= recoveryDelay && accumulated > 0f)
        {
            accumulated = Mathf.Max(accumulated - recoveryRate * deltaTime, 0f);
        }
    }

    private void OnDamageReceived(DamageInfo info)
    {
        // The killing blow belongs to CharacterDeath, not to a stagger.
        if (health != null && health.IsDepleted)
        {
            return;
        }

        timeSinceHit = 0f;

        // Hits landing during a stagger neither build toward the next one nor
        // extend this one, so a combo can break a guard but not hold a
        // character down for good.
        if (IsStaggered)
        {
            return;
        }

        accumulated += info.Payload.stagger;

        if (accumulated >= maxPoise)
        {
            BeginStagger();
        }
    }

    private void BeginStagger()
    {
        accumulated = 0f;
        staggerRemaining = staggerDuration;

        if (skillController != null)
        {
            skillController.Lock();
        }

        if (movement != null)
        {
            movement.Lock();
        }

        Staggered?.Invoke();
    }

    private void EndStagger()
    {
        staggerRemaining = 0f;

        if (skillController != null)
        {
            skillController.Unlock();
        }

        if (movement != null)
        {
            movement.Unlock();
        }

        StaggerEnded?.Invoke();
    }
}
