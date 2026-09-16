using System.Collections.Generic;
using UnityEngine;

// Plays one authored slash. The shape comes from the mesh the action supplies,
// so this only has to swap the mesh in, drive the shader over normalized time,
// and hand itself back to the pool when it is done.
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class SlashEffect : MonoBehaviour
{
    private const float DefaultDuration = 0.25f;
    private const float DefaultFadeOutDuration = 0.12f;
    private const float DefaultRevealSpan = 0.55f;
    private const float DefaultRevealSoftness = 0.12f;

    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
    private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
    private static readonly int HeadId = Shader.PropertyToID("_Head");
    private static readonly int TailId = Shader.PropertyToID("_Tail");
    private static readonly int EdgeSoftnessId =
        Shader.PropertyToID("_EdgeSoftness");
    private static readonly int ArcCenterId = Shader.PropertyToID("_ArcCenter");
    private static readonly int ArcSpanId = Shader.PropertyToID("_ArcSpan");
    private static readonly int InnerRadiusId =
        Shader.PropertyToID("_InnerRadius");
    private static readonly int OuterRadiusId =
        Shader.PropertyToID("_OuterRadius");
    private static readonly int UseMeshUVId = Shader.PropertyToID("_UseMeshUV");
    private static readonly int MeshUVRectId = Shader.PropertyToID("_MeshUVRect");
    private static readonly int ReverseSweepId =
        Shader.PropertyToID("_ReverseSweep");

    private static readonly Color DefaultColor =
        new Color(4f, 2.2f, 1f, 1f);

    // Measuring an arc means reading its vertices, which allocates. Meshes are
    // few and shared, so the result is cached per mesh.
    private static readonly Dictionary<Mesh, Vector4> ArcCache = new();
    private static readonly Dictionary<Mesh, Vector4> UnwrapCache = new();

    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private MaterialPropertyBlock propertyBlock;

    private CharacterEffectSpawner owner;
    private ActionEffectSettings settings;
    private Transform followAnchor;
    private Vector3 followOffset;
    private Vector3 baseScale = Vector3.one;
    private float duration = DefaultDuration;
    private float fadeOutDuration = DefaultFadeOutDuration;
    private float revealSpan = DefaultRevealSpan;
    private float revealSoftness = DefaultRevealSoftness;
    private float elapsedTime;
    private float fadeElapsedTime;
    private bool isPlaying;
    private bool isFading;

    private void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
        propertyBlock = new MaterialPropertyBlock();
        meshRenderer.enabled = false;
    }

    public void Play(
        CharacterEffectSpawner spawner,
        in ActionEffectSettings effectSettings,
        Transform followTarget)
    {
        owner = spawner;
        settings = effectSettings;

        // Only the target's translation is tracked. The swing plane was fixed
        // from the facing at spawn, and turning mid-swing must not swing the
        // crescent round with it.
        followAnchor = followTarget;
        followOffset = followTarget != null
            ? transform.position - followTarget.position
            : Vector3.zero;

        if (settings.mesh == null)
        {
            Debug.LogError(
                "Slash effect has no mesh assigned.",
                this);
            Stop();
            return;
        }

        meshFilter.sharedMesh = settings.mesh;

        duration = settings.duration > 0f
            ? settings.duration
            : DefaultDuration;

        fadeOutDuration = settings.fadeOutDuration > 0f
            ? settings.fadeOutDuration
            : DefaultFadeOutDuration;

        revealSpan = settings.revealSpan > 0f
            ? settings.revealSpan
            : DefaultRevealSpan;

        revealSoftness = settings.revealSoftness > 0f
            ? settings.revealSoftness
            : DefaultRevealSoftness;

        baseScale = settings.scale.sqrMagnitude > Mathf.Epsilon
            ? settings.scale
            : Vector3.one;

        elapsedTime = 0f;
        fadeElapsedTime = 0f;
        isPlaying = true;
        isFading = false;
        meshRenderer.enabled = true;
        Apply(0f, 1f);
    }

    // The attack's active window closed. The sweep carries on, but the whole
    // crescent now fades, so the effect can never outlast the motion that
    // produced it.
    public void End()
    {
        if (!isPlaying || isFading)
        {
            return;
        }

        isFading = true;
        fadeElapsedTime = 0f;
    }

    // The motion was cut short - a dash cancelling the recovery, a death, a
    // disable. Unlike End() this ignores the fade and kills the effect now.
    public void Cancel()
    {
        if (!isPlaying)
        {
            return;
        }

        Finish();
    }

    public void Stop()
    {
        isPlaying = false;
        isFading = false;
        followAnchor = null;
        meshRenderer.enabled = false;
    }

    private void Update()
    {
        if (!isPlaying)
        {
            return;
        }

        elapsedTime += Time.deltaTime;
        float normalizedTime = elapsedTime / duration;

        float fade = 1f;

        if (isFading)
        {
            fadeElapsedTime += Time.deltaTime;
            fade = 1f - Mathf.Clamp01(fadeElapsedTime / fadeOutDuration);
        }

        // Whichever runs out first ends the effect: its own sweep, or the fade
        // that started when the active window closed.
        if (normalizedTime >= 1f || fade <= 0f)
        {
            Finish();
            return;
        }

        Apply(normalizedTime, fade);
    }

    // Followed after movement and animation have run for the frame, so the
    // effect sits where the target is this frame rather than the last one.
    private void LateUpdate()
    {
        if (isPlaying && followAnchor != null)
        {
            transform.position = followAnchor.position + followOffset;
        }
    }

    private void Finish()
    {
        Stop();

        // The owner is gone when the character was destroyed mid-swing. A
        // detached slash outlives its spawner, so it has to clean itself up.
        if (owner == null)
        {
            Destroy(gameObject);
            return;
        }

        owner.Release(this);
    }

    private void Apply(float normalizedTime, float fade)
    {
        transform.localScale =
            baseScale * Evaluate(settings.scaleCurve, normalizedTime, 1f);

        Color color = settings.color.maxColorComponent > 0f
            ? settings.color
            : DefaultColor;

        // The band starts entirely before the arc and finishes entirely past
        // it, so the tail leads the reveal and the head leads the erase.
        float sweep = Evaluate(
            settings.revealCurve,
            normalizedTime,
            normalizedTime);
        float head = Mathf.Lerp(0f, 1f + revealSpan, sweep);
        float tail = head - revealSpan;

        Vector4 unwrap = ResolveUnwrap(settings.mesh);
        bool useMeshUV = unwrap.z > 0f;
        Vector4 arc = useMeshUV ? Vector4.zero : ResolveArc(settings.mesh);

        meshRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetFloat(UseMeshUVId, useMeshUV ? 1f : 0f);
        propertyBlock.SetVector(MeshUVRectId, unwrap);
        propertyBlock.SetFloat(ReverseSweepId, settings.reverseSweep ? 1f : 0f);
        propertyBlock.SetColor(ColorId, color);
        propertyBlock.SetFloat(
            AlphaId,
            Evaluate(settings.alphaCurve, normalizedTime, 1f) * fade);
        propertyBlock.SetFloat(
            DissolveId,
            Evaluate(settings.dissolveCurve, normalizedTime, 0f));
        propertyBlock.SetFloat(HeadId, head);
        propertyBlock.SetFloat(TailId, tail);
        propertyBlock.SetFloat(EdgeSoftnessId, revealSoftness);
        propertyBlock.SetFloat(ArcCenterId, arc.x);
        propertyBlock.SetFloat(ArcSpanId, arc.y);
        propertyBlock.SetFloat(InnerRadiusId, arc.z);
        propertyBlock.SetFloat(OuterRadiusId, arc.w);
        meshRenderer.SetPropertyBlock(propertyBlock);
    }

    // A free-form mesh carries its own unwrap (U along the cut, V inner to
    // outer); an older flat arc exported without one has every UV at zero and
    // still needs the coordinates derived from its vertices.
    //
    // Returns the unwrap's bounds as (min U, min V, size U, size V), or zero
    // size when there is none. The shader stretches those bounds to 0..1, so
    // the unwrap only has to run the right way round - fitting it exactly to
    // the UV square by hand in Blender is not needed. An unreadable mesh
    // cannot be inspected and is assumed to already fill 0..1.
    private static Vector4 ResolveUnwrap(Mesh mesh)
    {
        if (UnwrapCache.TryGetValue(mesh, out Vector4 cached))
        {
            return cached;
        }

        Vector4 rect = new Vector4(0f, 0f, 1f, 1f);

        if (mesh.isReadable)
        {
            Vector2[] uv = mesh.uv;
            rect = Vector4.zero;

            if (uv.Length > 0)
            {
                Vector2 min = uv[0];
                Vector2 max = uv[0];

                for (int i = 1; i < uv.Length; i++)
                {
                    min = Vector2.Min(min, uv[i]);
                    max = Vector2.Max(max, uv[i]);
                }

                Vector2 size = max - min;

                if (size.x > 1e-3f && size.y > 1e-3f)
                {
                    rect = new Vector4(min.x, min.y, size.x, size.y);
                }
            }
        }

        // Outside play the mesh may be re-exported and reimported between
        // previews, so only a running game trusts the cache.
        if (Application.isPlaying)
        {
            UnwrapCache[mesh] = rect;
        }

        return rect;
    }

    // Returns (centre degrees, span degrees, inner radius, outer radius). The
    // shader needs these to work out how far along the arc a fragment sits
    // without the mesh carrying a UV unwrap.
    private static Vector4 ResolveArc(Mesh mesh)
    {
        if (ArcCache.TryGetValue(mesh, out Vector4 cached))
        {
            return cached;
        }

        Vector4 arc = new Vector4(180f, 150f, 0.78f, 1f);

        if (mesh.isReadable)
        {
            Vector3[] vertices = mesh.vertices;

            if (vertices.Length > 0)
            {
                float sumSin = 0f;
                float sumCos = 0f;
                float inner = float.MaxValue;
                float outer = 0f;

                for (int i = 0; i < vertices.Length; i++)
                {
                    float angle = Mathf.Atan2(vertices[i].y, vertices[i].x);
                    sumSin += Mathf.Sin(angle);
                    sumCos += Mathf.Cos(angle);

                    float radius =
                        new Vector2(vertices[i].x, vertices[i].y).magnitude;

                    if (radius < inner)
                    {
                        inner = radius;
                    }

                    if (radius > outer)
                    {
                        outer = radius;
                    }
                }

                float centre =
                    Mathf.Atan2(sumSin, sumCos) * Mathf.Rad2Deg;

                float minOffset = 0f;
                float maxOffset = 0f;

                for (int i = 0; i < vertices.Length; i++)
                {
                    float angle =
                        Mathf.Atan2(vertices[i].y, vertices[i].x) *
                        Mathf.Rad2Deg;
                    float offset = Mathf.DeltaAngle(centre, angle);

                    if (offset < minOffset)
                    {
                        minOffset = offset;
                    }

                    if (offset > maxOffset)
                    {
                        maxOffset = offset;
                    }
                }

                arc = new Vector4(
                    centre,
                    Mathf.Max(maxOffset - minOffset, 1f),
                    inner,
                    Mathf.Max(outer, inner + 1e-4f));
            }
        }
        else
        {
            Debug.LogWarning(
                "Slash mesh '" + mesh.name + "' is not readable, so the arc " +
                "sweep falls back to defaults. Enable Read/Write on the model " +
                "importer.",
                mesh);
        }

        if (Application.isPlaying)
        {
            ArcCache[mesh] = arc;
        }

        return arc;
    }

    private static float Evaluate(
        AnimationCurve curve,
        float normalizedTime,
        float fallback)
    {
        return curve != null && curve.length > 0
            ? curve.Evaluate(normalizedTime)
            : fallback;
    }
}
