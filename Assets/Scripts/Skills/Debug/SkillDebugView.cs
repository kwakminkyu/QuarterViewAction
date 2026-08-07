using UnityEngine;

public sealed class SkillDebugView : MonoBehaviour
{
    [SerializeField] private bool logSkillInput = true;
    [SerializeField] private bool logOverlapHits = true;
    [SerializeField] private bool logRaycastHits = true;
    [SerializeField] private bool logProjectileHits = true;
    [SerializeField] private bool logAreaTicks = true;
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

    private Vector3 rayOrigin;
    private Vector3 rayEnd;
    private float rayVisibleUntil;
    private bool hasRay;
    private bool rayHasHit;

    private ProjectileCastShape projectileShape;
    private Vector3 projectileStart;
    private Vector3 projectileEnd;
    private Vector3 projectileDirection;
    private Quaternion projectileRotation;
    private Vector3 projectileBoxSize;
    private float projectileRadius;
    private float projectileCapsuleHeight;
    private float projectileVisibleUntil;
    private bool hasProjectileCast;
    private bool projectileHasHit;

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
                $"[Skill Test] {skill.skillId} " +
                $"combo {actionIndex + 1}/{skill.ActionCount}: " +
                $"overlap resolved, hits={hitCount}, " +
                $"damage={data.payload.damage}.",
                gameObject);
        }

        SetOverlapVisualization(
            data,
            hitboxPosition,
            hitboxRotation,
            hitCount > 0);
    }

    public void ReportRaycast(
        SkillDefinition skill,
        int actionIndex,
        RaycastAttackData data,
        Vector3 origin,
        Vector3 direction,
        RaycastHit hit,
        bool damageApplied)
    {
        if (data == null)
        {
            return;
        }

        bool collided = hit.collider != null;

        if (logRaycastHits)
        {
            Debug.Log(
                $"[Skill Test] {skill.skillId} " +
                $"action {actionIndex + 1}/{skill.ActionCount}: " +
                $"raycast resolved, collision={collided}, " +
                $"damageApplied={damageApplied}, " +
                $"damage={data.payload.damage}.",
                gameObject);
        }

        if (!drawHitboxes)
        {
            return;
        }

        rayOrigin = origin;
        rayEnd = collided
            ? hit.point
            : origin + direction.normalized * data.range;
        rayHasHit = collided;
        hasRay = true;
        rayVisibleUntil = Time.time + displayDuration;
    }

    public void ReportProjectileSpawn(
        ProjectileAttackData data,
        Vector3 spawnPosition)
    {
        if (!logProjectileHits || data == null)
        {
            return;
        }

        Debug.Log(
            $"[Skill Test] {data.name} projectile spawned: " +
            $"position={spawnPosition}, speed={data.speed}, " +
            $"lifetime={data.lifetime}.",
            gameObject);
    }

    public void ReportProjectileCast(
        ProjectileAttackData data,
        Vector3 start,
        Vector3 direction,
        float travelDistance,
        RaycastHit hit,
        bool collided)
    {
        if (!drawHitboxes || data == null)
        {
            return;
        }

        projectileShape = data.shape;
        projectileStart = start;
        projectileEnd = collided
            ? start + direction.normalized * hit.distance
            : start + direction.normalized * travelDistance;
        projectileDirection = direction.normalized;
        projectileRotation = Quaternion.LookRotation(
            projectileDirection,
            Vector3.up);
        projectileBoxSize = data.boxSize;
        projectileRadius = data.radius;
        projectileCapsuleHeight = data.capsuleHeight;
        projectileHasHit = collided;
        hasProjectileCast = true;
        projectileVisibleUntil = Time.time + displayDuration;
    }

    public void ReportProjectileHit(
        ProjectileAttackData data,
        RaycastHit hit,
        bool damageApplied)
    {
        if (!logProjectileHits || data == null)
        {
            return;
        }

        Debug.Log(
            $"[Skill Test] {data.name} projectile collision: " +
            $"target={hit.collider.name}, " +
            $"damageApplied={damageApplied}, " +
            $"damage={data.payload.damage}.",
            gameObject);
    }

    public void ReportAreaTick(
        AreaAttackData data,
        Vector3 areaPosition,
        Quaternion areaRotation,
        int hitCount)
    {
        if (data == null)
        {
            return;
        }

        if (logAreaTicks)
        {
            Debug.Log(
                $"[Skill Test] {data.name} area tick: " +
                $"hits={hitCount}, damage={data.payload.damage}.",
                gameObject);
        }

        SetOverlapVisualization(
            data,
            areaPosition,
            areaRotation,
            hitCount > 0);
    }

    private void SetOverlapVisualization(
        OverlapAttackData data,
        Vector3 hitboxPosition,
        Quaternion hitboxRotation,
        bool didHit)
    {
        if (!drawHitboxes)
        {
            return;
        }

        shape = data.shape;
        position = hitboxPosition;
        rotation = hitboxRotation;
        boxSize = data.boxSize;
        radius = data.radius;
        hasHit = didHit;
        hasHitbox = true;
        visibleUntil = Time.time + displayDuration;
    }

    private void OnDrawGizmos()
    {
        if (!Application.isPlaying ||
            !drawHitboxes)
        {
            return;
        }

        Color previousColor = Gizmos.color;
        Matrix4x4 previousMatrix = Gizmos.matrix;

        if (hasHitbox && Time.time <= visibleUntil)
        {
            DrawOverlap();
            Gizmos.matrix = Matrix4x4.identity;
        }

        if (hasRay && Time.time <= rayVisibleUntil)
        {
            Gizmos.color = rayHasHit ? hitColor : missColor;
            Gizmos.DrawLine(rayOrigin, rayEnd);
            Gizmos.DrawWireSphere(rayEnd, 0.08f);
        }

        if (hasProjectileCast &&
            Time.time <= projectileVisibleUntil)
        {
            DrawProjectileCast();
        }

        Gizmos.color = previousColor;
        Gizmos.matrix = previousMatrix;
    }

    private void DrawOverlap()
    {
        Gizmos.matrix = Matrix4x4.identity;
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
    }

    private void DrawProjectileCast()
    {
        Gizmos.matrix = Matrix4x4.identity;
        Gizmos.color = projectileHasHit ? hitColor : missColor;
        Gizmos.DrawLine(projectileStart, projectileEnd);

        switch (projectileShape)
        {
            case ProjectileCastShape.Sphere:
                Gizmos.DrawWireSphere(
                    projectileEnd,
                    projectileRadius);
                break;

            case ProjectileCastShape.Box:
                Gizmos.matrix = Matrix4x4.TRS(
                    projectileEnd,
                    projectileRotation,
                    Vector3.one);
                Gizmos.DrawWireCube(
                    Vector3.zero,
                    projectileBoxSize);
                break;

            case ProjectileCastShape.Capsule:
                DrawCapsule();
                break;
        }
    }

    private void DrawCapsule()
    {
        Gizmos.matrix = Matrix4x4.identity;
        float capsuleRadius = Mathf.Max(0f, projectileRadius);
        float height = Mathf.Max(
            projectileCapsuleHeight,
            capsuleRadius * 2f);
        float halfSegment = height * 0.5f - capsuleRadius;
        Vector3 point1 = projectileEnd +
            projectileDirection * halfSegment;
        Vector3 point2 = projectileEnd -
            projectileDirection * halfSegment;
        Vector3 right = projectileRotation * Vector3.right *
            capsuleRadius;
        Vector3 up = projectileRotation * Vector3.up *
            capsuleRadius;

        Gizmos.DrawWireSphere(point1, capsuleRadius);
        Gizmos.DrawWireSphere(point2, capsuleRadius);
        Gizmos.DrawLine(point1 + right, point2 + right);
        Gizmos.DrawLine(point1 - right, point2 - right);
        Gizmos.DrawLine(point1 + up, point2 + up);
        Gizmos.DrawLine(point1 - up, point2 - up);
    }
}
