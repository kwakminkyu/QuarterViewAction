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
// The weapon trail is previewed too: the clip is walked pose by pose from its
// BladeTrailClipDriver window up to the current frame, collecting the points
// the player's TrailRenderer would have by then.
public sealed class SlashEffectPreviewWindow : EditorWindow
{
    private const float ClipFrameRate = 30f;

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
    private bool showTrail = true;

    // Stands in for the game's frame rate. The ribbon records once per frame
    // and fills in between with sub-frame samples, so this is the rate play
    // mode would record at.
    private float trailSampleRate = 60f;
    private LineRenderer trailLine;
    private readonly List<Vector3> trailPoints = new();
    private readonly List<float> trailPointTimes = new();
    private readonly List<Vector3> trailSamples = new();
    private readonly List<float> trailSampleTimes = new();
    private readonly List<int> trailOwners = new();
    private readonly List<Vector3> trailLinePoints = new();

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
            "and the blade trail exactly where the runtime would. Nothing is " +
            "saved to the scene.",
            MessageType.None);

        if (player == null)
        {
            player = GameObject.Find("Player");
        }

        if (player == null)
        {
            BladeTrailClipDriver driver =
                FindAnyObjectByType<BladeTrailClipDriver>();

            if (driver != null)
            {
                player = driver.transform.root.gameObject;
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
        showTrail = EditorGUILayout.Toggle("Show Trail", showTrail);
        trailSampleRate = Mathf.Max(
            1f,
            EditorGUILayout.FloatField(
                new GUIContent(
                    "Trail Sample Rate",
                    "Frames per second the trail is recorded at, standing " +
                    "in for the game's frame rate."),
                trailSampleRate));
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

        DrawTrailRange(clip);
        EditorGUILayout.Space();
        DrawTimingInfo(action, clip);
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

        DrawTrailInfo(clip);
    }

    // Edits the driver's window for this clip in place, so the range picked
    // while scrubbing is the one play mode records over. Goes through a
    // SerializedObject, which makes every change undoable.
    private void DrawTrailRange(AnimationClip clip)
    {
        BladeTrailClipDriver driver = FindTrailDriver();

        if (driver == null)
        {
            return;
        }

        var serialized = new SerializedObject(driver);
        SerializedProperty windows = serialized.FindProperty("windows");
        int index = FindWindowIndex(windows, clip.name);
        int lastFrame = Mathf.RoundToInt(
            clip.length * BladeTrailClipDriver.FrameRate);

        EditorGUILayout.LabelField(
            "Trail Range (frames @30fps)", EditorStyles.boldLabel);

        if (index < 0)
        {
            if (GUILayout.Button("Add Trail Window for '" + clip.name + "'"))
            {
                index = windows.arraySize;
                windows.arraySize++;

                SerializedProperty added = windows.GetArrayElementAtIndex(index);
                added.FindPropertyRelative("clipName").stringValue = clip.name;
                added.FindPropertyRelative("startFrame").floatValue = 0f;
                added.FindPropertyRelative("endFrame").floatValue = lastFrame;

                serialized.ApplyModifiedProperties();
            }

            return;
        }

        SerializedProperty window = windows.GetArrayElementAtIndex(index);
        SerializedProperty startProperty = window.FindPropertyRelative("startFrame");
        SerializedProperty endProperty = window.FindPropertyRelative("endFrame");

        float start = startProperty.floatValue;
        float end = endProperty.floatValue;

        EditorGUI.BeginChangeCheck();

        EditorGUILayout.MinMaxSlider(
            "Frames", ref start, ref end, 0f, lastFrame);

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PrefixLabel(" ");
            start = EditorGUILayout.IntField(Mathf.RoundToInt(start));
            GUILayout.Label("..", GUILayout.ExpandWidth(false));
            end = EditorGUILayout.IntField(Mathf.RoundToInt(end));
            GUILayout.Label("/ " + lastFrame, GUILayout.ExpandWidth(false));
        }

        if (EditorGUI.EndChangeCheck())
        {
            // Whole frames only: that is how animation frames are talked about,
            // and a slider drag would otherwise leave fractions behind.
            int startFrame = Mathf.Clamp(Mathf.RoundToInt(start), 0, lastFrame);
            int endFrame = Mathf.Clamp(Mathf.RoundToInt(end), startFrame, lastFrame);

            startProperty.floatValue = startFrame;
            endProperty.floatValue = endFrame;
            serialized.ApplyModifiedProperties();
        }
    }

    private static int FindWindowIndex(SerializedProperty windows, string clipName)
    {
        for (int i = 0; i < windows.arraySize; i++)
        {
            if (windows.GetArrayElementAtIndex(i)
                    .FindPropertyRelative("clipName").stringValue == clipName)
            {
                return i;
            }
        }

        return -1;
    }

    private BladeTrailClipDriver FindTrailDriver()
    {
        return player != null
            ? player.GetComponentInChildren<BladeTrailClipDriver>(true)
            : null;
    }

    private void DrawTrailInfo(AnimationClip clip)
    {
        if (!TryGetTrailWindow(clip, out BladeTrailClipDriver.ClipWindow window))
        {
            EditorGUILayout.LabelField(
                "  trail", "no BladeTrailClipDriver window for '" + clip.name + "'");
            return;
        }

        ResolveTrailSpan(in window, clip, out float start, out float end);

        EditorGUILayout.LabelField(
            "  trail",
            "f" + (start * ClipFrameRate).ToString("F1") + " .. f" +
            (end * ClipFrameRate).ToString("F1"));
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
                "on the Player. Turn Show Slash off to preview only the trail.");
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
        DestroyTrailInstance();
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

        // The trail walks the pose through its own window and leaves it on
        // this frame again, so it goes before anything reads the pose.
        ApplyTrail(clip);

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

    // Rebuilds the trail as play mode would have it on this frame. A
    // TrailRenderer only simulates in play, so its points are worked out here
    // the way the driver lays them - one tip sample per game frame joined by
    // its spline, dropped once older than Time - and drawn by a LineRenderer
    // carrying the same width, colour and material.
    // Rebuilt from scratch on every call, so scrubbing backwards is as exact
    // as playing forwards.
    private void ApplyTrail(AnimationClip clip)
    {
        BladeTrailClipDriver driver = FindTrailDriver();
        TrailRenderer sourceRenderer = driver != null
            ? (TrailRenderer)Field(typeof(BladeTrailClipDriver), "trail").GetValue(driver)
            : null;

        if (sourceRenderer == null && player != null)
        {
            sourceRenderer = player.GetComponentInChildren<TrailRenderer>(true);
        }

        if (!showTrail || sourceRenderer == null ||
            !TryGetTrailWindow(clip, out BladeTrailClipDriver.ClipWindow window))
        {
            HideTrail();
            return;
        }

        ResolveTrailSpan(in window, clip, out float start, out float end);
        float time = frame / ClipFrameRate;

        if (time < start || time - end > sourceRenderer.time)
        {
            HideTrail();
            return;
        }

        trailPoints.Clear();
        trailPointTimes.Clear();

        float recordUntil = Mathf.Min(time, end);
        float step = 1f / trailSampleRate;
        int steps = Mathf.CeilToInt((recordUntil - start) / step);
        Transform tip = sourceRenderer.transform;

        trailSamples.Clear();
        trailSampleTimes.Clear();

        for (int i = 0; i <= steps; i++)
        {
            float sampleTime = Mathf.Min(start + i * step, recordUntil);
            SamplePose(clip, sampleTime);
            trailSamples.Add(tip.position);
            trailSampleTimes.Add(sampleTime);
        }

        // The driver's own layout, so the preview draws the curve play mode
        // would: every point is laid by the driver, none by the renderer.
        BladeTrailClipDriver.BuildTrail(
            trailSamples,
            driver != null ? driver.PointSpacing : 0.03f,
            trailPoints,
            trailOwners);

        for (int i = 0; i < trailOwners.Count; i++)
        {
            trailPointTimes.Add(trailSampleTimes[trailOwners[i]]);
        }

        // The walk above left the body at the end of the recording.
        SamplePose(clip, time);

        // While emitting, the trail's head stays on the blade between points.
        bool emitting = time <= end;
        Vector3 head = tip.position;

        // Not ??=: that skips Unity's null check and keeps a destroyed one.
        if (trailLine == null)
        {
            trailLine = CreateTrailLine();
        }

        CopyTrailLook(sourceRenderer, trailLine);

        // Newest first: a TrailRenderer's width and colour run from the
        // emitter (0) back to the tail (1).
        trailLinePoints.Clear();

        if (emitting)
        {
            trailLinePoints.Add(head);
        }

        for (int i = trailPoints.Count - 1; i >= 0; i--)
        {
            if (time - trailPointTimes[i] > sourceRenderer.time)
            {
                break;
            }

            trailLinePoints.Add(trailPoints[i]);
        }

        if (trailLinePoints.Count < 2)
        {
            HideTrail();
            return;
        }

        trailLine.positionCount = trailLinePoints.Count;
        trailLine.SetPositions(trailLinePoints.ToArray());
        trailLine.enabled = true;
    }

    private void HideTrail()
    {
        if (trailLine != null)
        {
            trailLine.enabled = false;
        }
    }

    private static LineRenderer CreateTrailLine()
    {
        var go = new GameObject("WeaponTrailPreview (temporary)");
        go.hideFlags = HideFlags.HideAndDontSave;

        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.loop = false;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        return line;
    }

    // Read every time rather than once, so edits to the TrailRenderer show up
    // while scrubbing.
    private static void CopyTrailLook(TrailRenderer from, LineRenderer to)
    {
        to.sharedMaterial = from.sharedMaterial;
        to.widthCurve = from.widthCurve;
        to.widthMultiplier = from.widthMultiplier;
        to.colorGradient = from.colorGradient;
        to.alignment = from.alignment;
        to.textureMode = from.textureMode;
        to.textureScale = from.textureScale;
        to.numCornerVertices = from.numCornerVertices;
        to.numCapVertices = from.numCapVertices;
    }

    private void DestroyTrailInstance()
    {
        if (trailLine == null)
        {
            return;
        }

        DestroyImmediate(trailLine.gameObject);
        trailLine = null;
    }

    private bool TryGetTrailWindow(
        AnimationClip clip,
        out BladeTrailClipDriver.ClipWindow window)
    {
        window = default;

        BladeTrailClipDriver driver = FindTrailDriver();

        if (driver == null || clip == null)
        {
            return false;
        }

        var windows = (BladeTrailClipDriver.ClipWindow[])
            Field(typeof(BladeTrailClipDriver), "windows").GetValue(driver);

        if (windows == null)
        {
            return false;
        }

        for (int i = 0; i < windows.Length; i++)
        {
            if (windows[i].clipName == clip.name)
            {
                window = windows[i];
                return true;
            }
        }

        return false;
    }

    // The window in seconds of clip time, counting its frames at the
    // driver's rate as the driver does.
    private static void ResolveTrailSpan(
        in BladeTrailClipDriver.ClipWindow window,
        AnimationClip clip,
        out float start,
        out float end)
    {
        const float rate = BladeTrailClipDriver.FrameRate;

        start = window.startFrame / rate;
        end = Mathf.Min(window.endFrame / rate, clip.length);
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
