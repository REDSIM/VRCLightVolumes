#if UNITY_EDITOR && !COMPILER_UDONSHARP
using UnityEngine;
using UnityEngine.Serialization;

namespace VRCLightVolumes {
    // Serialized v2/v3-dev authoring schema retained only so the project migrator can read old
    // scenes and prefabs before removing this component. It intentionally has no lifecycle code.
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class PointLightVolume : MonoBehaviour {
        public bool Dynamic = false;
        public LightType Type = LightType.PointLight;
        [Min(0.0001f)] public float LightSourceSize = 0.025f;
        [Min(0.0001f)] public float Range = 10f;
        [ColorUsage(false)] public Color Color = Color.white;
        public float Intensity = 100f;
        [Range(0f, 1f)] public float ShadingStrength = 1f;
        [FormerlySerializedAs("Shape")] public LightProjection Projection = LightProjection.Parametric;
        [Range(0.1f, 360f)] public float Angle = 60f;
        [Range(0.001f, 1f)] public float Falloff = 1f;
        public UnityEngine.Object FalloffLUT;
        public UnityEngine.Object Cookie;
        [Min(0.001f)] public float SpotCookieAspect = 1f;
        public UnityEngine.Object Cubemap;
        public bool BakeIntoProbes = false;
        public bool DebugRange = false;

        public bool Shadows = false;
        public bool BakeInGame = false;
        public bool RebakeShadows = true;
        public LayerMask LayerMask = 270849;
        public GameObject[] ExclusionMask = new GameObject[0];
        [Min(0f)] public float Bias = 0.01f;
        [Min(0.0001f)] public float NearPlane = 0.01f;
        [FormerlySerializedAs("ShadowFarClip")]
        [Min(0f)] public float FarPlane = 0f;
        public bool DebugClipPlanes = false;
        [Min(0f)] public float Blur = 1f;
        [Range(0f, 1f)] public float ContactHardening = 0f;
        public bool UseWorldSpace = false;
        public bool ForceCubemapShadows = false;
        public UnityEngine.Object ShadowMap;

        // References are retained for deterministic migration of the latest v3 development scenes.
        public PointLightVolumeInstance PointLightVolumeInstance;
#pragma warning disable CS0618
        public LightVolumeSetup LightVolumeSetup;
#pragma warning restore CS0618

        public enum LightProjection {
            Parametric,
            LUT,
            Custom
        }

        public enum LightType {
            PointLight,
            SpotLight,
            AreaLight
        }
    }
}
#endif
