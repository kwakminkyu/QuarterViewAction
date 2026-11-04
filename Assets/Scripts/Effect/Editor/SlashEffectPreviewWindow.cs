using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// Editor-only scrubber for the authored slash. Deliberately standalone: one
// file, no changes to any runtime script, nothing added to a prefab or scene.
// Deleting this file removes the feature entirely.
//
// Placement and the sweep are driven by calling the real CharacterEffectSpawner
// and SlashEffect through reflection instead of reimplementing them, so what
// shows here is what plays at runtime. The trade is that renaming those private
// members breaks the preview - it is meant to be disposable.
//
// It also bakes the slash itself from the blade's path over a frame range of
// the clip (Trajectory Bake); see SlashTrajectoryBaker.
public sealed class SlashEffectPreviewWindow : EditorWindow
{
    private const float ClipFrameRate = SlashTrajectoryBaker.FrameRate;

    // The empty at the blade's point; the sword it hangs from is the grip.
    private const string BladeTipName = "BladeTip";

    private const BindingFlags Hidden =
        BindingFlags.NonPublic | BindingFlags.Instance;

    private GameObject player;
    // Into PreviewActions(), which is rebuilt from the player's skills each
    // time, so it follows whatever is equipped.
    private int actionIndex;
    private float frame;
    private bool previewing;

    // One per authored effect entry, so a crescent and a later ground impact
    // can both be inspected on the same frame.
    private readonly List<SlashEffect> instances = new();
    private Animator animator;

    private Vector2 scrollPosition;

    // On by default so the preview matches play, where root motion is off.
    private bool keepInPlace = true;
    private PlayableGraph poseGraph;
    private AnimationClip poseGraphClip;

    // Empty plays the action's own clip; otherwise the named clip from the
    // player's controller, for clips no action uses yet.
    private string clipOverride = "";
    private bool showSlash = true;

    // Kept per clip in EditorPrefs, since each swing wants its own range and
    // reach.
    private SlashTrajectoryBaker.Settings bakeSettings =
        SlashTrajectoryBaker.Settings.Default;
    private string bakeSettingsClip;
    private string bakeReport;
    private MessageType bakeReportType;

    [MenuItem("Tools/Slash Effect Preview")]
    private static void Open()
    {
        GetWindow<SlashEffectPreviewWindow>("Slash Preview");
    }

    private void OnEnable()
    {
        SceneView.duringSceneGui += DrawHitbox;
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= DrawHitbox;
        StopPreview();
    }

    // Draws the action's hit volume where OverlapSkillAction puts it, so it
    // can be matched against the effect by eye. Red while the attack is
    // active and actually hitting, grey outside that window.
    private void DrawHitbox(SceneView view)
    {
        if (!previewing || !showSlash || player == null ||
            Event.current.type != EventType.Repaint)
        {
            return;
        }

        OverlapSkillAction action = SelectedAction();

        if (action == null || action.attackData == null)
        {
            return;
        }

        // Same placement as OverlapSkillAction: the flat facing, then the
        // centre offset in that frame from the character's root.
        Vector3 facing = player.transform.forward;
        facing.y = 0f;
        facing = facing.sqrMagnitude > Mathf.Epsilon ? facing.normalized : Vector3.forward;
        Quaternion rotation = Quaternion.LookRotation(facing, Vector3.up);
        Vector3 centre = player.transform.position + rotation * action.centerOffset;

        float activeStart = action.ClipActiveStartTime * ClipFrameRate;
        float activeEnd = activeStart + action.activeDuration * ClipFrameRate;
        bool active = frame >= activeStart && frame <= activeEnd;

        Color previous = Handles.color;
        Matrix4x4 previousMatrix = Handles.matrix;
        Handles.color = active ? new Color(1f, 0.2f, 0.2f, 1f) : new Color(0.6f, 0.6f, 0.6f, 0.6f);
        Handles.matrix = Matrix4x4.TRS(centre, rotation, Vector3.one);

        OverlapAttackData data = action.attackData;

        if (data.shape == OverlapShape.Box)
        {
            Handles.DrawWireCube(Vector3.zero, data.boxSize);
        }
        else
        {
            Handles.DrawWireDisc(Vector3.zero, Vector3.up, data.radius);
            Handles.DrawWireDisc(Vector3.zero, Vector3.right, data.radius);
            Handles.DrawWireDisc(Vector3.zero, Vector3.forward, data.radius);
        }

        Handles.matrix = previousMatrix;
        Handles.Label(centre, active ? "hitbox (active)" : "hitbox");
        Handles.color = previous;
    }

