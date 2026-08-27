using UnityEngine;

public sealed class CharacterAim : MonoBehaviour
{
    [SerializeField, Min(0f)] private float turnSpeed = 720f;
    private Transform rotationTarget;

    public Vector3 AimDirection { get; private set; } = Vector3.forward;

    private void Awake()
    {
        rotationTarget = transform;
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
