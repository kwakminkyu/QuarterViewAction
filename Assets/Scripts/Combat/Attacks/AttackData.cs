using UnityEngine;

public abstract class AttackData : ScriptableObject
{
    [SerializeField] private DamagePayload payload;

    public DamagePayload Payload => payload;

    // TODO: Add AttackCategory and a stable attack ID when
    // damage analysis is implemented.
}
