using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Moves the enemy and decides when it acts; EnemySkillSelector decides what.
// Once close to the target the enemy is engaged: it reacts to the player when
// pressed, goes in for an attack when it holds an attack token
// (EnemyAttackDirector), and otherwise waits its turn on a ring around the
// target, spread from the others. A running skill is never cut short.
public sealed class EnemyAIController : MonoBehaviour
{
    private enum AIState
    {
        Idle,
        Chase,
        Engage
    }

    // Everyone engaged right now, so a waiting enemy can keep clear of the
    // others' spots.
    private static readonly List<EnemyAIController> Engaged = new();

    [SerializeField, Min(0f)] private float aggroRange = 10f;
    [Tooltip("Reach of attacks whose selector entry gives no range of its own.")]
    [SerializeField, Min(0f)] private float attackRange = 3f;
    [Tooltip("An engaged enemy without a token goes back to chasing once the " +
        "target is this many times the engage distance away.")]
    [SerializeField, Min(1f)] private float attackRangeExitMultiplier = 1.15f;

    [Header("Waiting for a turn")]
    [Tooltip("Distance from the target that enemies without an attack token keep.")]
    [SerializeField, Min(0f)] private float waitRadius = 3.5f;
    [SerializeField, Min(0f)] private float waitRadiusJitter = 0.5f;
    [Tooltip("Roughly how often a waiting enemy moves to another spot.")]
    [SerializeField, Min(0.1f)] private float repositionInterval = 2.5f;
    [SerializeField, Min(0f)] private float arriveDistance = 0.3f;
    [Tooltip("A token not turned into an attack within this long goes back.")]
    [SerializeField, Min(0.1f)] private float approachTimeout = 3f;

    private NavMeshAgent agent;
    private CharacterMovement characterMovement;
    private EnemySkillSelector skillSelector;
    private SkillController skillController;
    private CharacterAim characterAim;
    private Health health;
    private Transform target;
    private Health targetHealth;
    private AIState currentState;
    private bool engaged;

    private int plannedSlot = -1;
    private float approachTime;
    private bool attackInFlight;
    private float waitStart = -1f;
    private Vector3 waitPoint;
    private bool hasWaitPoint;
    private float nextRepositionTime;

    // For EnemyAttackDirector's choice of who goes next.
    public float TokenWaitTime => waitStart < 0f ? 0f : Time.time - waitStart;
    public float TargetDistance => target != null ? GetFlatDistance() : float.MaxValue;

    private float EngageDistance => Mathf.Max(waitRadius + waitRadiusJitter, attackRange);

    // Chases the target from anywhere, not only once it comes within aggro
    // range - for enemies spawned into a fight already going on.
    public void Engage()
    {
        engaged = true;
    }

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        characterMovement = GetComponent<CharacterMovement>();
        skillSelector = GetComponent<EnemySkillSelector>();
        skillController = GetComponent<SkillController>();
        health = GetComponent<Health>();

        // Optional: without it the body just never turns.
        characterAim = GetComponent<CharacterAim>();

