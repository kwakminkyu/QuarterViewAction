using System.Collections.Generic;
using UnityEngine;

// The one place spawned effects live: a scene-root "Effects" object holding
// both the effects playing now and a pool of idle ones shared by every
// character. It sits at the origin with no rotation and unit scale, so putting
// an effect under it changes nothing about where the effect ends up - effects
// stay clear of any character's transform, as they must - it only keeps them
// in one place in the hierarchy instead of loose at the scene root.
//
// Created on first use when the scene does not already have one.
public sealed class EffectPool : MonoBehaviour
{
    private const string RootName = "Effects";
    private const string IdleName = "Pool";

    // Idle instances kept per prefab; any returned beyond this are destroyed.
    [SerializeField, Min(1)] private int maxPooledPerPrefab = 8;

    private static EffectPool instance;

    private readonly Dictionary<SlashEffect, Stack<SlashEffect>> slashes = new();
    private readonly Dictionary<GameObject, Stack<ParticleEffect>> particles = new();
    private Transform idleRoot;

    // Takes an instance of the prefab from the pool, or makes one. It comes
    // back active, parented under the root and ready to be placed and played.
    public static SlashEffect Rent(SlashEffect prefab)
    {
        return Instance.RentSlash(prefab);
    }

    public static ParticleEffect Rent(GameObject prefab)
    {
        return Instance.RentParticles(prefab);
    }

    public static void Return(SlashEffect effect)
    {
        if (effect == null)
        {
            return;
        }

        // The pool is only gone while its scene is being torn down, and the
        // effect is going with it; do not build a new root in the middle.
        if (instance == null)
        {
            Destroy(effect.gameObject);
            return;
        }

        instance.Park(effect, effect.Source, instance.slashes);
    }

    public static void Return(ParticleEffect effect)
    {
        if (effect == null)
        {
            return;
        }

        if (instance == null)
        {
            Destroy(effect.gameObject);
            return;
        }

        instance.Park(effect, effect.Source, instance.particles);
    }

    private static EffectPool Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindAnyObjectByType<EffectPool>();

                if (instance == null)
                {
                    instance = new GameObject(RootName).AddComponent<EffectPool>();
                }
            }

            return instance;
        }
    }

    // Survives the editor's fast enter-play-mode option, which skips the
    // domain reload that would otherwise clear the static.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearStatic()
    {
        instance = null;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning(
                "More than one EffectPool in the scene; '" + name + "' is unused.",
                this);
            return;
        }

        instance = this;
        ResetTransform();
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    // Anything moving the root would drag every effect with it, so it is put
    // straight back.
    private void LateUpdate()
    {
        if (!transform.hasChanged)
        {
            return;
        }

        if (transform.localPosition != Vector3.zero ||
            transform.localRotation != Quaternion.identity ||
            transform.localScale != Vector3.one ||
            transform.parent != null)
        {
            Debug.LogWarning(
                "The Effects root must stay at the origin, unrotated and " +
                "unscaled; it has been reset.",
                this);
            ResetTransform();
        }

        transform.hasChanged = false;
    }

    private void ResetTransform()
    {
        transform.SetParent(null, false);
        transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        transform.localScale = Vector3.one;
        transform.hasChanged = false;
    }

    private SlashEffect RentSlash(SlashEffect prefab)
    {
        SlashEffect pooled = TakeIdle(prefab, slashes);

        if (pooled != null)
        {
            return pooled;
        }

        SlashEffect created = Instantiate(prefab, transform);
        created.Source = prefab;
        return created;
    }

    private ParticleEffect RentParticles(GameObject prefab)
    {
        ParticleEffect pooled = TakeIdle(prefab, particles);

        if (pooled != null)
        {
            return pooled;
        }

        GameObject created = Instantiate(prefab, transform);

        // A plain particle prefab works too; it just gets the lifetime
        // handling attached on first use.
        ParticleEffect effect = created.GetComponent<ParticleEffect>();

        if (effect == null)
        {
            effect = created.AddComponent<ParticleEffect>();
        }

        effect.Source = prefab;
        return effect;
    }

    private T TakeIdle<TKey, T>(TKey prefab, Dictionary<TKey, Stack<T>> pools)
        where T : Component
    {
        if (!pools.TryGetValue(prefab, out Stack<T> stack))
        {
            return null;
        }

        // Entries can be destroyed out from under the pool, for instance by a
        // scene reload, so skip the dead ones.
        while (stack.Count > 0)
        {
            T pooled = stack.Pop();

            if (pooled != null)
            {
                pooled.transform.SetParent(transform, false);
                pooled.gameObject.SetActive(true);
                return pooled;
            }
        }

        return null;
    }

    private void Park<TKey, T>(T effect, TKey source, Dictionary<TKey, Stack<T>> pools)
        where T : Component
    {
        if (source == null)
        {
            Destroy(effect.gameObject);
            return;
        }

        if (!pools.TryGetValue(source, out Stack<T> stack))
        {
            stack = new Stack<T>(maxPooledPerPrefab);
            pools.Add(source, stack);
        }

        if (stack.Count >= maxPooledPerPrefab)
        {
            Destroy(effect.gameObject);
            return;
        }

        // Parked inactive so nothing idle updates, renders or simulates.
        effect.gameObject.SetActive(false);
        effect.transform.SetParent(IdleRoot, false);
        stack.Push(effect);
    }

    private Transform IdleRoot
    {
        get
        {
            if (idleRoot == null)
            {
                idleRoot = new GameObject(IdleName).transform;
                idleRoot.SetParent(transform, false);
            }

            return idleRoot;
        }
    }
}
