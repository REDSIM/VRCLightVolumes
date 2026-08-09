#if UNITY_EDITOR && !COMPILER_UDONSHARP
using UnityEngine;

namespace VRCLightVolumes {
    // Serialized v2/v3-dev authoring payload retained only so the project migrator can read existing scenes and prefabs before removing this component.
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public class LightVolume : MonoBehaviour {
        [Header("Volume Setup")]
        [Tooltip("Defines whether this volume can be moved at runtime.")]
        public bool Dynamic;
        [Tooltip("Additive volumes apply their light on top of others as an overlay.")]
        public bool Additive;
        [Tooltip("Multiplies the volume's color by this value.")]
        [ColorUsage(showAlpha: false)] public Color Color = Color.white;
        [Tooltip("Brightness of the volume.")]
        public float Intensity = 1f;
        [Tooltip("Size in meters of this Light Volume's overlapping regions for smooth blending with other volumes.")]
        [Range(0, 1)] public float SmoothBlending = 0.25f;

        [Header("Baked Data")]
        public Texture3D Texture0;
        public Texture3D Texture1;
        public Texture3D Texture2;

        [Header("Color Correction")]
        public float Exposure = 0f;
        [Range(-1, 1)] public float Shadows = 0f;
        [Range(-1, 1)] public float Highlights = 0f;

        [Header("Baking Setup")]
        public bool Bake = true;
        public bool ReserveUVSpace = false;
        public bool AdaptiveResolution = true;
        public float VoxelsPerUnit = 3f;
        public Vector3Int Resolution = new Vector3Int(16, 16, 16);

        public Component BakeryVolume;

        public LightVolumeInstance LightVolumeInstance;
#pragma warning disable CS0618
        public LightVolumeSetup LightVolumeSetup;
#pragma warning restore CS0618
    }
}
#endif
