using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Builds a slash mesh from the blade's own path through a clip, so the
// crescent lies exactly where the blade swept: each column of the mesh is the
// blade at one moment of the chosen frame range, from just above the
// grip out past the tip by Reach Extension, which is what lets the arc cover
// the hitbox the blade alone falls short of.
//
// With Fit To Hitbox each column instead reaches exactly as far as the hitbox
// does in the blade's direction, seen from above: further where the box is
// deep, shorter where the blade is near its edge, and nothing at all where the
// blade swings outside it; the whole arc is cut off straight along the box's
// back edge. Reach Extension then only caps how far past the tip
// it may go, and the part past the tip also stays within the box's height, so
// a blade raised overhead is not stretched up into the sky.
//
// Fit Shape picks the outline fitted to:
//   - Box: the box itself, which gives the arc straight sides;
//   - Arc: seen from above, the ellipse spanning the box's width and reaching
//     its front edge - a smooth horizontal sweep. Its centre sits Arc Center
//     ahead of the back edge: at 0 it passes through both back corners; further
//     forward its sides are widest mid-box and curl in again behind, for a
//     swing that carries on round behind the body. Arc Back Radius flattens
//     just the half behind the centre, pulling in where it curls behind the
//     body. Arc Squareness above 2 turns the ellipse into a rounded rectangle
//     that fills more of the box;
//   - Side Arc: seen from the side, the quarter-ellipse from the highest point
//     the tip reaches, straight above the back edge, down to the front edge on
//     the ground - for an overhead chop. It may rise above the box, since the
//     raised blade does.
//
// The blade is read only at the clip's own samples and joined by a spline in
// between. Unity blends a Humanoid clip's poses almost linearly from one sample
// to the next, so reading it in between gives a path with a corner at every
// sample, and the outer edge, far out from the hand, shows each one as a bump.
//
// U runs with the clip's time rather than with distance, so a sweep driven
// linearly over the window keeps its head on the blade even where the swing
// speeds up and slows down. V runs from the inner edge to the outer, as the
// Slash shader expects.
//
// The mesh is laid out in the frame CharacterEffectSpawner places effects in -
// the effect pivot, turned to the character's facing - so the entry using it
// needs no offset, swing plane or rotation of its own.
public static class SlashTrajectoryBaker
{
    public const string Folder = "Assets/Models/Effect/Trajectory";

    // The rate the attacks are animated at, which frames are counted in. Clips
    // are exported at a multiple of it, so their own frame numbers are larger.
    public const float FrameRate = 30f;

    private static readonly Color DefaultColor = new Color(4f, 2f, 1.1f, 1f);

    // Values are stored in EditorPrefs, so they stay fixed.
    public enum FitShape
    {
        Box = 0,
        Arc = 1,
        SideArc = 2,
    }

    [Serializable]
    public struct Settings
    {
        // The frames of the clip the blade is swept over, at FrameRate.
        public float startFrame;
        public float endFrame;

        // Where the inner edge sits along the blade, in metres from the grip.
        public float innerDistance;

        // How far the outer edge reaches past the tip, in metres. With Fit To
        // Hitbox, the most it may.
        public float reachExtension;

        // Trims and stretches each column to the hitbox's footprint.
        public bool fitToHitbox;
        public FitShape fitShape;

        // Arc only: how far ahead of the hitbox's back edge the ellipse's
        // centre sits, in metres.
        public float arcCenter;

        // Arc only: the ellipse's radius behind its centre, in metres, so the
        // part that curls round behind the body can be pulled in without
        // changing the front. 0 matches the front.
        public float arcBackRadius;

        // Arc only: the outline's exponent. 2 is an ellipse; higher squares
        // it off towards the box's corners.
        public float arcSquareness;

        // Narrows the band towards both ends of the sweep, like the horns of a
        // crescent moon: the outer edge stays, and the inner edge is drawn out
        // to meet it. The width is scaled by sin(pi u) to this power, so 0 is
        // off and larger values thin the ends sooner.
        public float crescent;

        // How long the crescent stays after the blade has passed, in seconds;
        // the tail erases it over this time.
        public float linger;

        // Mesh columns per animation frame. The blade between the clip's own
        // samples comes from a spline, so this only sets how finely the arc's
        // curve is cut into straight pieces.
        public int samplesPerFrame;

