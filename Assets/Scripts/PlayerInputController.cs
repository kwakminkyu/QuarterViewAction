using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(
    typeof(CharacterMovement),
    typeof(CharacterAim),
    typeof(SkillController))]
public sealed class PlayerInputController : MonoBehaviour
{
    private const int BasicAttackSlot = 0;
    private const int DashSlot = 1;

    // Not bound to a key: an attack press while dashing uses it instead of
    // the basic attack.
    private const int DashAttackSlot = 2;

    // How long a refused attack press is kept and retried, so pressing a
    // moment early - at the tail of the last combo hit, or during a dash -
    // still attacks once the character is free.
    [SerializeField, Min(0f)] private float attackBufferDuration = 0.2f;

    // The same for a dash pressed a moment before the attack reaches its
    // recovery, or before the cooldown runs out. A dash outranks an attack:
    // pressing it drops any attack still waiting, and while it waits no
    // attack goes first.
    [SerializeField, Min(0f)] private float dashBufferDuration = 0.15f;

    private CharacterMovement characterMovement;
    private CharacterAim characterAim;
    private SkillController skillController;
    private SkillDebugView skillDebugView;
    private Camera worldCamera;
    private float bufferedAttackTime = float.NegativeInfinity;
    private float bufferedDashTime = float.NegativeInfinity;

    public Vector2 MoveInput { get; private set; }
    public Vector2 AimInput { get; private set; }

    private void Awake()
    {
        characterMovement = GetComponent<CharacterMovement>();
        characterAim = GetComponent<CharacterAim>();
        skillController = GetComponent<SkillController>();
        skillDebugView = GetComponent<SkillDebugView>();
        worldCamera = Camera.main;
    }

    public void OnMove(InputAction.CallbackContext context)
    {
        MoveInput = context.ReadValue<Vector2>();
    }

    public void OnLook(InputAction.CallbackContext context)
    {
        AimInput = context.ReadValue<Vector2>();
    }

