using UnityEngine;

public abstract class AttackData : ScriptableObject
{
    public DamagePayload payload;

    // How the hit feels, set per attack next to what it does. Played by
    // HitFeedback whenever this attack lands.
    [Header("Hit feedback")]
    [Tooltip("Camera shake strength in metres when this lands. Zero for none.")]
    [Min(0f)] public float cameraShake;
    [Tooltip("Particle prefab played where this lands, facing along the hit.")]
    public GameObject impactEffect;

    // TODO: Add AttackCategory and a stable attack ID when
    // damage analysis is implemented.
}
