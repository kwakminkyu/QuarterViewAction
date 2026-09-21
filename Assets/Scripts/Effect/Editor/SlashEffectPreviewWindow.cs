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
public sealed class SlashEffectPreviewWindow : EditorWindow
{
    private const string ActionPathFormat =
        "Assets/SkillData/Player/BasicAttack/ActionData/PlayerBasicAttackMeleeAction{0}.asset";
    private const string ClipPathFormat = "Assets/Animation/Clip/BasicCombo{0}.fbx";
    private const float ClipFrameRate = 30f;

    private const BindingFlags Hidden =
        BindingFlags.NonPublic | BindingFlags.Instance;

    private GameObject player;
    private int comboIndex = 4;
    private float frame;
    private bool previewing;

    // One per authored effect entry, so a crescent and a later ground impact
    // can both be inspected on the same frame.
    private readonly List<SlashEffect> instances = new();
    private Animator animator;

    // Blade guide: the real blade tip path drawn in the scene, with the point an
    // effect entry is placed at, so a mesh can be traced over the path in
    // Blender (Export Blade Path) and checked against it afterwards.
    private const float PathSamplesPerFrame = 4f;
    private const string EffectPivotName = "EffectPivot";
    private const string BladeTipName = "BladeTip";

    private Vector2 scrollPosition;
    private bool showGuide = true;
    private int guideEffectIndex;

    private Transform effectPivot;
    private Transform bladeTip;

    // Sampled once per combo and timing, in the player's local space so a
    // rotated player does not invalidate it.
    private readonly List<Vector3> bladePathLocal = new();
    private int cachedPathCombo = -1;
    private float cachedPathStart = -1f;
    private float cachedPathEnd = -1f;
    private bool cachedPathInPlace;

    // On by default so the preview matches play, where root motion is off.
    private bool keepInPlace = true;
    private PlayableGraph poseGraph;
    private AnimationClip poseGraphClip;

    [MenuItem("Tools/Slash Effect Preview")]
    private static void Open()
    {
        GetWindow<SlashEffectPreviewWindow>("Slash Preview");
    }

