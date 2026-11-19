using System;
using UnityEngine;

// Decides which of the enemy's skills to use; EnemyAIController decides when
// and where. Two lists:
// - skills: the enemy's own picks, weighted, each with the distance it is used
//   from and whether it needs an attack token;
// - reactions: skills fired by what the player does (EnemyThreatSense), checked
//   first whenever the enemy is free. A skill can sit in either list or both.
// Nothing here ever cuts a running skill short.
[RequireComponent(typeof(SkillController))]
public sealed class EnemySkillSelector : MonoBehaviour
{
    [Serializable]
    private sealed class WeightedSkill
    {
        [Min(0)] public int slotIndex = 0;
        [Min(0f)] public float weight = 1f;
        [Tooltip("Used only with the target at least this far away.")]
        [Min(0f)] public float minRange = 0f;
        [Tooltip("Used only with the target within this distance. 0 uses the " +
            "AI's attack range.")]
        [Min(0f)] public float maxRange = 0f;
        [Tooltip("An attack: waits for an attack token. Off for moves that " +
            "never threaten the player.")]
        public bool needsToken = true;
    }

    [Serializable]
    private sealed class Reaction
    {
        [Min(0)] public int slotIndex = 0;
        public ThreatSignal signals =
            ThreatSignal.Dash | ThreatSignal.Approach | ThreatSignal.Proximity;
        [Tooltip("Pressure of the chosen signals, summed, that sets it off.")]
        [Min(0f)] public float threshold = 1f;
        [Range(0f, 1f)] public float chance = 0.6f;
        [Tooltip("Before this reaction can fire again, on top of the skill's " +
            "own cooldown.")]
        [Min(0f)] public float cooldown = 4f;
        [Min(0f)] public float minRange = 0f;
        [Tooltip("0 = any distance.")]
        [Min(0f)] public float maxRange = 0f;

        [NonSerialized] public float readyTime;
    }

    [SerializeField] private WeightedSkill[] skills =
        Array.Empty<WeightedSkill>();
    [Tooltip("Checked in order; the first one that fires wins.")]
    [SerializeField] private Reaction[] reactions =
        Array.Empty<Reaction>();
    [SerializeField, Range(0f, 1f)]
    private float recentSkillWeightMultiplier = 0.4f;
    [SerializeField, Min(0f)]
    private float comboInputSafetyTime = 0.01f;

    private SkillController skillController;
    private EnemyThreatSense threat;
    private int selectedSlot = -1;
    private int lastUsedSlot = -1;

    public int SelectedSlot => selectedSlot;
    public int LastUsedSlot => lastUsedSlot;

    private void Awake()
    {
        skillController = GetComponent<SkillController>();

        // Optional: without it the reactions simply never fire.
        threat = GetComponent<EnemyThreatSense>();
    }

    // While a skill runs: carries a combo on to its next step, nothing else.
    public void TickExecuting(Transform target)
    {
        TryContinueCombo(target);
    }

    // A reaction whose pressure is up, fired at once. Each burst of pressure
    // gets one roll: a failed roll spends it too, or the chance would be
    // retried every frame until it came up.
    public bool TryReact(Transform target, float distance)
    {
        if (threat == null || skillController.IsExecuting)
        {
            return false;
        }

        for (int i = 0; i < reactions.Length; i++)
        {
            Reaction reaction = reactions[i];

            if (reaction == null ||
                Time.time < reaction.readyTime ||
                distance < reaction.minRange ||
                (reaction.maxRange > 0f && distance > reaction.maxRange) ||
                !IsReady(reaction.slotIndex) ||
                threat.Get(reaction.signals) < reaction.threshold)
            {
                continue;
            }

            threat.Consume(reaction.signals);

            if (UnityEngine.Random.value > reaction.chance)
            {
                continue;
            }

            if (Use(reaction.slotIndex, target))
            {
                reaction.readyTime = Time.time + reaction.cooldown;
                return true;
            }
        }

        return false;
    }

    // Whether any attack (token skill) is off cooldown, so asking for a token
    // can lead somewhere.
    public bool HasReadyAttack()
    {
        for (int i = 0; i < skills.Length; i++)
        {
            if (skills[i] != null && skills[i].needsToken &&
                GetAvailableWeight(skills[i]) > 0f)
            {
                return true;
            }
        }

        return false;
    }