        public static Settings Default => new()
        {
            innerDistance = 0.12f,
            reachExtension = 1f,
            fitToHitbox = true,
            arcSquareness = 2f,
            linger = 0.12f,
            samplesPerFrame = 16,
        };
    }

    // Rows across the cut. The Slash shader shades by V, so a few are enough.
    private const int Rows = 4;

    // Nothing is laid below this, since the ground would hide it.
    private const float MinHeight = 0.03f;

    // How even the last baked arc came out: the sharpest bend between
    // neighbouring columns along its inner and outer edges.
    public static string LastRoughness { get; private set; } = "";

    public static string MeshPath(AnimationClip clip)
    {
        return Folder + "/Slash_" + clip.name + ".asset";
    }

    // Poses the character at the clip's samples within [start, end] (clip
    // seconds) with poseAt and records the blade from grip to tip. Writes into
    // the mesh
    // asset for the clip, creating it the first time, so entries already
    // pointing at it pick the new shape up.
    //
    // hitbox is the hit volume in the same frame as the mesh, and is only read
    // with Fit To Hitbox.
    public static Mesh Bake(
        AnimationClip clip,
        Action<float> poseAt,
        Transform grip,
        Transform tip,
        Vector3 origin,
        Quaternion facing,
        float start,
        float end,
        in Settings settings,
        Bounds hitbox)
    {
        int columns = Mathf.Max(
            2,
            Mathf.CeilToInt((end - start) * FrameRate * Mathf.Max(1, settings.samplesPerFrame)));

        var vertices = new Vector3[(columns + 1) * (Rows + 1)];
        var uv = new Vector2[vertices.Length];
        var triangles = new int[columns * Rows * 6];
        Quaternion toLocal = Quaternion.Inverse(facing);

        // The clip's own samples inside the range, plus its two ends.
        var keyTimes = new List<float> { start };
        float step = 1f / Mathf.Max(clip.frameRate, FrameRate);

        for (float k = Mathf.Floor(start / step + 1f) * step; k < end - step * 0.01f; k += step)
        {
            keyTimes.Add(k);
        }

        keyTimes.Add(end);

        var keyGrips = new Vector3[keyTimes.Count];
        var keyTips = new Vector3[keyTimes.Count];

        for (int k = 0; k < keyTimes.Count; k++)
        {
            poseAt(keyTimes[k]);
            keyGrips[k] = toLocal * (grip.position - origin);
            keyTips[k] = toLocal * (tip.position - origin);
        }

        var grips = new Vector3[columns + 1];
        var tips = new Vector3[columns + 1];
        float arcHeight = 0f;

        for (int i = 0; i <= columns; i++)
        {
            float time = Mathf.Lerp(start, end, i / (float)columns);
            grips[i] = Spline(keyTimes, keyGrips, time);
            tips[i] = Spline(keyTimes, keyTips, time);
            arcHeight = Mathf.Max(arcHeight, tips[i].y - hitbox.min.y);
        }

        var inners = new Vector3[columns + 1];
        var outers = new Vector3[columns + 1];

        for (int i = 0; i <= columns; i++)
        {
            Vector3 gripLocal = grips[i];
            Vector3 tipLocal = tips[i];
            Vector3 along = (tipLocal - gripLocal).normalized;
            float tipDistance = Vector3.Distance(gripLocal, tipLocal);
            float outerDistance = tipDistance + settings.reachExtension;

            if (settings.fitToHitbox)
            {
                float exitDistance =
                    ExitDistance(gripLocal, along, hitbox, in settings, arcHeight);
                outerDistance = Mathf.Min(outerDistance, exitDistance);

                // Past the tip the arc is only reaching for the box, so it
                // must not leave it upwards or downwards either. The side arc
                // sets its own height.
                if (outerDistance > tipDistance && settings.fitShape != FitShape.SideArc)
                {
                    outerDistance = Mathf.Max(
                        tipDistance,
                        Mathf.Min(outerDistance, HeightLimit(gripLocal, along, hitbox)));
                }

                // Never shorter than the inner edge: a blade outside the box
                // gives a column of no width, which draws nothing.
                outerDistance = Mathf.Max(outerDistance, settings.innerDistance);
            }

            float innerDistance = settings.innerDistance;

            if (settings.crescent > 0f)
            {
                float width = Mathf.Max(outerDistance - innerDistance, 0f) * Mathf.Pow(
                    Mathf.Max(Mathf.Sin(Mathf.PI * i / columns), 0f),
                    settings.crescent);
                innerDistance = outerDistance - width;
            }

            inners[i] = gripLocal + along * innerDistance;
            outers[i] = gripLocal + along * outerDistance;
        }

        LastRoughness = "Sharpest bend inner / outer edge: " +
            Roughness(inners) + " / " + Roughness(outers);

        for (int i = 0; i <= columns; i++)
        {
            float u = i / (float)columns;

            for (int r = 0; r <= Rows; r++)
            {
                float v = r / (float)Rows;
                Vector3 local = Vector3.Lerp(inners[i], outers[i], v);
                local.y = Mathf.Max(local.y, MinHeight);

                int index = i * (Rows + 1) + r;
                vertices[index] = local;
                uv[index] = new Vector2(u, v);
            }
        }

        int t = 0;

        for (int i = 0; i < columns; i++)
        {
            for (int r = 0; r < Rows; r++)
            {
                int a = i * (Rows + 1) + r;
                int b = a + 1;
                int c = a + Rows + 1;
                int d = c + 1;

                triangles[t++] = a;
                triangles[t++] = c;
                triangles[t++] = b;
                triangles[t++] = b;
                triangles[t++] = c;
                triangles[t++] = d;
            }
        }

        // Anything behind the box's back edge is cut off along it rather than
        // tapered column by column, which left it ragged - except at the two
        // ends: the arc has to begin and end on the blade, so a swing that
        // opens or finishes just behind the box is filled from the blade to
        // the edge. The cut only applies between the first and the last
        // moment the whole blade is inside.
        if (settings.fitToHitbox)
        {
            int firstInside = -1;
            int lastInside = -1;

            for (int i = 0; i <= columns; i++)
            {
                if (vertices[i * (Rows + 1)].z >= hitbox.min.z &&
                    vertices[i * (Rows + 1) + Rows].z >= hitbox.min.z)
                {
                    if (firstInside < 0)
                    {
                        firstInside = i;
                    }

                    lastInside = i;
                }
            }

            if (firstInside >= 0)
            {
                ClipBehind(
                    hitbox.min.z,
                    firstInside * Rows * 6,
                    lastInside * Rows * 6,
                    ref vertices,
                    ref uv,
                    ref triangles);
            }
        }

        string path = MeshPath(clip);
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        bool created = mesh == null;

        if (created)
        {
            mesh = new Mesh();
        }

        mesh.Clear();
        mesh.name = "Slash_" + clip.name;
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        if (created)
        {
            if (!AssetDatabase.IsValidFolder(Folder))
            {
                AssetDatabase.CreateFolder("Assets/Models/Effect", "Trajectory");
            }

            AssetDatabase.CreateAsset(mesh, path);
        }
        else
        {
            EditorUtility.SetDirty(mesh);
        }

        AssetDatabase.SaveAssets();
        return mesh;
    }