    // Tuning means editing the action asset while watching this window, so it
    // has to refresh on its own - otherwise a changed duration only shows up
    // after nudging the frame slider.
    private void OnInspectorUpdate()
    {
        if (previewing)
        {
            Repaint();
        }
    }

    private void OnGUI()
    {
        using (var scroll = new EditorGUILayout.ScrollViewScope(scrollPosition))
        {
            scrollPosition = scroll.scrollPosition;
            DrawWindow();
        }
    }

    private void DrawWindow()
    {
        EditorGUILayout.HelpBox(
            "Scrubs a melee action's clip in edit mode and places the authored slash " +
            "exactly where the runtime would. Nothing is saved to the scene.",
            MessageType.None);

        if (player == null)
        {
            player = GameObject.Find("Player");
        }

        if (player == null)
        {
            PlayerInputController input =
                FindAnyObjectByType<PlayerInputController>();

            if (input != null)
            {
                player = input.gameObject;
            }
        }

        using (new EditorGUI.DisabledScope(previewing))
        {
            player = (GameObject)EditorGUILayout.ObjectField(
                "Player", player, typeof(GameObject), true);
            actionIndex = DrawActionPopup(actionIndex);
            string pickedClip = DrawClipPopup(clipOverride);

            // The hitbox, slash and timing always come from Action, so a clip
            // that one of the actions plays selects that action instead of
            // standing over the wrong one's data. Only clips no action plays
            // stay as an override on the current Action.
            if (pickedClip != clipOverride)
            {
                int playing = ActionPlaying(pickedClip);

                if (playing >= 0)
                {
                    actionIndex = playing;
                    pickedClip = "";
                }
            }

            clipOverride = pickedClip;
        }

        EditorGUI.BeginChangeCheck();
        showSlash = EditorGUILayout.Toggle("Show Slash", showSlash);
        bool togglesChanged = EditorGUI.EndChangeCheck();

        SkillAction action = SelectedAction();
        AnimationClip clip = string.IsNullOrEmpty(clipOverride)
            ? ActionClip(action)
            : FindControllerClip(clipOverride);

        if (player == null || action == null || clip == null)
        {
            EditorGUILayout.HelpBox(
                "Needs a Player in the open scene with at least one melee " +
                "action that plays an animator state, and that state's clip.",
                MessageType.Warning);
            return;
        }

        DrawTimingInfo(action, clip);
        EditorGUILayout.Space();
        DrawBake(SelectedAction(), clip);
        EditorGUILayout.Space();

        EditorGUI.BeginChangeCheck();
        frame = EditorGUILayout.Slider(
            "Frame", frame, 0f, clip.length * ClipFrameRate);
        keepInPlace = EditorGUILayout.Toggle(
            new GUIContent(
                "Keep In Place",
                "Discard the clip's root travel, as play mode does with " +
                "root motion off."),
            keepInPlace);
        bool frameChanged = EditorGUI.EndChangeCheck() || togglesChanged;

        EditorGUILayout.BeginHorizontal();

        if (!previewing && GUILayout.Button("Start Preview"))
        {
            StartPreview();
            frameChanged = previewing;
        }

        if (previewing && GUILayout.Button("Stop Preview"))
        {
            StopPreview();
        }

        EditorGUILayout.EndHorizontal();

        // Re-applying on the repaint pass keeps the view honest against asset
        // edits, not just slider drags.
        if (previewing && (frameChanged || Event.current.type == EventType.Repaint))
        {
            Apply(action, clip);
        }
    }

    // Sampling the clip directly applies its root curves, so the body walks
    // off with the motion. In play the Animator has root motion off and that
    // travel is discarded; going through a playable graph handles the root the
    // same way, which keeps the preview where the game would.
    private void SamplePose(AnimationClip clip, float time)
    {
        AnimationMode.BeginSampling();

        if (keepInPlace)
        {
            if (!poseGraph.IsValid() || poseGraphClip != clip)
            {
                DestroyPoseGraph();
                poseGraph = PlayableGraph.Create("SlashEffectPreview");
                var output = AnimationPlayableOutput.Create(
                    poseGraph, "Pose", animator);
                output.SetSourcePlayable(
                    AnimationClipPlayable.Create(poseGraph, clip));
                poseGraphClip = clip;
            }

            AnimationMode.SamplePlayableGraph(poseGraph, 0, time);
        }
        else
        {
            AnimationMode.SampleAnimationClip(animator.gameObject, clip, time);
        }

        AnimationMode.EndSampling();
    }

