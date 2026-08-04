using System;
using UnityEngine;

public enum SkillPhase
{
    Startup,
    Active,
    Recovery,
    Finished
}

[RequireComponent(typeof(CharacterMovement))]
public sealed class SkillController : MonoBehaviour
{
    private const int MaxPhaseTransitionsPerUpdate = 16;

    [SerializeField] private SkillDefinition[] equippedSkills =
        Array.Empty<SkillDefinition>();

    private CharacterMovement movement;
    private Health health;
    private SkillInstance[] skillInstances =
        Array.Empty<SkillInstance>();

    private SkillInstance currentSkill;
    private int currentActionIndex = -1;
    private SkillPhase currentPhase = SkillPhase.Finished;
    private float phaseElapsedTime;
    private Vector3 currentDirection;
    private Transform currentTarget;
    private bool isFinishingSkill;

    public bool IsExecuting => currentSkill != null;
    public SkillPhase CurrentPhase => currentPhase;
    public int CurrentActionIndex => currentActionIndex;
    public int SkillSlotCount => skillInstances.Length;

    private void Awake()
    {
        movement = GetComponent<CharacterMovement>();
        health = GetComponent<Health>();
        CreateSkillInstances();
    }

    private void OnEnable()
    {
        if (health != null)
        {
            health.Depleted += OnHealthDepleted;
        }
    }

    private void OnDisable()
    {
        if (health != null)
        {
            health.Depleted -= OnHealthDepleted;
        }

        CancelCurrentSkill();
    }

    private void Update()
    {
        float deltaTime = Time.deltaTime;
        UpdateCooldowns(deltaTime);
        UpdateCurrentExecution(deltaTime);
    }

    public bool TryUseSkill(
        int slotIndex,
        Vector3 direction,
        Transform target = null)
    {
        if (isFinishingSkill ||
            !TryGetSkillInstance(slotIndex, out SkillInstance skill))
        {
            return false;
        }

        if (currentSkill != null)
        {
            return TryContinueCombo(skill, direction, target);
        }

        if (!skill.IsReady ||
            !TryGetAction(skill.Definition, 0, out _))
        {
            return false;
        }

        BeginSkill(skill, direction, target);

        if (!skill.Definition.IsCombo)
        {
            skill.StartCooldown();
        }

        UpdateCurrentExecution(0f);
        return true;
    }

    public void CancelCurrentSkill()
    {
        FinishCurrentSkill();
    }

    public SkillInstance GetSkillInstance(int slotIndex)
    {
        if (!TryGetSkillInstance(slotIndex, out SkillInstance skill))
        {
            throw new ArgumentOutOfRangeException(nameof(slotIndex));
        }

        return skill;
    }

    private void CreateSkillInstances()
    {
        int skillCount = equippedSkills?.Length ?? 0;
        skillInstances = new SkillInstance[skillCount];

        for (int i = 0; i < skillCount; i++)
        {
            SkillDefinition definition = equippedSkills[i];

            if (definition != null)
            {
                skillInstances[i] = new SkillInstance(definition);
            }
        }
    }

    private void UpdateCooldowns(float deltaTime)
    {
        for (int i = 0; i < skillInstances.Length; i++)
        {
            skillInstances[i]?.UpdateCooldown(deltaTime);
        }
    }

    private void BeginSkill(
        SkillInstance skill,
        Vector3 direction,
        Transform target)
    {
        currentSkill = skill;
        currentActionIndex = 0;
        currentDirection = direction;
        currentTarget = target;
        ChangePhase(SkillPhase.Startup);
        movement.BeginSkillControl();
    }

