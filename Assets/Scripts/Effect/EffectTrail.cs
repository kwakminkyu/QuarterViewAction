using UnityEngine;

// A TrailRenderer inside a prefab effect, emitting only while the effect does:
// the streak drawn behind a dash. The renderer lays its own points as the
// effect follows the character; this only switches it on, off and clear.
[RequireComponent(typeof(TrailRenderer))]
public sealed class EffectTrail : MonoBehaviour, IEffectPart
{
    private TrailRenderer trail;
    private float stoppedTime = float.NegativeInfinity;
    private bool isEmitting;

    public bool IsAlive =>
        isEmitting || Time.time - stoppedTime < trail.time;

    private void Awake()
    {
        trail = GetComponent<TrailRenderer>();
        trail.emitting = false;
    }

    public void Play(CharacterEffectSpawner owner)
    {
        // The pooled instance was parked somewhere else; without this the
        // first segment would stretch from there to where it was placed.
        trail.Clear();
        trail.emitting = true;
        isEmitting = true;
    }

    public void StopEmitting()
    {
        if (!isEmitting)
        {
            return;
        }

        trail.emitting = false;
        isEmitting = false;
        stoppedTime = Time.time;
    }

    public void Clear()
    {
        trail.emitting = false;
        trail.Clear();
        isEmitting = false;
        stoppedTime = float.NegativeInfinity;
    }
}
