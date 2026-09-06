using UnityEngine;

[RequireComponent(typeof(TrailRenderer))]
public sealed class WeaponTrail : MonoBehaviour
{
    private TrailRenderer trailRenderer;

    private void Awake()
    {
        trailRenderer = GetComponent<TrailRenderer>();

        // The prefab leaves the renderer enabled so the trail stays
        // visible while authoring. Emission starts off at runtime and
        // is opened only for the active phase of an attack.
        trailRenderer.emitting = false;
        trailRenderer.Clear();
    }

    public void Begin()
    {
        // Combo actions re-open emission within the trail's lifetime, so
        // stale points are dropped to avoid a straight streak between the
        // previous swing and this one.
        trailRenderer.Clear();
        trailRenderer.emitting = true;
    }

    public void End()
    {
        trailRenderer.emitting = false;
    }
}
