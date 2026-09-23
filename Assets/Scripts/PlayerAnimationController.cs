using UnityEngine;

[RequireComponent(typeof(Animator))]
public sealed class PlayerAnimationController : MonoBehaviour
{
    private const string ComboStatePrefix = "BasicCombo";
    private const int ComboStateCount = 4;

    private static readonly int MoveHash = Animator.StringToHash("Move");
    private static readonly int AttackEndHash = Animator.StringToHash("AttackEnd");
    private static readonly int DashHash = Animator.StringToHash("Dash");

    private Animator animator;
    private int[] comboStateHashes;
    private bool isMoving;

    private void Awake()
    {
        animator = GetComponent<Animator>();

        comboStateHashes = new int[ComboStateCount];

        for (int i = 0; i < ComboStateCount; i++)
        {
            string stateName = ComboStatePrefix + (i + 1);
            comboStateHashes[i] = Animator.StringToHash(stateName);

            if (!animator.HasState(0, comboStateHashes[i]))
            {
                Debug.LogError(
                    $"Animator state '{stateName}' is missing.",
                    this);
            }
        }
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

    // The combo state is addressed by the logic's action index rather than
    // chained through triggers, so the motion always matches the hitbox no
    // matter what state the animator believes it is in. Entry is an instant
    // cut by design: combo input must respond on the same frame.
    public void PlayAttack(int actionIndex)
    {
        if (actionIndex < 0 || actionIndex >= comboStateHashes.Length)
        {
            Debug.LogError(
                $"No combo state for action index {actionIndex}.",
                this);
            return;
        }

        // A pending AttackEnd would pull the new attack straight back out
        // through the AnyState return transition.
        animator.ResetTrigger(AttackEndHash);
        animator.Play(comboStateHashes[actionIndex], 0, 0f);
    }

    public void PlayDash()
    {
        animator.SetTrigger(DashHash);
    }

    public void EndAttack()
    {
        animator.SetTrigger(AttackEndHash);
    }
}