    // An attack to go in for, chosen by weight. -1 when there is none.
    public int PickAttack(float distance, float fallbackRange)
    {
        return PickWeighted(true, distance, fallbackRange, RangeFilter.Any);
    }

    // An attack that closing in on the target can still bring into range:
    // none whose minimum distance is already passed. -1 when there is none.
    public int PickCloserAttack(float distance, float fallbackRange)
    {
        return PickWeighted(true, distance, fallbackRange, RangeFilter.NotTooClose);
    }

    // A move that needs no token, usable from here, fired straight away.
    public bool TryUseFreeSkill(Transform target, float distance, float fallbackRange)
    {
        int slot = PickWeighted(false, distance, fallbackRange, RangeFilter.InRange);
        return slot >= 0 && Use(slot, target);
    }

    public bool IsInRange(int slotIndex, float distance, float fallbackRange)
    {
        WeightedSkill skill = FindSkill(slotIndex);
        return skill != null &&
            distance >= skill.minRange &&
            distance <= MaxRange(skill, fallbackRange);
    }

    public bool IsTooClose(int slotIndex, float distance)
    {
        WeightedSkill skill = FindSkill(slotIndex);
        return skill != null && distance < skill.minRange;
    }

    public bool IsReady(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= skillController.SkillSlotCount)
        {
            return false;
        }

        SkillInstance instance = skillController.GetSkillInstance(slotIndex);
        return instance != null &&
            instance.IsReady &&
            instance.Definition.ActionCount > 0;
    }

    public bool Use(int slotIndex, Transform target)
    {
        bool accepted = skillController.TryUseSkill(
            slotIndex,
            GetTargetDirection(target),
            target);

        if (accepted)
        {
            selectedSlot = slotIndex;
            lastUsedSlot = slotIndex;
        }

        return accepted;
    }

    private enum RangeFilter
    {
        Any,
        InRange,
        NotTooClose
    }

    private int PickWeighted(
        bool attacks,
        float distance,
        float fallbackRange,
        RangeFilter filter)
    {
        float totalWeight = 0f;

        for (int i = 0; i < skills.Length; i++)
        {
            totalWeight += CandidateWeight(skills[i], attacks, distance, fallbackRange, filter);
        }

        if (totalWeight <= Mathf.Epsilon)
        {
            return -1;
        }

        float randomValue = UnityEngine.Random.value * totalWeight;
        int fallback = -1;

        for (int i = 0; i < skills.Length; i++)
        {
            float weight = CandidateWeight(skills[i], attacks, distance, fallbackRange, filter);

            if (weight <= 0f)
            {
                continue;
            }

            fallback = skills[i].slotIndex;
            randomValue -= weight;

            if (randomValue <= 0f)
            {
                return skills[i].slotIndex;
            }
        }

        return fallback;
    }

    private float CandidateWeight(
        WeightedSkill skill,
        bool attacks,
        float distance,
        float fallbackRange,
        RangeFilter filter)
    {
        if (skill == null || skill.needsToken != attacks)
        {
            return 0f;
        }

        if (filter != RangeFilter.Any && distance < skill.minRange)
        {
            return 0f;
        }

        if (filter == RangeFilter.InRange && distance > MaxRange(skill, fallbackRange))
        {
            return 0f;
        }

        return GetAvailableWeight(skill);
    }

    private static float MaxRange(WeightedSkill skill, float fallbackRange)
    {
        return skill.maxRange > 0f ? skill.maxRange : fallbackRange;
    }

    private WeightedSkill FindSkill(int slotIndex)
    {
        for (int i = 0; i < skills.Length; i++)
        {
            if (skills[i] != null && skills[i].slotIndex == slotIndex)
            {
                return skills[i];
            }
        }

        return null;
    }

    private float GetAvailableWeight(WeightedSkill skill)
    {
        if (skill == null || skill.weight <= 0f || !IsReady(skill.slotIndex))
        {
            return 0f;
        }

        float multiplier = skill.slotIndex == lastUsedSlot
            ? recentSkillWeightMultiplier
            : 1f;
        return skill.weight * multiplier;
    }

    private void TryContinueCombo(Transform target)
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
            GetTargetDirection(target),
            target);
    }

    private Vector3 GetTargetDirection(Transform target)
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
