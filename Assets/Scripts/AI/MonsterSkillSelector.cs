using System;
using UnityEngine;

[RequireComponent(typeof(SkillController))]
public sealed class MonsterSkillSelector : MonoBehaviour
{
    [Serializable]
    private sealed class WeightedSkill
    {
        [Min(0)] public int slotIndex = 0;
        [Min(0f)] public float weight = 1f;
    }

    [SerializeField] private WeightedSkill[] skills =
        Array.Empty<WeightedSkill>();
    [SerializeField, Range(0f, 1f)]
    private float recentSkillWeightMultiplier = 0.4f;
    [SerializeField, Min(0f)]
    private float comboInputSafetyTime = 0.01f;
    public Transform target;

    private SkillController skillController;
    private int selectedSlot = -1;
    private int lastUsedSlot = -1;

    public int SelectedSlot => selectedSlot;
    public int LastUsedSlot => lastUsedSlot;

    private void Awake()
    {
        skillController = GetComponent<SkillController>();
    }

    private void Update()
    {
        if (skillController.IsExecuting)
        {
            TryContinueCombo();
            return;
        }

        selectedSlot = -1;
        TryUseRandomSkill();
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }

    private void TryUseRandomSkill()
    {
        WeightedSkill selectedSkill = SelectWeightedSkill();

        if (selectedSkill == null)
        {
            return;
        }

        int slotIndex = selectedSkill.slotIndex;
        bool accepted = skillController.TryUseSkill(
            slotIndex,
            GetTargetDirection(),
            target);

        if (!accepted)
        {
            return;
        }

        selectedSlot = slotIndex;
        lastUsedSlot = slotIndex;
    }

    private WeightedSkill SelectWeightedSkill()
    {
        float totalWeight = 0f;

        for (int i = 0; i < skills.Length; i++)
        {
            totalWeight += GetAvailableWeight(skills[i]);
        }

        if (totalWeight <= Mathf.Epsilon)
        {
            return null;
        }

        float randomValue = UnityEngine.Random.value * totalWeight;
        WeightedSkill fallback = null;

        for (int i = 0; i < skills.Length; i++)
        {
            WeightedSkill skill = skills[i];
            float weight = GetAvailableWeight(skill);

            if (weight <= 0f)
            {
                continue;
            }

            fallback = skill;
            randomValue -= weight;

            if (randomValue <= 0f)
            {
                return skill;
            }
        }

        return fallback;
    }

    private float GetAvailableWeight(WeightedSkill skill)
    {
        if (skill == null ||
            skill.weight <= 0f ||
            skill.slotIndex < 0 ||
            skill.slotIndex >= skillController.SkillSlotCount)
        {
            return 0f;
        }

        SkillInstance instance = skillController.GetSkillInstance(
            skill.slotIndex);

        if (!instance.IsReady || instance.Definition.ActionCount <= 0)
        {
            return 0f;
        }

        float multiplier = skill.slotIndex == lastUsedSlot
            ? recentSkillWeightMultiplier
            : 1f;
        return skill.weight * multiplier;
    }

    private void TryContinueCombo()
    {
        SkillDefinition definition =
            skillController.CurrentSkillDefinition;

        if (selectedSlot < 0 ||
            definition == null ||
            !definition.IsCombo ||
            skillController.CurrentPhase != SkillPhase.Recovery ||
            skillController.CurrentActionIndex >=
                definition.ActionCount - 1)
        {
            return;
        }

        float requestThreshold =
            Time.deltaTime + comboInputSafetyTime;

        if (skillController.CurrentPhaseRemainingTime >
            requestThreshold)
        {
            return;
        }

        skillController.TryUseSkill(
            selectedSlot,
            GetTargetDirection(),
            target);
    }

    private Vector3 GetTargetDirection()
    {
        if (target == null)
        {
            return transform.forward;
        }

        Vector3 direction = target.position - transform.position;
        direction.y = 0f;

        return direction.sqrMagnitude <= Mathf.Epsilon
            ? transform.forward
            : direction.normalized;
    }
}