    // The action's hit box in the mesh's frame. OverlapSkillAction places the
    // box from the character's root, while the mesh is laid out from the
    // effect pivot. Only box hits have one.
    public static bool TryGetHitBox(
        OverlapSkillAction action,
        Vector3 rootPosition,
        Vector3 origin,
        Quaternion facing,
        out Bounds hitBox)
    {
        hitBox = default;

        if (action == null || action.attackData == null ||
            action.attackData.shape != OverlapShape.Box)
        {
            return false;
        }

        Vector3 centre = action.centerOffset +
            Quaternion.Inverse(facing) * (rootPosition - origin);
        hitBox = new Bounds(centre, action.attackData.boxSize);
        return true;
    }

    // Cubic Hermite through the samples, with each sample's slope taken from
    // its two neighbours (Catmull-Rom, allowing for uneven spacing at the
    // range's ends), so the path bends smoothly through every sample.
    private static Vector3 Spline(List<float> times, Vector3[] points, float time)
    {
        int last = times.Count - 1;
        int k = 0;

        while (k < last - 1 && time > times[k + 1])
        {
            k++;
        }

        float t0 = times[k];
        float t1 = times[k + 1];
        float h = Mathf.Max(t1 - t0, 1e-6f);
        float s = Mathf.Clamp01((time - t0) / h);

        Vector3 m0 = Slope(times, points, k) * h;
        Vector3 m1 = Slope(times, points, k + 1) * h;
        float s2 = s * s;
        float s3 = s2 * s;

        return (2f * s3 - 3f * s2 + 1f) * points[k] +
            (s3 - 2f * s2 + s) * m0 +
            (-2f * s3 + 3f * s2) * points[k + 1] +
            (s3 - s2) * m1;
    }

