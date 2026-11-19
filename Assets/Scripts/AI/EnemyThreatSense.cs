using System;
using UnityEngine;

// What a reaction can listen for. Kept as separate pressures so each reaction
// picks the signals it cares about - a hop away on being crowded, a counter
// on being dashed at.
[Flags]
public enum ThreatSignal
{
    None = 0,
    // A player attack whose hit area covers this enemy as it starts.
    Attack = 1 << 0,
    // A player dash aimed at this enemy.
    Dash = 1 << 1,
    // The player closing in fast.
    Approach = 1 << 2,
    // The player standing right next to this enemy.
    Proximity = 1 << 3,
}

// How hard the player is pressing this enemy, per signal. Each pressure rises
// with what the player does and drains over time; reactions read and consume
// it. Measuring only - deciding what to do about it is EnemySkillSelector's.
public sealed class EnemyThreatSense : MonoBehaviour
{
    private const int SignalCount = 4;

    [Header("Attack aimed at me")]
    [SerializeField, Min(0f)] private float attackPressure = 1f;
    [Tooltip("Added around the attack's hit area, so a swing that only just " +
        "misses still counts as aimed here.")]
    [SerializeField, Min(0f)] private float attackMargin = 0.4f;

    [Header("Dash at me")]
    [SerializeField, Min(0f)] private float dashPressure = 0.8f;
    [SerializeField, Range(0f, 90f)] private float dashAngle = 35f;
    [Tooltip("Added to the dash distance.")]
    [SerializeField, Min(0f)] private float dashMargin = 1f;

    [Header("Approach")]
    [SerializeField, Min(0f)] private float approachRange = 2.5f;
    [Tooltip("Speed toward this enemy, in metres per second, above which the " +
        "player counts as closing in.")]
    [SerializeField, Min(0f)] private float approachSpeed = 3f;
    [SerializeField, Min(0f)] private float approachPerSecond = 2.5f;

    [Header("Proximity")]
    [Tooltip("Keep it inside the distance this enemy attacks from, or its own " +
        "approach would count as being crowded.")]
    [SerializeField, Min(0f)] private float proximityRange = 0.9f;
    [SerializeField, Min(0f)] private float proximityPerSecond = 1.5f;

    [Header("Memory")]
    [Tooltip("Drains every signal; the per-second rises above must beat it " +
        "for a steady push to build up.")]
    [SerializeField, Min(0f)] private float decayPerSecond = 0.5f;
    [SerializeField, Min(0f)] private float maxPressure = 2f;

    private readonly float[] pressure = new float[SignalCount];
    private Transform player;
    private SkillController playerSkills;
    private CharacterController body;
    private SkillController ownSkills;
    private Vector3 lastPlayerPosition;
    private bool hasLastPlayerPosition;

    private void Awake()
    {
        body = GetComponent<CharacterController>();
        ownSkills = GetComponent<SkillController>();
    }

    private void Start()
    {
        player = PlayerReference.Instance != null
            ? PlayerReference.Instance.Target
            : null;
        playerSkills = player != null
            ? player.GetComponent<SkillController>()
            : null;

        if (playerSkills != null)
        {
            playerSkills.ActionStarted += OnPlayerActionStarted;
        }
    }

    private void OnDestroy()
    {
        if (playerSkills != null)
        {
            playerSkills.ActionStarted -= OnPlayerActionStarted;
        }
    }

    private void OnDisable()
    {
        Array.Clear(pressure, 0, pressure.Length);
        hasLastPlayerPosition = false;
    }

    // Sum of the asked-for signals.
    public float Get(ThreatSignal signals)
    {
        float total = 0f;

        for (int i = 0; i < SignalCount; i++)
        {
            if (((int)signals & (1 << i)) != 0)
            {
                total += pressure[i];
            }
        }

        return total;
    }

    // Spent by a reaction, so one burst of pressure answers once.
    public void Consume(ThreatSignal signals)
    {
        for (int i = 0; i < SignalCount; i++)
        {
            if (((int)signals & (1 << i)) != 0)
            {
                pressure[i] = 0f;
            }
        }
    }

    private void Update()
    {
        if (player == null)
        {
            return;
        }

        float deltaTime = Time.deltaTime;

        for (int i = 0; i < SignalCount; i++)
        {
            pressure[i] = Mathf.Max(pressure[i] - decayPerSecond * deltaTime, 0f);
        }

        Vector3 toMe = Flat(transform.position - player.position);
        float distance = toMe.magnitude;
        Vector3 playerPosition = player.position;

        // Its own moves put it next to the player too - a lunge ending
        // beside them - and that is not the player pressing.
        bool ownMove = ownSkills != null && ownSkills.IsExecuting;

        // Measured from the player's own motion, not the distance between
        // the two, so this enemy chasing the player never counts as being
        // approached.
        if (!ownMove && hasLastPlayerPosition && deltaTime > Mathf.Epsilon &&
            distance <= approachRange && distance > Mathf.Epsilon)
        {
            Vector3 velocity = Flat(playerPosition - lastPlayerPosition) / deltaTime;
            float towardMe = Vector3.Dot(velocity, toMe / distance);

            if (towardMe >= approachSpeed)
            {
                Add(ThreatSignal.Approach, approachPerSecond * deltaTime);
            }
        }

        if (!ownMove && distance <= proximityRange)
        {
            Add(ThreatSignal.Proximity, proximityPerSecond * deltaTime);
        }

        lastPlayerPosition = playerPosition;
        hasLastPlayerPosition = true;
    }

    private void OnPlayerActionStarted(SkillAction action)
    {
        if (player == null || !isActiveAndEnabled)
        {
            return;
        }

        Vector3 direction = Flat(playerSkills.CurrentDirection);

        if (direction.sqrMagnitude <= Mathf.Epsilon)
        {
            direction = Flat(player.forward);
        }

        if (direction.sqrMagnitude <= Mathf.Epsilon)
        {
            return;
        }

        direction.Normalize();
        Vector3 toMe = Flat(transform.position - player.position);

        if (action is DashSkillAction)
        {
            float reach = Mathf.Abs(action.movementSettings.lungeDistance) + dashMargin;

            if (toMe.magnitude <= reach &&
                Vector3.Angle(direction, toMe) <= dashAngle)
            {
                Add(ThreatSignal.Dash, dashPressure);
            }

            return;
        }

        if (!action.TryGetHitArea(out Vector3 offset, out Vector2 size))
        {
            return;
        }

        // The same ground AttackTelegraph warns about: the hit box swept
        // forward by the action's lunge.
        float lunge = action.ActiveLungeDistance;
        Vector3 local = Quaternion.Inverse(Quaternion.LookRotation(direction)) * toMe;
        float margin = attackMargin + (body != null ? body.radius : 0f);
        float halfWidth = size.x * 0.5f + margin;
        float halfLength = (size.y + lunge) * 0.5f + margin;
        float centreZ = offset.z + lunge * 0.5f;

        if (Mathf.Abs(local.x - offset.x) <= halfWidth &&
            Mathf.Abs(local.z - centreZ) <= halfLength)
        {
            Add(ThreatSignal.Attack, attackPressure);
        }
    }

    private void Add(ThreatSignal signal, float amount)
    {
        int index = SignalIndex(signal);
        pressure[index] = Mathf.Min(pressure[index] + amount, maxPressure);
    }

    private static int SignalIndex(ThreatSignal signal)
    {
        int value = (int)signal;
        int index = 0;

        while (value > 1)
        {
            value >>= 1;
            index++;
        }

        return index;
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }
}
