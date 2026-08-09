using UnityEditor;
using UnityEngine;

namespace VRCLightVolumes {
    // Shared read-only debug field rendering for all Light Volume inspectors.
    internal static class LightVolumeDebugGUI {
        private static GUIStyle _valueStyle;

        private static GUIStyle ValueStyle =>
            _valueStyle ?? (_valueStyle = new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold });

        // Uses the serialized field as the single source of its Inspector name and tooltip.
        private static GUIContent GetPropertyContent(SerializedObject source, string propertyName, string label) {
            SerializedProperty property = source.FindProperty(propertyName);
            return property == null
                ? new GUIContent(label ?? ObjectNames.NicifyVariableName(propertyName))
                : new GUIContent(label ?? property.displayName, property.tooltip);
        }

        // Draws a titled debug group with optional spacing and hover help.
        public static void DrawGroupHeader(string title, bool addTopSpacing, string tooltip) {
            GUILayout.Space(addTopSpacing ? 7f : 3f);
            EditorGUILayout.LabelField(new GUIContent(title, tooltip), EditorStyles.boldLabel);
        }

        // Draws a disabled object reference using the requested Unity object type.
        public static void DrawObject(string label, Object value, System.Type type, string tooltip) {
            EditorGUI.ObjectField(
                EditorGUILayout.GetControlRect(),
                new GUIContent(label, tooltip),
                value,
                type,
                true);
        }

        // Draws an object value while taking its label help from the serialized runtime field.
        public static void DrawObject(SerializedObject source, string propertyName, Object value, System.Type type, string label = null) {
            EditorGUI.ObjectField(EditorGUILayout.GetControlRect(), GetPropertyContent(source, propertyName, label), value, type, true);
        }

        // Draws a read-only text value.
        public static void DrawText(string label, string value, string tooltip) {
            Rect valueRect = EditorGUI.PrefixLabel(
                EditorGUILayout.GetControlRect(),
                new GUIContent(label, tooltip));
            EditorGUI.LabelField(valueRect, new GUIContent(value), ValueStyle);
        }

        // Draws text while taking its label help from the serialized runtime field.
        public static void DrawText(SerializedObject source, string propertyName, string value, string label = null) {
            Rect valueRect = EditorGUI.PrefixLabel(EditorGUILayout.GetControlRect(), GetPropertyContent(source, propertyName, label));
            EditorGUI.LabelField(valueRect, new GUIContent(value), ValueStyle);
        }

        // Draws a read-only integer value.
        public static void DrawInt(string label, int value, string tooltip) {
            DrawText(label, value.ToString(), tooltip);
        }

        public static void DrawInt(SerializedObject source, string propertyName, int value, string label = null) {
            DrawText(source, propertyName, value.ToString(), label);
        }

        // Draws a read-only floating-point value.
        public static void DrawFloat(string label, float value, string tooltip) {
            DrawText(label, value.ToString("0.###"), tooltip);
        }

        public static void DrawFloat(SerializedObject source, string propertyName, float value, string label = null) {
            DrawText(source, propertyName, value.ToString("0.###"), label);
        }

        // Draws a read-only three-component vector.
        public static void DrawVector3(string label, Vector3 value, string tooltip) {
            DrawText(label, value.ToString("0.###"), tooltip);
        }

        public static void DrawVector3(SerializedObject source, string propertyName, Vector3 value, string label = null) {
            DrawText(source, propertyName, value.ToString("0.###"), label);
        }

        // Draws a read-only integer vector.
        public static void DrawVector3Int(string label, Vector3Int value, string tooltip) {
            DrawText(label, value.ToString(), tooltip);
        }

        public static void DrawVector3Int(SerializedObject source, string propertyName, Vector3Int value, string label = null) {
            DrawText(source, propertyName, value.ToString(), label);
        }

        // Draws a read-only four-component vector.
        public static void DrawVector4(string label, Vector4 value, string tooltip) {
            DrawText(label, value.ToString("0.###"), tooltip);
        }

        public static void DrawVector4(SerializedObject source, string propertyName, Vector4 value, string label = null) {
            DrawText(source, propertyName, value.ToString("0.###"), label);
        }

        // Draws a read-only quaternion as four numeric components.
        public static void DrawQuaternion(string label, Quaternion value, string tooltip) {
            DrawText(label, value.eulerAngles.ToString("0.###") + " deg", tooltip);
        }

        public static void DrawQuaternion(SerializedObject source, string propertyName, Quaternion value, string label = null) {
            DrawText(source, propertyName, value.eulerAngles.ToString("0.###") + " deg", label);
        }

        // Draws a read-only boolean toggle.
        public static void DrawBool(string label, bool value, string tooltip) {
            EditorGUI.Toggle(
                EditorGUILayout.GetControlRect(),
                new GUIContent(label, tooltip),
                value);
        }

        public static void DrawBool(SerializedObject source, string propertyName, bool value, string label = null) {
            EditorGUI.Toggle(EditorGUILayout.GetControlRect(), GetPropertyContent(source, propertyName, label), value);
        }
    }
}
