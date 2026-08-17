using UnityEngine;
using UnityEngine.AI;

public sealed class MonsterAIController : MonoBehaviour
{
    private enum AIState
    {
        Idle,
        Chase,
        Attack
    }

    [SerializeField, Min(0f)] private float aggroRange = 10f;
    [SerializeField, Min(0f)] private float attackRange = 3f;
    [SerializeField, Min(1f)] private float attackRangeExitMultiplier = 1.15f;

    private NavMeshAgent agent;
    private CharacterMovement characterMovement;
    private MonsterSkillSelector skillSelector;
    private SkillController skillController;
    private Health health;
    private Transform target;
    private AIState currentState;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        characterMovement = GetComponent<CharacterMovement>();
        skillSelector = GetComponent<MonsterSkillSelector>();
        skillController = GetComponent<SkillController>();
        health = GetComponent<Health>();

        agent.updatePosition = false;
        agent.updateRotation = false;
    }

    private void OnEnable()
    {
        health.Depleted += OnDeath;
    }

    private void OnDisable()
    {
        health.Depleted -= OnDeath;
    }

    private void Start()
    {
        target = PlayerReference.Instance != null
            ? PlayerReference.Instance.Target
            : null;

        ChangeState(AIState.Idle);
    }

    private void Update()
    {
        if (target == null || health.IsDepleted)
        {
            return;
        }

        float distance = GetFlatDistance();
        UpdateState(distance);
        TickState();
    }

    private void LateUpdate()
    {
        agent.nextPosition = transform.position;
    }

    private float GetFlatDistance()
    {
        Vector3 offset = target.position - transform.position;
        offset.y = 0f;
        return offset.magnitude;
    }

    private void UpdateState(float distance)
    {
        switch (currentState)
        {
            case AIState.Idle:
                if (distance <= aggroRange)
                {
                    ChangeState(AIState.Chase);
                }
                break;

            case AIState.Chase:
                if (distance <= attackRange)
                {
                    ChangeState(AIState.Attack);
                }
                break;

            case AIState.Attack:
                if (distance > attackRange * attackRangeExitMultiplier &&
                    !skillController.IsExecuting)
                {
                    ChangeState(AIState.Chase);
                }
                break;
        }
    }

    private void ChangeState(AIState next)
    {
        currentState = next;

        switch (next)
        {
            case AIState.Idle:
                agent.isStopped = true;
                characterMovement.Move(Vector3.zero);
                break;

            case AIState.Chase:
                agent.isStopped = false;
                break;

            case AIState.Attack:
                agent.isStopped = true;
                characterMovement.Move(Vector3.zero);
                break;
        }
    }

    private void TickState()
    {
        switch (currentState)
        {
            case AIState.Chase:
                agent.SetDestination(target.position);

                Vector3 direction = agent.desiredVelocity;
                direction.y = 0f;
                characterMovement.Move(direction);
                break;

            case AIState.Attack:
                skillSelector.Tick(target);
                break;
        }
    }

    private void OnDeath()
    {
        enabled = false;
        agent.isStopped = true;
        characterMovement.Move(Vector3.zero);
    }
}
