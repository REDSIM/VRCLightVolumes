using UnityEngine;

namespace VRCLightVolumes {
    // Draws a Vector2 as a two-handle min/max slider. x = low handle, y = high handle.
    public class MinMaxSliderAttribute : PropertyAttribute {
        public readonly float Min;
        public readonly float Max;

        public MinMaxSliderAttribute(float min, float max) {
            Min = min;
            Max = max;
        }
    }
}
