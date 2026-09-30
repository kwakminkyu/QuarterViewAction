using UnityEngine;

// Slows the whole game for a moment when this character takes a hit, so the
// player registers being hurt. Put on the player only: slowing for the hits
// the player lands would drag every combo.
[RequireComponent(typeof(DamageReceiver))]
public sealed class HitSlowMotion : MonoBehaviour
{
    [SerializeField, Range(0.01f, 1f)] private float timeScale = 0.2f;
    // Both in real seconds, whatever the slowed clock says.
    [SerializeField, Min(0f)] private float holdDuration = 0.12f;
    [SerializeField, Min(0f)] private float recoverDuration = 0.1f;

    private DamageReceiver damageReceiver;

    private void Awake()
    {
        damageReceiver = GetComponent<DamageReceiver>();
    }

    private void OnEnable()
    {
        damageReceiver.DamageReceived += OnDamageReceived;
    }

    private void OnDisable()
    {
        damageReceiver.DamageReceived -= OnDamageReceived;
    }

    private void OnDamageReceived(DamageInfo info)
    {
        TimeScaleController.SlowMotion(timeScale, holdDuration, recoverDuration);
    }
}
