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
    private static readonly int HeadId = Shader.PropertyToID("_Head");
    private static readonly int TailId = Shader.PropertyToID("_Tail");
    private static readonly int EdgeSoftnessId =
        Shader.PropertyToID("_EdgeSoftness");
    private static readonly int MeshUVRectId = Shader.PropertyToID("_MeshUVRect");

    // How far through its life the effect is, 0..1, for shaders that animate
    // on their own - the shockwave ring retracts its spikes with it.
    private static readonly int ProgressId = Shader.PropertyToID("_Progress");

    private static readonly Color DefaultColor =
        new Color(4f, 2.2f, 1f, 1f);

    // Measuring a mesh's unwrap means reading its UVs, which allocates. Meshes
    // are few and shared, so the result is cached per mesh.
    private static readonly Dictionary<Mesh, Vector4> UnwrapCache = new();

    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private MaterialPropertyBlock propertyBlock;

    // The prefab's own material, restored whenever an entry does not bring its
    // own, since pooled instances are reused across entries.
    private Material defaultMaterial;

    // The prefab this instance was made from, so it goes back to the right
    // pool. Set by EffectPool.
    public SlashEffect Source { get; internal set; }

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
        defaultMaterial = meshRenderer.sharedMaterial;
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
        meshRenderer.sharedMaterial = settings.material != null
            ? settings.material
            : defaultMaterial;

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
        // A ground effect is not part of the swing, so the swing ending does
        // not cut it short; only the skill ending does, through Cancel.
        if (!isPlaying || isFading || settings.stayInWorld)
        {
            return;
        }

        isFading = true;
        fadeElapsedTime = 0f;
    }

    // Left to play out when the attack that spawned it ends; see
    // ActionEffectSettings.stayInWorld.
    public bool StaysInWorld => isPlaying && settings.stayInWorld;

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

        // The character may be gone by now - destroyed mid-swing, or a ground
        // effect outliving it - and the pool is shared, so the effect can go
        // back on its own.
        if (owner != null)
        {
            owner.Release(this);
        }
        else
        {
            EffectPool.Return(this);
        }
    }

    private void Apply(float normalizedTime, float fade)
    {
        float curveScale = Evaluate(settings.scaleCurve, normalizedTime, 1f);
        Vector3 axes = settings.scaleCurveAxes.sqrMagnitude > Mathf.Epsilon
            ? settings.scaleCurveAxes
            : Vector3.one;

        // Each axis blends from no change to the full curve by its weight, so
        // a height-only curve leaves the footprint untouched.
        transform.localScale = Vector3.Scale(
            baseScale,
            new Vector3(
                Mathf.LerpUnclamped(1f, curveScale, axes.x),
                Mathf.LerpUnclamped(1f, curveScale, axes.y),
                Mathf.LerpUnclamped(1f, curveScale, axes.z)));

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

        meshRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetVector(MeshUVRectId, ResolveUnwrap(settings.mesh));
        propertyBlock.SetFloat(ProgressId, normalizedTime);
        propertyBlock.SetColor(ColorId, color);
        propertyBlock.SetFloat(
            AlphaId,
            Evaluate(settings.alphaCurve, normalizedTime, 1f) * fade);
        propertyBlock.SetFloat(HeadId, head);
        propertyBlock.SetFloat(TailId, tail);
        propertyBlock.SetFloat(EdgeSoftnessId, revealSoftness);
        meshRenderer.SetPropertyBlock(propertyBlock);
    }

    // Every effect mesh carries its own unwrap: U along the cut, V inner to
    // outer.
    //
    // Returns the unwrap's bounds as (min U, min V, size U, size V). The shader
    // stretches those bounds to 0..1, so the unwrap only has to run the right
    // way round - fitting it exactly to the UV square by hand in Blender is not
    // needed. An unreadable mesh cannot be inspected and is assumed to already
    // fill 0..1.
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
            Vector2 min = uv.Length > 0 ? uv[0] : Vector2.zero;
            Vector2 max = min;

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
            else
            {
                Debug.LogWarning(
                    "Effect mesh '" + mesh.name + "' has no UV unwrap, so it " +
                    "cannot be swept. Unwrap it with U along the cut and V " +
                    "from inner to outer edge.",
                    mesh);
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
