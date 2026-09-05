using System;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(
    typeof(CharacterMovement),
    typeof(CharacterAim),
    typeof(SkillController))]
public sealed class PlayerInputController : MonoBehaviour
{
    private const int BasicAttackSlot = 0;
    private const int SpecialAttackSlot = 1;
    private const int DashSlot = 2;
    private const int UltimateSlot = 3;

    private CharacterMovement characterMovement;
    private CharacterAim characterAim;
    private SkillController skillController;
    private SkillDebugView skillDebugView;
    private PlayerAnimationController animationController;
    private Camera worldCamera;
    private SkillDefinition basicAttackDefinition;
    private bool wasAttacking;
    private int lastAnimatedActionIndex = -1;

    public Vector2 MoveInput { get; private set; }
    public Vector2 AimInput { get; private set; }

    private void Awake()
    {
        characterMovement = GetComponent<CharacterMovement>();
        characterAim = GetComponent<CharacterAim>();
        skillController = GetComponent<SkillController>();
        skillDebugView = GetComponent<SkillDebugView>();
        animationController = GetComponentInChildren<PlayerAnimationController>();
        worldCamera = Camera.main;

        try
        {
            basicAttackDefinition =
                skillController.GetSkillInstance(BasicAttackSlot).Definition;
        }
        catch (ArgumentOutOfRangeException)
        {
            basicAttackDefinition = null;
        }
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
        TryUseSkill(
            context,
            BasicAttackSlot,
            "Basic attack",
            characterAim.AimDirection);
    }

    public void OnSpecialAttack(InputAction.CallbackContext context)
    {
        TryUseSkill(
            context,
            SpecialAttackSlot,
            "Special attack",
            characterAim.AimDirection);
    }

    public void OnDash(InputAction.CallbackContext context)
    {
        Vector3 direction = GetCameraRelativeMoveDirection(MoveInput);

        if (direction.sqrMagnitude <= Mathf.Epsilon)
        {
            direction = characterAim.AimDirection;
        }

        TryUseSkill(
            context,
            DashSlot,
            "Dash",
            direction);
    }

    public void OnUltimate(InputAction.CallbackContext context)
    {
        TryUseSkill(
            context,
            UltimateSlot,
            "Ultimate",
            characterAim.AimDirection);
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

        UpdateAttackAnimation();
        UpdateAttackEnd();
    }

    private void UpdateAttackAnimation()
    {
        bool isBasicAttackActive =
            skillController.IsExecuting &&
            skillController.CurrentSkillDefinition == basicAttackDefinition;

        if (!isBasicAttackActive)
        {
            lastAnimatedActionIndex = -1;
            return;
        }

        int actionIndex = skillController.CurrentActionIndex;

        if (actionIndex == lastAnimatedActionIndex)
        {
            return;
        }

        lastAnimatedActionIndex = actionIndex;
        animationController?.PlayAttack(actionIndex);
    }

    private void UpdateAttackEnd()
    {
        bool isAttacking = skillController.IsExecuting;

        if (wasAttacking && !isAttacking && animationController != null)
        {
            animationController.EndAttack();
        }

        wasAttacking = isAttacking;
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

        bool accepted = skillController.TryUseSkill(
            slotIndex,
            direction);

        skillDebugView?.ReportSkillInput(
            skillName,
            slotIndex,
            accepted,
            skillController.CurrentActionIndex,
            skillController.CurrentPhase);

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