    private void DestroyPoseGraph()
    {
        if (poseGraph.IsValid())
        {
            poseGraph.Destroy();
        }

        poseGraphClip = null;
    }

    // Shows where the effect's own window sits inside the attack. A duration
    // longer than the active phase is cut off by the fade before the sweep
    // finishes, and that is far easier to see here than to reason about.
    private void DrawTimingInfo(SkillAction action, AnimationClip clip)
    {
        // In clip frames: a startup played at another speed shifts where the
        // active phase sits in the clip, not how long the startup lasts.
        float startupF = action.ClipActiveStartTime * ClipFrameRate;
        float activeEndF = startupF + action.activeDuration * ClipFrameRate;
        float skillEndF = activeEndF + action.recoveryDuration * ClipFrameRate;

        EditorGUILayout.LabelField(
            "Timing (frames @30fps)", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "  clip",
            clip.length.ToString("F3") + "s / " +
            (clip.length * ClipFrameRate).ToString("F1") + "f");
        EditorGUILayout.LabelField(
            "  active",
            "f" + startupF.ToString("F1") + " .. f" + activeEndF.ToString("F1"));
        EditorGUILayout.LabelField(
            "  skill ends",
            "f" + skillEndF.ToString("F1"));

        int count = action.effects == null ? 0 : action.effects.Length;
        EditorGUILayout.LabelField("  effects", count.ToString());

        for (int i = 0; i < count; i++)
        {
            ActionEffectSettings e = action.effects[i];
            float startF = startupF + e.delay * ClipFrameRate;
            float endF = startF + e.duration * ClipFrameRate;

            EditorGUILayout.LabelField(
                "    [" + i + "] " + (e.prefab != null
                    ? e.prefab.name + " (particles, play mode only)"
                    : e.mesh == null ? "no mesh" : e.mesh.name),
                "f" + startF.ToString("F1") + " .. f" + endF.ToString("F1") +
                (e.enabled ? "" : "   (DISABLED)"));

            // Anything still alive when the skill ends is cancelled outright,
            // so a run past that point is never seen.
            if (e.enabled && endF > skillEndF)
            {
                EditorGUILayout.HelpBox(
                    "Effect " + i + " would run to f" + endF.ToString("F1") +
                    " but the skill ends at f" + skillEndF.ToString("F1") +
                    ", where it is cancelled.",
                    MessageType.Warning);
            }
        }
    }

