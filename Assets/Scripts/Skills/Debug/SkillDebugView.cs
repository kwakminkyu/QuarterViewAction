using UnityEngine;

public sealed class SkillDebugView : MonoBehaviour
{
    [SerializeField] private bool logSkillInput = true;
    [SerializeField] private bool logOverlapHits = true;
    [SerializeField] private bool drawHitboxes = true;
    [SerializeField, Min(0f)] private float displayDuration = 0.4f;
    [SerializeField] private Color missColor = Color.green;
    [SerializeField] private Color hitColor = Color.red;

    private OverlapShape shape;
    private Vector3 position;
    private Quaternion rotation;
    private Vector3 boxSize;
    private float radius;
    private float visibleUntil;
    private bool hasHitbox;
    private bool hasHit;

    public void ReportSkillInput(
        string skillName,
        int slotIndex,
        bool accepted,
        int actionIndex,
        SkillPhase phase)
    {
        if (!logSkillInput)
        {
            return;
        }

        Debug.Log(
            $"[Skill Test] {skillName} input " +
            $"{(accepted ? "accepted" : "rejected")}: " +
            $"slot={slotIndex}, action={actionIndex + 1}, " +
            $"phase={phase}.",
            this);
    }

    public void ReportOverlap(
        SkillDefinition skill,
        int actionIndex,
        OverlapAttackData data,
        Vector3 hitboxPosition,
        Quaternion hitboxRotation,
        int hitCount)
    {
        if (data == null)
        {
            return;
        }

        if (logOverlapHits)
        {
            Debug.Log(
                $"[Skill Test] {skill.SkillId} " +
                $"combo {actionIndex + 1}/{skill.ActionCount}: " +
                $"overlap resolved, hits={hitCount}, " +
                $"damage={data.Payload.Damage}.",
                gameObject);
        }

        if (!drawHitboxes)
        {
            return;
        }

        shape = data.Shape;
        position = hitboxPosition;
        rotation = hitboxRotation;
        boxSize = data.BoxSize;
        radius = data.Radius;
        hasHit = hitCount > 0;
        hasHitbox = true;
        visibleUntil = Time.time + displayDuration;
    }

    private void OnDrawGizmos()
    {
        if (!Application.isPlaying ||
            !drawHitboxes ||
            !hasHitbox ||
            Time.time > visibleUntil)
        {
            return;
        }

        Color previousColor = Gizmos.color;
        Matrix4x4 previousMatrix = Gizmos.matrix;

        Gizmos.color = hasHit ? hitColor : missColor;

        switch (shape)
        {
            case OverlapShape.Box:
                Gizmos.matrix = Matrix4x4.TRS(
                    position,
                    rotation,
                    Vector3.one);
                Gizmos.DrawWireCube(Vector3.zero, boxSize);
                break;

            case OverlapShape.Sphere:
                Gizmos.DrawWireSphere(position, radius);
                break;
        }

        Gizmos.color = previousColor;
        Gizmos.matrix = previousMatrix;
    }
}
