using System.Collections.Generic;
using UnityEngine;

// Owns every piece of runtime effect state for one character. SkillAction is a
// shared asset, so the actions describe what to play and this component is
// what actually plays it. Monsters get the same behaviour by adding it.
public sealed class CharacterEffectSpawner : MonoBehaviour
{
    [SerializeField] private SlashEffect slashPrefab;
    [SerializeField, Min(1)] private int poolSize = 4;

    // Origin every effect is placed from and follows for its whole life, and
    // the centre the authored arcs are drawn around. Kept as a transform on the
    // character so it can be dragged in the scene while tuning rather than
    // typed in as numbers.
    [SerializeField] private string effectPivotName = "EffectPivot";

    private Transform effectPivot;
    private Stack<SlashEffect> pool;

    // Particle instances are pooled per prefab, since each one is built from a
    // different prefab and cannot stand in for another.
    private readonly Dictionary<GameObject, Stack<ParticleEffect>> particlePools = new();

    // Effects have to be reachable after they are handed out so the skill that
    // spawned them can end or cancel them. Without this the effect only ended
    // on its own clock and could outlive the motion.
    private readonly List<SlashEffect> active = new();
    private readonly List<ParticleEffect> activeParticles = new();

    private Transform poolRoot;

    private void Awake()
    {
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            if (child.name == effectPivotName)
            {
                effectPivot = child;
                break;
            }
        }
    }

    // Plays one effect entry: its particle prefab when it has one, otherwise
    // its mesh as a slash.
    public void PlaySlash(
        in ActionEffectSettings settings,
        Vector3 direction)
    {
        if (!settings.enabled)
        {
            return;
        }

        if (settings.prefab != null)
        {
            PlayParticles(in settings, direction);
            return;
        }

        if (slashPrefab == null)
        {
            return;
        }

        ResolvePlacement(
            in settings, direction, out Vector3 position, out Quaternion rotation);

        SlashEffect slash = Rent();
        Transform slashTransform = slash.transform;

        // Never parented to the pivot: that would drag the character's turning
        // in too. Following is done as a position offset instead, so the swing
        // plane fixed from the facing at spawn stays put.
        slashTransform.SetParent(null, false);
        slashTransform.SetPositionAndRotation(position, rotation);

        // Tracked for the effect's whole life, otherwise the character lunges
        // away from an effect left hanging where it spawned.
        active.Add(slash);
        slash.Play(this, in settings, FollowTargetFor(in settings));
    }

    // The active window closed: let every live slash fade out and stop new
    // particles, leaving the ones in flight to finish.
    public void EndSlash()
    {
        for (int i = active.Count - 1; i >= 0; i--)
        {
            if (active[i] != null)
            {
                active[i].End();
            }
        }

        for (int i = activeParticles.Count - 1; i >= 0; i--)
        {
            if (activeParticles[i] != null)
            {
                activeParticles[i].End();
            }
        }
    }

    // The motion is over, finished or cut short, so nothing on the swing may
    // outlive it. Ground effects are left alone: they belong to the world now
    // and finish on their own clock.
    public void CancelSlash()
    {
        // Cancel calls back into Release, which removes from these lists, so
        // the walks have to run backwards.
        for (int i = active.Count - 1; i >= 0; i--)
        {
            SlashEffect slash = active[i];

            if (slash == null)
            {
                active.RemoveAt(i);
                continue;
            }

            if (!slash.StaysInWorld)
            {
                slash.Cancel();
            }
        }

        for (int i = activeParticles.Count - 1; i >= 0; i--)
        {
            ParticleEffect particles = activeParticles[i];

            if (particles == null)
            {
                activeParticles.RemoveAt(i);
                continue;
            }

            if (!particles.StaysInWorld)
            {
                particles.Cancel();
            }
        }
    }

    public void Release(SlashEffect slash)
    {
        if (slash == null)
        {
            return;
        }

        active.Remove(slash);

        pool ??= new Stack<SlashEffect>(poolSize);

        if (pool.Count >= poolSize)
        {
            Destroy(slash.gameObject);
            return;
        }

        slash.transform.SetParent(PoolRoot, false);
        pool.Push(slash);
    }

    public void Release(ParticleEffect particles)
    {
        if (particles == null)
        {
            return;
        }

        activeParticles.Remove(particles);

        if (particles.Source == null ||
            !particlePools.TryGetValue(particles.Source, out Stack<ParticleEffect> stack))
        {
            Destroy(particles.gameObject);
            return;
        }

        if (stack.Count >= poolSize)
        {
            Destroy(particles.gameObject);
            return;
        }

        // Parked inactive so a stopped system cannot be woken by anything else.
        particles.gameObject.SetActive(false);
        particles.transform.SetParent(PoolRoot, false);
        stack.Push(particles);
    }

    private void PlayParticles(in ActionEffectSettings settings, Vector3 direction)
    {
        ResolvePlacement(
            in settings, direction, out Vector3 position, out Quaternion rotation);

        ParticleEffect particles = RentParticles(settings.prefab);
        Transform particleTransform = particles.transform;

        particleTransform.SetParent(null, false);
        particleTransform.SetPositionAndRotation(position, rotation);

        // Scale only reaches the particles when a system's Scaling Mode is
        // Hierarchy; the prefab decides that.
        particleTransform.localScale =
            settings.scale.sqrMagnitude > Mathf.Epsilon
                ? settings.scale
                : Vector3.one;

        particles.gameObject.SetActive(true);
        activeParticles.Add(particles);
        particles.Play(
            this,
            settings.prefab,
            FollowTargetFor(in settings),
            !settings.stayInWorld);
    }

    // Where an effect entry goes and which way it faces, shared by slashes and
    // particles so both answer to Offset, Swing Plane Normal and Local Euler
    // in the same way.
    private void ResolvePlacement(
        in ActionEffectSettings settings,
        Vector3 direction,
        out Vector3 position,
        out Quaternion rotation)
    {
        Vector3 facing = direction;
        facing.y = 0f;

        if (facing.sqrMagnitude <= Mathf.Epsilon)
        {
            facing = transform.forward;
            facing.y = 0f;
        }

        if (facing.sqrMagnitude <= Mathf.Epsilon)
        {
            facing = Vector3.forward;
        }

        facing = facing.normalized;

        // The mesh's own pivot is the centre of the arc it draws, so the point
        // it is placed at is that arc's centre - not where the blade is. The
        // EffectPivot transform is that centre; offset nudges from there.
        Vector3 origin = effectPivot != null
            ? effectPivot.position
            : transform.position;
        position = origin + Quaternion.LookRotation(facing, Vector3.up) * settings.offset;

        rotation = ResolveSwingPlaneRotation(facing, in settings) *
            Quaternion.Euler(settings.localEuler);
    }

    // What an effect tracks for its life: the pivot for effects on the swing,
    // nothing for ground effects, which stay where they landed.
    private Transform FollowTargetFor(in ActionEffectSettings settings)
    {
        if (settings.stayInWorld)
        {
            return null;
        }

        return effectPivot != null ? effectPivot : transform;
    }

    // Lays the authored mesh into the plane the blade really sweeps through.
    // The normal is measured per clip and authored on the action, which is
    // what lets an overhead chop read as vertical - the earlier camera-facing
    // version flattened every attack into the screen plane and turned the
    // fourth combo's chop into a horizontal sweep.
    private Quaternion ResolveSwingPlaneRotation(
        Vector3 facing,
        in ActionEffectSettings settings)
    {
        Quaternion facingRotation = Quaternion.LookRotation(facing, Vector3.up);

        if (settings.swingPlaneNormal.sqrMagnitude <= Mathf.Epsilon)
        {
            return facingRotation;
        }

        // The normal is authored in the character's own frame, so it has to be
        // carried into world space through wherever the character is aiming.
        Vector3 worldNormal =
            (facingRotation * settings.swingPlaneNormal).normalized;

        // The mesh lies in its local XY plane, so local +Z has to become the
        // plane normal. Local up is taken from the aim direction projected
        // into the plane, which keeps the arc pointing where the attack does.
        Vector3 inPlaneAim = Vector3.ProjectOnPlane(facing, worldNormal);

        if (inPlaneAim.sqrMagnitude <= Mathf.Epsilon)
        {
            inPlaneAim = Vector3.ProjectOnPlane(Vector3.up, worldNormal);
        }

        if (inPlaneAim.sqrMagnitude <= Mathf.Epsilon)
        {
            return facingRotation;
        }

        return Quaternion.LookRotation(worldNormal, inPlaneAim.normalized);
    }

    private SlashEffect Rent()
    {
        pool ??= new Stack<SlashEffect>(poolSize);

        // Pooled entries can be destroyed out from under us when a detached
        // slash is cleaned up with the scene, so skip the dead ones.
        while (pool.Count > 0)
        {
            SlashEffect pooled = pool.Pop();

            if (pooled != null)
            {
                return pooled;
            }
        }

        return Instantiate(slashPrefab);
    }

    private ParticleEffect RentParticles(GameObject prefab)
    {
        if (!particlePools.TryGetValue(prefab, out Stack<ParticleEffect> stack))
        {
            stack = new Stack<ParticleEffect>(poolSize);
            particlePools.Add(prefab, stack);
        }

        while (stack.Count > 0)
        {
            ParticleEffect pooled = stack.Pop();

            if (pooled != null)
            {
                return pooled;
            }
        }

        GameObject created = Instantiate(prefab);

        // A plain particle prefab works too; it just gets the lifetime
        // handling attached on first use.
        ParticleEffect particles = created.GetComponent<ParticleEffect>();
        return particles != null ? particles : created.AddComponent<ParticleEffect>();
    }

    private Transform PoolRoot
    {
        get
        {
            if (poolRoot == null)
            {
                poolRoot = new GameObject("SlashPool").transform;
                poolRoot.SetParent(transform, false);
            }

            return poolRoot;
        }
    }
}
