using System;
using UnityEngine;

[CreateAssetMenu(fileName = "SkillDefinition", menuName = "Skills/Skill Definition")]
public sealed class SkillDefinition : ScriptableObject
{
    public string skillId;
    [Min(0f)] public float cooldown;
    public bool canStartDuringRecovery;

    // Running skills this one may cut off in any phase and start at once,
    // such as a dash attack out of a dash. Anything not listed still waits
    // for its recovery (canStartDuringRecovery) or its end.
    public SkillDefinition[] interrupts =
        Array.Empty<SkillDefinition>();

    public SkillAction[] actions =
        Array.Empty<SkillAction>();

    public int ActionCount => actions?.Length ?? 0;
    public bool IsCombo => ActionCount > 1;

    public bool CanInterrupt(SkillDefinition running)
    {
        return running != null &&
            interrupts != null &&
            Array.IndexOf(interrupts, running) >= 0;
    }

    public SkillAction GetAction(int index)
    {
        if (actions == null || index < 0 || index >= actions.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return actions[index];
    }
}
