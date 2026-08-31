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
        [Tooltip("Defines whether this point light volume can be moved at runtime. Disabling this option slightly improves performance. Don't forget to enable \"Auto Update Volumes\" in your Light Volumes Setup to get these dynamic updates!")]
        public bool IsDynamic = false;
        [Tooltip("Point Light is the most performant type. For static lighting, prefer baked additive Light Volumes.")]
        public int LightType = 0; // 0: point, 1: spot, 2: area
        [Tooltip("Multiplies the point light volume’s color by this value.")]
        [ColorUsage(showAlpha: false)] public Color Color = Color.white;
        [Tooltip("Brightness of the point light volume.")]
        public float Intensity = 100f;
        [Tooltip("Controls per-surface Point Light shading and shadow opacity based on surface normal. 0 disables this extra shading and shadows for this light; 1 applies them fully. Modern individual speculars use the same light mask.")]
        [Range(0, 1)] public float ShadingStrength = 1f;

        [Header("Position Data")]
        [Tooltip("World-space position used by this point light volume.")]
        public Vector3 Position = Vector3.zero;
        [Tooltip("Light source size used by parametric Point Lights, parametric Spot Lights, cookies and cubemap projections. It affects calculated range and broadens size-aware specular highlights in modern compatible shaders.")]
        [Min(0.0001f)] public float LightSourceSize = 0.025f;
        [Tooltip("Inverse squared range used by LUT projection.")]
        [Min(0)] public float InverseSquaredRange = 1f;
        [Tooltip("Area Light width in meters. Affects textured Area Light emission and size-aware Area Light speculars in modern compatible shaders.")]
        [Min(0.001f)] public float Width = 1f;

        [Header("Direction Data")]
        [Tooltip("World-space spotlight direction used by parametric and LUT spot lights.")]
        public Vector3 Direction = Vector3.forward;
        [Tooltip("Rotation used by area lights, cubemap projections and cookie projections.")]
        public Quaternion Rotation = Quaternion.identity;
        [Tooltip("Spotlight cone falloff multiplier used by parametric spot lights.")]
        public float ConeFalloff = 1f;

        [Header("Angle Data")]
        [Tooltip("Spotlight cone angle shown in degrees in the inspector. Stored internally as a half-angle in radians.")]
        public float Angle = 0.5235988f;
        [Tooltip("Cosine of the spotlight outer angle used by parametric and LUT spot lights.")]
        public float OuterAngleCos = 1f;
        [Tooltip("Tangent of the spotlight outer angle used by cookie projection.")]
        public float OuterAngleTan = 0f;
        [Tooltip("Width / height aspect used by custom spotlight cookie projection. 1 keeps a square projector; values above 1 compress projected height.")]
        [Min(0.001f)] public float SpotCookieAspect = 1f;
        [Tooltip("Area Light height in meters. Affects textured Area Light emission and size-aware Area Light speculars in modern compatible shaders.")]
        [Min(0.001f)] public float Height = 1f;

        [Header("Runtime State")]
        [Tooltip("Squared range after which light will be culled. Recalculated by the Light Volume Manager.")]
        public float SquaredRange = 1f;
        [Tooltip("Average squared lossy scale of the light. Light Source Size uses this for range and size-aware specular calculations. Updates with UpdateTransform() method.")]
        public float SquaredScale = 1f;
        [Tooltip("Reference to the world's single Light Volume Manager. Assign it before registration and do not change it afterwards.")]
        public LightVolumeManager LightVolumeManager;
        [Tooltip("Internal stable manager registry tie-breaker used when this point light volume is enabled at runtime. Use SetWeight(float weight) to change priority.")]
        [HideInInspector] public int RegistryOrder = 2147483647;
        [Tooltip("Manager registry sort weight. Higher weights are uploaded to shaders first.")]
        [HideInInspector] public float RegistryWeight = 0f;
        [HideInInspector] public bool IsActive = true;
        [Header("Projection Source")]
        [Tooltip("Texture source used by this light's active LUT, cookie or cubemap projection.")]
        public Texture CustomTexture;
        [Tooltip("Material source used by this light's active LUT, cookie or cubemap projection.")]
        public Material CustomTextureMaterial;
        [Tooltip("Projection mode used by this light. 0 = parametric, 1 = LUT, 2 = custom cookie or cubemap.")]
        public int ProjectionMode = 0; // 0: parametric, 1: LUT, 2: custom cookie or cubemap
        [Tooltip("Updates this light's custom projection slice every frame while the Manager's Auto Update Textures option is enabled. Disable it to keep a snapshot captured during the last texture-array rebuild.")]
        public bool AutoUpdateCustomTexture = false;

        [Header("Shadow Source")]
        [Tooltip("Texture source used by this light's shadow map.")]
        public Texture ShadowMapTexture;
        [Tooltip("Material source used by this light's shadow map.")]
        public Material ShadowMapMaterial;
        [Tooltip("Updates this light's shadow map texture every frame.")]
        public bool AutoUpdateShadowMap = false;
        [Tooltip("Index of the shadow map used by this light. -1 means no shadow.")]
        public float ShadowMapID = -1f;
        [Tooltip("Keeps baked shadows fixed in world space instead of moving with the light. This costs slightly more at runtime.")]
        public bool WorldSpaceShadows = false;
        [Tooltip("World-space position where the shadow map was baked.")]
        public Vector3 ShadowBakePosition = Vector3.zero;
        [Tooltip("World-space rotation where the shadow map was baked.")]
        public Quaternion ShadowBakeRotation = Quaternion.identity;

        [Header("Shadow Bake Settings")]
        [Tooltip("Layers that can cast shadows.")]
        public int LayerMask = 270849;
        [Tooltip("Near clip plane used by the shadow bake camera. Higher values can clip nearby occluders.")]
        [Min(0.0001f)] public float NearClip = 0.01f;
        [Tooltip("Far clip plane used by the shadow bake camera. Shadow casters outside the near-far range are clipped. 0 uses this light's current culling range.")]
        [Min(0)] public float FarClip = 0f;
        // Serialized source of truth for the far clip actually used by the latest shadow bake.
        [HideInInspector] public float BakedFarClip = 0f;
        [Tooltip("World-space bias in meters applied while baking this light's shadow map. Larger values reduce self-shadow artifacts, but can detach contact edges. Requires rebaking.")]
        [Min(0)] public float Bias = 0.01f;
        [Tooltip("Shadow blur radius applied after baking, normalized to 128x128 shadow resolution. Editor and runtime baking use the Spherical Blur setting below. 0 keeps the baked shadow map unblurred. Requires rebaking.")]
        [Min(0)] public float Blur = 1f;
        [Tooltip("Hardens shadows near the contact areas. Can produce artefacts, so use with caution. Requires rebaking. More performant when set to 0 in realtime mode. Spherical Blur also applies to contact hardening samples.")]
        [Range(0, 1)] public float ContactHardening = 0f;

        [Tooltip("Queues this light for a one-shot in-game shadow bake when its runtime instance starts. The Manager completes one queued light per frame. The editor-baked shadow texture is not included in the build or asset bundle.")]
        public bool BakeInGame = false;
        [Tooltip("Resolution used by runtime shadow baking.")]
        [Min(16)] public int RuntimeShadowResolution = 128;
        [Tooltip("Runtime blur and contact hardening sample preset. 0 = low, 1 = medium, 2 = high.")]
        [Range(0, 2)] public int RuntimeShadowBlurSamplePreset = 2;
        [Tooltip("Uses spherical shadow-space blur for editor and in-game shadow bakes, reducing cubemap and single-slice spot projection seams. Disable it to use faster planar blur.")]
        public bool RuntimeShadowSphericalBlur = true;
        [Tooltip("Writes a complete realtime shadow bake directly into the Manager atlas when its resolution matches this light. External runtime bakers enable this mode automatically for realtime updates.")]
        [NonSerialized] public bool RuntimeShadowDirectOutput = false;

        // Persistent authoring state. These fields deliberately remain part of the Udon program so the UdonSharp proxy and backing behaviour always share one serializable schema. Duplicate texture
        // references are cleared from the temporary build scene, while runtime authoring references such as the excluded shadow renderers remain available to Udon.
        [Tooltip("Parametric computes light falloff from settings. LUT uses X for cone falloff and Y for attenuation. Custom projects a cookie or cubemap.")]
        [HideInInspector] public int Projection = 0; // 0: parametric, 1: LUT, 2: custom cookie or cubemap
        [Tooltip("Radius in meters beyond which the light is culled. Fewer overlapping lights improve performance.")]
        [HideInInspector] public float Range = 10f;
        [Tooltip("Controls the Spot Light cone falloff.")]
        [HideInInspector] public float Falloff = 1f;
        [Tooltip("LUT texture or material. X controls cone falloff and Y controls attenuation. Uncompressed RGBA Half or RGBA Float is recommended for textures.")]
        [HideInInspector] public UnityEngine.Object FalloffLUT;
        [Tooltip("Texture or material projected by a Spot Light, or used as the textured emitter surface of an Area Light.")]
        [HideInInspector] public UnityEngine.Object Cookie;
        [Tooltip("Cubemap texture or material projected by a Point Light.")]
        [HideInInspector] public UnityEngine.Object Cubemap;
        [Tooltip("Bakes this light into light probes so it can affect objects without Light Volumes support. Intended for static lights.")]
        [HideInInspector] public bool BakeIntoProbes = false;
        [Tooltip("Shows the light's culling range gizmo. Use it to reduce unnecessary overlap between Point Light Volumes.")]
        [HideInInspector] public bool DebugRange = false;
        [Tooltip("Enables baked shadows for this light. Baked shadows can still affect dynamic objects such as avatars.")]
        [HideInInspector] public bool Shadows = false;
        [Tooltip("Includes this light when Bake Shadows is clicked in the Light Volume Manager. Disable it to keep the current shadow map during batch bakes.")]
        [HideInInspector] public bool RebakeShadows = true;
        [Tooltip("Renderers that must not cast shadows for this light. Listed renderers are temporarily excluded from both editor and runtime shadow baking.")]
        [HideInInspector] public Renderer[] ExclusionMask = new Renderer[0];
        [Tooltip("Shows the shadow near and far clip plane gizmo.")]
        [HideInInspector] public bool DebugClipPlanes = false;
        [Tooltip("Forces Spot Light shadows to bake and store as a cubemap even when the spot angle is below 180 degrees.")]
        [HideInInspector] public bool ForceCubemapShadows = false;
        [Tooltip("Baked shadow map source for this light. Bake Shadows generates it automatically; compatible textures and materials can also be assigned manually.")]
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
                NotifyManager((Mathf.Clamp01(oldStrength) <= 0) != (Mathf.Clamp01(ShadingStrength) <= 0), false, false);
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
            RegisterWithManager();
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
            IsActive = enabled && gameObject.activeInHierarchy && Intensity != 0 && Color != Color.black;
            if (LightVolumeManager == null || !gameObject.activeInHierarchy || !enabled) return;
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
            MarkRangeDirtyAndNotify(false, false, false);
        }

        // Sets LUT mode
        public void SetLut() {
            ProjectionMode = 1; // 1: LUT
            if (LightType == 1) OuterAngleTan = Mathf.Tan(Angle); // 1: spot
            OuterAngleCos = Mathf.Cos(Angle);
            UpdateRotationFromTransformCore();
            MarkRangeDirtyAndNotify(true, CustomTexture != null || CustomTextureMaterial != null, false);
        }

        // Selects custom projection mode after a source was assigned through the public fields.
        public void SetCustomTexture() {
            SetCustomProjectionMode();
            MarkRangeDirtyAndNotify(true, CustomTexture != null || CustomTextureMaterial != null, false);
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
            MarkRangeDirtyAndNotify(true, true, false);
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
            MarkRangeDirtyAndNotify(true, true, false);
        }

        // Sets the light into parametric mode
        public void SetParametric() {
            if (ProjectionMode == 0) return;
            SetParametricMode();
            MarkRangeDirtyAndNotify(true, CustomTexture != null || CustomTextureMaterial != null, false);
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
            if (ProjectionMode != 0) UpdateRotationCore(instanceTransform.rotation, Matrix4x4.identity);
            MarkRangeDirtyAndNotify(false, customTexturesChanged, shadowTexturesChanged);
        }

        // Sets the light into the spotlight type with both angle and falloff because angle is required to determine falloff
        public void SetSpotLight(float angleDeg, float falloff) {
            float angle = angleDeg * Mathf.Deg2Rad * 0.5f;
            float outerAngleTan = Mathf.Tan(angle);
            float outerAngleCos = Mathf.Cos(angle);
            float coneFalloff = 1f / (Mathf.Cos(angle * (1.0f - Mathf.Clamp01(falloff))) - outerAngleCos);
            Transform instanceTransform = transform;
            Vector3 position = instanceTransform.position;
            Quaternion transformRotation = instanceTransform.rotation;
            Vector3 direction = transformRotation * Vector3.forward;
            Quaternion rotation = Quaternion.Inverse(transformRotation);
            if (LightType == 1 && Angle == angle && OuterAngleTan == outerAngleTan && Position == position && (ProjectionMode == 2 ? Rotation == rotation : Direction == direction && OuterAngleCos == outerAngleCos && ConeFalloff == coneFalloff)) return;
            bool customTexturesChanged = CustomCookieStateChangesWithLightType(1);
            LightType = 1; // 1: spot
            Angle = angle;
            OuterAngleTan = outerAngleTan;
            if (ProjectionMode != 2) { // 2: custom cookie or cubemap
                OuterAngleCos = outerAngleCos;
                ConeFalloff = coneFalloff;
            }
            Position = position;
            UpdateRotationCore(transformRotation, Matrix4x4.identity);
            MarkRangeDirtyAndNotify(false, customTexturesChanged, false);
        }

        // Sets the light into the spotlight type with a specified angle
        public void SetSpotLight(float angleDeg) {
            float angle = angleDeg * Mathf.Deg2Rad * 0.5f;
            float outerAngleTan = Mathf.Tan(angle);
            float outerAngleCos = Mathf.Cos(angle);
            Transform instanceTransform = transform;
            Vector3 position = instanceTransform.position;
            Quaternion transformRotation = instanceTransform.rotation;
            Vector3 direction = transformRotation * Vector3.forward;
            Quaternion rotation = Quaternion.Inverse(transformRotation);
            if (LightType == 1 && Angle == angle && OuterAngleTan == outerAngleTan && Position == position && (ProjectionMode == 2 ? Rotation == rotation : Direction == direction && OuterAngleCos == outerAngleCos)) return;
            bool customTexturesChanged = CustomCookieStateChangesWithLightType(1);
            LightType = 1; // 1: spot
            Angle = angle;
            OuterAngleTan = outerAngleTan;
            if (ProjectionMode != 2) OuterAngleCos = outerAngleCos; // 2: custom cookie or cubemap
            Position = position;
            UpdateRotationCore(transformRotation, Matrix4x4.identity);
            MarkRangeDirtyAndNotify(false, customTexturesChanged, false);
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
            UpdateRotationCore(transformRotation, instanceTransform.localToWorldMatrix);
            MarkRangeDirtyAndNotify(true, customTexturesChanged, shadowTexturesChanged);
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
            NotifyManager((Mathf.Clamp01(oldStrength) <= 0) != (strength <= 0), false, false);
        }

        // Sets custom spotlight cookie projection aspect
        public void SetSpotCookieAspect(float aspect) {
            float safeAspect = Mathf.Max(Mathf.Abs(aspect), 0.001f);
            if (SpotCookieAspect == safeAspect) return;
            SpotCookieAspect = safeAspect;
            NotifyManager(false, false, false);
        }

        // Marks this light range dirty and tells the manager which runtime data needs rebuilding.
        private void MarkRangeDirtyAndNotify(bool rebuildFinalData, bool customTexturesChanged, bool shadowTexturesChanged) {
            IsRangeDirty = true;
            NotifyManager(rebuildFinalData, customTexturesChanged, shadowTexturesChanged);
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
            Vector3 matrixXAxis = new Vector3(localToWorldMatrix.m00, localToWorldMatrix.m10, localToWorldMatrix.m20);
            Vector3 matrixYAxis = new Vector3(localToWorldMatrix.m01, localToWorldMatrix.m11, localToWorldMatrix.m21);
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