    private static Vector3 Slope(List<float> times, Vector3[] points, int k)
    {
        int a = Mathf.Max(k - 1, 0);
        int b = Mathf.Min(k + 1, times.Count - 1);
        return (points[b] - points[a]) / Mathf.Max(times[b] - times[a], 1e-6f);
    }

    // The sharpest turn between consecutive segments of a polyline, in
    // degrees, ignoring segments too short to have a direction.
    private static string Roughness(Vector3[] points)
    {
        float worst = 0f;

        for (int i = 1; i + 1 < points.Length; i++)
        {
            Vector3 a = points[i] - points[i - 1];
            Vector3 b = points[i + 1] - points[i];

            if (a.sqrMagnitude > 1e-6f && b.sqrMagnitude > 1e-6f)
            {
                worst = Mathf.Max(worst, Vector3.Angle(a, b));
            }
        }

        return worst.ToString("F0") + "deg";
    }

    // How far along the blade's line, from the grip, it leaves the outline.
    // Box and Arc are seen from above and ignore height: the hit volume is
    // judged on the ground, and an overhead chop would otherwise lose its upper
    // arc to the box's top. Side Arc is seen from the side and ignores width.
    // Below zero when the line never crosses the outline.
    //
    // The back edge is left out: Bake cuts the mesh along it afterwards, so
    // columns just behind it keep their full reach and the cut is straight.
    private static float ExitDistance(
        Vector3 from,
        Vector3 along,
        Bounds box,
        in Settings settings,
        float arcHeight)
    {
        float enter = float.NegativeInfinity;
        float exit = float.PositiveInfinity;

        bool crosses = settings.fitShape switch
        {
            // The ellipse centred Arc Center ahead of the back edge, spanning
            // the box's width and reaching its front.
            FitShape.Arc => SuperEllipse(
                from.x - box.center.x,
                from.z - (box.min.z + settings.arcCenter),
                along.x,
                along.z,
                box.extents.x,
                Mathf.Max(box.size.z - settings.arcCenter, 0.01f),
                settings.arcBackRadius > 0f
                    ? settings.arcBackRadius
                    : Mathf.Max(box.size.z - settings.arcCenter, 0.01f),
                settings.arcSquareness,
                ref enter,
                ref exit),

            // The same seen from the side: centred where the back edge meets
            // the box's floor, reaching the front edge and up to arcHeight.
            FitShape.SideArc => Ellipse(
                from.z - box.min.z,
                from.y - box.min.y,
                along.z,
                along.y,
                box.size.z,
                Mathf.Max(arcHeight, 0.01f),
                ref enter,
                ref exit),

            _ => Slab(from.x, along.x, box.min.x, box.max.x, ref enter, ref exit) &&
                Slab(from.z, along.z, float.NegativeInfinity, box.max.z, ref enter, ref exit),
        };

        if (!crosses || exit < Mathf.Max(enter, 0f))
        {
            return -1f;
        }

        return exit;
    }

    // As Ellipse, for the curve |x/a|^n + |z/b|^n = 1, where b is bBack
    // behind the origin. The two halves meet where both are widest, so the
    // inside stays convex: along the line the curve's value falls to a single
    // lowest point and rises again either side, and each crossing is found by
    // bisection from it.
    private static bool SuperEllipse(
        float x,
        float z,
        float dx,
        float dz,
        float a,
        float b,
        float bBack,
        float n,
        ref float enter,
        ref float exit)
    {
        if (n <= 2f + 1e-3f && Mathf.Abs(bBack - b) < 1e-4f)
        {
            return Ellipse(x, z, dx, dz, a, b, ref enter, ref exit);
        }

        float Value(float t)
        {
            float pz = z + dz * t;
            return Mathf.Pow(Mathf.Abs((x + dx * t) / a), n) +
                Mathf.Pow(Mathf.Abs(pz / (pz < 0f ? bBack : b)), n) - 1f;
        }

        if (dx * dx + dz * dz < 1e-8f)
        {
            return Value(0f) <= 0f;
        }

        // Far enough that the line is well outside at both ends.
        float reach = 4f * (Mathf.Abs(x) + Mathf.Abs(z) + a + b) /
            Mathf.Sqrt(dx * dx + dz * dz);

        float lo = -reach;
        float hi = reach;

        for (int i = 0; i < 80; i++)
        {
            float m1 = lo + (hi - lo) / 3f;
            float m2 = hi - (hi - lo) / 3f;

            if (Value(m1) < Value(m2))
            {
                hi = m2;
            }
            else
            {
                lo = m1;
            }
        }

        float lowest = (lo + hi) * 0.5f;

        if (Value(lowest) > 0f)
        {
            return false;
        }

        enter = Mathf.Max(enter, Crossing(Value, lowest, -reach));
        exit = Mathf.Min(exit, Crossing(Value, lowest, reach));
        return true;
    }

