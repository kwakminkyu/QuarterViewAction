using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "SkillDefinition", menuName = "Skills/Skill Definition")]
public sealed class SkillDefinition : ScriptableObject
{
    [SerializeField] private string skillId;
    [SerializeField] private float cooldown;
    [SerializeField] private bool canStartDuringRecovery;
    [SerializeField] private SkillAction[] actions =
        Array.Empty<SkillAction>();

    public string SkillId => skillId;
    public float Cooldown => Mathf.Max(0f, cooldown);
    public bool CanStartDuringRecovery => canStartDuringRecovery;
    public IReadOnlyList<SkillAction> Actions =>
        actions ?? Array.Empty<SkillAction>();
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
