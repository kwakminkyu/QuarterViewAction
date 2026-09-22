using UnityEngine;

// Makes a hit visible on whatever it is added to: a white flash, a quick
// squash, and a shove along the hit direction by the attack's knockback.
// Listens to the DamageReceiver, so any attack that deals damage triggers it
// with no changes on the attack side.
//
// Characters with a CharacterMovement are already knocked back by it, through
// their CharacterController, so the shove here only runs on bodies without
// one (a training dummy) - and uses the same rule, so an attack pushes both
// the same distance.
[RequireComponent(typeof(DamageReceiver))]
public sealed class HitReaction : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    [Header("Flash")]
    [SerializeField] private Color flashColor = Color.white;
    [SerializeField, Min(0f)] private float flashDuration = 0.12f;

    [Header("Squash")]
    // How much the body squashes on impact, and how fast it springs back.
    [SerializeField, Range(0f, 0.5f)] private float squashAmount = 0.15f;
    [SerializeField, Min(0.01f)] private float squashDuration = 0.15f;

    [Header("Knockback (bodies without CharacterMovement only)")]
    // Matches CharacterMovement: the attack's knockback is the starting speed
    // in metres per second, slowed by this many metres per second each second.
    [SerializeField, Min(0.01f)] private float knockbackDeceleration = 20f;

    private DamageReceiver receiver;
    private bool movesItself;
    private Renderer[] renderers;
    private Color[] baseColors;
    private MaterialPropertyBlock propertyBlock;
    private Vector3 restScale;

    private float flashTimer;
    private float squashTimer;
    private Vector3 knockbackVelocity;

    private void Awake()
    {
        receiver = GetComponent<DamageReceiver>();
        movesItself = GetComponent<CharacterMovement>() != null;
        renderers = GetComponentsInChildren<Renderer>();
        propertyBlock = new MaterialPropertyBlock();
        restScale = transform.localScale;

        baseColors = new Color[renderers.Length];

        for (int i = 0; i < renderers.Length; i++)
        {
            Material material = renderers[i].sharedMaterial;
            baseColors[i] = material != null && material.HasProperty(BaseColorId)
                ? material.GetColor(BaseColorId)
                : Color.white;
        }
    }

    private void OnEnable()
    {
        receiver.DamageReceived += OnDamageReceived;
    }

    private void OnDisable()
    {
        receiver.DamageReceived -= OnDamageReceived;

        // Leave the body as it was, not frozen mid-flash or mid-squash.
        flashTimer = 0f;
        squashTimer = 0f;
        knockbackVelocity = Vector3.zero;
        ApplyFlash(0f);
        transform.localScale = restScale;
    }

    private void OnDamageReceived(DamageInfo info)
    {
        flashTimer = flashDuration;
        squashTimer = squashDuration;

        if (movesItself)
        {
            return;
        }

        // Along the ground only; a hit never lifts the body.
        Vector3 direction = info.HitDirection;
        direction.y = 0f;

        if (direction.sqrMagnitude > Mathf.Epsilon && info.Payload.knockback > 0f)
        {
            knockbackVelocity = direction.normalized * info.Payload.knockback;
        }
    }

    private void Update()
    {
        float deltaTime = Time.deltaTime;

        if (flashTimer > 0f)
        {
            flashTimer = Mathf.Max(flashTimer - deltaTime, 0f);
            ApplyFlash(flashDuration > 0f ? flashTimer / flashDuration : 0f);
        }

        if (squashTimer > 0f)
        {
            squashTimer = Mathf.Max(squashTimer - deltaTime, 0f);
            float squash = squashAmount * (squashTimer / squashDuration);

            // Squashed down and bulging out, keeping roughly the same volume.
            transform.localScale = Vector3.Scale(
                restScale,
                new Vector3(1f + squash * 0.5f, 1f - squash, 1f + squash * 0.5f));
        }

        if (knockbackVelocity.sqrMagnitude > 0f)
        {
            // Same order as CharacterMovement: move, then slow down.
            transform.position += knockbackVelocity * deltaTime;
            knockbackVelocity = Vector3.MoveTowards(
                knockbackVelocity,
                Vector3.zero,
                knockbackDeceleration * deltaTime);
        }
    }

    private void ApplyFlash(float amount)
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(BaseColorId, Color.Lerp(baseColors[i], flashColor, amount));
            renderers[i].SetPropertyBlock(propertyBlock);
        }
    }
}
