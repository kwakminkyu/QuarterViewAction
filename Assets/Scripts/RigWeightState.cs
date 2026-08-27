using UnityEngine;

public sealed class RigWeightState : StateMachineBehaviour
{
    [SerializeField, Range(0f, 1f)] private float weight = 1f;
    [SerializeField, Min(0f)] private float blendSpeed = 8f;

    private PlayerAnimationController animationController;

    public void SetController(PlayerAnimationController controller)
    {
        animationController = controller;
    }

    public override void OnStateEnter(
        Animator animator,
        AnimatorStateInfo stateInfo,
        int layerIndex)
    {
        if (animationController == null)
        {
            return;
        }

        animationController.SetRigWeightTarget(weight, blendSpeed);
    }
}
