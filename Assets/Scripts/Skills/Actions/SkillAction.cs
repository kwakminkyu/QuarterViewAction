using UnityEngine;

public abstract class SkillAction : ScriptableObject
{
    [Min(0f)] public float startupDuration;
    [Min(0f)] public float activeDuration;
    [Min(0f)] public float recoveryDuration;

    public virtual void OnActiveEnter(in SkillActionContext context)
    {
    }

    public virtual void OnActiveUpdate(in SkillActionContext context)
    {
    }

    public virtual void OnActiveExit(in SkillActionContext context)
    {
    }

    public virtual void OnSkillEnd(in SkillActionContext context)
    {
    }
}