    private void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        StopPreview();
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
            "Scrubs a combo clip in edit mode and places the authored slash " +
            "exactly where the runtime would. Nothing is saved to the scene.",
            MessageType.None);

        if (player == null)
        {
            player = GameObject.Find("Player");
        }

        using (new EditorGUI.DisabledScope(previewing))
        {
            player = (GameObject)EditorGUILayout.ObjectField(
                "Player", player, typeof(GameObject), true);
            comboIndex = EditorGUILayout.IntSlider("Combo", comboIndex, 1, 4);
        }

        SkillAction action = LoadAction(comboIndex);
        AnimationClip clip = LoadClip(comboIndex);

        if (player == null || action == null || clip == null)
        {
            EditorGUILayout.HelpBox(
                "Needs a Player in the open scene plus the combo's action asset " +
                "and clip.",
                MessageType.Warning);
            return;
        }

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
        bool frameChanged = EditorGUI.EndChangeCheck();

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

        DrawGuideControls(action);
    }

    // ---- Blade guide -----------------------------------------------------

    private void DrawGuideControls(SkillAction action)
    {
        EditorGUILayout.Space();
        showGuide = EditorGUILayout.ToggleLeft(
            "Blade Guide (Scene view)", showGuide, EditorStyles.boldLabel);

        if (!showGuide)
        {
            return;
        }

        int count = action.effects == null ? 0 : action.effects.Length;

        if (count == 0)
        {
            EditorGUILayout.HelpBox(
                "The guide is drawn for an effect entry, and this action has " +
                "none.",
                MessageType.Info);
            return;
        }

        EditorGUILayout.HelpBox(
            "Yellow: where the entry is placed (EffectPivot + Offset)   " +
            "Red: blade tip over the active window   White: blade tip now.",
            MessageType.None);

        guideEffectIndex = Mathf.Clamp(guideEffectIndex, 0, count - 1);

        if (count > 1)
        {
            guideEffectIndex = EditorGUILayout.IntSlider(
                "Effect entry", guideEffectIndex, 0, count - 1);
        }

        ActionEffectSettings settings = action.effects[guideEffectIndex];

        using (new EditorGUI.DisabledScope(!previewing || bladePathLocal.Count < 2))
        {
            if (GUILayout.Button("Export Blade Path (OBJ for Blender)"))
            {
                ExportBladePath(action, in settings);
            }
        }

        if (!previewing)
        {
            EditorGUILayout.HelpBox(
                "Start the preview to draw the guide and sample the blade.",
                MessageType.Info);
        }
    }

    private void OnSceneGUI(SceneView view)
    {
        if (!previewing || !showGuide || player == null ||
            Event.current.type != EventType.Repaint)
        {
            return;
        }

        SkillAction action = LoadAction(comboIndex);
        Vector3 origin;
        Quaternion rotation;

        if (action == null || !TryGetEffectFrame(action, out origin, out rotation))
        {
            return;
        }

        // Where the entry is placed. The offset moves it off the pivot itself;
        // show both so a leftover offset is not mistaken for the pivot.
        Handles.color = Color.yellow;
        float pivotSize = HandleUtility.GetHandleSize(origin) * 0.08f;
        Handles.SphereHandleCap(0, origin, Quaternion.identity, pivotSize, EventType.Repaint);

        Vector3 pivot = effectPivot != null
            ? effectPivot.position
            : player.transform.position;

        if ((pivot - origin).sqrMagnitude > 1e-6f)
        {
            Handles.Label(origin, "effect origin (pivot + offset)");
            Handles.DrawDottedLine(pivot, origin, 2f);
            Handles.SphereHandleCap(
                0, pivot, Quaternion.identity, pivotSize, EventType.Repaint);
            Handles.Label(pivot, "EffectPivot");
        }
        else
        {
            Handles.Label(origin, "EffectPivot");
        }

        // Blade tip path across the active window.
        if (bladePathLocal.Count > 1)
        {
            var path = new Vector3[bladePathLocal.Count];

            for (int i = 0; i < path.Length; i++)
            {
                path[i] = player.transform.TransformPoint(bladePathLocal[i]);
            }

            Handles.color = Color.red;
            Handles.DrawAAPolyLine(4f, path);
        }

        // Blade tip on the current frame.
        if (bladeTip != null)
        {
            Handles.color = Color.white;
            Handles.SphereHandleCap(
                0, bladeTip.position, Quaternion.identity,
                HandleUtility.GetHandleSize(bladeTip.position) * 0.06f,
                EventType.Repaint);
        }
    }

    // The frame the runtime lays an entry's mesh into, before localEuler:
    // local XY is the swing plane, local Z its normal.
    private bool TryGetEffectFrame(
        SkillAction action,
        out Vector3 origin,
        out Quaternion rotation)
    {
        origin = Vector3.zero;
        rotation = Quaternion.identity;

        int count = action.effects == null ? 0 : action.effects.Length;
        CharacterEffectSpawner spawner =
            player == null ? null : player.GetComponent<CharacterEffectSpawner>();

        if (count == 0 || spawner == null)
        {
            return false;
        }

        ResolveGuideTransforms();

        ActionEffectSettings settings =
            action.effects[Mathf.Clamp(guideEffectIndex, 0, count - 1)];

        // Same flattening PlaySlash applies to the direction it is handed.
        Vector3 facing = player.transform.forward;
        facing.y = 0f;
        facing = facing.sqrMagnitude > Mathf.Epsilon
            ? facing.normalized
            : Vector3.forward;

        Vector3 pivot = effectPivot != null
            ? effectPivot.position
            : player.transform.position;
        origin = pivot + Quaternion.LookRotation(facing, Vector3.up) * settings.offset;

        rotation = (Quaternion)Method(
                typeof(CharacterEffectSpawner), "ResolveSwingPlaneRotation")
            .Invoke(spawner, new object[] { facing, settings });

        return true;
    }

    // Writes the red path in the space of the mesh this entry will draw with,
    // so a band traced over it in Blender drops in with no further placement:
    // offset, swing plane, localEuler and scale are all undone here.
    //
    // Unity mirrors X when importing a Blender FBX, so X is negated going out.
    // Blender's OBJ importer then maps OBJ (x, y, z) to Blender (x, -z, y) with
    // its default axes, hence the reordering below.
    private void ExportBladePath(SkillAction action, in ActionEffectSettings settings)
    {
        string folder = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(Application.dataPath, "..", "BladePaths"));
        System.IO.Directory.CreateDirectory(folder);

        string path = EditorUtility.SaveFilePanel(
            "Export Blade Path",
            folder,
            "Combo" + comboIndex + "_Effect" + guideEffectIndex + "_BladePath",
            "obj");

        if (!string.IsNullOrEmpty(path) &&
            WriteBladePathObj(action, in settings, path))
        {
            Debug.Log("Blade path written to " + path);
        }
    }

    private bool WriteBladePathObj(
        SkillAction action,
        in ActionEffectSettings settings,
        string path)
    {
        Vector3 origin;
        Quaternion rotation;

        if (bladePathLocal.Count < 2 ||
            !TryGetEffectFrame(action, out origin, out rotation))
        {
            return false;
        }

        Quaternion toMesh = Quaternion.Inverse(
            rotation * Quaternion.Euler(settings.localEuler));
        Vector3 scale = settings.scale.sqrMagnitude > Mathf.Epsilon
            ? settings.scale
            : Vector3.one;
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var obj = new System.Text.StringBuilder();

        obj.AppendLine("# Blade tip path, combo " + comboIndex +
            ", in the mesh space of effect " + guideEffectIndex);
        obj.AppendLine("o BladePath_Combo" + comboIndex);

        for (int i = 0; i < bladePathLocal.Count; i++)
        {
            Vector3 world = player.transform.TransformPoint(bladePathLocal[i]);
            Vector3 local = toMesh * (world - origin);
            local = new Vector3(
                local.x / scale.x, local.y / scale.y, local.z / scale.z);

            obj.AppendLine(string.Format(
                culture, "v {0:F5} {1:F5} {2:F5}", -local.x, local.z, -local.y));
        }

        obj.Append('l');

        for (int i = 1; i <= bladePathLocal.Count; i++)
        {
            obj.Append(' ').Append(i.ToString(culture));
        }

        obj.AppendLine();
        System.IO.File.WriteAllText(path, obj.ToString());
        return true;
    }

    // Poses the clip across the active window once and keeps the tip positions,
    // so the whole path can be drawn while scrubbing a single frame.
    private void EnsureBladePath(SkillAction action, AnimationClip clip)
    {
        float start = action.startupDuration;
        float end = start + action.activeDuration;

        if (cachedPathCombo == comboIndex &&
            cachedPathInPlace == keepInPlace &&
            Mathf.Approximately(cachedPathStart, start) &&
            Mathf.Approximately(cachedPathEnd, end))
        {
            return;
        }

        cachedPathCombo = comboIndex;
        cachedPathInPlace = keepInPlace;
        cachedPathStart = start;
        cachedPathEnd = end;
        bladePathLocal.Clear();

        ResolveGuideTransforms();

        if (bladeTip == null)
        {
            return;
        }

        int steps = Mathf.Max(
            2, Mathf.CeilToInt((end - start) * ClipFrameRate * PathSamplesPerFrame));

        for (int i = 0; i <= steps; i++)
        {
            float time = Mathf.Lerp(start, end, (float)i / steps);
            SamplePose(clip, time);

            bladePathLocal.Add(
                player.transform.InverseTransformPoint(bladeTip.position));
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

    private void ResolveGuideTransforms()
    {
        if (player == null || (effectPivot != null && bladeTip != null))
        {
            return;
        }

        foreach (Transform child in player.GetComponentsInChildren<Transform>(true))
        {
            if (effectPivot == null && child.name == EffectPivotName)
            {
                effectPivot = child;
            }
            else if (bladeTip == null && child.name == BladeTipName)
            {
                bladeTip = child;
            }
        }
    }

    // Shows where the effect's own window sits inside the attack. A duration
    // longer than the active phase is cut off by the fade before the sweep
    // finishes, and that is far easier to see here than to reason about.
    private void DrawTimingInfo(SkillAction action, AnimationClip clip)
    {
        float startupF = action.startupDuration * ClipFrameRate;
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

        if (spawner == null)
        {
            Debug.LogError(
                "Preview needs a CharacterEffectSpawner on the Player.");
            return;
        }

        // Awake never runs in edit mode, so the spawner's cached references are
        // still null. Run it by hand before asking it to place anything.
        InvokeHidden(spawner, "Awake");

        var prefab = (SlashEffect)
            Field(typeof(CharacterEffectSpawner), "slashPrefab").GetValue(spawner);

        if (prefab == null)
        {
            Debug.LogError("The spawner has no slash prefab assigned.");
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

        // The pose the path was sampled from is gone, and the next preview may
        // run against edited timings or a different player.
        bladePathLocal.Clear();
        cachedPathCombo = -1;
        DestroyPoseGraph();
        effectPivot = null;
        bladeTip = null;
    }

    private void Apply(SkillAction action, AnimationClip clip)
    {
        if (animator == null)
        {
            return;
        }

        // The path sweep poses the character itself, so it has to run before
        // the current frame is sampled rather than after.
        EnsureBladePath(action, clip);

        // Pose the character first; placement reads the posed transforms.
        SamplePose(clip, frame / ClipFrameRate);

        CharacterEffectSpawner spawner =
            player.GetComponent<CharacterEffectSpawner>();

        // An action can fire several effects at different delays, so the
        // preview needs one instance per entry rather than a single one.
        EnsureInstances(action.effects == null ? 0 : action.effects.Length, spawner);

        float activeStartF = action.startupDuration * ClipFrameRate;

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
        var prefab = (SlashEffect)
            Field(typeof(CharacterEffectSpawner), "slashPrefab").GetValue(spawner);

        while (instances.Count < count)
        {
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

    private static SkillAction LoadAction(int combo)
    {
        return AssetDatabase.LoadAssetAtPath<SkillAction>(
            string.Format(ActionPathFormat, combo));
    }

    private static AnimationClip LoadClip(int combo)
    {
        string path = string.Format(ClipPathFormat, combo);

        foreach (UnityEngine.Object asset in
            AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (asset is AnimationClip clip && !clip.name.StartsWith("__"))
            {
                return clip;
            }
        }

        return null;
    }
}
