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

    private Vector2 scrollPosition;

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
        SceneView.duringSceneGui += DrawHitbox;
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= DrawHitbox;
        StopPreview();
    }

    // Draws the combo's hit volume where OverlapSkillAction puts it, so it
    // can be matched against the effect by eye. Red while the attack is
    // active and actually hitting, grey outside that window.
    private void DrawHitbox(SceneView view)
    {
        if (!previewing || player == null || Event.current.type != EventType.Repaint)
        {
            return;
        }

        var action = LoadAction(comboIndex) as OverlapSkillAction;

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

        float activeStart = action.startupDuration * ClipFrameRate;
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
