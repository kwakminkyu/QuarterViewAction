using UnityEngine;

public sealed class CharacterAim : MonoBehaviour
{
    [SerializeField, Min(0f)] private float turnSpeed = 720f;
    private Transform rotationTarget;
    private SkillController skillController;

    public Vector3 AimDirection { get; private set; } = Vector3.forward;

    private void Awake()
    {
        rotationTarget = transform;

        // Optional: a character without skills only ever turns gradually.
        skillController = GetComponent<SkillController>();
    }

    private void OnEnable()
    {
        if (skillController != null)
        {
            skillController.ActionStarted += OnActionStarted;
        }
    }

    private void OnDisable()
    {
        if (skillController != null)
        {
            skillController.ActionStarted -= OnActionStarted;
        }
    }

    // An action - an attack or a dash - goes where it is aimed from its first
    // frame, so the body is put there at once rather than swung round; the
    // per-frame FaceDirection that follows then has nothing left to turn.
    private void OnActionStarted(SkillAction action)
    {
        SnapToDirection(skillController.CurrentDirection);
    }

    public void SnapToDirection(Vector3 worldDirection)
    {
        worldDirection.y = 0f;

        if (worldDirection.sqrMagnitude <= Mathf.Epsilon)
        {
            return;
        }

        rotationTarget.rotation = Quaternion.LookRotation(worldDirection.normalized, Vector3.up);
    }

    public void AimAt(Vector3 worldPosition)
    {
        SetAimDirection(worldPosition - rotationTarget.position);
    }

    public void SetAimDirection(Vector3 worldDirection)
    {
        worldDirection.y = 0f;

        if (worldDirection.sqrMagnitude <= Mathf.Epsilon)
        {
            return;
        }

        AimDirection = worldDirection.normalized;
    }

    public void FaceDirection(Vector3 worldDirection)
    {
        worldDirection.y = 0f;

        if (worldDirection.sqrMagnitude <= Mathf.Epsilon)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(worldDirection.normalized, Vector3.up);

        rotationTarget.rotation = Quaternion.RotateTowards(rotationTarget.rotation, targetRotation, turnSpeed * Time.deltaTime);
    }
}
