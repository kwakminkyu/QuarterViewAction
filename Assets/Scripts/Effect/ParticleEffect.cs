using UnityEngine;

// Plays one particle prefab spawned by an action - flying debris, dust -
// under the same lifetime rules as a slash: the active window closing stops
// new particles but lets the ones already out finish; the motion being cut
// short clears everything at once. Added to the prefab's root, or by
// EffectPool when a prefab lacks it.
//
// Besides particle systems the prefab may hold IEffectPart components -
// trails, afterimages - which are started, stopped and cleared along with
// the systems.
public sealed class ParticleEffect : MonoBehaviour
{
    private ParticleSystem[] systems;
    private IEffectPart[] parts;
    private CharacterEffectSpawner owner;
    private Transform followAnchor;
    private Vector3 followOffset;
    private bool isPlaying;
    private bool endsWithSwing;
    private bool outlivesSkill;

    // The prefab this instance was made from, so it goes back to the right
    // pool. Set by EffectPool.
    public GameObject Source { get; internal set; }

    private void Awake()
    {
        systems = GetComponentsInChildren<ParticleSystem>(true);
        parts = GetComponentsInChildren<IEffectPart>(true);
    }

    public void Play(
        CharacterEffectSpawner spawner,
        Transform followTarget,
        bool endsWithSwing,
        bool outlivesSkill)
    {
        owner = spawner;
        this.endsWithSwing = endsWithSwing;
        this.outlivesSkill = outlivesSkill;

        // Position only, as with the slashes: the burst keeps the direction it
        // was fired in even if the character turns.
        followAnchor = followTarget;
        followOffset = followTarget != null
            ? transform.position - followTarget.position
            : Vector3.zero;

        isPlaying = true;

        for (int i = 0; i < systems.Length; i++)
        {
            // A pooled instance may still hold particles from its last use.
            systems[i].Clear(false);
            systems[i].Play(false);
        }

        for (int i = 0; i < parts.Length; i++)
        {
            parts[i].Play(spawner);
        }
    }

    // The attack's active window closed: nothing new is emitted, but particles
    // already in flight play out. Ground effects ignore it and play in full.
    public void End()
    {
        if (!isPlaying || !endsWithSwing)
        {
            return;
        }

        StopEmitting();
    }

    // Left to play out when the attack that spawned it ends; see
    // ActionEffectSettings.stayInWorld.
    public bool StaysInWorld => isPlaying && !endsWithSwing;

    // The motion was cut short - nothing it spawned may survive it, unless the
    // entry outlives the skill: then it stops emitting and following, and
    // what is out fades where it is.
    public void Cancel()
    {
        if (!isPlaying)
        {
            return;
        }

        if (outlivesSkill)
        {
            followAnchor = null;
            StopEmitting();
            return;
        }

        for (int i = 0; i < systems.Length; i++)
        {
            systems[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        for (int i = 0; i < parts.Length; i++)
        {
            parts[i].Clear();
        }

        Finish();
    }

    private void StopEmitting()
    {
        for (int i = 0; i < systems.Length; i++)
        {
            systems[i].Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }

        for (int i = 0; i < parts.Length; i++)
        {
            parts[i].StopEmitting();
        }
    }

    private void LateUpdate()
    {
        if (!isPlaying)
        {
            return;
        }

        if (followAnchor != null)
        {
            transform.position = followAnchor.position + followOffset;
        }

        // Finished once every system and part has run out of things to show.
        for (int i = 0; i < systems.Length; i++)
        {
            if (systems[i].IsAlive(false))
            {
                return;
            }
        }

        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].IsAlive)
            {
                return;
            }
        }

        Finish();
    }

    private void Finish()
    {
        isPlaying = false;
        followAnchor = null;

        // The character may be gone by now - a ground effect outlives it - and
        // the pool is shared, so the effect can go back on its own.
        if (owner != null)
        {
            owner.Release(this);
        }
        else
        {
            EffectPool.Return(this);
        }
    }
}
