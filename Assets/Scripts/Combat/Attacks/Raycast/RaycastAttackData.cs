using UnityEngine;

[CreateAssetMenu(fileName = "RaycastAttackData", menuName = "Combat/Attacks/Raycast Attack")]
public sealed class RaycastAttackData : AttackData
{
    [Min(0f)] public float range = 10f;
    public LayerMask targetMask;
    public QueryTriggerInteraction triggerInteraction =
        QueryTriggerInteraction.Collide;
}
