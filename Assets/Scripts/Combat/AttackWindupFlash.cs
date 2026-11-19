using UnityEngine;

// A glint on the body as an attack winds up, on top of the ground warning, so
// a coming hit reads even in a crowd. Only actions with a hit area count;
// moves such as a back flip stay quiet. Drawn through HitReaction's flash.
[RequireComponent(typeof(SkillController))]
public sealed class AttackWindupFlash : MonoBehaviour
{
    [SerializeField] private Color color = new Color(1f, 0.25f, 0.15f, 0.85f);
    [SerializeField, Min(0f)] private float duration = 0.15f;

    private SkillController skillController;
    private HitReaction hitReaction;

    private void Awake()
    {
        skillController = GetComponent<SkillController>();
        hitReaction = GetComponentInChildren<HitReaction>();
    }

    private void OnEnable()
    {
        skillController.ActionStarted += OnActionStarted;
    }

    private void OnDisable()
    {
        skillController.ActionStarted -= OnActionStarted;
    }

    private void OnActionStarted(SkillAction action)
    {
        if (hitReaction == null || !action.TryGetHitArea(out _, out _))
        {
            return;
        }

        hitReaction.Flash(color, duration);
    }
}
