using UnityEngine;
using UnityEditor;

namespace VRCLightVolumes {
    // Renders a [MinMaxSlider] Vector2 as: label | min field | two-handle slider | max field.
    [CustomPropertyDrawer(typeof(MinMaxSliderAttribute))]
    public class MinMaxSliderDrawer : PropertyDrawer {
        const float FieldWidth = 50f;
        const float Pad = 4f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
            if (property.propertyType != SerializedPropertyType.Vector2) {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            MinMaxSliderAttribute attr = (MinMaxSliderAttribute)attribute;

            EditorGUI.BeginProperty(position, label, property);
            Rect r = EditorGUI.PrefixLabel(position, label);

            Vector2 value = property.vector2Value;
            float min = value.x;
            float max = value.y;

            Rect minRect = new Rect(r.x, r.y, FieldWidth, r.height);
            Rect sliderRect = new Rect(r.x + FieldWidth + Pad, r.y, r.width - 2f * (FieldWidth + Pad), r.height);
            Rect maxRect = new Rect(r.xMax - FieldWidth, r.y, FieldWidth, r.height);

            EditorGUI.BeginChangeCheck();
            min = EditorGUI.FloatField(minRect, min);
            EditorGUI.MinMaxSlider(sliderRect, ref min, ref max, attr.Min, attr.Max);
            max = EditorGUI.FloatField(maxRect, max);

            if (EditorGUI.EndChangeCheck()) {
                min = Mathf.Clamp(min, attr.Min, attr.Max);
                max = Mathf.Clamp(max, attr.Min, attr.Max);
                if (min > max) min = max;
                property.vector2Value = new Vector2(min, max);
            }

            EditorGUI.EndProperty();
        }
    }
}
