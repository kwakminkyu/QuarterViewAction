using System;
using UnityEngine;

[Flags]
public enum SkillMovementPhases
{
    None = 0,
    Startup = 1 << 0,
    Active = 1 << 1,
    Recovery = 1 << 2,
}

// Both parts apply together in the chosen phases, and each is off at 0: a
// lunge carries the character along the attack direction, and the multiplier
// lets the player steer at that fraction of walking speed (1 = full speed).
[Serializable]
public struct SkillMovementSettings
{
    public SkillMovementPhases phases;

    // Covered within each chosen phase, not split across them.
    [Min(0f)] public float lungeDistance;
    [Min(0f)] public float moveSpeedMultiplier;
}

// From which phase of a combo action the next step's input is taken. Input
// that arrives before recovery is held and still fires at recoveryCancelDelay,
// so this changes what is dropped, not the combo's tempo. Values are fixed
// because they are serialized: 0 has to stay the default.
public enum ComboInputWindow
{
    FromActive = 0,
    FromStartup = 1,
    RecoveryOnly = 2,
}

public abstract class SkillAction : ScriptableObject
{
    [Min(0f)] public float startupDuration;
    [Min(0f)] public float activeDuration;
    [Min(0f)] public float recoveryDuration;
    [Min(0f)] public float recoveryCancelDelay;
    public ComboInputWindow comboInputWindow;

    // Animator state the character plays when this action starts, entered by
    // name so any character whose controller has a state of that name can run
    // the action. Empty plays nothing and leaves the current motion alone.
    public string animationState;

    // Where the clip shows the hit landing, in seconds at normal speed. When
    // set, the startup plays the clip at whatever speed brings that moment
    // exactly to the active phase opening, so the startup can be retimed
    // without re-exporting the motion; from the active phase on the clip
    // plays at normal speed. 0 plays it at normal speed throughout.
    [Min(0f)] public float animationStartupTime;

    public SkillMovementSettings movementSettings;

    // Optional, and on the base so any action type can opt in without a
    // subclass change; SkillController drives these on the active window.
    // Several effects can fire from one swing - a crescent as the blade lands,
    // a ground shockwave a moment later - so each entry carries its own delay
    // measured from the active phase opening. Authored in ascending delay
    // order; SkillController walks the list with a cursor.
    public ActionEffectSettings[] effects = Array.Empty<ActionEffectSettings>();

    [NonSerialized] private int animationStateHash;

    public bool HasAnimation => !string.IsNullOrEmpty(animationState);    public int AnimationStateHash => animationStateHash;

    // Animator speed through the startup; 1 everywhere else.
    public float StartupAnimationSpeed =>
        animationStartupTime > 0f && startupDuration > 0f
            ? animationStartupTime / startupDuration
            : 1f;

    // Where the active phase opens in clip time, for tools that line the
    // clip up against the action's phases.
    public float ClipActiveStartTime =>
        animationStartupTime > 0f ? animationStartupTime : startupDuration;

    protected virtual void OnEnable()
    {
        CacheAnimationStateHash();
    }

    protected virtual void OnValidate()
    {
        CacheAnimationStateHash();
    }

    private void CacheAnimationStateHash()
    {
        animationStateHash = HasAnimation
            ? Animator.StringToHash(animationState)
            : 0;
    }

    public void ApplyPhaseMovement(
        SkillPhase phase,
        in SkillActionContext context,
        float phaseDuration)
    {
        SkillMovementPhases flag = phase switch
        {
            SkillPhase.Startup => SkillMovementPhases.Startup,
            SkillPhase.Active => SkillMovementPhases.Active,
            SkillPhase.Recovery => SkillMovementPhases.Recovery,
            _ => SkillMovementPhases.None,
        };

        if ((movementSettings.phases & flag) == 0)
        {
            return;
        }

        Vector3 velocity =
            LungeVelocity(
                in context,
                movementSettings.lungeDistance,
                phaseDuration) +
            context.Movement.GetInputVelocity(
                movementSettings.moveSpeedMultiplier);

        context.Movement.MoveBySkill(velocity);
    }

    // The ground the action's hit will cover, for warning about it ahead of
    // time: a centre offset in the frame of the attack direction, and a
    // width (x) and length (z). Actions without a fixed area report none.
    public virtual bool TryGetHitArea(out Vector3 centerOffset, out Vector2 size)
    {
        centerOffset = Vector3.zero;
        size = Vector2.zero;
        return false;
    }

    // How far the action carries its user forward while its hit is out, so a
    // warning can cover where a lunging hit will land.
    public float ActiveLungeDistance =>
        (movementSettings.phases & SkillMovementPhases.Active) != 0
            ? movementSettings.lungeDistance
            : 0f;

    public virtual void OnActiveEnter(in SkillActionContext context)
    {
    }

    public virtual void OnActiveUpdate(in SkillActionContext context)
    {
    }

    public virtual void OnActiveExit(in SkillActionContext context)
    {
    }

    public virtual void OnSkillEnd(in SkillActionContext context)
    {
    }

    // Direction the action aims its attack. Flat by default; actions that aim
    // in 3D, such as projectiles and raycasts, override it to keep the height.
    protected virtual Vector3 ResolveDirection(in SkillActionContext context)
    {
        return ResolveFlatDirection(in context);
    }

    // Direction the character itself moves in. Always flat, whatever the
    // attack aims at, so a lunge never lifts the character off the ground.
    private static Vector3 ResolveFlatDirection(in SkillActionContext context)
    {
        Vector3 direction = context.Direction;
        direction.y = 0f;

        if (direction.sqrMagnitude <= Mathf.Epsilon &&
            context.Target != null)
        {
            direction = context.Target.position -
                context.User.transform.position;
            direction.y = 0f;
        }

        if (direction.sqrMagnitude <= Mathf.Epsilon)
        {
            direction = context.User.transform.forward;
            direction.y = 0f;
        }

        return direction.sqrMagnitude <= Mathf.Epsilon
            ? Vector3.forward
            : direction.normalized;
    }

    // The velocity that covers this frame's share of the phase's lunge. The
    // share is measured in the phase's own time, which can be less than a
    // whole frame when the phase opens or closes mid-frame.
    private static Vector3 LungeVelocity(
        in SkillActionContext context,
        float distance,
        float phaseDuration)
    {
        if (phaseDuration <= Mathf.Epsilon ||
            distance <= Mathf.Epsilon ||
            context.DeltaTime <= Mathf.Epsilon ||
            Time.deltaTime <= Mathf.Epsilon)
        {
            return Vector3.zero;
        }

        float phaseDistance =
            distance * context.DeltaTime / phaseDuration;
        float frameSpeed = phaseDistance / Time.deltaTime;

        return ResolveFlatDirection(in context) * frameSpeed;
    }
}
