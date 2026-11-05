using System;
using UnityEngine;

[RequireComponent(typeof(CharacterController), typeof(DamageReceiver))]
public sealed class CharacterMovement : MonoBehaviour
{
    [SerializeField, Min(0f)] private float moveSpeed = 5f;
    [SerializeField] private float gravity = -9.81f;
    [SerializeField, Min(0f)] private float groundedVerticalSpeed = 2f;
    [SerializeField, Min(0.01f)] private float knockbackDeceleration = 20f;

    private CharacterController characterController;
    private DamageReceiver damageReceiver;
    private Vector3 knockbackVelocity;

    // Distance skills have asked for and the body has not covered yet. Kept as
    // a distance rather than a velocity, and only emptied once moved, so
    // nothing is lost to frame timing: not the last piece of a dash whose
    // skill ends on the same frame, nor a frame that runs in a different
    // order or at a different length from the one that asked.
    private Vector3 pendingSkillDisplacement;
    private float verticalVelocity;
    private bool isSkillControlling;
    private int lockCount;

    // Collision layers switched off on the CharacterController: whatever the
    // controller was authored with, what the running action passes through,
    // and what an earlier action passed through that the body is still inside
    // - kept off until it is clear, so ending a dash inside an enemy neither
    // snags on it nor shoves the body back out.
    private LayerMask authoredExcludeLayers;
    private int passThroughLayers;
    private int releasingLayers;

    public Vector3 MoveDirection { get; private set; }
    public bool IsMoving => MoveDirection.sqrMagnitude > 0f;
    public bool IsLocked => lockCount > 0;

    // Raised only when IsMoving flips, not on every Move call.
    public event Action<bool> MovingChanged;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        damageReceiver = GetComponent<DamageReceiver>();
        authoredExcludeLayers = characterController.excludeLayers;
    }

    private void OnEnable()
    {
        if (damageReceiver != null)
        {
            damageReceiver.DamageReceived += OnDamageReceived;
        }
    }

    private void OnDisable()
    {
        if (damageReceiver != null)
        {
            damageReceiver.DamageReceived -= OnDamageReceived;
        }

        EndSkillControl();

        // A body switched off - by death or otherwise - goes nowhere more.
        pendingSkillDisplacement = Vector3.zero;
        releasingLayers = 0;
        ApplyExcludeLayers();
    }

    private void Update()
    {
        UpdateReleasingLayers();
        UpdateVerticalVelocity();

        // While a skill steers, walking is off and the skill's own distance
        // is all the planar motion there is.
        Vector3 locomotionVelocity = isSkillControlling
            ? Vector3.zero
            : MoveDirection * moveSpeed;

        Vector3 velocity = locomotionVelocity + knockbackVelocity;
        velocity.y = verticalVelocity;

        characterController.Move(
            velocity * Time.deltaTime + pendingSkillDisplacement);

        knockbackVelocity = Vector3.MoveTowards(
            knockbackVelocity,
            Vector3.zero,
            knockbackDeceleration * Time.deltaTime);

        pendingSkillDisplacement = Vector3.zero;
    }

    public void Move(Vector2 input)
    {
        Move(new Vector3(input.x, 0f, input.y));
    }

    public void Move(Vector3 worldDirection)
    {
        // Whoever steers keeps asking every frame; while locked the answer is
        // to stand still, so the next request after Unlock takes over again.
        if (IsLocked)
        {
            worldDirection = Vector3.zero;
        }

        worldDirection.y = 0f;

        bool wasMoving = IsMoving;
        MoveDirection = Vector3.ClampMagnitude(worldDirection, 1f);

        if (IsMoving != wasMoving)
        {
            MovingChanged?.Invoke(IsMoving);
        }
    }

    // Stops locomotion for a state that passes, such as a stagger. Knockback
    // and gravity still apply, so a staggering hit still shoves the body.
    public void Lock()
    {
        lockCount++;
        Move(Vector3.zero);
    }

    public void Unlock()
    {
        lockCount = Mathf.Max(lockCount - 1, 0);
    }

    // Leaves any distance still pending alone: when one skill cuts another
    // off, the last piece the old one asked for is still covered.
    public void BeginSkillControl()
    {
        isSkillControlling = true;
    }

    // Adds to this frame's skill motion rather than replacing it, so a frame
    // that spans two moving phases covers both shares.
    public void AddSkillDisplacement(Vector3 displacement)
    {
        if (!isSkillControlling)
        {
            return;
        }

        displacement.y = 0f;
        pendingSkillDisplacement += displacement;
    }

    // Walking velocity from the current move input, scaled, for a skill that
    // lets its user steer; zero while locked, since Move then holds no input.
    public Vector3 GetInputVelocity(float speedMultiplier)
    {
        return MoveDirection * moveSpeed * speedMultiplier;
    }

    // Hands steering back to walking. Distance already asked for is still
    // covered on the next move, so a skill ending on the frame its motion
    // finishes keeps that frame's piece.
    public void EndSkillControl()
    {
        isSkillControlling = false;
        SetPassThroughLayers(0);
    }

    // Layers to move through from now on, replacing the last set. Layers
    // dropped from it stay passable while the body still overlaps them.
    public void SetPassThroughLayers(LayerMask layers)
    {
        releasingLayers |= passThroughLayers & ~layers.value;
        passThroughLayers = layers.value;
        ApplyExcludeLayers();
    }

    private void UpdateReleasingLayers()
    {
        if (releasingLayers == 0 || OverlapsLayers(releasingLayers))
        {
            return;
        }

        releasingLayers = 0;
        ApplyExcludeLayers();
    }

    private void ApplyExcludeLayers()
    {
        characterController.excludeLayers =
            authoredExcludeLayers | passThroughLayers | releasingLayers;
    }

    // Whether the controller's capsule, as placed now, overlaps anything on
    // the given layers.
    private bool OverlapsLayers(int layers)
    {
        Vector3 scale = transform.lossyScale;
        float radius = characterController.radius *
            Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        float height = Mathf.Max(
            characterController.height * Mathf.Abs(scale.y),
            radius * 2f);
        Vector3 center = transform.TransformPoint(characterController.center);
        Vector3 halfSegment = Vector3.up * (height * 0.5f - radius);

        return Physics.CheckCapsule(
            center + halfSegment,
            center - halfSegment,
            radius,
            layers,
            QueryTriggerInteraction.Ignore);
    }

    private void UpdateVerticalVelocity()
    {
        if (characterController.isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -groundedVerticalSpeed;
        }
        else
        {
            verticalVelocity += gravity * Time.deltaTime;
        }
    }

    private void OnDamageReceived(DamageInfo info)
    {
        if (info.Payload.knockback <= 0f)
        {
            return;
        }

        Vector3 direction = Vector3.ProjectOnPlane(
            info.HitDirection,
            Vector3.up);

        if (direction.sqrMagnitude <= Mathf.Epsilon)
        {
            return;
        }

        knockbackVelocity =
            direction.normalized * info.Payload.knockback;
    }
}
