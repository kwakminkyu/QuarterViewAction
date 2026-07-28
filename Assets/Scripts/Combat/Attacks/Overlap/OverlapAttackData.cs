using UnityEngine;

public enum OverlapShape
{
    Box,
    Sphere
}

[CreateAssetMenu(fileName = "OverlapAttackData",menuName = "Combat/Attacks/Overlap Attack")]
public sealed class OverlapAttackData : AttackData
{
    [SerializeField] private OverlapShape shape;
    [SerializeField] private Vector3 boxSize = Vector3.one;
    [SerializeField, Min(0f)] private float radius = 0.5f;
    [SerializeField] private LayerMask targetMask;
    [SerializeField] private QueryTriggerInteraction triggerInteraction =
        QueryTriggerInteraction.Collide;

    public OverlapShape Shape => shape;
    public Vector3 BoxSize => new Vector3(
        Mathf.Max(0f, boxSize.x),
        Mathf.Max(0f, boxSize.y),
        Mathf.Max(0f, boxSize.z));
    public float Radius => Mathf.Max(0f, radius);
    public LayerMask TargetMask => targetMask;
    public QueryTriggerInteraction TriggerInteraction => triggerInteraction;
}
