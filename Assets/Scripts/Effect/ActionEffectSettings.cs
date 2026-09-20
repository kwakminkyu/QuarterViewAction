using System;
using UnityEngine;

// One authored mesh effect on a SkillAction. An action carries a list of these
// so a single swing can fire several - a crescent as the blade lands, a ground
// shockwave a moment later. Kept pure data: the action is a shared
// ScriptableObject and must never hold a spawned instance, so
// CharacterEffectSpawner owns everything with runtime state.
//
// The shader derives its arc coordinates from vertex positions, which makes a
// full ring just a 360 degree arc. That is why a ground shockwave needs no new
// component or shader - only a ring mesh laid flat and a growing scale curve.
[Serializable]
public struct ActionEffectSettings
{
    public bool enabled;

    // Seconds after the active phase opens before this one fires. Measured from
    // active enter and allowed to run past it, so an impact can land during the
    // recovery; the skill ending still cancels whatever is still alive.
    [Min(0f)] public float delay;

    // The shape is modelled rather than derived from the blade. Deriving it
    // from the animation meant wind-up and follow-through leaked into the arc
    // and every clip needed its own trimming pass.
    public Mesh mesh;

    // A particle prefab to play instead of the mesh - a flash, flying debris.
    // When set it takes over from Mesh and the sweep, colour and curve fields
    // below do nothing; placement (Delay, Offset, Scale, Swing Plane Normal,
    // Local Euler) and the lifetime rules still apply. The particles set their
    // own timing, so Duration and Fade Out Duration are unused too.
    public GameObject prefab;

    // Draws the mesh with this material instead of the slash prefab's own, for
    // effects whose look comes from a shader of their own - the shockwave ring
    // is a flat quad whose band and spikes are drawn entirely by its shader.
    // Colour, alpha and the effect's progress are still handed to it.
    public Material material;

    [ColorUsage(true, true)] public Color color;

    // How long the sweep takes. The effect also ends when the attack's active
    // window closes, so a duration longer than that window means the sweep
    // gets cut off partway by the fade.
    [Min(0f)] public float duration;

    // Fade applied once the active window closes, matching how the ribbon
    // behaves. A cancel skips this entirely and kills the effect outright.
    [Min(0f)] public float fadeOutDuration;

    // Placement, in the user's local frame.
    public Vector3 offset;
    public Vector3 scale;

    // Normal of the plane the mesh is laid into. For a blade arc this is the
    // measured swing plane, which is what makes an overhead chop read as
    // vertical without any camera trickery. For a ground effect it is simply
    // up, which lays the ring flat.
    public Vector3 swingPlaneNormal;

    // Applied after the swing-plane alignment. Corrects for however the mesh
    // happens to be oriented in its authoring package, and lets one mesh be
    // reused mirrored across combo steps.
    public Vector3 localEuler;

    // For effects that belong to the ground rather than the swing - a
    // shockwave, dust, debris. Once spawned the effect is left to the world:
    // it stays where it landed instead of following the character, and plays
    // out its whole Duration however the attack ends - the active window
    // closing, the skill finishing, or a dash cutting it short.
    public bool stayInWorld;

    // How much of the arc is lit at once, as a fraction of its length. The
    // crescent is drawn by a band that sweeps from the start of the cut to the
    // end, so the tail is revealed first, the head catches up, and the tail
    // erases behind it. 1 lights the whole arc at once, which is the "pops
    // into existence" look this replaced.
    [Range(0.05f, 1f)] public float revealSpan;

    // Softness of the two moving edges, in the same fraction-of-arc units.
    [Range(0f, 0.5f)] public float revealSoftness;

    // Evaluated over normalized effect time. Left empty they fall back to the
    // defaults in SlashEffect, which keeps a half-authored asset visible
    // instead of silently invisible.
    public AnimationCurve scaleCurve;

    // Which of the mesh's own axes the scale curve drives: 1 follows the curve,
    // 0 stays at Scale, anything between follows it partly. All zero - the
    // value older entries load with - means every axis. The mesh's Z is the
    // swing plane's normal, so for a ground burst laid with Swing Plane Normal
    // (0, 1, 0) it points up: (0, 0, 1) makes the burst shoot upwards while
    // its ring keeps its size.
    public Vector3 scaleCurveAxes;

    public AnimationCurve alphaCurve;
    public AnimationCurve dissolveCurve;

    // Drives the sweep. Left empty it runs linearly, which already reads well;
    // easing it makes the cut snap and the tail linger.
    public AnimationCurve revealCurve;
}
