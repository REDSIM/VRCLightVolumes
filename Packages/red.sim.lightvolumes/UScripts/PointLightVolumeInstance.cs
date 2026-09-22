#if !UDONSHARP && COMPILER_UDONSHARP
#define UDONSHARP
#endif

using UnityEngine;
using UnityEngine.Rendering;
using System;
#if UDONSHARP
using UdonSharp;
#endif
#if COMPILER_UDONSHARP
using VRC.SDK3.Rendering;
#endif

namespace VRCLightVolumes {
    // Companion partials keep runtime shadow baking and Editor authoring separate without creating another component or Udon program.
    [AddComponentMenu("VRC Light Volumes/Point Light Volume")]
    [DisallowMultipleComponent]
#if UDONSHARP
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public partial class PointLightVolumeInstance : UdonSharpBehaviour
#else
    public partial class PointLightVolumeInstance : MonoBehaviour
#endif
    {
        [Tooltip("Enable for a light that moves in game. Also enable Auto Update Volumes on the Manager.")]
        public bool IsDynamic = false;
        [Tooltip("Choose Point for a bulb, Spot for a flashlight or Area for a panel.")]
        public int LightType = 0; // 0: point, 1: spot, 2: area
        [Tooltip("Tints the light. White keeps the colors of its texture source.")]
        [ColorUsage(showAlpha: false)] public Color Color = Color.white;
        [Tooltip("Adjust brightness after setting Light Source Size. Set to 0 to turn the light off.")]
        public float Intensity = 100f;
        [Tooltip("Controls surface shading and shadow strength. At 0, the light has no directional shading or shadows.")]
        [Range(0, 1)] public float ShadingStrength = 1f;

        [Header("Position Data")]
        [Tooltip("Light position in world space. Updated from the Transform.")]
        public Vector3 Position = Vector3.zero;
        [Tooltip("Emitter radius in meters. Larger sources reach farther and produce broader specular highlights.")]
        [Min(0.0001f)] public float LightSourceSize = 0.025f;
        [Tooltip("Inverse squared range used by LUT projection.")]
        [Min(0)] public float InverseSquaredRange = 1f;
        [Tooltip("Area light width in meters. Changes its lit area and specular highlight.")]
        [Min(0.001f)] public float Width = 1f;

        [Header("Direction Data")]
        [Tooltip("Spot direction in world space. Updated from the Transform.")]
        public Vector3 Direction = Vector3.forward;
        [Tooltip("Orientation of the Area light or projected image. Updated from the Transform.")]
        public Quaternion Rotation = Quaternion.identity;
        [Tooltip("Controls how softly a Spot light fades at its cone edge.")]
        public float ConeFalloff = 1f;

        [Header("Angle Data")]
        [Tooltip("Spot cone half-angle in radians. The Inspector shows the full angle in degrees.")]
        public float Angle = 0.5235988f;
        [Tooltip("Cosine of the Spot cone half-angle. Set from Angle.")]
        public float OuterAngleCos = 1f;
        [Tooltip("Tangent of the Spot cone half-angle. Used for cookie projection.")]
        public float OuterAngleTan = 0f;
        [Tooltip("Width-to-height ratio of a Spot cookie. 1 is square; higher values make the projection shorter.")]
        [Min(0.001f)] public float SpotCookieAspect = 1f;
        [Tooltip("Area light height in meters. Changes its lit area and specular highlight.")]
        [Min(0.001f)] public float Height = 1f;

        [Header("Runtime State")]
        [Tooltip("Squared light range. Calculated by the Manager.")]
        public float SquaredRange = 1f;
        [Tooltip("Squared scale used for light range and specular size. Set by UpdateTransform().")]
        public float SquaredScale = 1f;
        [Tooltip("The Manager that owns this light. Assign it before registration and keep the same Manager afterwards.")]
        public LightVolumeManager LightVolumeManager;
        [Tooltip("Breaks ties between lights with the same priority. Use SetWeight() to change priority.")]
        [HideInInspector] public int RegistryOrder = 2147483647;
        [Tooltip("Light priority. Higher values are considered first.")]
        [HideInInspector] public float RegistryWeight = 0f;
        [HideInInspector] public bool IsActive = true;
        [Header("Projection Source")]
        [Tooltip("Texture used for the active LUT, cookie or cubemap projection.")]
        public Texture CustomTexture;
        [Tooltip("Material used for the active LUT, cookie or cubemap projection.")]
        public Material CustomTextureMaterial;
        [Tooltip("Projection type: 0 = Parametric, 1 = LUT, 2 = Custom cookie or cubemap.")]
        public int ProjectionMode = 0; // 0: parametric, 1: LUT, 2: custom cookie or cubemap
        [Tooltip("Refreshes animated projection sources each frame. Also requires Auto Update Textures on the Manager. Turn off to keep a snapshot.")]
        public bool AutoUpdateCustomTexture = false;

        [Header("Shadow Source")]
        [Tooltip("Texture source used by this light's shadow map.")]
        public Texture ShadowMapTexture;
        [Tooltip("Material source used by this light's shadow map.")]
        public Material ShadowMapMaterial;
        [Tooltip("Refreshes the assigned shadow source each frame. Also requires Auto Update Textures on the Manager.")]
        public bool AutoUpdateShadowMap = false;
        [Tooltip("Assigned shadow map slot. -1 means no shadow map.")]
        public float ShadowMapID = -1f;
        [Tooltip("Keeps baked shadows in their original world positions when the light moves.")]
        public bool WorldSpaceShadows = false;
        [Tooltip("Light position when its shadows were baked.")]
        public Vector3 ShadowBakePosition = Vector3.zero;
        [Tooltip("Light rotation when its shadows were baked.")]
        public Quaternion ShadowBakeRotation = Quaternion.identity;

        [Header("Shadow Bake Settings")]
        [Tooltip("Layers that can cast shadows.")]
        public int LayerMask = 270849;
        [Tooltip("Closest distance captured by the shadow camera. Objects nearer than this cannot cast shadows.")]
        [Min(0.0001f)] public float NearClip = 0.01f;
        [Tooltip("Farthest distance captured by the shadow camera. Set to 0 to use the light range.")]
        [Min(0)] public float FarClip = 0f;
        // Serialized source of truth for the far clip actually used by the latest shadow bake.
        [HideInInspector] public float BakedFarClip = 0f;
        [Tooltip("Moves the shadow capture depth by this distance in meters. Increase to reduce self-shadow artifacts; too much separates shadows from objects. Rebake after changes.")]
        [Min(0)] public float Bias = 0.01f;
        [Tooltip("Softens the shadow edges. Set to 0 for no blur. Rebake after changes.")]
        [Min(0)] public float Blur = 1f;
        [Tooltip("Sharpens shadows near contact points. Reduce it if artifacts appear. Rebake after changes.")]
        [Range(0, 1)] public float ContactHardening = 0f;

        [Tooltip("Captures shadows once at startup. The Manager bakes one queued light per frame. The Editor-baked shadow is omitted from the build.")]
        public bool BakeInGame = false;
        [Tooltip("Resolution of the in-game shadow capture. The Manager resizes it to the shared shadow resolution.")]
        [Min(16)] public int RuntimeShadowResolution = 128;
        [Tooltip("Blur quality for in-game shadows: 0 = Low, 1 = Medium, 2 = High.")]
        [Range(0, 2)] public int RuntimeShadowBlurSamplePreset = 2;
        [Tooltip("Reduces seams when shadows are blurred. Turn off for faster planar blur.")]
        public bool RuntimeShadowSphericalBlur = true;
        [Tooltip("Writes shadows directly to the Manager atlas when resolutions match. The runtime baker sets this for repeated updates.")]
        [NonSerialized] public bool RuntimeShadowDirectOutput = false;

        // Persistent authoring state. These fields deliberately remain part of the Udon program so the UdonSharp proxy and backing behaviour always share one serializable schema. Duplicate texture
        // references are cleared from the temporary build scene, while runtime authoring references such as the excluded shadow renderers remain available to Udon.
        [Tooltip("Parametric uses light settings. LUT uses a falloff texture. Custom projects a cookie or cubemap.")]
        [HideInInspector] public int Projection = 0; // 0: parametric, 1: LUT, 2: custom cookie or cubemap
        [Tooltip("Light range in meters. Keep it as small as your scene needs to reduce overlap.")]
        [HideInInspector] public float Range = 10f;
        [Tooltip("Controls how softly the Spot light fades at its cone edge.")]
        [HideInInspector] public float Falloff = 1f;
        [Tooltip("Falloff texture or material. X controls the cone; Y controls distance falloff. Use uncompressed RGBA Half or RGBA Float textures.")]
        [HideInInspector] public UnityEngine.Object FalloffLUT;
        [Tooltip("Image or material projected by a Spot light or emitted by an Area light.")]
        [HideInInspector] public UnityEngine.Object Cookie;
        [Tooltip("Cubemap or material projected by a Point light.")]
        [HideInInspector] public UnityEngine.Object Cubemap;
        [Tooltip("Includes this light in baked Light Probes for objects without Light Volumes support. Use for static lights.")]
        [HideInInspector] public bool BakeIntoProbes = false;
        [Tooltip("Shows the light range. Use it to reduce overlap between lights.")]
        [HideInInspector] public bool DebugRange = false;
        [Tooltip("Enables shadows. Click Bake Shadows after changing the light or nearby objects.")]
        [HideInInspector] public bool Shadows = false;
        [Tooltip("Includes this light when Bake Shadows is clicked in the Light Volume Manager. Disable it to keep the current shadow map during batch bakes.")]
        [HideInInspector] public bool RebakeShadows = true;
        [Tooltip("Renderers that must not cast shadows for this light. Listed renderers are temporarily excluded from both editor and runtime shadow baking.")]
        [HideInInspector] public Renderer[] ExclusionMask = new Renderer[0];
        [Tooltip("Shows the nearest and farthest distances included in the shadow capture.")]
        [HideInInspector] public bool DebugClipPlanes = false;
        [Tooltip("Uses a cubemap for Spot shadows. Enable for a full Spot Angle of 180 degrees or more.")]
        [HideInInspector] public bool ForceCubemapShadows = false;
        [Tooltip("Filled by Bake Shadows. You can also assign a compatible shadow texture or material.")]
        [HideInInspector] public UnityEngine.Object ShadowMap;

        // Shared disabled runtime shadow bake camera assigned by the Light Volume Manager.
        [NonSerialized] public Camera RuntimeShadowCamera;
        // Cached shared runtime shadow depth encode material assigned by the Light Volume Manager.
        [NonSerialized] public Material RuntimeShadowDepthEncodeMaterial;
        // Cached shared runtime shadow blur material assigned by the Light Volume Manager.
        [NonSerialized] public Material RuntimeShadowBlurMaterial;
        // Temporary exclusion state kept only while the shadow camera renders.
        private Renderer[] _appliedExclusionMask;
        private bool[] _shadowExclusionRendererStates;

        // Internal shadow source metadata resolved by the editor authoring layer
        [HideInInspector] public bool ShadowMapTextureIsCubemap = false;
        [HideInInspector] public bool ShadowMapTextureHasDepthSlices = false;
        [HideInInspector] public bool ShadowMapUsesCubemap = true;

        // Internal dirty flag consumed by LightVolumeManager to recalculate this light's range
        [HideInInspector] public bool IsRangeDirty = false;

        private Vector3 _prevPosition = Vector3.zero;
        private Quaternion _prevRotation = Quaternion.identity;
        private Vector3 _prevScale = Vector3.one;

        private Color _old_Color = Color.white;
        private float _old_Intensity = 100f;
        private float _old_ShadingStrength = 1;
        private bool _isRegisteredWithManager = false;
        [NonSerialized] public Color AreaLightFallbackColor = Color.clear;
        [HideInInspector] public float AreaCookieMirror = 1f;
        [NonSerialized] public int AreaCookieAverageCustomId = -1;
        [NonSerialized] public bool AreaCookieAverageReadbackPending = false;
        [NonSerialized] public bool AreaCookieAverageReadbackDirty = false;
        // Append-only authoring field. 0 inherits the Manager's Shadow Resolution.
        [Tooltip("Resolution used to render this light's shadow bake in both the editor and Bake In Game. 0 inherits Shadow Resolution from the Light Volume Manager.")]
        [Min(0)] public int ShadowBakeResolution = 0;
#if COMPILER_UDONSHARP
        private Color32[] _areaCookieAveragePixels = new Color32[1];
#endif

        // Local shader keywords used by runtime shadow blur material
        private const string ShadowQualityKeywordLow = "VRCLV_RUNTIME_SHADOW_QUALITY_LOW";
        private const string ShadowQualityKeywordMedium = "VRCLV_RUNTIME_SHADOW_QUALITY_MEDIUM";
        private const string ShadowQualityKeywordHigh = "VRCLV_RUNTIME_SHADOW_QUALITY_HIGH";
        private const string ShadowQualityKeywordEditor = "VRCLV_EDITOR_SHADOW_BLUR_QUALITY";
        private const string ShadowBlurKeywordUniform = "VRCLV_RUNTIME_SHADOW_BLUR_UNIFORM";
        private const string ShadowBlurKeywordDirect = "VRCLV_RUNTIME_SHADOW_BLUR_DIRECT";
        private const string ShadowBlurKeywordSpherical = "VRCLV_RUNTIME_SHADOW_BLUR_SPHERICAL";
        private const float ShadowBlurBaseResolution = 128f;
        private const int ShadowTextureFormatHalf = 0;

        // Runtime shadow bake lifecycle and source state.
        private bool _runtimeShadowSourceInitialized = false;
        private bool _runtimeShadowShaderPropertiesInitialized = false;
        private float _runtimeShadowReceiverNearClip = 0f;
        private float _runtimeShadowReceiverFarClip = 0f;

        // Locally-owned runtime shadow render targets.
        private RenderTexture _runtimeShadowDepthTexture;
        private RenderTexture _runtimeShadowTexture;
        private RenderTexture _runtimeShadowBlurTempTexture;
        private RenderTexture _runtimeShadowMaterialBlitInputTexture;

        // Local cubemap face rotations used by point-light runtime shadow rendering.
        private Quaternion _runtimeShadowFaceRotation0 = new Quaternion(0f, -0.70710678f, 0f, 0.70710678f);
        private Quaternion _runtimeShadowFaceRotation1 = new Quaternion(0f, 0.70710678f, 0f, 0.70710678f);
        private Quaternion _runtimeShadowFaceRotation2 = new Quaternion(0f, -0.70710678f, 0.70710678f, 0f);
        private Quaternion _runtimeShadowFaceRotation3 = new Quaternion(0f, 0.70710678f, 0.70710678f, 0f);
        private Quaternion _runtimeShadowFaceRotation4 = new Quaternion(0f, 1f, 0f, 0f);

        // Shader property IDs used by runtime shadow depth encode and blur passes.
        private int _runtimeShadowDepthTextureID;
        private int _runtimeShadowFarClipID;
        private int _runtimeShadowNearClipID;
        private int _runtimeShadowBiasID;
        private int _runtimeShadowTanHalfFovID;
        private int _runtimeShadowSourceArrayID;
        private int _runtimeShadowDepthArrayID;
        private int _runtimeShadowFaceIndexID;
        private int _runtimeShadowSourceBaseSliceID;
        private int _runtimeShadowDepthBaseSliceID;
        private int _runtimeShadowBlurDirectionID;
        private int _runtimeShadowBlurRadiusID;
        private int _runtimeShadowBlurDepthID;
        private int _runtimeShadowInvResolutionID;

#if UDONSHARP
        // Works only when changing values directly on UdonBehaviour
        // Low level Udon hacks:
        // _old_(Name) variables are the old values of the variables
        // _onVarChange_(Name) methods (events) are called when the variable changes
        public void _onVarChange_IsDynamic() {
            NotifyManager(true, false, false);
        }
        // Recalculates range and uploads data when Udon changes the light color.
        public void _onVarChange_Color() {
            if (_old_Color != Color) {
                _old_Color = Color;
                MarkColorRangeDirtyAndNotify();
            }
        }
        // Recalculates range and uploads data when Udon changes light intensity.
        public void _onVarChange_Intensity() {
            if (_old_Intensity != Intensity) {
                _old_Intensity = Intensity;
                MarkColorRangeDirtyAndNotify();
            }
        }
        // Rebuilds active-light data when Udon changes shading strength across zero.
        public void _onVarChange_ShadingStrength() {
            if (_old_ShadingStrength != ShadingStrength) {
                float oldStrength = _old_ShadingStrength;
                _old_ShadingStrength = ShadingStrength;
                NotifyManager((oldStrength <= 0) != (ShadingStrength <= 0), false, false);
            }
        }
#endif

#if UDONSHARP || UNITY_EDITOR
        // Registers a newly spawned instance after its initially empty manager reference is assigned.
        public void _onVarChange_LightVolumeManager() {
            RegisterWithManager();
        }
#endif

        // Sends this instance change to the manager when it is active.
        private void NotifyManager(bool rebuildFinalData, bool customTexturesChanged, bool shadowTexturesChanged) {
            bool wasActive = IsActive;
            bool runtimeEnabled = enabled && gameObject.activeInHierarchy;
            IsActive = runtimeEnabled && Intensity != 0 && Color != Color.black;
            if (wasActive != IsActive && RuntimeShadowDirectOutput) _runtimeShadowSourceInitialized = false;
            if (!runtimeEnabled) return;
            if (!_isRegisteredWithManager) RegisterWithManager();
            if (LightVolumeManager == null) return;
            if (wasActive != IsActive) {
                if (CustomTexture != null || CustomTextureMaterial != null) customTexturesChanged = true;
                if (ShadowMapID >= 0 || ShadowMapTexture != null || ShadowMapMaterial != null) shadowTexturesChanged = true;
            }
            LightVolumeManager.NotifyPointLightVolumeChanged(this, rebuildFinalData, customTexturesChanged, shadowTexturesChanged);
        }

        // Registers once with the world's single manager.
        private void RegisterWithManager() {
            if (_isRegisteredWithManager) return;
            bool runtimeEnabled = enabled && gameObject.activeInHierarchy;
            IsActive = runtimeEnabled && Intensity != 0 && Color != Color.black;
            if (LightVolumeManager == null || !runtimeEnabled) return;
            _isRegisteredWithManager = true;
            LightVolumeManager.InitializePointLightVolume(this);
        }

        // Registers the light and queues its optional one-shot runtime shadow bake.
        private void Start() {
#if !UDONSHARP
            if (LightVolumeManager == null) {
                LightVolumeManager = FindObjectOfType<LightVolumeManager>();
            }
#endif
            RegisterWithManager();
            if (!BakeInGame || LightVolumeManager == null) return;
            LightVolumeManager.EnqueueBakeInGameLight(this);
        }

        // Registers the light when its component or GameObject becomes active.
        private void OnEnable() {
            RegisterWithManager();
        }

        // Removes the light from the Manager registry and marks it inactive.
        private void OnDisable() {
            _isRegisteredWithManager = false;
            if (LightVolumeManager != null) {
                bool customTexturesChanged = IsActive && (CustomTexture != null || CustomTextureMaterial != null);
                bool shadowTexturesChanged = IsActive && ShadowMapID >= 0;
                LightVolumeManager.DeinitializePointLightVolume(this, customTexturesChanged, shadowTexturesChanged);
            }
            IsActive = false;
            if (RuntimeShadowDirectOutput) _runtimeShadowSourceInitialized = false;
            // Component-level blinking is allowed while explicit bakes are requested. A disabled GameObject cannot render, so release scratch resources until the next request.
            if (!gameObject.activeInHierarchy) ReleaseIdleRuntimeShadowTextures();
        }

#if COMPILER_UDONSHARP
        // Receives the area-cookie fallback average and sends it back to the manager
        public override void OnAsyncGpuReadbackComplete(VRCAsyncGPUReadbackRequest request) {
            if (LightVolumeManager == null) {
                AreaCookieAverageReadbackPending = false;
                AreaCookieAverageReadbackDirty = false;
                AreaCookieAverageCustomId = -1;
                return;
            }
            if (request.hasError) {
                LightVolumeManager.CompleteAreaCookieAverageReadback(this, false, Color.clear);
                return;
            }
            if (!request.TryGetData(_areaCookieAveragePixels)) {
                LightVolumeManager.CompleteAreaCookieAverageReadback(this, false, Color.clear);
                return;
            }
            LightVolumeManager.CompleteAreaCookieAverageReadback(this, true, _areaCookieAveragePixels[0]);
        }
#else
        // Receives the area-cookie fallback average and sends it back to the manager
        internal void OnUnityAsyncGpuReadbackComplete(AsyncGPUReadbackRequest request) {
            if (LightVolumeManager == null) {
                AreaCookieAverageReadbackPending = false;
                AreaCookieAverageReadbackDirty = false;
                AreaCookieAverageCustomId = -1;
                return;
            }
            if (request.hasError) {
                LightVolumeManager.CompleteAreaCookieAverageReadback(this, false, Color.clear);
                return;
            }
            Unity.Collections.NativeArray<Color32> pixels = request.GetData<Color32>();
            if (pixels.Length <= 0) {
                LightVolumeManager.CompleteAreaCookieAverageReadback(this, false, Color.clear);
                return;
            }
            LightVolumeManager.CompleteAreaCookieAverageReadback(this, true, pixels[0]);
#if UNITY_EDITOR
            if (!Application.isPlaying) {
                UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
                UnityEditor.SceneView.RepaintAll();
            }
#endif
        }
#endif

        // Sets dynamic mode and rebuilds the manager light list only when it changes
        public void SetDynamic(bool isDynamic) {
            if (IsDynamic == isDynamic) return;
            IsDynamic = isDynamic;
            NotifyManager(true, false, false);
        }

        // Sets runtime registry weight and reorders this point light volume in the manager registry
        public void SetWeight(float weight) {
            if (RegistryWeight == weight) return;
            RegistryWeight = weight;
            if (_isRegisteredWithManager)
                IsActive = enabled && gameObject.activeInHierarchy && Intensity != 0 && Color != Color.black;
            else RegisterWithManager();
            if (LightVolumeManager != null) LightVolumeManager.ReorderPointLightVolume(this);
        }

        // Sets light source size or range data for LUT mode
        public void SetLightSourceSize(float size) {
            float safeSize = Mathf.Max(Mathf.Abs(size), 0.0001f);
            float inverseSquaredRange = 1f / (safeSize * safeSize);
            if (LightSourceSize == safeSize && InverseSquaredRange == inverseSquaredRange) return;
            LightSourceSize = safeSize;
            InverseSquaredRange = inverseSquaredRange;
            IsRangeDirty = true;
            NotifyManager(false, false, false);
        }

        // Sets LUT mode
        public void SetLut() {
            ProjectionMode = 1; // 1: LUT
            if (LightType == 1) OuterAngleTan = Mathf.Tan(Angle); // 1: spot
            OuterAngleCos = Mathf.Cos(Angle);
            UpdateRotationFromTransformCore();
            IsRangeDirty = true;
            NotifyManager(true, CustomTexture != null || CustomTextureMaterial != null, false);
        }

        // Selects custom projection mode after a source was assigned through the public fields.
        public void SetCustomTexture() {
            SetCustomProjectionMode();
            IsRangeDirty = true;
            NotifyManager(true, CustomTexture != null || CustomTextureMaterial != null, false);
        }

        // Assigns a texture source and uses the dev.16 automatic update default: RenderTexture-derived sources are live, immutable Texture assets are snapshots.
        public void SetCustomTexture(Texture texture) {
            SetCustomTexture(texture, false, texture != null && typeof(RenderTexture).IsInstanceOfType(texture));
        }

        // Backward-compatible dev.15 API. Source layout is now inferred from the Texture itself; isCubemap is retained so existing callers keep compiling.
        public void SetCustomTexture(Texture texture, bool isCubemap, bool autoUpdate) {
            CustomTexture = texture;
            CustomTextureMaterial = null;
            AutoUpdateCustomTexture = texture != null && autoUpdate;
            if (texture != null) {
                SetCustomProjectionMode();
            } else {
                SetParametricMode();
            }
            IsRangeDirty = true;
            NotifyManager(true, true, false);
        }

        // Assigns a material source using the dev.16 live-source default.
        public void SetCustomMaterial(Material material) {
            SetCustomMaterial(material, true);
        }

        // Assigns a material source and chooses whether its rendered projection stays live or remains a rebuild-time snapshot.
        public void SetCustomMaterial(Material material, bool autoUpdate) {
            CustomTexture = null;
            CustomTextureMaterial = material;
            AutoUpdateCustomTexture = material != null && autoUpdate;
            if (material != null) {
                SetCustomProjectionMode();
            } else {
                SetParametricMode();
            }
            IsRangeDirty = true;
            NotifyManager(true, true, false);
        }

        // Sets the light into parametric mode
        public void SetParametric() {
            if (ProjectionMode == 0) return;
            SetParametricMode();
            IsRangeDirty = true;
            NotifyManager(true, CustomTexture != null || CustomTextureMaterial != null, false);
        }

        // A custom Point cookie occupies six atlas slices; Spot and Area cookies occupy one.
        private bool CustomCookieStateChangesWithLightType(int targetLightType) {
            return LightType != targetLightType && ProjectionMode == 2 && (CustomTexture != null || CustomTextureMaterial != null);
        }

        // Sets the light into the point light type
        public void SetPointLight() {
            Transform instanceTransform = transform;
            Vector3 position = instanceTransform.position;
            if (LightType == 0 && Position == position && ShadowMapUsesCubemap) return;
            bool customTexturesChanged = CustomCookieStateChangesWithLightType(0);
            bool shadowTexturesChanged = !ShadowMapUsesCubemap && (ShadowMapID >= 0 || ShadowMapTexture != null || ShadowMapMaterial != null);
            LightType = 0; // 0: point
            ShadowMapUsesCubemap = true;
            Position = position;
            if (ProjectionMode != 0) Rotation = Quaternion.Inverse(instanceTransform.rotation);
            IsRangeDirty = true;
            NotifyManager(false, customTexturesChanged, shadowTexturesChanged);
        }

        // Sets the light into the spotlight type with both angle and falloff because angle is required to determine falloff
        public void SetSpotLight(float angleDeg, float falloff) {
            float angle = angleDeg * Mathf.Deg2Rad * 0.5f;
            float outerAngleTan = Mathf.Tan(angle);
            Transform instanceTransform = transform;
            Vector3 position = instanceTransform.position;
            Quaternion transformRotation = instanceTransform.rotation;
            bool unchangedSpot = LightType == 1 && Angle == angle && OuterAngleTan == outerAngleTan && Position == position;
            // Custom cookies use inverse rotation; other Spot projections use direction and cone data. Derive only the selected representation and reuse it below.
            if (ProjectionMode == 2) {
                Quaternion rotation = Quaternion.Inverse(transformRotation);
                if (unchangedSpot && Rotation == rotation) return;
                Rotation = rotation;
            } else {
                float outerAngleCos = Mathf.Cos(angle);
                float coneFalloff = 1f / (Mathf.Cos(angle * (1.0f - Mathf.Clamp01(falloff))) - outerAngleCos);
                Vector3 direction = transformRotation * Vector3.forward;
                if (unchangedSpot && Direction == direction && OuterAngleCos == outerAngleCos && ConeFalloff == coneFalloff) return;
                Direction = direction;
                OuterAngleCos = outerAngleCos;
                ConeFalloff = coneFalloff;
            }
            bool customTexturesChanged = CustomCookieStateChangesWithLightType(1);
            LightType = 1; // 1: spot
            Angle = angle;
            OuterAngleTan = outerAngleTan;
            Position = position;
            IsRangeDirty = true;
            NotifyManager(false, customTexturesChanged, false);
        }

        // Sets the light into the spotlight type with a specified angle
        public void SetSpotLight(float angleDeg) {
            float angle = angleDeg * Mathf.Deg2Rad * 0.5f;
            float outerAngleTan = Mathf.Tan(angle);
            Transform instanceTransform = transform;
            Vector3 position = instanceTransform.position;
            Quaternion transformRotation = instanceTransform.rotation;
            bool unchangedSpot = LightType == 1 && Angle == angle && OuterAngleTan == outerAngleTan && Position == position;
            if (ProjectionMode == 2) {
                Quaternion rotation = Quaternion.Inverse(transformRotation);
                if (unchangedSpot && Rotation == rotation) return;
                Rotation = rotation;
            } else {
                float outerAngleCos = Mathf.Cos(angle);
                Vector3 direction = transformRotation * Vector3.forward;
                if (unchangedSpot && Direction == direction && OuterAngleCos == outerAngleCos) return;
                Direction = direction;
                OuterAngleCos = outerAngleCos;
            }
            bool customTexturesChanged = CustomCookieStateChangesWithLightType(1);
            LightType = 1; // 1: spot
            Angle = angle;
            OuterAngleTan = outerAngleTan;
            Position = position;
            IsRangeDirty = true;
            NotifyManager(false, customTexturesChanged, false);
        }

        // Sets the light into the area light type
        public void SetAreaLight() {
            bool customTexturesChanged = CustomCookieStateChangesWithLightType(2);
            bool shadowTexturesChanged = !ShadowMapUsesCubemap && (ShadowMapID >= 0 || ShadowMapTexture != null || ShadowMapMaterial != null);
            Transform instanceTransform = transform;
            Vector3 lossyScale = instanceTransform.lossyScale;
            Quaternion transformRotation = instanceTransform.rotation;
            LightType = 2; // 2: area
            ShadowMapUsesCubemap = true;
            Position = instanceTransform.position;
            Width = Mathf.Max(Mathf.Abs(lossyScale.x), 0.001f);
            Height = Mathf.Max(Mathf.Abs(lossyScale.y), 0.001f);
            Rotation = transformRotation;
            RefreshAreaCookieMirror(transformRotation, instanceTransform.localToWorldMatrix);
            IsRangeDirty = true;
            NotifyManager(true, customTexturesChanged, shadowTexturesChanged);
        }

        // Sets light source color
        public void SetColor(Color color) {
            if (Color == color) return;
            Color = color;
            _old_Color = color;
            MarkColorRangeDirtyAndNotify();
        }

        // Sets light source intensity
        public void SetIntensity(float intensity) {
            if (Intensity == intensity) return;
            Intensity = intensity;
            _old_Intensity = intensity;
            MarkColorRangeDirtyAndNotify();
        }

        // Sets color and intensity with one cross-behaviour manager notification.
        public void SetColorAndIntensity(Color color, float intensity) {
            if (Color == color && Intensity == intensity) return;
            Color = color;
            Intensity = intensity;
            _old_Color = color;
            _old_Intensity = intensity;
            MarkColorRangeDirtyAndNotify();
        }

        // Sets Normal Masking and shadow strength
        public void SetShadingStrength(float shadingStrength) {
            float strength = Mathf.Clamp01(shadingStrength);
            if (ShadingStrength == strength) return;
            float oldStrength = ShadingStrength;
            ShadingStrength = strength;
            _old_ShadingStrength = strength;
            NotifyManager((oldStrength <= 0) != (strength <= 0), false, false);
        }

        // Sets custom spotlight cookie projection aspect
        public void SetSpotCookieAspect(float aspect) {
            float safeAspect = Mathf.Max(Mathf.Abs(aspect), 0.001f);
            if (SpotCookieAspect == safeAspect) return;
            SpotCookieAspect = safeAspect;
            NotifyManager(false, false, false);
        }

        // Color and intensity share a narrow notification; the manager coalesces repeated writes to this compact shader slot and widens unsupported Point profiles to a full record pack.
        private void MarkColorRangeDirtyAndNotify() {
            bool wasActive = IsActive;
            bool wasRegistered = _isRegisteredWithManager;
            IsRangeDirty = true;
            bool runtimeEnabled = enabled && gameObject.activeInHierarchy;
            IsActive = runtimeEnabled && Intensity != 0 && Color != Color.black;
            if (wasActive != IsActive && RuntimeShadowDirectOutput) _runtimeShadowSourceInitialized = false;
            if (!runtimeEnabled) return;
            if (!wasRegistered) RegisterWithManager();
            LightVolumeManager manager = LightVolumeManager;
            if (manager == null) return;

            // Suite 1.6 cases 138-139 show that moving this exact basic-Point calculation to the source is neutral for one write and wins when Color and Intensity both change. Keep structural transitions and every richer profile on the manager's canonical path.
            bool sourceLocalRange = wasActive && IsActive && wasRegistered && LightType == 0 && ProjectionMode == 0 && ShadowMapID < 0f && ShadowMapTexture == null && ShadowMapMaterial == null;
            if (sourceLocalRange) {
                float cutoff = manager.LightsBrightnessCutoff;
                float luminance = Mathf.Max(Color.r, Mathf.Max(Color.g, Color.b));
                float squaredSize = Mathf.Abs(SquaredScale * LightSourceSize * LightSourceSize);
                SquaredRange = Mathf.Max(Mathf.PI * 2f * luminance * Mathf.Abs(Intensity) / (cutoff * cutoff) - 1f, 0f) * squaredSize;
                IsRangeDirty = false;
            }

            manager.NotifyPointLightColorRangeChanged(this);
        }

        // Applies the internal custom projection mode without touching texture source fields
        private void SetCustomProjectionMode() {
            ProjectionMode = 2; // 2: custom cookie or cubemap
            if (LightType == 1) OuterAngleTan = Mathf.Tan(Angle); // 1: spot
            UpdateRotationFromTransformCore();
        }

        // Applies the internal parametric projection mode without touching texture source fields
        private void SetParametricMode() {
            ProjectionMode = 0; // 0: parametric
            if (LightType == 1) OuterAngleTan = Mathf.Tan(Angle); // 1: spot
            OuterAngleCos = Mathf.Cos(Angle);
            UpdateRotationFromTransformCore();
        }

        // Updates data required for shader
        public void UpdateTransform() {
            if (!UpdateTransformCore()) return;
            NotifyManager(false, false, false);
        }

        // Synchronizes cached transform/range inputs without publishing an intermediate Manager state.
        // Runtime shadow baking uses this core and publishes once after the complete cubemap is ready, so moving a pooled light cannot rebuild or flash the shared atlas between faces.
        private bool UpdateTransformCore() {
            Transform instanceTransform = transform;
            Vector3 position = instanceTransform.position;
            Quaternion rotation = instanceTransform.rotation;
            Vector3 lossyScale = instanceTransform.lossyScale;
            bool positionChanged = _prevPosition != position;
            bool rotationChanged = _prevRotation != rotation;
            bool scaleChanged = _prevScale != lossyScale;
            if (!positionChanged && !rotationChanged && !scaleChanged) return false;

            if (positionChanged) {
                _prevPosition = position;
                Position = position;
            }
            if (scaleChanged) {
                _prevScale = lossyScale;
                UpdateScaleCore(lossyScale);
                IsRangeDirty = true;
            }
            if (rotationChanged || (scaleChanged && LightType == 2)) {
                _prevRotation = rotation;
                Matrix4x4 localToWorldMatrix = LightType == 2 ? instanceTransform.localToWorldMatrix : Matrix4x4.identity;
                UpdateRotationCore(rotation, localToWorldMatrix);
            }
            return true;
        }

        // Force update position
        public void UpdatePosition() {
            Transform instanceTransform = transform;
            Vector3 position = instanceTransform.position;
            _prevPosition = position;
            Position = position;
            NotifyManager(false, false, false);
        }

        // Resolves the Area Cookie X/Y reflection relative to the quaternion frame sent to shaders.
        private void RefreshAreaCookieMirror(Quaternion transformRotation, Matrix4x4 localToWorldMatrix) {
            Vector3 matrixXAxis = localToWorldMatrix.GetColumn(0);
            Vector3 matrixYAxis = localToWorldMatrix.GetColumn(1);
            bool flipCookieX = Vector3.Dot(matrixXAxis, transformRotation * Vector3.right) < 0f;
            bool flipCookieY = Vector3.Dot(matrixYAxis, transformRotation * Vector3.up) < 0f;
            AreaCookieMirror = (flipCookieY ? 2f : 1f) * (flipCookieX ? -1f : 1f);
        }

        // Applies caller-cached rotation data without notifying the manager.
        private void UpdateRotationCore(Quaternion transformRotation, Matrix4x4 localToWorldMatrix) {
            if (LightType == 2) { // 2: area
                Rotation = transformRotation;
                RefreshAreaCookieMirror(transformRotation, localToWorldMatrix);
            } else if (LightType == 1 && ProjectionMode != 2) { // 1: spot, 2: custom cookie
                Direction = transformRotation * Vector3.forward;
            } else if (ProjectionMode != 0) { // 0: parametric; non-parametric point/cookie uses inverse rotation
                Rotation = Quaternion.Inverse(transformRotation);
            }
        }

        // Reads the Transform once and applies rotation data without notifying the manager.
        private void UpdateRotationFromTransformCore() {
            Transform instanceTransform = transform;
            Quaternion transformRotation = instanceTransform.rotation;
            _prevRotation = transformRotation;
            if (LightType == 0 && ProjectionMode == 0) return;
            Matrix4x4 localToWorldMatrix = LightType == 2 ? instanceTransform.localToWorldMatrix : Matrix4x4.identity;
            UpdateRotationCore(transformRotation, localToWorldMatrix);
        }

        // Force update rotation
        public void UpdateRotation() {
            UpdateRotationFromTransformCore();
            NotifyManager(false, false, false);
        }

        // Applies caller-cached scale data without notifying the manager.
        private void UpdateScaleCore(Vector3 lossyScale) {
            if (LightType == 2) { // 2: area
                Width = Mathf.Max(Mathf.Abs(lossyScale.x), 0.001f);
                Height = Mathf.Max(Mathf.Abs(lossyScale.y), 0.001f);
            }
            float averageScale = (Mathf.Abs(lossyScale.x) + Mathf.Abs(lossyScale.y) + Mathf.Abs(lossyScale.z)) / 3;
            SquaredScale = averageScale * averageScale;
        }

        // Force update scale
        public void UpdateScale() {
            Transform instanceTransform = transform;
            Vector3 lossyScale = instanceTransform.lossyScale;
            _prevScale = lossyScale;
            UpdateScaleCore(lossyScale);
            if (LightType == 2) { // 2: area
                Quaternion transformRotation = instanceTransform.rotation;
                _prevRotation = transformRotation;
                UpdateRotationCore(transformRotation, instanceTransform.localToWorldMatrix);
            }
            IsRangeDirty = true;
            NotifyManager(false, false, false);
        }


    }

}