    // Where value turns positive between inside (<= 0) and outside (> 0).
    private static float Crossing(Func<float, float> value, float inside, float outside)
    {
        for (int i = 0; i < 60; i++)
        {
            float mid = (inside + outside) * 0.5f;

            if (value(mid) <= 0f)
            {
                inside = mid;
            }
            else
            {
                outside = mid;
            }
        }

        return (inside + outside) * 0.5f;
    }

    // Narrows [enter, exit] to where the line from (x, z) along (dx, dz) is
    // inside the ellipse at the origin with semi-axes a across and b forward;
    // false when it never is.
    private static bool Ellipse(
        float x,
        float z,
        float dx,
        float dz,
        float a,
        float b,
        ref float enter,
        ref float exit)
    {
        float qa = dx * dx / (a * a) + dz * dz / (b * b);
        float qb = 2f * (x * dx / (a * a) + z * dz / (b * b));
        float qc = x * x / (a * a) + z * z / (b * b) - 1f;

        // A vertical blade does not move across the ground: it is in or out.
        if (qa < 1e-8f)
        {
            return qc <= 0f;
        }

        float discriminant = qb * qb - 4f * qa * qc;

        if (discriminant < 0f)
        {
            return false;
        }

        float root = Mathf.Sqrt(discriminant);
        enter = Mathf.Max(enter, (-qb - root) / (2f * qa));
        exit = Mathf.Min(exit, (-qb + root) / (2f * qa));
        return true;
    }

    // Cuts away everything behind z = back, splitting the triangles that
    // straddle it so the new edge lies exactly on the line. Only triangles in
    // [cutFrom, cutUntil) (indices into triangles) are cut; the rest are kept
    // whole. The cut triangles no longer share corners, which the Slash
    // shader does not need.
    private static void ClipBehind(
        float back,
        int cutFrom,
        int cutUntil,
        ref Vector3[] vertices,
        ref Vector2[] uv,
        ref int[] triangles)
    {
        var outVertices = new List<Vector3>();
        var outUv = new List<Vector2>();
        var outTriangles = new List<int>();
        var polygon = new List<(Vector3 p, Vector2 uv)>(4);

        for (int t = 0; t < triangles.Length; t += 3)
        {
            polygon.Clear();
            bool keep = t < cutFrom || t >= cutUntil;

            for (int k = 0; k < 3; k++)
            {
                int a = triangles[t + k];
                int b = triangles[t + (k + 1) % 3];
                bool aIn = keep || vertices[a].z >= back;
                bool bIn = keep || vertices[b].z >= back;

                if (aIn)
                {
                    polygon.Add((vertices[a], uv[a]));
                }

                if (aIn != bIn)
                {
                    float s = (back - vertices[a].z) / (vertices[b].z - vertices[a].z);
                    polygon.Add((
                        Vector3.Lerp(vertices[a], vertices[b], s),
                        Vector2.Lerp(uv[a], uv[b], s)));
                }
            }

            int first = outVertices.Count;

            for (int k = 0; k < polygon.Count; k++)
            {
                outVertices.Add(polygon[k].p);
                outUv.Add(polygon[k].uv);
            }

            for (int k = 1; k + 1 < polygon.Count; k++)
            {
                outTriangles.Add(first);
                outTriangles.Add(first + k);
                outTriangles.Add(first + k + 1);
            }
        }

        vertices = outVertices.ToArray();
        uv = outUv.ToArray();
        triangles = outTriangles.ToArray();
    }