    // Bakes the action's slash from the blade's path over a frame range of
    // the clip (SlashTrajectoryBaker). Writes the mesh and the timing into the
    // action's first effect entry.
    private void DrawBake(OverlapSkillAction action, AnimationClip clip)
    {
        if (action == null)
        {
            return;
        }

        if (bakeSettingsClip != clip.name)
        {
            LoadBakeSettings(clip, action);
            bakeReport = null;
        }

        EditorGUILayout.LabelField("Trajectory Bake", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        DrawBakeRange(clip);
        bakeSettings.innerDistance = EditorGUILayout.FloatField(
            new GUIContent("Inner (m from grip)", "Where the crescent's inner edge sits along the blade."),
            bakeSettings.innerDistance);
        bakeSettings.fitToHitbox = EditorGUILayout.Toggle(
            new GUIContent(
                "Fit To Hitbox",
                "Each moment of the swing reaches exactly to the hitbox's edge seen from " +
                "above, and nothing is drawn where the blade is outside it."),
            bakeSettings.fitToHitbox);

        if (bakeSettings.fitToHitbox)
        {
            bakeSettings.fitShape = (SlashTrajectoryBaker.FitShape)EditorGUILayout.EnumPopup(
                new GUIContent(
                    "Fit Shape",
                    "Box: the hitbox's own outline, so the arc has straight sides. Arc: seen " +
                    "from above, the half-ellipse through both back corners and the front's " +
                    "middle, for a horizontal sweep. Side Arc: seen from the side, the " +
                    "quarter-ellipse from the tip's highest point down to the front edge on " +
                    "the ground, for an overhead chop."),
                bakeSettings.fitShape);

            if (bakeSettings.fitShape == SlashTrajectoryBaker.FitShape.Arc)
            {
                bakeSettings.arcCenter = EditorGUILayout.FloatField(
                    new GUIContent(
                        "Arc Center (m)",
                        "How far ahead of the hitbox's back edge the ellipse's centre sits. " +
                        "0 runs the arc through both back corners; further forward rounds " +
                        "its sides and curls them in behind."),
                    bakeSettings.arcCenter);
                bakeSettings.arcBackRadius = EditorGUILayout.FloatField(
                    new GUIContent(
                        "Arc Back Radius (m)",
                        "The ellipse's radius behind its centre, to pull in the part that " +
                        "curls round behind the body without changing the front. 0 matches " +
                        "the front."),
                    bakeSettings.arcBackRadius);
                bakeSettings.arcSquareness = EditorGUILayout.Slider(
                    new GUIContent(
                        "Arc Squareness",
                        "2 is an ellipse; higher squares the arc off towards the box's " +
                        "corners so it fills more of the box."),
                    bakeSettings.arcSquareness, 2f, 6f);
            }
        }

        bakeSettings.reachExtension = EditorGUILayout.FloatField(
            bakeSettings.fitToHitbox
                ? new GUIContent("Max Reach Past Tip (m)", "The furthest the hitbox may stretch the arc beyond the tip.")
                : new GUIContent("Reach Past Tip (m)", "How far the outer edge reaches beyond the tip, towards the hitbox's edge."),
            bakeSettings.reachExtension);
        bakeSettings.crescent = EditorGUILayout.Slider(
            new GUIContent(
                "Crescent",
                "Thins the band towards both ends like a crescent moon's horns. 0 is off; " +
                "higher thins the ends sooner."),
            bakeSettings.crescent, 0f, 3f);
        bakeSettings.linger = EditorGUILayout.FloatField(
            new GUIContent("Linger (s)", "How long the crescent stays after the blade has passed."),
            bakeSettings.linger);
        bakeSettings.samplesPerFrame = EditorGUILayout.IntSlider(
            new GUIContent(
                "Columns / Frame",
                "Mesh columns per animation frame; more cuts the arc's curve finer."),
            bakeSettings.samplesPerFrame, 1, 32);

        if (EditorGUI.EndChangeCheck())
        {
            SaveBakeSettings(clip.name);
        }

        using (new EditorGUI.DisabledScope(!previewing))
        {
            if (GUILayout.Button(previewing
                ? "Bake Slash (into effect 0)"
                : "Bake (start the preview first)"))
            {
                Bake(action, clip);
            }
        }

        if (!string.IsNullOrEmpty(bakeReport))
        {
            EditorGUILayout.HelpBox(bakeReport, bakeReportType);
        }
    }

    // The frames of the clip the blade is swept over, in whole frames, since
    // that is how animation frames are talked about.
    private void DrawBakeRange(AnimationClip clip)
    {
        int lastFrame = Mathf.RoundToInt(clip.length * ClipFrameRate);
        float start = bakeSettings.startFrame;
        float end = bakeSettings.endFrame;

        EditorGUILayout.MinMaxSlider(
            new GUIContent("Frames", "The part of the clip the slash is swept over."),
            ref start, ref end, 0f, lastFrame);

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PrefixLabel(" ");
            start = EditorGUILayout.IntField(Mathf.RoundToInt(start));
            GUILayout.Label("..", GUILayout.ExpandWidth(false));
            end = EditorGUILayout.IntField(Mathf.RoundToInt(end));
            GUILayout.Label("/ " + lastFrame, GUILayout.ExpandWidth(false));
        }

        bakeSettings.startFrame = Mathf.Clamp(Mathf.RoundToInt(start), 0, lastFrame);
        bakeSettings.endFrame = Mathf.Clamp(
            Mathf.RoundToInt(end), (int)bakeSettings.startFrame, lastFrame);
    }

