using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Shows an effect entry with only the fields its kind reads, so an entry does
// not carry a page of values that silently do nothing:
//   - a particle Prefab brings its own look and timing, so the mesh, material,
//     colour, timing and curve fields are hidden;
//   - a mesh with its own Material is drawn by that shader, which does not
//     sweep, so the reveal fields are hidden;
//   - an entry that stays in the world never fades when the swing ends, so
//     Fade Out Duration is hidden.
// Hidden fields keep their values; they come back if the entry changes kind.
[CustomPropertyDrawer(typeof(ActionEffectSettings))]
public sealed class ActionEffectSettingsDrawer : PropertyDrawer
{
    private static readonly HashSet<string> SweepOnly = new()
    {
        "revealSpan",
        "revealSoftness",
        "revealCurve",
    };

    // Everything a particle prefab ignores.
    private static readonly HashSet<string> MeshOnly = new()
    {
        "mesh",
        "material",
        "color",
        "duration",
        "fadeOutDuration",
        "scaleCurve",
        "scaleCurveAxes",
        "alphaCurve",
        "revealSpan",
        "revealSoftness",
        "revealCurve",
    };

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float height = EditorGUIUtility.singleLineHeight;

        if (!property.isExpanded)
        {
            return height;
        }

        foreach (SerializedProperty child in VisibleChildren(property))
        {
            height += EditorGUIUtility.standardVerticalSpacing +
                EditorGUI.GetPropertyHeight(child, true);
        }

        return height;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        var line = new Rect(
            position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        property.isExpanded = EditorGUI.Foldout(
            line,
            property.isExpanded,
            new GUIContent(label.text + "   (" + KindName(property) + ")", label.tooltip),
            true);

        if (property.isExpanded)
        {
            EditorGUI.indentLevel++;
            float y = line.yMax;

            foreach (SerializedProperty child in VisibleChildren(property))
            {
                float childHeight = EditorGUI.GetPropertyHeight(child, true);
                y += EditorGUIUtility.standardVerticalSpacing;
                EditorGUI.PropertyField(
                    new Rect(position.x, y, position.width, childHeight), child, true);
                y += childHeight;
            }

            EditorGUI.indentLevel--;
        }

        EditorGUI.EndProperty();
    }

    private static string KindName(SerializedProperty property)
    {
        if (property.FindPropertyRelative("prefab").objectReferenceValue != null)
        {
            return "Particles";
        }

        if (property.FindPropertyRelative("material").objectReferenceValue != null)
        {
            return "Shader Mesh";
        }

        return "Slash";
    }

    private static IEnumerable<SerializedProperty> VisibleChildren(SerializedProperty property)
    {
        bool particles = property.FindPropertyRelative("prefab").objectReferenceValue != null;
        bool ownShader = property.FindPropertyRelative("material").objectReferenceValue != null;
        bool staysInWorld = property.FindPropertyRelative("stayInWorld").boolValue;

        SerializedProperty child = property.Copy();
        SerializedProperty end = property.GetEndProperty();
        bool enterChildren = true;

        while (child.NextVisible(enterChildren) &&
            !SerializedProperty.EqualContents(child, end))
        {
            enterChildren = false;
            string name = child.name;

            if (particles && MeshOnly.Contains(name))
            {
                continue;
            }

            if (ownShader && SweepOnly.Contains(name))
            {
                continue;
            }

            if (staysInWorld && name == "fadeOutDuration")
            {
                continue;
            }

            yield return child.Copy();
        }
    }
}
