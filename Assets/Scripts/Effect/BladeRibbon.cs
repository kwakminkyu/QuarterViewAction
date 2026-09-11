using System.Collections.Generic;
using UnityEngine;

// Builds the swept surface the blade actually traced, as a mesh, instead of
// drawing an authored shape at the character. Because the geometry comes from
// the blade transforms, a vertical chop produces a vertical ribbon and a
// horizontal slash a horizontal one, with no per-attack orientation data.
//
// This replaces the old TrailRenderer, which could only emit one vertex per
// frame (four to eight over an active window) and could only fade oldest-first.
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class BladeRibbon : MonoBehaviour
{
    private const float DefaultLengthMultiplier = 1.6f;
    private const float DefaultWidthScale = 1f;
    private const float DefaultFadeOutDuration = 0.18f;

    // Centripetal parameterisation. Uniform knots (alpha = 0) overshoot into
    // loops once sample spacing is uneven, which it always is here; alpha 0.5
    // is provably free of cusps and self-intersections.
    private const float KnotAlpha = 0.5f;
    private const float MinKnotDelta = 1e-4f;

    private static readonly Color DefaultColor = new Color(3.2f, 2.4f, 1.6f, 1f);

    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
    private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");

    [SerializeField] private Transform bladeBase;
    [SerializeField] private Transform bladeTip;

    // The blade sweeps 100 to 173 degrees per frame, so one sample per frame
    // is well past Nyquist. Four sub-samples puts a 60fps game at roughly
    // 240Hz of blade motion.
    [SerializeField, Range(1, 8)] private int subFrameSamples = 4;

    [SerializeField, Min(2)] private int maxSamples = 128;
    [SerializeField, Range(1, 8)] private int smoothingSubdivisions = 2;

    // The ribbon spans the blade, so its face follows wherever the blade is
    // pointing. Over a swing that sweeps through the view direction it turns
    // edge-on and pinches to a line, which is what reads as the trail
    // rippling. Rebuilding each cross-section to face the camera keeps the
    // path - and therefore the vertical-versus-horizontal read - untouched
    // while holding the width steady. 0 keeps the true blade span.
    [SerializeField, Range(0f, 1f)] private float cameraAlignment = 1f;

    // Only meant to drop genuinely duplicate poses. Sub-frame sampling makes
    // each segment several times shorter, so a large threshold here would
    // start culling real samples and re-introduce the uneven spacing that
    // makes the spline wobble.
    [SerializeField, Min(0f)] private float minSampleDistance = 0.002f;

    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private MaterialPropertyBlock propertyBlock;
    private Mesh mesh;

    private readonly List<Vector3> innerSamples = new();
    private readonly List<Vector3> tipSamples = new();
    private readonly List<Vector3> smoothedInner = new();
    private readonly List<Vector3> smoothedTip = new();
    private readonly List<Vector3> vertices = new();
    private readonly List<Vector2> uvs = new();
    private readonly List<int> triangles = new();

    private BladeTrailSettings settings;
    private float lengthMultiplier = DefaultLengthMultiplier;
    private float widthScale = DefaultWidthScale;
    private float fadeOutDuration = DefaultFadeOutDuration;
    private Color color = DefaultColor;

    private Camera ribbonCamera;
    private Vector3 localTipOffset;
    private Vector3 previousBasePosition;
    private Quaternion previousRotation;
    private bool hasPreviousPose;

    private bool isEmitting;
    private bool isFading;
    private float emitElapsedTime;
    private float fadeElapsedTime;

    private void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
        propertyBlock = new MaterialPropertyBlock();

        mesh = new Mesh { name = "BladeRibbon" };
        mesh.MarkDynamic();
        meshFilter.sharedMesh = mesh;

        meshRenderer.enabled = false;
    }

    private void OnDestroy()
    {
        if (mesh != null)
        {
            Destroy(mesh);
        }
    }

    public void Begin(in BladeTrailSettings trailSettings)
    {
        settings = trailSettings;

        if (!settings.enabled || bladeBase == null || bladeTip == null)
        {
            Stop();
            return;
        }

        lengthMultiplier = settings.lengthMultiplier > 0f
            ? settings.lengthMultiplier
            : DefaultLengthMultiplier;

        widthScale = settings.widthScale > 0f
            ? settings.widthScale
            : DefaultWidthScale;

        fadeOutDuration = settings.fadeOutDuration > 0f
            ? settings.fadeOutDuration
            : DefaultFadeOutDuration;

        color = settings.color.maxColorComponent > 0f
            ? settings.color
            : DefaultColor;

        // The markers are siblings under the sword with identity local
        // rotation, so the base transform's rotation is the blade's and this
        // offset is constant in blade space.
        localTipOffset = Quaternion.Inverse(bladeBase.rotation) *
            (bladeTip.position - bladeBase.position);

        // A combo step re-opens the ribbon inside the previous one's lifetime,
        // so old samples are dropped to avoid a straight streak joining the
        // two swings.
        innerSamples.Clear();
        tipSamples.Clear();

        hasPreviousPose = false;
        isEmitting = true;
        isFading = false;
        emitElapsedTime = 0f;
        fadeElapsedTime = 0f;
    }

    public void End()
    {
        if (!isEmitting)
        {
            return;
        }

        isEmitting = false;

        // Fading the whole ribbon at once is the part a TrailRenderer cannot
        // do; it always erodes oldest-first regardless of settings.
        if (innerSamples.Count >= 2)
        {
            isFading = true;
            fadeElapsedTime = 0f;
            return;
        }

        Stop();
    }

    // Ends the swing immediately, wherever it is. End() only starts a fade and
    // returns early once that fade is running, so a cancel arriving mid-fade
    // used to be ignored and the ribbon outlived the motion that made it.
    public void Cancel()
    {
        Stop();
    }

    private void Stop()
    {
        isEmitting = false;
        isFading = false;
        hasPreviousPose = false;
        innerSamples.Clear();
        tipSamples.Clear();
        meshRenderer.enabled = false;
    }

    // Sampling has to happen after the Animator has posed the skeleton, which
    // is why this is LateUpdate and not Update.
    private void LateUpdate()
    {
        if (isEmitting)
        {
            emitElapsedTime += Time.deltaTime;

            if (emitElapsedTime >= settings.startDelay && IsWithinEmitWindow())
            {
                Sample();
            }
        }

        float alpha = 1f;
        float dissolve = 0f;

        if (isFading)
        {
            fadeElapsedTime += Time.deltaTime;
            float t = Mathf.Clamp01(fadeElapsedTime / fadeOutDuration);

            alpha = 1f - t;
            dissolve = t;

            if (t >= 1f)
            {
                Stop();
                return;
            }
        }
        else if (!isEmitting)
        {
            return;
        }

        if (innerSamples.Count < 2)
        {
            meshRenderer.enabled = false;
            return;
        }

        BuildMesh();

        meshRenderer.enabled = true;
        meshRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor(ColorId, color);
        propertyBlock.SetFloat(AlphaId, alpha);
        propertyBlock.SetFloat(DissolveId, dissolve);
        meshRenderer.SetPropertyBlock(propertyBlock);
    }

    // Lets an action stop recording before the follow-through. Combo 2 and 4
    // carry genuine 68 to 86 degree corners at the end of the active window;
    // the spline draws those correctly, but they can be trimmed if the
    // deceleration reads as clutter.
    private bool IsWithinEmitWindow()
    {
        return settings.emitDuration <= 0f ||
            emitElapsedTime <= settings.startDelay + settings.emitDuration;
    }

    private void Sample()
    {
        Vector3 basePosition = bladeBase.position;
        Quaternion rotation = bladeBase.rotation;

        if (!hasPreviousPose)
        {
            PushPose(basePosition, rotation);
            previousBasePosition = basePosition;
            previousRotation = rotation;
            hasPreviousPose = true;
            return;
        }

        // The blade is rigid, so interpolating its pose between the previous
        // frame and this one reconstructs the intra-frame arc without
        // stepping the Animator a second time.
        for (int i = 1; i <= subFrameSamples; i++)
        {
            float t = i / (float)subFrameSamples;

            PushPose(
                ArcInterpolate(
                    previousBasePosition,
                    basePosition,
                    previousRotation,
                    rotation,
                    t),
                Quaternion.Slerp(previousRotation, rotation, t));
        }

        previousBasePosition = basePosition;
        previousRotation = rotation;
    }

    // Straight-line interpolation between two poses cuts the corner off the
    // path the blade really took, and measurement showed that chord error -
    // not the spline - was where nearly all of the remaining wobble lived.
    // Two poses one frame apart are related by a rotation, so the shared
    // rotation centre can be recovered and the position swung around it.
    private static Vector3 ArcInterpolate(
        Vector3 from,
        Vector3 to,
        Quaternion fromRotation,
        Quaternion toRotation,
        float t)
    {
        Quaternion delta = toRotation * Quaternion.Inverse(fromRotation);

        float angle;
        Vector3 axis;
        delta.ToAngleAxis(out angle, out axis);

        // Match the shortest-arc convention Slerp uses for the rotation.
        if (angle > 180f)
        {
            angle -= 360f;
        }

        Vector3 chord = to - from;
        Vector3 axial = Vector3.Dot(chord, axis) * axis;
        Vector3 planar = chord - axial;

        float halfRadians = angle * Mathf.Deg2Rad * 0.5f;
        float sinHalf = Mathf.Sin(halfRadians);

        // Below a couple of degrees the arc and its chord differ by about a
        // hundredth of the chord, while the recovered radius grows without
        // bound and wrecks the precision of the swing that follows. At high
        // frame rates almost every step lands here.
        if (Mathf.Abs(angle) < 2f ||
            planar.sqrMagnitude < 1e-8f ||
            axis.sqrMagnitude < 1e-8f)
        {
            return Vector3.Lerp(from, to, t);
        }

        // cos/sin rather than tan so a half-turn, where the centre lands on
        // the midpoint, stays finite.
        float offset = planar.magnitude * Mathf.Cos(halfRadians) /
            (2f * sinHalf);

        Vector3 centre = (from + to) * 0.5f +
            Vector3.Cross(axis, planar).normalized * offset;

        return centre +
            Quaternion.AngleAxis(angle * t, axis) * (from - centre) +
            axial * t;
    }

    private void PushPose(Vector3 basePosition, Quaternion rotation)
    {
        Vector3 axis = rotation * localTipOffset;

        Vector3 tip = basePosition + axis * lengthMultiplier;
        Vector3 inner = tip - (tip - basePosition) * widthScale;

        if (tipSamples.Count > 0)
        {
            Vector3 previousTip = tipSamples[tipSamples.Count - 1];

            if ((tip - previousTip).sqrMagnitude <
                minSampleDistance * minSampleDistance)
            {
                return;
            }
        }

        innerSamples.Add(inner);
        tipSamples.Add(tip);

        if (innerSamples.Count > maxSamples)
        {
            innerSamples.RemoveAt(0);
            tipSamples.RemoveAt(0);
        }
    }

    private void BuildMesh()
    {
        Smooth();

        int count = smoothedTip.Count;

        vertices.Clear();
        uvs.Clear();
        triangles.Clear();

        Vector3 previousSide = Vector3.zero;
        bool hasPreviousSide = false;

        // Samples are kept in world space so a lunging or turning character
        // does not drag the already-drawn arc along with it. They are folded
        // into local space here, against the transform of this frame.
        for (int i = 0; i < count; i++)
        {
            Vector3 centre = (smoothedInner[i] + smoothedTip[i]) * 0.5f;
            Vector3 rib = smoothedTip[i] - smoothedInner[i];
            float halfWidth = rib.magnitude * 0.5f;

            Vector3 side = ResolveSide(
                rib,
                Travel(i, count),
                centre,
                ref previousSide,
                ref hasPreviousSide);

            vertices.Add(transform.InverseTransformPoint(centre - side * halfWidth));
            vertices.Add(transform.InverseTransformPoint(centre + side * halfWidth));

            float u = count > 1 ? i / (float)(count - 1) : 0f;
            uvs.Add(new Vector2(u, 0f));
            uvs.Add(new Vector2(u, 1f));
        }

        for (int i = 0; i < count - 1; i++)
        {
            int v = i * 2;

            triangles.Add(v);
            triangles.Add(v + 1);
            triangles.Add(v + 2);

            triangles.Add(v + 2);
            triangles.Add(v + 1);
            triangles.Add(v + 3);
        }

        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
    }

    // Central difference so the direction at a cross-section reflects the
    // curve through it rather than just the segment ahead.
    private Vector3 Travel(int index, int count)
    {
        if (count < 2)
        {
            return Vector3.forward;
        }

        if (index == 0)
        {
            return smoothedTip[1] - smoothedTip[0];
        }

        if (index >= count - 1)
        {
            return smoothedTip[count - 1] - smoothedTip[count - 2];
        }

        return smoothedTip[index + 1] - smoothedTip[index - 1];
    }

    // Turns the cross-section to face the viewer, perpendicular to the
    // direction of travel. The blade's own span is kept as the fallback and
    // as the zero end of the blend.
    private Vector3 ResolveSide(
        Vector3 rib,
        Vector3 travel,
        Vector3 position,
        ref Vector3 previousSide,
        ref bool hasPreviousSide)
    {
        Vector3 bladeSide = rib.sqrMagnitude > 1e-10f
            ? rib.normalized
            : (hasPreviousSide ? previousSide : Vector3.up);

        Vector3 side = bladeSide;

        if (cameraAlignment > 0f && travel.sqrMagnitude > 1e-10f)
        {
            if (ribbonCamera == null)
            {
                ribbonCamera = Camera.main;
            }

            if (ribbonCamera != null)
            {
                Transform cameraTransform = ribbonCamera.transform;

                Vector3 view = ribbonCamera.orthographic
                    ? cameraTransform.forward
                    : position - cameraTransform.position;

                Vector3 aligned = Vector3.Cross(
                    travel.normalized,
                    view.normalized);

                if (aligned.sqrMagnitude > 1e-6f)
                {
                    aligned.Normalize();

                    // Where the blade crosses the view direction the cross
                    // product flips; without this the ribbon would turn
                    // inside out at that point.
                    Vector3 reference = hasPreviousSide
                        ? previousSide
                        : bladeSide;

                    if (Vector3.Dot(aligned, reference) < 0f)
                    {
                        aligned = -aligned;
                    }

                    side = Vector3.Slerp(bladeSide, aligned, cameraAlignment);
                }
                else if (hasPreviousSide)
                {
                    side = previousSide;
                }
            }
        }

        if (side.sqrMagnitude < 1e-10f)
        {
            side = bladeSide;
        }
        else
        {
            side = side.normalized;
        }

        previousSide = side;
        hasPreviousSide = true;
        return side;
    }

    // Both rows are smoothed in one pass against a single set of knots taken
    // from the tip curve. Parameterising them separately would space the two
    // rows differently - the inner row is shorter - and shear the ribbon.
    private void Smooth()
    {
        smoothedTip.Clear();
        smoothedInner.Clear();

        int count = tipSamples.Count;

        if (count < 2)
        {
            smoothedTip.AddRange(tipSamples);
            smoothedInner.AddRange(innerSamples);
            return;
        }

        for (int i = 0; i < count - 1; i++)
        {
            int i0 = Mathf.Max(i - 1, 0);
            int i1 = i;
            int i2 = i + 1;
            int i3 = Mathf.Min(i + 2, count - 1);

            float k0 = 0f;
            float k1 = k0 + KnotDelta(tipSamples[i0], tipSamples[i1]);
            float k2 = k1 + KnotDelta(tipSamples[i1], tipSamples[i2]);
            float k3 = k2 + KnotDelta(tipSamples[i2], tipSamples[i3]);

            for (int s = 0; s < smoothingSubdivisions; s++)
            {
                float t = Mathf.Lerp(
                    k1,
                    k2,
                    s / (float)smoothingSubdivisions);

                smoothedTip.Add(Interpolate(
                    tipSamples[i0], tipSamples[i1],
                    tipSamples[i2], tipSamples[i3],
                    k0, k1, k2, k3, t));

                smoothedInner.Add(Interpolate(
                    innerSamples[i0], innerSamples[i1],
                    innerSamples[i2], innerSamples[i3],
                    k0, k1, k2, k3, t));
            }
        }

        smoothedTip.Add(tipSamples[count - 1]);
        smoothedInner.Add(innerSamples[count - 1]);
    }

    private static float KnotDelta(Vector3 a, Vector3 b)
    {
        return Mathf.Max(
            Mathf.Pow((b - a).magnitude, KnotAlpha),
            MinKnotDelta);
    }

    // Barry-Goldman pyramidal evaluation, which is what makes a non-uniform
    // knot vector usable.
    private static Vector3 Interpolate(
        Vector3 p0,
        Vector3 p1,
        Vector3 p2,
        Vector3 p3,
        float k0,
        float k1,
        float k2,
        float k3,
        float t)
    {
        Vector3 a1 = ((k1 - t) * p0 + (t - k0) * p1) / (k1 - k0);
        Vector3 a2 = ((k2 - t) * p1 + (t - k1) * p2) / (k2 - k1);
        Vector3 a3 = ((k3 - t) * p2 + (t - k2) * p3) / (k3 - k2);

        Vector3 b1 = ((k2 - t) * a1 + (t - k0) * a2) / (k2 - k0);
        Vector3 b2 = ((k3 - t) * a2 + (t - k1) * a3) / (k3 - k1);

        return ((k2 - t) * b1 + (t - k1) * b2) / (k2 - k1);
    }
}