    private void Bake(OverlapSkillAction action, AnimationClip clip)
    {
        Transform tip = FindChild(player.transform, BladeTipName);
        Transform grip = tip != null ? tip.parent : null;

        if (grip == null)
        {
            bakeReport = "Needs a '" + BladeTipName + "' at the blade's point, under the sword.";
            bakeReportType = MessageType.Error;
            return;
        }

        float start = bakeSettings.startFrame / ClipFrameRate;
        float end = Mathf.Min(bakeSettings.endFrame / ClipFrameRate, clip.length);

        // Effects fire from the active phase opening at the earliest, so a
        // range opening before it is cut to it.
        float activeStart = action.ClipActiveStartTime;
        bool clipped = start < activeStart;
        start = Mathf.Max(start, activeStart);

        if (end - start <= 1e-4f)
        {
            bakeReport = "The frame range ends before the active phase opens.";
            bakeReportType = MessageType.Error;
            return;
        }

        var spawner = player.GetComponent<CharacterEffectSpawner>();
        var pivot = spawner != null
            ? (Transform)Field(typeof(CharacterEffectSpawner), "effectPivot").GetValue(spawner)
            : null;
        Vector3 origin = pivot != null ? pivot.position : player.transform.position;
        Quaternion facing = FlatFacing(player.transform);
        bool hasHitBox = SlashTrajectoryBaker.TryGetHitBox(
            action, player.transform.position, origin, facing, out Bounds hitBox);

        if (bakeSettings.fitToHitbox && !hasHitBox)
        {
            bakeReport = "Fit To Hitbox needs the action's hit to be a box.";
            bakeReportType = MessageType.Error;
            return;
        }

        Mesh mesh = SlashTrajectoryBaker.Bake(
            clip,
            time => SamplePose(clip, time),
            grip,
            tip,
            origin,
            facing,
            start,
            end,
            in bakeSettings,
            hitBox);

        SlashTrajectoryBaker.ApplyToAction(
            action, mesh, start - activeStart, end - start, in bakeSettings);

        // The walk above left the body at the window's end.
        SamplePose(clip, frame / ClipFrameRate);

        bakeReport = ReachReport(action, mesh, origin, facing) +
            "\n" + SlashTrajectoryBaker.LastRoughness +
            (clipped ? "\nThe range opened before the active phase and was cut to it." : "");
        bakeReportType = MessageType.Info;
    }

    // How far the crescent's outer edge reaches against the hitbox, in the
    // character's frame, so Reach Past Tip can be matched to it.
    private string ReachReport(
        OverlapSkillAction action,
        Mesh mesh,
        Vector3 origin,
        Quaternion facing)
    {
        Vector3[] vertices = mesh.vertices;
        Vector2[] uv = mesh.uv;
        float forward = float.MinValue;
        float back = float.MaxValue;
        float left = float.MaxValue;
        float right = float.MinValue;

        for (int i = 0; i < vertices.Length; i++)
        {
            if (uv[i].y < 0.999f)
            {
                continue;
            }

            forward = Mathf.Max(forward, vertices[i].z);
            back = Mathf.Min(back, vertices[i].z);
            left = Mathf.Min(left, vertices[i].x);
            right = Mathf.Max(right, vertices[i].x);
        }

        string report =
            "Baked " + mesh.name + "\nOuter edge: front " + forward.ToString("F2") +
            "  back " + back.ToString("F2") + "  x " + left.ToString("F2") +
            " .. " + right.ToString("F2");

        if (SlashTrajectoryBaker.TryGetHitBox(
            action, player.transform.position, origin, facing, out Bounds hitBox))
        {
            report += "\nHitbox:     front " + hitBox.max.z.ToString("F2") +
                "  back " + hitBox.min.z.ToString("F2") + "  x " +
                hitBox.min.x.ToString("F2") + " .. " +
                hitBox.max.x.ToString("F2");
        }

        return report;
    }

    private static Quaternion FlatFacing(Transform root)
    {
        Vector3 facing = root.forward;
        facing.y = 0f;

        return Quaternion.LookRotation(
            facing.sqrMagnitude > Mathf.Epsilon ? facing.normalized : Vector3.forward,
            Vector3.up);
    }