        agent.updatePosition = false;
        agent.updateRotation = false;
    }

    private void OnEnable()
    {
        skillController.PhaseChanged += OnPhaseChanged;
        skillController.SkillEnded += OnAttackOver;
    }

    private void OnDisable()
    {
        skillController.PhaseChanged -= OnPhaseChanged;
        skillController.SkillEnded -= OnAttackOver;
        attackInFlight = false;
        LeaveEngage();
    }

    // The token goes back once the hit is out: the next enemy's wind-up can
    // overlap this one's recovery, but never its active frames.
    private void OnPhaseChanged(SkillPhase phase)
    {
        if (phase == SkillPhase.Recovery)
        {
            OnAttackOver();
        }
    }

    // Also on the skill ending, for an attack cut short before its recovery.
    private void OnAttackOver()
    {
        if (attackInFlight)
        {
            attackInFlight = false;
            ReleaseToken();
        }
    }

    private void Start()
    {
        target = PlayerReference.Instance != null
            ? PlayerReference.Instance.Target
            : null;
        targetHealth = target != null
            ? target.GetComponent<Health>()
            : null;

        ChangeState(AIState.Idle);
    }

    private void Update()
    {
        // Its own death is taken care of by CharacterDeath, which switches
        // this off along with the agent; this only keeps it idle meanwhile.
        if (target == null || health.IsDepleted)
        {
            return;
        }

        // Stands down over a dead target rather than swinging at it.
        if (targetHealth != null && targetHealth.IsDepleted)
        {
            if (currentState != AIState.Idle && !skillController.IsExecuting)
            {
                ChangeState(AIState.Idle);
            }

            return;
        }

        float distance = GetFlatDistance();
        UpdateState(distance);
        TickState(distance);
        UpdateFacing();
    }

    // Faces the swing while attacking, the path while moving, and the target
    // otherwise, so the body always shows where a hit will come from. A
    // staggered body stays as it was knocked, and an idle one as it stood.
    private void UpdateFacing()
    {
        if (characterAim == null ||
            currentState == AIState.Idle ||
            characterMovement.IsLocked)
        {
            return;
        }

        Vector3 direction = skillController.IsExecuting
            ? skillController.CurrentDirection
            : characterMovement.IsMoving
                ? characterMovement.MoveDirection
                : target.position - transform.position;

        characterAim.FaceDirection(direction);
    }

    private void LateUpdate()
    {
        if (!agent.isActiveAndEnabled)
        {
            return;
        }

        agent.nextPosition = transform.position;
    }

    private float GetFlatDistance()
    {
        return FlatDistance(target.position, transform.position);
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        Vector3 offset = a - b;
        offset.y = 0f;
        return offset.magnitude;
    }

    private void UpdateState(float distance)
    {
        switch (currentState)
        {
            case AIState.Idle:
                if (engaged || distance <= aggroRange)
                {
                    ChangeState(AIState.Chase);
                }
                break;

            case AIState.Chase:
                if (distance <= EngageDistance)
                {
                    ChangeState(AIState.Engage);
                }
                break;

            case AIState.Engage:
                if (distance > EngageDistance * attackRangeExitMultiplier &&
                    !skillController.IsExecuting &&
                    !HoldsToken())
                {
                    ChangeState(AIState.Chase);
                }
                break;
        }
    }

    private void ChangeState(AIState next)
    {
        if (currentState == AIState.Engage && next != AIState.Engage)
        {
            LeaveEngage();
        }

        currentState = next;

        switch (next)
        {
            case AIState.Idle:
                StopMoving();
                break;

            case AIState.Engage:
                if (!Engaged.Contains(this))
                {
                    Engaged.Add(this);
                }
                break;
        }
    }

    private void LeaveEngage()
    {
        Engaged.Remove(this);
        ReleaseToken();
        plannedSlot = -1;
        waitStart = -1f;
        hasWaitPoint = false;
    }

    private void TickState(float distance)
    {
        switch (currentState)
        {
            case AIState.Chase:
                MoveTo(target.position);
                break;

            case AIState.Engage:
                TickEngage(distance);
                break;
        }
    }

    private void TickEngage(float distance)
    {
        if (skillController.IsExecuting)
        {
            StopMoving();
            skillSelector.TickExecuting(target);
            return;
        }

        // Staggered: the turn is lost, the token goes back.
        if (characterMovement.IsLocked)
        {
            StopMoving();
            ReleaseToken();
            plannedSlot = -1;
            return;
        }

        if (skillSelector.TryReact(target, distance))
        {
            StopMoving();
            ReleaseToken();
            plannedSlot = -1;
            return;
        }

        if (HoldsToken())
        {
            TickAttackRun(distance);
            return;
        }

        if (skillSelector.HasReadyAttack())
        {
            if (waitStart < 0f)
            {
                waitStart = Time.time;
            }

            RequestToken();
        }
        else
        {
            waitStart = -1f;
        }

        if (skillSelector.TryUseFreeSkill(target, distance, attackRange))
        {
            StopMoving();
            return;
        }

        TickWaiting();
    }

    // With a token: pick an attack, close to its range, and use it.
    private void TickAttackRun(float distance)
    {
        waitStart = -1f;
        hasWaitPoint = false;

        if (plannedSlot < 0 || !skillSelector.IsReady(plannedSlot))
        {
            plannedSlot = skillSelector.PickAttack(distance, attackRange);
            approachTime = 0f;
        }

        // Too close for the one it went in for: switch to one that closing
        // in can still bring into range - between two attacks' ranges it
        // just keeps pressing in until the shorter one reaches.
        if (plannedSlot >= 0 && skillSelector.IsTooClose(plannedSlot, distance))
        {
            plannedSlot = skillSelector.PickCloserAttack(distance, attackRange);
        }

        if (plannedSlot < 0)
        {
            ReleaseToken();
            TickWaiting();
            return;
        }

        if (skillSelector.IsInRange(plannedSlot, distance, attackRange))
        {
            StopMoving();

            // Held until the hit is out (OnPhaseChanged), so no more than
            // the token count are ever mid-swing at once.
            if (skillSelector.Use(plannedSlot, target))
            {
                attackInFlight = true;
            }

            plannedSlot = -1;
            return;
        }

        approachTime += Time.deltaTime;

        if (approachTime > approachTimeout)
        {
            ReleaseToken();
            plannedSlot = -1;
            StopMoving();
            return;
        }

        MoveTo(target.position);
    }

    // Without a token: hold a spot on the ring around the target, moving to
    // another now and then, spread from the other waiting enemies.
    private void TickWaiting()
    {
        bool driftedOff = hasWaitPoint &&
            Mathf.Abs(FlatDistance(waitPoint, target.position) - waitRadius) >
                waitRadiusJitter + 1f;

        if (!hasWaitPoint || driftedOff || Time.time >= nextRepositionTime)
        {
            hasWaitPoint = TryPickWaitPoint(out waitPoint);
            nextRepositionTime = Time.time +
                repositionInterval * Random.Range(0.8f, 1.25f);
        }

        if (hasWaitPoint &&
            FlatDistance(transform.position, waitPoint) > arriveDistance)
        {
            MoveTo(waitPoint);
        }
        else
        {
            StopMoving();
        }
    }

    private bool TryPickWaitPoint(out Vector3 point)
    {
        Vector3 fromTarget = transform.position - target.position;
        fromTarget.y = 0f;
        float baseAngle = Mathf.Atan2(fromTarget.x, fromTarget.z);
        float radius = waitRadius + Random.Range(-waitRadiusJitter, waitRadiusJitter);

        point = transform.position;
        float bestScore = float.NegativeInfinity;

        for (int step = -3; step <= 3; step++)
        {
            float angle = baseAngle + step * 20f * Mathf.Deg2Rad;
            Vector3 candidate = target.position +
                new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius;

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 1f, NavMesh.AllAreas))
            {
                continue;
            }

            // Room from the others, capped so a crowd-free spot doesn't win
            // just for being far; a little randomness keeps the waiting
            // enemies drifting rather than parked.
            float room = 3f;

            for (int i = 0; i < Engaged.Count; i++)
            {
                EnemyAIController other = Engaged[i];

                if (other == this || other == null)
                {
                    continue;
                }

                Vector3 otherSpot = other.hasWaitPoint
                    ? other.waitPoint
                    : other.transform.position;
                room = Mathf.Min(room, FlatDistance(hit.position, otherSpot));
            }

            float score = room + Random.Range(0f, 0.6f) - 0.1f * Mathf.Abs(step);

            if (score > bestScore)
            {
                bestScore = score;
                point = hit.position;
            }
        }

        return !float.IsNegativeInfinity(bestScore);
    }

    private bool HoldsToken()
    {
        EnemyAttackDirector director = EnemyAttackDirector.Instance;
        return director == null || director.Holds(this);
    }

    private void RequestToken()
    {
        EnemyAttackDirector director = EnemyAttackDirector.Instance;

        if (director != null)
        {
            director.Request(this);
        }
    }

    private void ReleaseToken()
    {
        EnemyAttackDirector director = EnemyAttackDirector.Instance;

        if (director != null)
        {
            director.Release(this);
        }
    }

    private void MoveTo(Vector3 destination)
    {
        if (!agent.isActiveAndEnabled)
        {
            return;
        }

        agent.isStopped = false;
        agent.SetDestination(destination);

        Vector3 direction = agent.desiredVelocity;
        direction.y = 0f;
        characterMovement.Move(direction);
    }

    private void StopMoving()
    {
        if (agent.isActiveAndEnabled)
        {
            agent.isStopped = true;
        }

        characterMovement.Move(Vector3.zero);
    }
}