    private void UpdateCurrentExecution(float deltaTime)
    {
        float remainingTime = Mathf.Max(0f, deltaTime);
        int transitionCount = 0;

        while (currentSkill != null)
        {
            transitionCount++;

            if (transitionCount > MaxPhaseTransitionsPerUpdate)
            {
                Debug.LogError(
                    "Skill execution exceeded the phase transition limit.",
                    this);
                FinishCurrentSkill();
                return;
            }

            SkillInstance executingSkill = currentSkill;
            int executingActionIndex = currentActionIndex;

            if (!TryGetAction(
                    executingSkill.Definition,
                    executingActionIndex,
                    out SkillAction action))
            {
                Debug.LogError(
                    "The current skill action is missing.",
                    this);
                FinishCurrentSkill();
                return;
            }

            switch (currentPhase)
            {
                case SkillPhase.Startup:
                    ConsumePhaseTime(
                        action.StartupDuration,
                        ref remainingTime);

                    if (!IsPhaseComplete(action.StartupDuration))
                    {
                        return;
                    }

                    ChangePhase(SkillPhase.Active);
                    InvokeActionEnter(action);

                    if (!IsStillExecuting(
                            executingSkill,
                            executingActionIndex))
                    {
                        return;
                    }

                    break;

                case SkillPhase.Active:
                    float activeDeltaTime = ConsumePhaseTime(
                        action.ActiveDuration,
                        ref remainingTime);

                    if (activeDeltaTime > 0f)
                    {
                        InvokeActionUpdate(action, activeDeltaTime);
                    }

                    if (!IsStillExecuting(
                            executingSkill,
                            executingActionIndex))
                    {
                        return;
                    }

                    if (!IsPhaseComplete(action.ActiveDuration))
                    {
                        return;
                    }

                    InvokeActionExit(action);

                    if (!IsStillExecuting(
                            executingSkill,
                            executingActionIndex))
                    {
                        return;
                    }

                    ChangePhase(SkillPhase.Recovery);
                    break;

                case SkillPhase.Recovery:
                    ConsumePhaseTime(
                        action.RecoveryDuration,
                        ref remainingTime);

                    if (!IsPhaseComplete(action.RecoveryDuration))
                    {
                        return;
                    }

                    FinishCurrentSkill();
                    return;

                case SkillPhase.Finished:
                    FinishCurrentSkill();
                    return;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    private bool TryContinueCombo(
        SkillInstance requestedSkill,
        Vector3 direction,
        Transform target)
    {
        if (!ReferenceEquals(currentSkill, requestedSkill) ||
            !requestedSkill.Definition.IsCombo ||
            currentPhase != SkillPhase.Recovery ||
            currentActionIndex >=
                requestedSkill.Definition.ActionCount - 1 ||
            !TryGetAction(
                requestedSkill.Definition,
                currentActionIndex + 1,
                out _))
        {
            return false;
        }

        currentActionIndex++;
        currentDirection = direction;
        currentTarget = target;
        ChangePhase(SkillPhase.Startup);
        UpdateCurrentExecution(0f);
        return true;
    }

    private float ConsumePhaseTime(
        float phaseDuration,
        ref float remainingTime)
    {
        float timeUntilPhaseEnd = Mathf.Max(
            phaseDuration - phaseElapsedTime,
            0f);
        float consumedTime = Mathf.Min(
            remainingTime,
            timeUntilPhaseEnd);

        phaseElapsedTime += consumedTime;
        remainingTime -= consumedTime;
        return consumedTime;
    }

    private bool IsPhaseComplete(float phaseDuration)
    {
        return phaseElapsedTime >= phaseDuration;
    }

    private bool IsStillExecuting(
        SkillInstance skill,
        int actionIndex)
    {
        return ReferenceEquals(currentSkill, skill) &&
            currentActionIndex == actionIndex;
    }

    private void ChangePhase(SkillPhase phase)
    {
        currentPhase = phase;
        phaseElapsedTime = 0f;
    }

    private void InvokeActionEnter(SkillAction action)
    {
        SkillActionContext context = CreateActionContext(0f);
        action.OnActiveEnter(in context);
    }

    private void InvokeActionUpdate(
        SkillAction action,
        float deltaTime)
    {
        SkillActionContext context = CreateActionContext(deltaTime);
        action.OnActiveUpdate(in context);
    }

    private void InvokeActionExit(SkillAction action)
    {
        SkillActionContext context = CreateActionContext(0f);
        action.OnActiveExit(in context);
    }

    private SkillActionContext CreateActionContext(float deltaTime)
    {
        return new SkillActionContext(
            gameObject,
            movement,
            currentSkill.Definition,
            currentActionIndex,
            currentDirection,
            currentTarget,
            phaseElapsedTime,
            deltaTime);
    }

    private void FinishCurrentSkill()
    {
        SkillInstance finishingSkill = currentSkill;

        if (finishingSkill == null || isFinishingSkill)
        {
            return;
        }

        SkillAction action = null;
        bool shouldExitAction =
            currentPhase == SkillPhase.Active &&
            TryGetAction(
                finishingSkill.Definition,
                currentActionIndex,
                out action);
        SkillActionContext exitContext = shouldExitAction
            ? CreateActionContext(0f)
            : default;

        isFinishingSkill = true;
        ClearCurrentExecution();

        try
        {
            if (shouldExitAction)
            {
                action.OnActiveExit(in exitContext);
            }
        }
        finally
        {
            movement.EndSkillControl();

            if (finishingSkill.Definition.IsCombo)
            {
                finishingSkill.StartCooldown();
            }

            isFinishingSkill = false;
        }
    }

    private void ClearCurrentExecution()
    {
        currentSkill = null;
        currentActionIndex = -1;
        currentPhase = SkillPhase.Finished;
        phaseElapsedTime = 0f;
        currentDirection = Vector3.zero;
        currentTarget = null;
    }

    private bool TryGetSkillInstance(
        int slotIndex,
        out SkillInstance skill)
    {
        if (slotIndex < 0 || slotIndex >= skillInstances.Length)
        {
            skill = null;
            return false;
        }

        skill = skillInstances[slotIndex];
        return skill != null;
    }

    private static bool TryGetAction(
        SkillDefinition definition,
        int actionIndex,
        out SkillAction action)
    {
        if (definition == null ||
            actionIndex < 0 ||
            actionIndex >= definition.ActionCount)
        {
            action = null;
            return false;
        }

        action = definition.GetAction(actionIndex);
        return action != null;
    }

    private void OnHealthDepleted()
    {
        CancelCurrentSkill();
    }
}
