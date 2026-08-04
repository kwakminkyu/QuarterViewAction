using UnityEngine;

public abstract class SkillAction : ScriptableObject
{
    [SerializeField] private float startupDuration;
    [SerializeField] private float activeDuration;
    [SerializeField] private float recoveryDuration;

    public float StartupDuration => Mathf.Max(0f, startupDuration);
    public float ActiveDuration => Mathf.Max(0f, activeDuration);
    public float RecoveryDuration => Mathf.Max(0f, recoveryDuration);

    public virtual void OnActiveEnter(in SkillActionContext context)
    {
    }

    public virtual void OnActiveUpdate(in SkillActionContext context)
    {
    }

    public virtual void OnActiveExit(in SkillActionContext context)
    {
    }
}