    // A clip baked for the first time starts on its active phase.
    private void LoadBakeSettings(AnimationClip clip, SkillAction action)
    {
        SlashTrajectoryBaker.Settings defaults = SlashTrajectoryBaker.Settings.Default;
        string key = "SlashTrajectoryBake." + clip.name + ".";
        float activeStartF = Mathf.Round(action.ClipActiveStartTime * ClipFrameRate);
        float activeEndF = Mathf.Round(activeStartF + action.activeDuration * ClipFrameRate);

        bakeSettings = new SlashTrajectoryBaker.Settings
        {
            startFrame = EditorPrefs.GetFloat(key + "start", activeStartF),
            endFrame = EditorPrefs.GetFloat(key + "end", activeEndF),
            innerDistance = EditorPrefs.GetFloat(key + "inner", defaults.innerDistance),
            reachExtension = EditorPrefs.GetFloat(key + "reach", defaults.reachExtension),
            fitToHitbox = EditorPrefs.GetBool(key + "fit", defaults.fitToHitbox),
            arcCenter = EditorPrefs.GetFloat(key + "center", defaults.arcCenter),
            arcSquareness = EditorPrefs.GetFloat(key + "square", defaults.arcSquareness),
            crescent = EditorPrefs.GetFloat(key + "crescent", defaults.crescent),
            arcBackRadius = EditorPrefs.GetFloat(key + "back", defaults.arcBackRadius),
            fitShape = (SlashTrajectoryBaker.FitShape)EditorPrefs.GetInt(
                key + "shape", (int)defaults.fitShape),
            linger = EditorPrefs.GetFloat(key + "linger", defaults.linger),
            samplesPerFrame = EditorPrefs.GetInt(key + "samples", defaults.samplesPerFrame),
        };
        bakeSettingsClip = clip.name;
    }

    private void SaveBakeSettings(string clipName)
    {
        string key = "SlashTrajectoryBake." + clipName + ".";

        EditorPrefs.SetFloat(key + "start", bakeSettings.startFrame);
        EditorPrefs.SetFloat(key + "end", bakeSettings.endFrame);
        EditorPrefs.SetFloat(key + "inner", bakeSettings.innerDistance);
        EditorPrefs.SetFloat(key + "reach", bakeSettings.reachExtension);
        EditorPrefs.SetBool(key + "fit", bakeSettings.fitToHitbox);
        EditorPrefs.SetFloat(key + "center", bakeSettings.arcCenter);
        EditorPrefs.SetFloat(key + "square", bakeSettings.arcSquareness);
        EditorPrefs.SetFloat(key + "crescent", bakeSettings.crescent);
        EditorPrefs.SetFloat(key + "back", bakeSettings.arcBackRadius);
        EditorPrefs.SetInt(key + "shape", (int)bakeSettings.fitShape);
        EditorPrefs.SetFloat(key + "linger", bakeSettings.linger);
        EditorPrefs.SetInt(key + "samples", bakeSettings.samplesPerFrame);
    }

    private void StartPreview()
    {
        animator = player.GetComponentInChildren<Animator>();

        if (animator == null)
        {
            Debug.LogError("Preview needs an Animator under the Player.");
            return;
        }

        CharacterEffectSpawner spawner =
            player.GetComponent<CharacterEffectSpawner>();

        if (spawner != null)
        {
            // Awake never runs in edit mode, so the spawner's cached
            // references are still null. Run it by hand before asking it to
            // place anything.
            InvokeHidden(spawner, "Awake");
        }

        if (showSlash && !HasSlashPrefab(spawner))
        {
            Debug.LogError(
                "Preview needs a CharacterEffectSpawner with a slash prefab " +
                "on the Player. Turn Show Slash off to preview only the pose.");
            return;
        }

        AnimationMode.StartAnimationMode();
        previewing = true;
    }

    private void StopPreview()
    {
        if (AnimationMode.InAnimationMode())
        {
            AnimationMode.StopAnimationMode();
        }

        for (int i = 0; i < instances.Count; i++)
        {
            if (instances[i] != null)
            {
                DestroyImmediate(instances[i].gameObject);
            }
        }

        instances.Clear();
        previewing = false;
        DestroyPoseGraph();
    }

