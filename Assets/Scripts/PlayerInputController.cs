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

    private CharacterMovement characterMovement;
    private CharacterAim characterAim;
    private SkillController skillController;
    private SkillDebugView skillDebugView;
    private Camera worldCamera;
    private float bufferedAttackTime = float.NegativeInfinity;

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
        // Only a fresh press: a press buffered before the dash began is not
        // aimed at the dash and still waits for the basic attack.
        if (IsDashing() &&
            TryUseSkill(
                DashAttackSlot,
                "Dash attack",
                characterAim.AimDirection))
        {
            bufferedAttackTime = float.NegativeInfinity;
            return;
        }

        if (TryUseAttack())
        {
            bufferedAttackTime = float.NegativeInfinity;
        }
        else if (ShouldBufferAttack())
        {
            bufferedAttackTime = Time.time;
        }
    }

    // Along the held direction; with none, where the character faces - after
    // stopping that is the way it was going, so a standing dash never flips
    // round toward the cursor.
    public void OnDash(InputAction.CallbackContext context)
    {
        Vector3 direction = GetCameraRelativeMoveDirection(MoveInput);

        if (direction.sqrMagnitude <= Mathf.Epsilon)
        {
            direction = transform.forward;
            direction.y = 0f;
        }

        TryUseSkill(
            context,
            DashSlot,
            "Dash",
            direction);
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

        UpdateBufferedAttack();
    }

    private bool TryUseAttack(bool reportRefusal = true)
    {
        return TryUseSkill(
            BasicAttackSlot,
            "Basic attack",
            characterAim.AimDirection,
            reportRefusal);
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

    private void UpdateBufferedAttack()
    {
        if (Time.time - bufferedAttackTime > attackBufferDuration)
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

    private bool TryUseSkill(
        InputAction.CallbackContext context,
        int slotIndex,
        string skillName,
        Vector3 direction)
    {
        if (!context.performed)
        {
            return false;
        }

        return TryUseSkill(slotIndex, skillName, direction);
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
