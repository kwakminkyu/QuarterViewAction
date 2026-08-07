using System;
using UnityEngine;

public sealed class SkillInstance
{
    public SkillDefinition Definition { get; }
    public float RemainingCooldown { get; private set; }
    public bool IsReady => RemainingCooldown <= 0f;

    public SkillInstance(SkillDefinition definition)
    {
        Definition = definition != null
            ? definition
            : throw new ArgumentNullException(nameof(definition));
    }

    public void UpdateCooldown(float deltaTime)
    {
        if (deltaTime <= 0f || RemainingCooldown <= 0f)
        {
            return;
        }

        RemainingCooldown = Mathf.Max(
            RemainingCooldown - deltaTime,
            0f);
    }

    public void StartCooldown()
    {
        RemainingCooldown = Definition.cooldown;
    }
}
