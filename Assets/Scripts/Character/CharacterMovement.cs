using UnityEngine;

[DefaultExecutionOrder(100)]
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
    private Vector3 skillVelocity;
    private float verticalVelocity;
    private bool isSkillControlling;

    public Vector3 MoveDirection { get; private set; }
    public bool IsMoving => MoveDirection.sqrMagnitude > 0f;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        damageReceiver = GetComponent<DamageReceiver>();
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
    }

    private void Update()
    {
        UpdateVerticalVelocity();

        Vector3 locomotionVelocity = MoveDirection * moveSpeed;
        Vector3 planarVelocity = isSkillControlling
            ? skillVelocity
            : locomotionVelocity;

        Vector3 velocity = planarVelocity + knockbackVelocity;
        velocity.y = verticalVelocity;

        characterController.Move(velocity * Time.deltaTime);

        knockbackVelocity = Vector3.MoveTowards(
            knockbackVelocity,
            Vector3.zero,
            knockbackDeceleration * Time.deltaTime);

        skillVelocity = Vector3.zero;
    }

    public void Move(Vector2 input)
    {
        Move(new Vector3(input.x, 0f, input.y));
    }

    public void Move(Vector3 worldDirection)
    {
        worldDirection.y = 0f;
        MoveDirection = Vector3.ClampMagnitude(worldDirection, 1f);
    }

    public void BeginSkillControl()
    {
        isSkillControlling = true;
        skillVelocity = Vector3.zero;
    }

    public void MoveBySkill(Vector3 velocity)
    {
        if (!isSkillControlling)
        {
            return;
        }

        velocity.y = 0f;
        skillVelocity = velocity;
    }

    public void EndSkillControl()
    {
        isSkillControlling = false;
        skillVelocity = Vector3.zero;
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
        if (info.Payload.Knockback <= 0f)
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
            direction.normalized * info.Payload.Knockback;
    }
}