    private void Apply(SkillAction action, AnimationClip clip)
    {
        if (animator == null)
        {
            return;
        }

        // Pose the character first; placement reads the posed transforms.
        SamplePose(clip, frame / ClipFrameRate);

        CharacterEffectSpawner spawner =
            player.GetComponent<CharacterEffectSpawner>();

        // An action can fire several effects at different delays, so the
        // preview needs one instance per entry rather than a single one.
        int effectCount = showSlash && HasSlashPrefab(spawner) &&
            action.effects != null
                ? action.effects.Length
                : 0;
        EnsureInstances(effectCount, spawner);

        float activeStartF = action.ClipActiveStartTime * ClipFrameRate;

        for (int i = 0; i < instances.Count; i++)
        {
            SlashEffect instance = instances[i];
            var renderer = instance.GetComponent<MeshRenderer>();
            ActionEffectSettings settings = action.effects[i];

            // Where this frame sits inside that entry's own lifetime. This is
            // the only thing the preview works out for itself; placement and
            // the sweep come from the runtime code below.
            float startF = activeStartF + settings.delay * ClipFrameRate;
            float elapsed = (frame - startF) / ClipFrameRate;
            float normalized =
                settings.duration > 0f ? elapsed / settings.duration : 0f;

            // Particle entries are left out: their systems only simulate in
            // play, and handing one to the spawner here would instantiate the
            // prefab into the edited scene.
            bool visible = settings.enabled && settings.mesh != null &&
                settings.prefab == null &&
                normalized >= 0f && normalized <= 1f;

            if (!visible)
            {
                renderer.enabled = false;
                continue;
            }

            // The spawner's own placement, so the preview cannot drift from
            // runtime. Only the maths is borrowed: going through PlaySlash
            // would rent from the shared EffectPool and build its Effects root
            // in the edited scene.
            var placement = new object[] { settings, player.transform.forward, null, null };
            Method(typeof(CharacterEffectSpawner), "ResolvePlacement")
                .Invoke(spawner, placement);
            instance.transform.SetPositionAndRotation(
                (Vector3)placement[2], (Quaternion)placement[3]);
            instance.Play(null, in settings, null);

            renderer.enabled = true;
            Method(typeof(SlashEffect), "Apply")
                .Invoke(instance, new object[] { normalized, 1f });
        }

        // No Repaint() here - this can run from inside a repaint, and
        // OnInspectorUpdate already keeps the window ticking.
        SceneView.RepaintAll();
    }

    private void EnsureInstances(int count, CharacterEffectSpawner spawner)
    {
        while (instances.Count < count)
        {
            var prefab = (SlashEffect)
                Field(typeof(CharacterEffectSpawner), "slashPrefab").GetValue(spawner);
            SlashEffect created = Instantiate(prefab);
            created.name = "SlashPreview " + instances.Count + " (temporary)";
            created.gameObject.hideFlags = HideFlags.HideAndDontSave;
            InvokeHidden(created, "Awake");
            instances.Add(created);
        }

        while (instances.Count > count)
        {
            int last = instances.Count - 1;

            if (instances[last] != null)
            {
                DestroyImmediate(instances[last].gameObject);
            }

            instances.RemoveAt(last);
        }
    }