    public void OnAttack(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            PressAttack();
        }
    }

    private void PressAttack()
    {
        // A dash still waiting goes first; the attack waits behind it, kept
        // even mid-combo, so dash-then-attack comes out as the dash attack.
        bool dashWaiting = IsDashBuffered();

        if (!dashWaiting && TryUseAttack())
        {
            bufferedAttackTime = float.NegativeInfinity;
        }
        else if (dashWaiting || ShouldBufferAttack())
        {
            bufferedAttackTime = Time.time;
        }
    }

    public void OnDash(InputAction.CallbackContext context)
    {
        if (!context.performed)
        {
            return;
        }

        // The dash outranks every attack asked for before it: one waiting in
        // the buffer, and a combo step held for the recovery, which would
        // otherwise start first and shut the dash out again.
        bufferedAttackTime = float.NegativeInfinity;
        skillController.ClearPendingCombo();

        if (TryUseDash())
        {
            bufferedDashTime = float.NegativeInfinity;
        }
        else
        {
            bufferedDashTime = Time.time;
        }
    }

    private void Update()
    {
        Vector3 moveDirection = GetCameraRelativeMoveDirection(MoveInput);
        characterMovement.Move(moveDirection);
        UpdateAim(AimInput);

        Vector3 facingDirection = skillController.IsExecuting
            ? skillController.CurrentDirection
            : moveDirection;
        characterAim.FaceDirection(facingDirection);

        // Dash first, so on a frame where both could start the dash wins.
        UpdateBufferedDash();
        UpdateBufferedAttack();
    }

    // While dashing an attack becomes the dash attack. Any attack press alive
    // during a dash came after the dash was pressed - pressing the dash drops
    // the older ones - so it is aimed at the dash, buffered or not.
    private bool TryUseAttack(bool reportRefusal = true)
    {
        if (IsDashing() &&
            TryUseSkill(
                DashAttackSlot,
                "Dash attack",
                characterAim.AimDirection,
                reportRefusal))
        {
            return true;
        }

        return TryUseSkill(
            BasicAttackSlot,
            "Basic attack",
            characterAim.AimDirection,
            reportRefusal);
    }

    // Aimed when it actually starts, not when it was pressed, so a buffered
    // dash follows the direction held at that moment.
    private bool TryUseDash(bool reportRefusal = true)
    {
        return TryUseSkill(
            DashSlot,
            "Dash",
            GetDashDirection(),
            reportRefusal);
    }

    // Along the held direction; with none, where the character faces - after
    // stopping that is the way it was going, so a standing dash never flips
    // round toward the cursor.
    private Vector3 GetDashDirection()
    {
        Vector3 direction = GetCameraRelativeMoveDirection(MoveInput);

        if (direction.sqrMagnitude <= Mathf.Epsilon)
        {
            direction = transform.forward;
            direction.y = 0f;
        }

        return direction;
    }

    private bool IsDashBuffered()
    {
        return Time.time - bufferedDashTime <= dashBufferDuration;
    }

    private bool IsDashing()
    {
        return skillController.IsExecuting &&
            skillController.SkillSlotCount > DashAttackSlot &&
            skillController.CurrentSkillDefinition ==
                skillController.GetSkillInstance(DashSlot).Definition;
    }

    // Only presses that nothing else will pick up are kept. Mid-combo, the
    // action's own comboInputWindow decides which early presses count; a
    // buffer there would quietly widen that window.
    private bool ShouldBufferAttack()
    {
        if (!skillController.IsExecuting)
        {
            return true;
        }

        SkillDefinition current = skillController.CurrentSkillDefinition;
        bool isAttackCombo = current ==
            skillController.GetSkillInstance(BasicAttackSlot).Definition;
        bool hasNextStep =
            skillController.CurrentActionIndex < current.ActionCount - 1;

        return !isAttackCombo || !hasNextStep;
    }

    private void UpdateBufferedDash()
    {
        if (!IsDashBuffered())
        {
            return;
        }

        if (!TryUseDash(reportRefusal: false))
        {
            return;
        }

        // An attack pressed while this dash waited is replayed as late after
        // the dash starts as it was pressed after the dash, rather than on
        // this same frame, which would cut the dash off before it moved.
        // Pressing the dash dropped every older attack, so one still in the
        // buffer always came after it.
        float delay = Time.time - bufferedDashTime;
        bufferedAttackTime += delay;
        bufferedDashTime = float.NegativeInfinity;
    }

    private void UpdateBufferedAttack()
    {
        // Not before its (replayed) press time, and not after the buffer.
        if (Time.time < bufferedAttackTime ||
            Time.time - bufferedAttackTime > attackBufferDuration ||
            IsDashBuffered())
        {
            return;
        }

        // Consumed the moment it lands, or one press would keep firing for
        // the whole buffer and run the combo on by itself.
        if (TryUseAttack(reportRefusal: false))
        {
            bufferedAttackTime = float.NegativeInfinity;
        }
    }

    private Vector3 GetCameraRelativeMoveDirection(Vector2 input)
    {
        Vector3 cameraForward = Vector3.ProjectOnPlane(
            worldCamera.transform.forward,
            Vector3.up).normalized;

        Vector3 cameraRight = Vector3.ProjectOnPlane(
            worldCamera.transform.right,
            Vector3.up).normalized;

        return cameraRight * input.x + cameraForward * input.y;
    }

    // A buffered retry runs every frame, so it reports only the press that
    // lands; the refusals were reported once, when the key went down.
    private bool TryUseSkill(
        int slotIndex,
        string skillName,
        Vector3 direction,
        bool reportRefusal = true)
    {
        bool accepted = skillController.TryUseSkill(
            slotIndex,
            direction);

        if (accepted || reportRefusal)
        {
            skillDebugView?.ReportSkillInput(
                skillName,
                slotIndex,
                accepted,
                skillController.CurrentActionIndex,
                skillController.CurrentPhase);
        }

        return accepted;
    }

    private void UpdateAim(Vector2 screenPosition)
    {
        Ray pointerRay = worldCamera.ScreenPointToRay(screenPosition);
        Plane characterPlane = new Plane(Vector3.up, transform.position);

        if (characterPlane.Raycast(pointerRay, out float distance))
        {
            characterAim.AimAt(pointerRay.GetPoint(distance));
        }
    }
}
