using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

// Takes a character out of play when its health runs out, the same way for
// the player and for monsters: whatever steers it is switched off, its
// colliders stop blocking and being hit, and the body falls over. Monsters
// then sink into the floor and are removed; the player stays down.
//
// The fall is done by moving the body, not by an animation, so a character
// without a Death motion (every monster today) still reads as dead.
[RequireComponent(typeof(Health))]
public sealed class CharacterDeath : MonoBehaviour
{
    [Tooltip("Whatever drives this character - player input, monster AI. " +
        "Switched off at death so nothing keeps steering the body.")]
    [SerializeField] private Behaviour[] disableOnDeath =
        Array.Empty<Behaviour>();

    [Header("Fall")]
    [Tooltip("What tips over. Leave empty to tip the whole character; " +
        "point it at the model so a camera following the root holds still.")]
    [SerializeField] private Transform body;
    [SerializeField, Range(0f, 90f)] private float fallAngle = 90f;
    [SerializeField, Min(0.01f)] private float fallDuration = 0.35f;

    [Header("Removal")]
    [SerializeField] private bool removeAfterDeath = true;
    // Measured from the end of the fall.
    [SerializeField, Min(0f)] private float removeDelay = 0.6f;
    [SerializeField, Min(0.01f)] private float sinkDuration = 0.8f;
    [SerializeField, Min(0f)] private float sinkDepth = 1.5f;

    private Health health;
    private DamageReceiver damageReceiver;
    private Vector3 lastHitDirection;

    public bool IsDead { get; private set; }

    private void Awake()
    {
        health = GetComponent<Health>();
        damageReceiver = GetComponent<DamageReceiver>();

        if (body == null)
        {
            body = transform;
        }
    }

    private void OnEnable()
    {
        health.Depleted += OnDepleted;

        if (damageReceiver != null)
        {
            damageReceiver.DamageReceived += OnDamageReceived;
        }
    }

    private void OnDisable()
    {
        health.Depleted -= OnDepleted;

        if (damageReceiver != null)
        {
            damageReceiver.DamageReceived -= OnDamageReceived;
        }
    }

    private void OnDamageReceived(DamageInfo info)
    {
        lastHitDirection = info.HitDirection;
    }

    private void OnDepleted()
    {
        if (IsDead)
        {
            return;
        }

        IsDead = true;
        StopCharacter();
        StartCoroutine(PlayDeath());
    }

    private void StopCharacter()
    {
        for (int i = 0; i < disableOnDeath.Length; i++)
        {
            if (disableOnDeath[i] != null)
            {
                disableOnDeath[i].enabled = false;
            }
        }

        if (TryGetComponent(out SkillController skillController))
        {
            skillController.enabled = false;
        }

        if (TryGetComponent(out CharacterMovement movement))
        {
            // Reported first, so the animator drops back out of the run.
            movement.Move(Vector3.zero);
            movement.enabled = false;
        }

        if (TryGetComponent(out NavMeshAgent agent))
        {
            agent.enabled = false;
        }

        // Includes the CharacterController: the body no longer blocks anyone
        // and no longer turns up in an attack's overlap.
        Collider[] colliders = GetComponentsInChildren<Collider>();

        for (int i = 0; i < colliders.Length; i++)
        {
            colliders[i].enabled = false;
        }
    }

    private IEnumerator PlayDeath()
    {
        // Depleted is raised before DamageReceived for the killing blow, so
        // the fall waits a frame to know which way that blow came from.
        yield return null;
        yield return Fall();

        if (!removeAfterDeath)
        {
            yield break;
        }

        if (removeDelay > 0f)
        {
            yield return new WaitForSeconds(removeDelay);
        }

        yield return Sink();
        Destroy(gameObject);
    }

    private IEnumerator Fall()
    {
        Vector3 fallDirection = GetFallDirection();

        // Tipping about the edge of the body that faces the fall, at floor
        // height, leaves it lying on that side on the floor rather than
        // half sunk into it or floating above it.
        // The body is treated as an upright cylinder, so the edge is the same
        // distance out whichever way it falls; the narrower extent keeps a
        // wide sword or arm from lifting it off the floor.
        Bounds bounds = GetBodyBounds();
        float edgeDistance = Mathf.Min(bounds.extents.x, bounds.extents.z);
        Vector3 pivot = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) +
            fallDirection * edgeDistance;
        Vector3 axis = Vector3.Cross(Vector3.up, fallDirection);

        Vector3 startPosition = body.position;
        Quaternion startRotation = body.rotation;
        float elapsed = 0f;

        while (elapsed < fallDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fallDuration);

            // Eased in: slow to leave upright, fast as it hits the floor.
            ApplyTilt(pivot, axis, fallAngle * t * t, startPosition, startRotation);
            yield return null;
        }

        ApplyTilt(pivot, axis, fallAngle, startPosition, startRotation);
    }

    private IEnumerator Sink()
    {
        Vector3 startPosition = body.position;
        float elapsed = 0f;

        while (elapsed < sinkDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / sinkDuration);
            body.position = startPosition + Vector3.down * (sinkDepth * t);
            yield return null;
        }
    }

    private void ApplyTilt(
        Vector3 pivot,
        Vector3 axis,
        float angle,
        Vector3 startPosition,
        Quaternion startRotation)
    {
        Quaternion tilt = Quaternion.AngleAxis(angle, axis);
        body.SetPositionAndRotation(
            pivot + tilt * (startPosition - pivot),
            tilt * startRotation);
    }

    // Knocked away from the hit; straight backwards when nothing hit it.
    private Vector3 GetFallDirection()
    {
        Vector3 direction = Vector3.ProjectOnPlane(lastHitDirection, Vector3.up);

        if (direction.sqrMagnitude <= Mathf.Epsilon)
        {
            direction = Vector3.ProjectOnPlane(-transform.forward, Vector3.up);
        }

        return direction.sqrMagnitude <= Mathf.Epsilon
            ? Vector3.back
            : direction.normalized;
    }

    private Bounds GetBodyBounds()
    {
        Renderer[] renderers = body.GetComponentsInChildren<Renderer>();
        bool hasBounds = false;
        Bounds bounds = new Bounds(body.position, Vector3.zero);

        for (int i = 0; i < renderers.Length; i++)
        {
            // Particles and trails would stretch the box far past the body.
            if (!(renderers[i] is MeshRenderer || renderers[i] is SkinnedMeshRenderer))
            {
                continue;
            }

            if (hasBounds)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
            else
            {
                bounds = renderers[i].bounds;
                hasBounds = true;
            }
        }

        return bounds;
    }
}
