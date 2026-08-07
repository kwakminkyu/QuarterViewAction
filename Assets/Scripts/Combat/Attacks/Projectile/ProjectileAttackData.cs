using UnityEngine;

public enum ProjectileCastShape
{
    Sphere,
    Box,
    Capsule
}

[CreateAssetMenu(fileName = "ProjectileAttackData", menuName = "Combat/Attacks/Projectile Attack")]
public sealed class ProjectileAttackData : AttackData
{
    public Projectile projectilePrefab;
    [Min(0f)] public float speed = 12f;
    [Min(0f)] public float lifetime = 5f;
    public ProjectileCastShape shape;
    [Min(0f)] public float radius = 0.1f;
    public Vector3 boxSize = Vector3.one * 0.2f;
    [Min(0f)] public float capsuleHeight = 1f;
    public LayerMask targetMask;
    public QueryTriggerInteraction triggerInteraction =
        QueryTriggerInteraction.Collide;
}
