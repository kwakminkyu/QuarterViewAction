using UnityEngine;

public enum OverlapShape
{
    Box,
    Sphere
}

[CreateAssetMenu(fileName = "OverlapAttackData", menuName = "Combat/Attacks/Overlap Attack")]
public class OverlapAttackData : AttackData
{
    public OverlapShape shape;
    public Vector3 boxSize = Vector3.one;
    [Min(0f)] public float radius = 0.5f;
    public LayerMask targetMask;
    public QueryTriggerInteraction triggerInteraction =
        QueryTriggerInteraction.Collide;

    protected virtual void OnValidate()
    {
        boxSize = new Vector3(
            Mathf.Max(0f, boxSize.x),
            Mathf.Max(0f, boxSize.y),
            Mathf.Max(0f, boxSize.z));
    }
}
