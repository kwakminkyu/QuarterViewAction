using UnityEngine;
using UnityEngine.Animations.Rigging;

[RequireComponent(typeof(Animator))]
public sealed class PlayerAnimationController : MonoBehaviour
{
    private static readonly int MoveHash = Animator.StringToHash("Move");
    private static readonly int AttackIndexHash = Animator.StringToHash("AttackIndex");
    private static readonly int AttackHash = Animator.StringToHash("Attack");
    private static readonly int AttackEndHash = Animator.StringToHash("AttackEnd");

    [SerializeField] private Rig rig;

    private Animator animator;
    private bool isMoving;
    private float rigWeightTarget;
    private float rigWeightBlendSpeed = 8f;

    private void Awake()
    {
        animator = GetComponent<Animator>();

        foreach (RigWeightState state in animator.GetBehaviours<RigWeightState>())
        {
            state.SetController(this);
        }
    }

    private void LateUpdate()
    {
        if (rig == null)
        {
            return;
        }

        rig.weight = Mathf.MoveTowards(
            rig.weight,
            rigWeightTarget,
            rigWeightBlendSpeed * Time.deltaTime);
    }

    public void SetRigWeightTarget(float target, float blendSpeed)
    {
        rigWeightTarget = target;
        rigWeightBlendSpeed = blendSpeed;
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

    public void PlayAttack(int attackIndex)
    {
        animator.SetInteger(AttackIndexHash, attackIndex);
        animator.SetTrigger(AttackHash);
    }

    public void EndAttack()
    {
        animator.SetTrigger(AttackEndHash);
    }
}