    // How far along the blade's line, from the grip, it stays within the box's
    // height. Unlimited along a level blade.
    private static float HeightLimit(Vector3 from, Vector3 along, Bounds box)
    {
        if (along.y > 1e-4f)
        {
            return (box.max.y - from.y) / along.y;
        }

        if (along.y < -1e-4f)
        {
            return (box.min.y - from.y) / along.y;
        }

        return float.PositiveInfinity;
    }

    // Narrows [enter, exit] to where the line is between min and max on one
    // axis; false when it never is.
    private static bool Slab(
        float from,
        float along,
        float min,
        float max,
        ref float enter,
        ref float exit)
    {
        if (Mathf.Abs(along) < 1e-6f)
        {
            return from >= min && from <= max;
        }

        float a = (min - from) / along;
        float b = (max - from) / along;
        enter = Mathf.Max(enter, Mathf.Min(a, b));
        exit = Mathf.Min(exit, Mathf.Max(a, b));
        return true;
    }

    // Points the action's first effect entry at the baked mesh and times it
    // so the sweep's head stays on the blade: the entry fires when the window
    // opens, its head crosses the arc over the window, and the tail then
    // catches up over Linger. Look settings already on the entry are kept.
    public static void ApplyToAction(
        SkillAction action,
        Mesh mesh,
        float delay,
        float window,
        in Settings settings)
    {
        Undo.RecordObject(action, "Bake Slash Trajectory");

        if (action.effects == null || action.effects.Length == 0)
        {
            action.effects = new ActionEffectSettings[1];
        }

        ActionEffectSettings entry = action.effects[0];
        bool fresh = entry.mesh == null && entry.prefab == null;

        entry.enabled = true;
        entry.mesh = mesh;
        entry.prefab = null;
        entry.material = null;
        entry.delay = Mathf.Max(0f, delay);
        entry.offset = Vector3.zero;
        entry.scale = Vector3.one;
        entry.swingPlaneNormal = Vector3.zero;
        entry.localEuler = Vector3.zero;
        entry.stayInWorld = false;
        entry.outlivesSkill = false;

        // The mesh already has the arc's true size.
        entry.scaleCurve = new AnimationCurve();
        entry.scaleCurveAxes = Vector3.zero;

        if (fresh || entry.color.maxColorComponent <= 0f)
        {
            entry.color = DefaultColor;
        }

        if (entry.revealSpan <= 0f)
        {
            entry.revealSpan = 0.5f;
        }

        if (entry.revealSoftness <= 0f)
        {
            entry.revealSoftness = 0.25f;
        }

        float linger = Mathf.Max(0.01f, settings.linger);
        float duration = window + linger;
        entry.duration = duration;
        entry.fadeOutDuration = linger;
        entry.revealCurve = RevealCurve(window, duration, entry.revealSpan);
        entry.alphaCurve = AlphaCurve(window / duration);

        action.effects[0] = entry;
        EditorUtility.SetDirty(action);
    }

    // SlashEffect puts the head at sweep * (1 + span), so for the head to
    // reach the end of the arc exactly as the window closes the sweep has to
    // hit 1 / (1 + span) there, rising linearly. After that it eases on to 1,
    // which is the tail running the rest of the way and slowing as it goes.
    private static AnimationCurve RevealCurve(float window, float duration, float span)
    {
        float atBlade = window / duration;
        float sweepAtBlade = 1f / (1f + span);
        float slope = sweepAtBlade / atBlade;

        // A Hermite segment ending flat overshoots once its start slope tops
        // three times the straight line's; staying under that keeps the tail
        // from running past the end and back.
        float straight = (1f - sweepAtBlade) / (1f - atBlade);
        float tailSlope = Mathf.Min(slope, 2.5f * straight);

        return new AnimationCurve(
            new Keyframe(0f, 0f, slope, slope),
            new Keyframe(atBlade, sweepAtBlade, slope, tailSlope),
            new Keyframe(1f, 1f, 0f, 0f));
    }

    // Full while the blade is drawing it, dimming as the tail erases it.
    private static AnimationCurve AlphaCurve(float atBlade)
    {
        return new AnimationCurve(
            new Keyframe(0f, 1f, 0f, 0f),
            new Keyframe(atBlade, 1f, 0f, 0f),
            new Keyframe(1f, 0.35f, 0f, 0f));
    }
}
