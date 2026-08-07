using UnityEngine;

[CreateAssetMenu(fileName = "AreaAttackData", menuName = "Combat/Attacks/Area Attack")]
public sealed class AreaAttackData : OverlapAttackData
{
    public const float MinimumTickInterval = 0.1f;

    public Area areaPrefab;
    [Min(0f)] public float lifetime = 5f;
    [Min(MinimumTickInterval)] public float tickInterval = 0.5f;

    protected override void OnValidate()
    {
        base.OnValidate();
        lifetime = Mathf.Max(0f, lifetime);
        tickInterval = Mathf.Max(
            MinimumTickInterval,
            tickInterval);
    }
}
