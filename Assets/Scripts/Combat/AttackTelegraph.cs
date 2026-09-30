using System;
using UnityEngine;

// Works out when and where a character's coming hit will land, for anything
// that wants to warn about it. It only tracks the warning - from an action
// starting until its hit comes out, or until the skill is cut short - and
// draws nothing itself; a view such as GroundTelegraphView subscribes and
// decides how the warning looks, so the look can change without this.
[RequireComponent(typeof(SkillController))]
public sealed class AttackTelegraph : MonoBehaviour
{
    private SkillController skillController;
    private SkillAction action;
    private Quaternion facing;
    private Vector3 localCenter;
    private float elapsed;

    public bool IsActive => action != null;

    // 0 when the action starts, 1 as its hit comes out.
    public float Progress => action == null || action.startupDuration <= 0f
        ? 1f
        : Mathf.Clamp01(elapsed / action.startupDuration);

    // Width (x) and length (z) of the covered ground, laid out along Rotation.
    public Vector2 Size { get; private set; }
    public Quaternion Rotation => facing;
    public Vector3 Center => transform.position + facing * localCenter;

    public event Action Started;
    public event Action Ended;

    private void Awake()
    {
        skillController = GetComponent<SkillController>();
    }

    private void OnEnable()
    {
        skillController.ActionStarted += OnActionStarted;
        skillController.SkillEnded += Stop;
    }

    private void OnDisable()
    {
        skillController.ActionStarted -= OnActionStarted;
        skillController.SkillEnded -= Stop;
        Stop();
    }

    private void OnActionStarted(SkillAction started)
    {
        // A combo step replaces the warning of the step before.
        Stop();

        if (!started.TryGetHitArea(out Vector3 offset, out Vector2 size))
        {
            return;
        }

        Vector3 direction = Vector3.ProjectOnPlane(
            skillController.CurrentDirection,
            Vector3.up);

        if (direction.sqrMagnitude <= Mathf.Epsilon)
        {
            direction = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        }

        // The hit travels with a lunge, so the warning stretches forward to
        // cover everywhere the box will sweep.
        float lunge = started.ActiveLungeDistance;

        action = started;
        facing = Quaternion.LookRotation(direction.normalized, Vector3.up);
        localCenter = new Vector3(offset.x, 0f, offset.z + lunge * 0.5f);
        Size = new Vector2(size.x, size.y + lunge);
        elapsed = 0f;

        Started?.Invoke();
    }

    private void Update()
    {
        if (action == null)
        {
            return;
        }

        elapsed += Time.deltaTime;

        // The hit is out: from here the attack itself is the message.
        if (skillController.CurrentPhase != SkillPhase.Startup)
        {
            Stop();
        }
    }

    private void Stop()
    {
        if (action == null)
        {
            return;
        }

        action = null;
        Ended?.Invoke();
    }
}
