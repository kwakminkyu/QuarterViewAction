using UnityEngine;

// Shows a Health as a bar: the fill's right edge is anchored at the current
// fraction of the maximum. Works the same above a monster's head (in a
// world-space canvas) and in the screen HUD.
//
// The fill is sized by its anchors rather than by an Image's fill amount, so
// the bar needs no sprite - a plain coloured Image is enough.
public sealed class HealthBarView : MonoBehaviour
{
    [Tooltip("Leave empty to use the Health on this object or a parent.")]
    [SerializeField] private Health health;
    [SerializeField] private RectTransform fill;
    [Tooltip("Hide the bar once the character is dead.")]
    [SerializeField] private bool hideWhenDepleted = true;

    private void Awake()
    {
        if (health == null)
        {
            health = GetComponentInParent<Health>();
        }
    }

    private void OnEnable()
    {
        if (health == null)
        {
            return;
        }

        health.HealthChanged += OnHealthChanged;
        health.Depleted += OnDepleted;
    }

    private void OnDisable()
    {
        if (health == null)
        {
            return;
        }

        health.HealthChanged -= OnHealthChanged;
        health.Depleted -= OnDepleted;
    }

    // Start rather than OnEnable: Health fills itself in its own Awake, which
    // may not have run yet when this is enabled.
    private void Start()
    {
        if (health != null)
        {
            SetFraction(health.CurrentHealth / health.maxHealth);
        }
    }

    private void OnHealthChanged(float current, float max)
    {
        SetFraction(current / max);
    }

    private void OnDepleted()
    {
        if (hideWhenDepleted)
        {
            gameObject.SetActive(false);
        }
    }

    private void SetFraction(float fraction)
    {
        Vector2 anchorMax = fill.anchorMax;
        anchorMax.x = Mathf.Clamp01(fraction);
        fill.anchorMax = anchorMax;
    }
}
