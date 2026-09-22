#if UNITY_EDITOR && !COMPILER_UDONSHARP
using UnityEngine;

namespace VRCLightVolumes {
    // Serialized v2/v3-dev authoring payload retained only so the project migrator can read existing scenes and prefabs before removing this component.
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public class LightVolume : MonoBehaviour {
        [Header("Volume Setup")]
        [Tooltip("Allow this volume to move in game.")]
        public bool Dynamic;
        [Tooltip("Add this volume's lighting on top of other volumes.")]
        public bool Additive;
        [Tooltip("Tint the volume's lighting.")]
        [ColorUsage(showAlpha: false)] public Color Color = Color.white;
        [Tooltip("Adjust the brightness.")]
        public float Intensity = 1f;
        [Tooltip("Blend with other volumes over this distance, in meters.")]
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
