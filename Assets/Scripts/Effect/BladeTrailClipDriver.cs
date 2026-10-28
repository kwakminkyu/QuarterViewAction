using System;
using System.Collections.Generic;
using UnityEngine;

// TEMP: Drives a TrailRenderer from the animation clip itself so new attack
// clips can be checked for how their trail reads before any SkillAction
// timing is authored for them. Each window names a clip and the frame range
// the trail emits over; the skill's active window plays no part here.
//
// A TrailRenderer adds at most one point a frame, and a fast swing covers a
// large arc between frames, so on its own the trail comes out as a polygon.
// This driver lays every point itself instead: the tip's position on each
// frame, joined by a centripetal Catmull-Rom spline. A spline segment needs
// the sample after it, so the newest segment is laid provisionally and
// rewritten in place once the next frame arrives. Rebuilding the in-betweens
// from the sword's rotation instead left a kink at every frame, because the
// tip does not turn about the grip.
//
// Runs in LateUpdate, after the Animator has posed the sword.
[DefaultExecutionOrder(-100)]
public sealed class BladeTrailClipDriver : MonoBehaviour
{
    [Serializable]
    public struct ClipWindow
    {
        public string clipName;

        // Frames at FrameRate from the clip's start, whatever rate the clip
        // itself was sampled at. A Blender action that starts at F1 is one
        // lower here.
        [Min(0f)] public float startFrame;
        [Min(0f)] public float endFrame;
    }

    // The rate the attacks are animated at. Clips are exported at a multiple
    // of it so the Humanoid import keeps the in-between poses, which would
    // make the clip's own frame numbers several times larger.
    public const float FrameRate = 30f;

    private const int MaxPointsPerSegment = 32;

    // Centripetal parameterisation: uniform knots overshoot into loops when
    // the sample spacing is uneven, which a swing that speeds up and slows
    // down always has.
    private const float KnotAlpha = 0.5f;
    private const float MinKnotDelta = 1e-4f;

    // Keeps the TrailRenderer from laying points of its own; every point
    // comes from here.
    private const float NoAutoPoints = 1000f;

    [SerializeField] private Animator animator;

    // The tip the trail follows.
    [SerializeField] private TrailRenderer trail;

    // Distance between the points laid along the spline. Smaller is smoother.
    [SerializeField, Min(0.005f)] private float pointSpacing = 0.03f;

    [SerializeField] private ClipWindow[] windows = Array.Empty<ClipWindow>();

    private readonly List<AnimatorClipInfo> clipInfos = new();
    private readonly List<Vector3> samples = new();
    private readonly List<Vector3> segment = new();

    private bool isEmitting;
    private int emittingWindow = -1;
    private int emittingStateHash;
    private float previousNormalizedTime;

    // Points of the newest, provisional segment between its two samples.
    private int provisionalCount;

    public float PointSpacing => pointSpacing;

    private void Awake()
    {
        if (trail == null)
        {
            trail = transform.root.GetComponentInChildren<TrailRenderer>();
        }

        if (animator == null)
        {
            animator = transform.root.GetComponentInChildren<Animator>();
        }

        if (trail != null)
        {
            // Off whatever the component says; only a window turns it on.
            trail.emitting = false;
            trail.minVertexDistance = NoAutoPoints;
            trail.Clear();
        }
    }

    private void OnDisable()
    {
        if (trail != null)
        {
            trail.emitting = false;
            trail.Clear();
        }

        isEmitting = false;
        emittingWindow = -1;
    }

    private void LateUpdate()
    {
        if (animator == null || trail == null)
        {
            return;
        }

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        int window = FindWindow(state, out float frame);

        bool shouldEmit = window >= 0 &&
            frame >= windows[window].startFrame &&
            frame <= windows[window].endFrame;

        // Re-entering the same clip from its start (a repeated attack) has to
        // open a fresh trail, or the new swing joins onto the old one.
        bool restarted = isEmitting &&
            (window != emittingWindow ||
                state.fullPathHash != emittingStateHash ||
                state.normalizedTime < previousNormalizedTime);

        if (shouldEmit && (!isEmitting || restarted))
        {
            // Drops the previous swing's points, and anything the renderer
            // gathered while emission was off.
            trail.Clear();
            trail.emitting = true;
            samples.Clear();
            provisionalCount = 0;
            AddSample(trail.transform.position);

            isEmitting = true;
            emittingWindow = window;
            emittingStateHash = state.fullPathHash;
        }
        else if (shouldEmit)
        {
            AddSample(trail.transform.position);
        }
        else if (isEmitting)
        {
            // Points already laid down age out on the renderer's own Time.
            trail.emitting = false;
            isEmitting = false;
            emittingWindow = -1;
        }

        previousNormalizedTime = state.normalizedTime;
    }

