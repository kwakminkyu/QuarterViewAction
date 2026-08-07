using UnityEngine;

public abstract class AttackData : ScriptableObject
{
    public DamagePayload payload;

    // TODO: Add AttackCategory and a stable attack ID when
    // damage analysis is implemented.
}
