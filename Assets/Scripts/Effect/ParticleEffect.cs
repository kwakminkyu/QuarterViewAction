using UnityEngine;

// Plays one particle prefab spawned by an action - flying debris, dust -
// under the same lifetime rules as a slash: the active window closing stops
// new particles but lets the ones already out finish; the motion being cut
// short clears everything at once. Added to the prefab's root, or by
// EffectPool when a prefab lacks it.
public sealed class ParticleEffect : MonoBehaviour
{
    private ParticleSystem[] systems;
    private CharacterEffectSpawner owner;
    private Transform followAnchor;
    private Vector3 followOffset;
    private bool isPlaying;
    private bool endsWithSwing;

    // The prefab this instance was made from, so it goes back to the right
    // pool. Set by EffectPool.
    public GameObject Source { get; internal set; }

    private void Awake()
    {
        systems = GetComponentsInChildren<ParticleSystem>(true);
    }

    public void Play(
        CharacterEffectSpawner spawner,
        Transform followTarget,
        bool endsWithSwing)
    {
        owner = spawner;
        this.endsWithSwing = endsWithSwing;

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
    }

    // The attack's active window closed: nothing new is emitted, but particles
    // already in flight play out. Ground effects ignore it and play in full.
    public void End()
    {
        if (!isPlaying || !endsWithSwing)
        {
            return;
        }

        for (int i = 0; i < systems.Length; i++)
        {
            systems[i].Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    // Left to play out when the attack that spawned it ends; see
    // ActionEffectSettings.stayInWorld.
    public bool StaysInWorld => isPlaying && !endsWithSwing;

    // The motion was cut short - nothing it spawned may survive it.
    public void Cancel()
    {
        if (!isPlaying)
        {
            return;
        }

        for (int i = 0; i < systems.Length; i++)
        {
            systems[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        Finish();
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

        // Finished once every system has run out of particles and emission.
        for (int i = 0; i < systems.Length; i++)
        {
            if (systems[i].IsAlive(false))
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