    private void AddSample(Vector3 position)
    {
        samples.Add(position);

        if (samples.Count > 4)
        {
            samples.RemoveAt(0);
        }

        int n = samples.Count;

        if (n == 1)
        {
            trail.AddPosition(position);
            return;
        }

        // The previous segment can be finished now that the sample after it
        // is known. Its endpoints are unchanged, so it has the same number of
        // points as its provisional version and is rewritten in place.
        if (n >= 3 && provisionalCount > 0)
        {
            Vector3 b = samples[n - 3];
            Vector3 c = samples[n - 2];
            Vector3 a = n >= 4 ? samples[n - 4] : b + b - c;

            segment.Clear();
            AppendSegment(a, b, c, position, pointSpacing, segment);

            int first = trail.positionCount - 1 - provisionalCount;

            for (int i = 0; i < segment.Count && first + i >= 0; i++)
            {
                trail.SetPosition(first + i, segment[i]);
            }
        }

        Vector3 from = samples[n - 2];
        Vector3 before = n >= 3 ? samples[n - 3] : from + from - position;

        segment.Clear();
        AppendSegment(before, from, position, position + position - from, pointSpacing, segment);

        for (int i = 0; i < segment.Count; i++)
        {
            trail.AddPosition(segment[i]);
        }

        trail.AddPosition(position);
        provisionalCount = segment.Count;
    }

    // The points strictly between b and c on the centripetal Catmull-Rom
    // spline through a, b, c, d, about spacing apart. Their number depends on
    // b and c alone, which is what lets a provisional segment be rewritten
    // in place. Shared with the editor preview so both draw the same curve.
    public static void AppendSegment(
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        float spacing,
        List<Vector3> points)
    {
        int segments = Mathf.Clamp(
            Mathf.CeilToInt((c - b).magnitude / Mathf.Max(spacing, 1e-4f)),
            1,
            MaxPointsPerSegment);

        float k0 = 0f;
        float k1 = k0 + KnotDelta(a, b);
        float k2 = k1 + KnotDelta(b, c);
        float k3 = k2 + KnotDelta(c, d);

        for (int i = 1; i < segments; i++)
        {
            float t = Mathf.Lerp(k1, k2, i / (float)segments);
            points.Add(Interpolate(a, b, c, d, k0, k1, k2, k3, t));
        }
    }

    // Lays out the whole trail for a run of samples the way AddSample does
    // frame by frame, for the editor preview. owner[i] is the index of the
    // sample on whose frame point i was laid, which is what its age is
    // measured from.
    public static void BuildTrail(
        IList<Vector3> path,
        float spacing,
        List<Vector3> points,
        List<int> owner)
    {
        points.Clear();
        owner.Clear();
        var scratch = new List<Vector3>();
        int provisional = 0;

        for (int n = 0; n < path.Count; n++)
        {
            Vector3 position = path[n];

            if (n == 0)
            {
                points.Add(position);
                owner.Add(0);
                continue;
            }

            if (n >= 2 && provisional > 0)
            {
                Vector3 b = path[n - 2];
                Vector3 c = path[n - 1];
                Vector3 a = n >= 3 ? path[n - 3] : b + b - c;

                scratch.Clear();
                AppendSegment(a, b, c, position, spacing, scratch);

                int first = points.Count - 1 - provisional;

                for (int i = 0; i < scratch.Count; i++)
                {
                    points[first + i] = scratch[i];
                }
            }

            Vector3 from = path[n - 1];
            Vector3 before = n >= 2 ? path[n - 2] : from + from - position;

            scratch.Clear();
            AppendSegment(before, from, position, position + position - from, spacing, scratch);

            for (int i = 0; i < scratch.Count; i++)
            {
                points.Add(scratch[i]);
                owner.Add(n);
            }

            points.Add(position);
            owner.Add(n);
            provisional = scratch.Count;
        }
    }

    private static float KnotDelta(Vector3 a, Vector3 b)
    {
        return Mathf.Max(Mathf.Pow((b - a).magnitude, KnotAlpha), MinKnotDelta);
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

    private int FindWindow(AnimatorStateInfo state, out float frame)
    {
        frame = 0f;
        animator.GetCurrentAnimatorClipInfo(0, clipInfos);

        if (clipInfos.Count == 0)
        {
            return -1;
        }

        AnimationClip clip = clipInfos[0].clip;

        if (clip == null)
        {
            return -1;
        }

        for (int i = 0; i < windows.Length; i++)
        {
            if (windows[i].clipName == clip.name)
            {
                // normalizedTime keeps counting past 1 on a held last frame;
                // attack clips do not loop, so it is clamped rather than
                // wrapped.
                float time = Mathf.Min(state.normalizedTime, 1f) * clip.length;
                frame = time * FrameRate;
                return i;
            }
        }

        return -1;
    }
}
