using System;
using UnityEngine;

// Authored on SkillAction assets. The ribbon itself is one persistent
// component per character, so these values are pushed into it at the start of
// each swing rather than being owned by the action.
[Serializable]
public struct BladeTrailSettings
{
    public bool enabled;

    // Seconds to wait after the active phase opens before sampling starts.
    // The active window is wider than the actual swing - the fourth combo
    // opens at frame 11.5 but the blade only starts moving at frame 14 - so
    // without this the wind-up gets recorded as part of the arc.
    [Min(0f)] public float startDelay;

    // How long to keep sampling after startDelay. 0 means until the active
    // phase closes. Combo 2 and 4 carry real 68 to 86 degree corners at the
    // end of their windows; this trims the follow-through when it reads as
    // clutter rather than impact.
    [Min(0f)] public float emitDuration;

    [ColorUsage(true, true)] public Color color;

    // Extends the tip away from the base, so the recorded arc is wider than
    // the blade really is. This is where the stylised exaggeration comes from.
    [Min(0f)] public float lengthMultiplier;

    // Pulls the inner edge toward the tip. 1 covers the whole blade, 0.5 keeps
    // only the outer half and reads as a thinner streak.
    [Range(0.05f, 1f)] public float widthScale;

    [Min(0f)] public float fadeOutDuration;
}
