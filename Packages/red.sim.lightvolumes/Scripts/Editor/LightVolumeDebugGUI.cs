using UnityEditor;
using UnityEngine;

namespace VRCLightVolumes {
    // Shared read-only debug field rendering for all Light Volume inspectors.
    internal static class LightVolumeDebugGUI {
        private static GUIStyle _valueStyle;

        private static GUIStyle ValueStyle =>
            _valueStyle ?? (_valueStyle = new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold });

        public static void DrawGroupHeader(string title, bool addTopSpacing, string tooltip) {
            GUILayout.Space(addTopSpacing ? 7f : 3f);
            EditorGUILayout.LabelField(new GUIContent(title, tooltip), EditorStyles.boldLabel);
        }

        public static void DrawObject(string label, Object value, System.Type type, string tooltip) {
            EditorGUI.ObjectField(
                EditorGUILayout.GetControlRect(),
                new GUIContent(label, tooltip),
                value,
                type,
                true);
        }

        public static void DrawText(string label, string value, string tooltip) {
            Rect valueRect = EditorGUI.PrefixLabel(
                EditorGUILayout.GetControlRect(),
                new GUIContent(label, tooltip));
            EditorGUI.LabelField(valueRect, new GUIContent(value), ValueStyle);
        }

        public static void DrawInt(string label, int value, string tooltip) {
            DrawText(label, value.ToString(), tooltip);
        }

        public static void DrawFloat(string label, float value, string tooltip) {
            DrawText(label, value.ToString("0.###"), tooltip);
        }

        public static void DrawVector3(string label, Vector3 value, string tooltip) {
            DrawText(label, value.ToString("0.###"), tooltip);
        }

        public static void DrawVector3Int(string label, Vector3Int value, string tooltip) {
            DrawText(label, value.ToString(), tooltip);
        }

        public static void DrawVector4(string label, Vector4 value, string tooltip) {
            DrawText(label, value.ToString("0.###"), tooltip);
        }

        public static void DrawQuaternion(string label, Quaternion value, string tooltip) {
            DrawText(label, value.eulerAngles.ToString("0.###") + " deg", tooltip);
        }

        public static void DrawBool(string label, bool value, string tooltip) {
            EditorGUI.Toggle(
                EditorGUILayout.GetControlRect(),
                new GUIContent(label, tooltip),
                value);
        }
    }
}
