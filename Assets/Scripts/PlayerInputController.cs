using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterMovement), typeof(CharacterAim))]
public sealed class PlayerInputController : MonoBehaviour
{
    private CharacterMovement characterMovement;
    private CharacterAim characterAim;
    private Camera worldCamera;

    public Vector2 MoveInput { get; private set; }
    public Vector2 AimInput { get; private set; }

    private void Awake()
    {
        characterMovement = GetComponent<CharacterMovement>();
        characterAim = GetComponent<CharacterAim>();
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

    private void Update()
    {
        characterMovement.Move(GetCameraRelativeMoveDirection(MoveInput));
        UpdateAim(AimInput);
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
