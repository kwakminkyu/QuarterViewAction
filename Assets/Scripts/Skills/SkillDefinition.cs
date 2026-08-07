using System;
using UnityEngine;

[CreateAssetMenu(fileName = "SkillDefinition", menuName = "Skills/Skill Definition")]
public sealed class SkillDefinition : ScriptableObject
{
    public string skillId;
    [Min(0f)] public float cooldown;
    public bool canStartDuringRecovery;
    public SkillAction[] actions =
        Array.Empty<SkillAction>();

    public int ActionCount => actions?.Length ?? 0;
    public bool IsCombo => ActionCount > 1;

    public SkillAction GetAction(int index)
    {
        if (actions == null || index < 0 || index >= actions.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return actions[index];
    }
}
