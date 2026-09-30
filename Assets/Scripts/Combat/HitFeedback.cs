using UnityEngine;

// What a landed hit looks like beyond the damage: the attack's camera shake
// and its impact effect at the hit point. Every attack type calls this once
// for each target its hit actually hurt, so the feel is authored on the
// attack data alone.
public static class HitFeedback
{
    public static void Play(in AttackContext context, in DamageInfo info)
    {
        AttackData data = context.AttackData;

        if (data == null)
        {
            return;
        }

        // Several targets hit on one frame shake the camera once: a shake
        // never gives way to one that is weaker.
        if (data.cameraShake > 0f)
        {
            CameraController.Shake(data.cameraShake);
        }

        if (data.impactEffect != null)
        {
            PlayImpact(data.impactEffect, in info);
        }
    }

    // Pulled this far off the struck surface, so the burst isn't swallowed by
    // the body it hit.
    private const float SurfaceClearance = 0.15f;

    private static void PlayImpact(GameObject prefab, in DamageInfo info)
    {
        Vector3 direction = info.HitDirection.sqrMagnitude > Mathf.Epsilon
            ? info.HitDirection
            : Vector3.forward;

        // The hit direction points into the target; the burst flies back out
        // of the surface, towards the attacker, where it can be seen.
        ParticleEffect impact = EffectPool.Rent(prefab);
        impact.transform.SetPositionAndRotation(
            info.HitPoint - direction * SurfaceClearance,
            Quaternion.LookRotation(-direction, Vector3.up));
        impact.transform.localScale = Vector3.one;

        // Owned by nobody and tied to no swing: it plays out where it landed
        // and goes back to the pool by itself.
        impact.Play(null, null, false);
    }
}
