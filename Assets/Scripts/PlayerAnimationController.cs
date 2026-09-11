using UnityEngine;

[RequireComponent(typeof(Animator))]
public sealed class PlayerAnimationController : MonoBehaviour
{
    private static readonly int MoveHash = Animator.StringToHash("Move");
    private static readonly int AttackHash = Animator.StringToHash("Attack");
    private static readonly int AttackEndHash = Animator.StringToHash("AttackEnd");
    private static readonly int DashHash = Animator.StringToHash("Dash");

    private Animator animator;
    private bool isMoving;

    private void Awake()
    {
        animator = GetComponent<Animator>();
    }

    public void SetMoving(bool moving)
    {
        if (isMoving == moving)
        {
            return;
        }

        isMoving = moving;
        animator.SetBool(MoveHash, isMoving);
    }

    public void PlayAttack()
    {
        animator.ResetTrigger(AttackEndHash);
        animator.SetTrigger(AttackHash);
    }

    public void PlayDash()
    {
        animator.SetTrigger(DashHash);
    }

    public void EndAttack()
    {
        animator.ResetTrigger(AttackHash);
        animator.SetTrigger(AttackEndHash);
    }
}