    private static Transform FindChild(Transform root, string childName)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == childName)
            {
                return child;
            }
        }

        return null;
    }

    private static bool HasSlashPrefab(CharacterEffectSpawner spawner)
    {
        return spawner != null &&
            Field(typeof(CharacterEffectSpawner), "slashPrefab")
                .GetValue(spawner) != null;
    }

    private string DrawClipPopup(string current)
    {
        AnimationClip[] clips = ControllerClips();
        var labels = new string[clips.Length + 1];
        OverlapSkillAction selectedAction = SelectedAction();
        labels[0] = (selectedAction != null ? selectedAction.animationState : "Action") +
            " clip";
        int selected = 0;

        for (int i = 0; i < clips.Length; i++)
        {
            labels[i + 1] = clips[i].name;

            if (clips[i].name == current)
            {
                selected = i + 1;
            }
        }

        selected = EditorGUILayout.Popup(
            new GUIContent(
                "Clip",
                "Any clip on the player's controller. A clip one of the " +
                "actions plays switches Action to it; any other clip plays " +
                "over the current Action's slash and hitbox."),
            selected,
            labels);

        return selected == 0 ? "" : clips[selected - 1].name;
    }

    // Labelled by the animator state each action plays, which names it
    // uniquely enough: BasicCombo1..3, DashAttack.
    private int DrawActionPopup(int current)
    {
        List<OverlapSkillAction> actions = PreviewActions();

        if (actions.Count == 0)
        {
            EditorGUILayout.Popup("Action", 0, new[] { "(none)" });
            return 0;
        }

        var labels = new string[actions.Count];

        for (int i = 0; i < actions.Count; i++)
        {
            labels[i] = actions[i].animationState;
        }

        return EditorGUILayout.Popup(
            new GUIContent(
                "Action",
                "Every melee action of the player's equipped skills that " +
                "plays an animator state. Its hitbox, slash and timing are " +
                "the ones previewed."),
            Mathf.Clamp(current, 0, actions.Count - 1),
            labels);
    }

    // Index into PreviewActions() of the action that plays this clip, or -1.
    private int ActionPlaying(string clipName)
    {
        if (string.IsNullOrEmpty(clipName))
        {
            return -1;
        }

        List<OverlapSkillAction> actions = PreviewActions();

        for (int i = 0; i < actions.Count; i++)
        {
            AnimationClip clip = ActionClip(actions[i]);

            if (clip != null && clip.name == clipName)
            {
                return i;
            }
        }

        return -1;
    }

    private AnimationClip FindControllerClip(string clipName)
    {
        foreach (AnimationClip clip in ControllerClips())
        {
            if (clip.name == clipName)
            {
                return clip;
            }
        }

        return null;
    }

    private AnimationClip[] ControllerClips()
    {
        Animator found = player != null
            ? player.GetComponentInChildren<Animator>()
            : null;

        if (found == null || found.runtimeAnimatorController == null)
        {
            return System.Array.Empty<AnimationClip>();
        }

        var unique = new List<AnimationClip>();

        foreach (AnimationClip clip in found.runtimeAnimatorController.animationClips)
        {
            if (clip != null && !unique.Contains(clip))
            {
                unique.Add(clip);
            }
        }

        unique.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        return unique.ToArray();
    }

    private static FieldInfo Field(System.Type type, string name)
    {
        return type.GetField(name, Hidden);
    }

    private static MethodInfo Method(System.Type type, string name)
    {
        return type.GetMethod(name, Hidden);
    }

    private static void InvokeHidden(object target, string method)
    {
        MethodInfo info = Method(target.GetType(), method);

        if (info != null)
        {
            info.Invoke(target, null);
        }
    }

    // Every melee action the player's equipped skills can play, in slot then
    // step order - the basic combo's steps, then the dash attack - read from
    // the player itself so the list follows whatever is equipped.
    private List<OverlapSkillAction> PreviewActions()
    {
        var actions = new List<OverlapSkillAction>();
        SkillController skills = player != null
            ? player.GetComponentInChildren<SkillController>()
            : null;

        if (skills == null)
        {
            return actions;
        }

        SerializedProperty equipped =
            new SerializedObject(skills).FindProperty("equippedSkills");

        for (int i = 0; equipped != null && i < equipped.arraySize; i++)
        {
            var skill = equipped.GetArrayElementAtIndex(i).objectReferenceValue
                as SkillDefinition;

            for (int step = 0; skill != null && step < skill.ActionCount; step++)
            {
                if (skill.GetAction(step) is OverlapSkillAction overlap &&
                    overlap.HasAnimation &&
                    !actions.Contains(overlap))
                {
                    actions.Add(overlap);
                }
            }
        }

        return actions;
    }

    private OverlapSkillAction SelectedAction()
    {
        List<OverlapSkillAction> actions = PreviewActions();

        return actions.Count > 0
            ? actions[Mathf.Clamp(actionIndex, 0, actions.Count - 1)]
            : null;
    }

    // The clip the action really plays: the motion of the state it names on
    // the player's controller, so the preview follows whatever the state holds.
    private AnimationClip ActionClip(SkillAction action)
    {
        if (action == null || !action.HasAnimation)
        {
            return null;
        }

        Animator found = player != null
            ? player.GetComponentInChildren<Animator>()
            : null;

        var controller = found != null
            ? found.runtimeAnimatorController as UnityEditor.Animations.AnimatorController
            : null;

        if (controller == null)
        {
            return null;
        }

        foreach (UnityEditor.Animations.AnimatorControllerLayer layer in controller.layers)
        {
            foreach (UnityEditor.Animations.ChildAnimatorState child in layer.stateMachine.states)
            {
                if (child.state.name == action.animationState)
                {
                    return child.state.motion as AnimationClip;
                }
            }
        }

        return null;
    }
}
