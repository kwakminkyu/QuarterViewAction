using UnityEngine;

// Draws an AttackTelegraph as a patch on the floor: an outline of the ground
// the hit will cover, filling from the attacker's side outwards and full the
// moment the hit comes out. A stand-in look; another view can replace it by
// listening to the same AttackTelegraph.
//
// Both parts are flat quads laid along +Z of this object, which is moved onto
// the covered ground every frame rather than parented to the attacker's
// turning body.
public sealed class GroundTelegraphView : MonoBehaviour
{
    [SerializeField] private AttackTelegraph telegraph;
    [SerializeField] private Transform outline;
    [SerializeField] private Transform fill;
    // Lifted just off the floor so it doesn't flicker against it.
    [SerializeField, Min(0f)] private float heightOffset = 0.02f;

    private void Awake()
    {
        if (telegraph == null)
        {
            telegraph = GetComponentInParent<AttackTelegraph>();
        }

        SetVisible(false);
    }

    private void OnEnable()
    {
        telegraph.Started += OnStarted;
        telegraph.Ended += OnEnded;
    }

    private void OnDisable()
    {
        telegraph.Started -= OnStarted;
        telegraph.Ended -= OnEnded;
        SetVisible(false);
    }

    private void OnStarted()
    {
        SetVisible(true);
        UpdateShape();
    }

    private void OnEnded()
    {
        SetVisible(false);
    }

    private void LateUpdate()
    {
        if (telegraph.IsActive)
        {
            UpdateShape();
        }
    }

    private void UpdateShape()
    {
        Vector3 center = telegraph.Center;
        center.y = telegraph.transform.position.y + heightOffset;
        transform.SetPositionAndRotation(center, telegraph.Rotation);

        Vector2 size = telegraph.Size;
        outline.localScale = new Vector3(size.x, size.y, 1f);

        // Grows from the near edge, so the far edge filling in is the cue.
        float length = size.y * telegraph.Progress;
        fill.localScale = new Vector3(size.x, length, 1f);
        fill.localPosition = new Vector3(0f, 0f, (length - size.y) * 0.5f);
    }

    private void SetVisible(bool visible)
    {
        outline.gameObject.SetActive(visible);
        fill.gameObject.SetActive(visible);
    }
}
