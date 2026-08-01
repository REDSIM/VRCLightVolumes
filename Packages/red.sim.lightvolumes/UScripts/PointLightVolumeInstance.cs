using UnityEngine;
using UnityEngine.Rendering;
using System;
#if UDONSHARP
using UdonSharp;
#endif
#if COMPILER_UDONSHARP
using VRC.SDK3.Rendering;
using VRCGraphics = VRC.SDKBase.VRCGraphics;
using VRCShader = VRC.SDKBase.VRCShader;
#else
using VRCGraphics = UnityEngine.Graphics;
using VRCShader = UnityEngine.Shader;
#endif

namespace VRCLightVolumes {
    [AddComponentMenu("VRC Light Volumes/Point Light Volume (U# Script)")]
    [DisallowMultipleComponent]
#if UDONSHARP
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class PointLightVolumeInstance : UdonSharpBehaviour
#else
    public class PointLightVolumeInstance : MonoBehaviour
#endif
    {
        [Tooltip("Defines whether this point light volume can be moved at runtime. Disabling this option slightly improves performance. Don't forget to enable \"Auto Update Volumes\" in your Light Volumes Setup to get these dynamic updates!")]
        public bool IsDynamic = false;
        [Tooltip("Point light volume shape. 0 = point, 1 = spot, 2 = area.")]
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
        [Tooltip("Half-angle of the spotlight cone, in radians.")]
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
        [Tooltip("Reference to the scene's single Light Volume Manager. Assign it before registration and do not change it afterwards.")]
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
        [Tooltip("Projection source type used by this light. 0 = none, 1 = texture, 2 = material.")]
        public int ProjectionType = 0; // 0: none, 1: texture, 2: material
        [Tooltip("Projection mode used by this light. 0 = parametric, 1 = LUT, 2 = custom cookie or cubemap.")]
        public int ProjectionMode = 0; // 0: parametric, 1: LUT, 2: custom cookie or cubemap
        [Tooltip("Updates this light's custom texture slice every frame.")]
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
        [Tooltip("Use it if you don't want to move baked shadows together with their light. Attaches shadows to the world space basically. Less optimized when turned on.")]
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
        [Tooltip("Far clip distance used when the shadow map was baked. 0 falls back to this light's current culling range.")]
        [Min(0)] public float FarClip = 0f;
        // Serialized source of truth for the far clip actually used by the latest shadow bake.
        [HideInInspector] public float BakedFarClip = 0f;
        [Tooltip("World-space bias in meters applied while baking this light's shadow map. Larger values reduce self-shadow artifacts, but can detach contact edges. Requires rebaking.")]
        [Min(0)] public float Bias = 0.1f;
        [Tooltip("Shadow blur radius applied after baking, normalized to 128x128 shadow resolution. Editor baking uses spherical shadow-space blur to reduce visible cubemap and Spot Light projection seams. " +
                 "Runtime baking uses Planar Blur unless Spherical Blur is enabled on the runtime baker. 0 keeps the baked shadow map unblurred. Requires rebaking.")]
        [Min(0)] public float Blur = 1f;
        [Tooltip("Hardens shadows near the contact areas. Can produce artefacts, so use with caution. Requires rebaking. More performant when set to 0 in realtime mode. Runtime baker Spherical Blur also applies to contact hardening samples.")]
        [Range(0, 1)] public float ContactHardening = 0f;

        [Tooltip("Bakes this light's shadow map once when the runtime instance starts. If enabled, the editor-baked shadow texture is used only in the editor and is not included in the build or asset bundle.")]
        public bool BakeInGame = false;
        [Tooltip("Resolution used by runtime shadow baking.")]
        [Min(16)] public int RuntimeShadowResolution = 128;
        [Tooltip("Runtime blur and contact hardening sample preset. 0 = low, 1 = medium, 2 = high, 3 = editor.")]
        [Range(0, 3)] public int RuntimeShadowBlurSamplePreset = 2;
        [Tooltip("Samples runtime blur in spherical shadow space. This is slower but reduces cubemap and single-slice spot projection seams.")]
        public bool RuntimeShadowSphericalBlur = true;
        [Tooltip("How many shadow faces or slices are rendered each time runtime shadow baking is triggered. Valid values are 1, 2, 3 and 6. 6 bakes a full point shadow in one trigger.")]
        [Range(1, 6)] public int RuntimeShadowFacesPerFrame = 6;
        [Tooltip("Writes runtime shadow output directly into the manager shadow atlas when the bake resolution matches it. Intended for external realtime baking; Bake In Game keeps a full-size source texture.")]
        [HideInInspector] public bool RuntimeShadowDirectOutput = false;

        // Persistent authoring state. These fields deliberately remain part of the Udon program so the
        // UdonSharp proxy and backing behaviour always share one serializable schema. Duplicate texture
        // references are cleared from the temporary build scene, while runtime authoring references such as
        // the shadow exclusion roots remain available to Udon.
        [HideInInspector] public int Projection = 0; // 0: parametric, 1: LUT, 2: custom cookie or cubemap
        [HideInInspector] public float Range = 10f;
        [HideInInspector] public float Falloff = 1f;
        [HideInInspector] public UnityEngine.Object FalloffLUT;
        [HideInInspector] public UnityEngine.Object Cookie;
        [HideInInspector] public UnityEngine.Object Cubemap;
        [HideInInspector] public bool BakeIntoProbes = false;
        [HideInInspector] public bool DebugRange = false;
        [HideInInspector] public bool Shadows = false;
        [HideInInspector] public bool RebakeShadows = false;
        [Tooltip("Objects that must not cast shadows for this light. Every Renderer under a listed root is temporarily excluded from both editor and runtime shadow baking.")]
        [HideInInspector] public GameObject[] ExclusionMask = new GameObject[0];
        [HideInInspector] public bool DebugClipPlanes = false;
        [HideInInspector] public bool ForceCubemapShadows = false;
        [HideInInspector] public UnityEngine.Object ShadowMap;

        // Shared disabled runtime shadow bake camera assigned by the Light Volume Manager.
        [NonSerialized] public Camera RuntimeShadowCamera;
        // Cached shared runtime shadow depth encode material assigned by the Light Volume Manager.
        [NonSerialized] public Material RuntimeShadowDepthEncodeMaterial;
        // Cached shared runtime shadow blur material assigned by the Light Volume Manager.
        [NonSerialized] public Material RuntimeShadowBlurMaterial;

        // Temporary exclusion state kept only while the shadow camera renders.
        private Renderer[] _shadowExclusionRenderers = new Renderer[0];
        private bool[] _shadowExclusionRendererStates = new bool[0];

        // Internal projection source metadata resolved by the editor authoring layer
        [HideInInspector] public bool CustomTextureIsCubemap = false;
        [HideInInspector] public bool CustomTextureHasDepthSlices = false;

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
#if COMPILER_UDONSHARP
        private Color32[] _areaCookieAveragePixels = new Color32[0];
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

        // Runtime shadow bake lifecycle and published source state.
        private bool _inGameBakeStarted = false;
        private bool _runtimeShadowSourceInitialized = false;
        private bool _runtimeShadowShaderPropertiesInitialized = false;
        private float _runtimeShadowReceiverNearClip = 0f;
        private float _runtimeShadowReceiverFarClip = 0f;

        // Incremental runtime bake progress for the current face cycle.
        private int _runtimeShadowFaceIndex = 0;

        // Locally-owned runtime shadow render targets.
        private RenderTexture _runtimeShadowDepthTexture;
        private RenderTexture _runtimeShadowTexture;
        private RenderTexture _runtimeShadowRegistrationTexture;
        private RenderTexture _runtimeShadowBlurTempTexture;
        private RenderTexture _runtimeShadowMaterialBlitInputTexture;

#if UNITY_EDITOR && !COMPILER_UDONSHARP
        // Editor-only views of existing runtime state; no backing fields are added.
        public bool RegisteredWithManagerPreview => _isRegisteredWithManager;
        public bool RuntimeShadowBakeStartedPreview => _inGameBakeStarted;
        public bool RuntimeShadowSourceInitializedPreview => _runtimeShadowSourceInitialized;
        public int RuntimeShadowFaceIndexPreview => _runtimeShadowFaceIndex;
        public float RuntimeShadowReceiverNearClipPreview => _runtimeShadowReceiverNearClip;
        public float RuntimeShadowReceiverFarClipPreview => _runtimeShadowReceiverFarClip;
        public RenderTexture RuntimeShadowDepthTexturePreview => _runtimeShadowDepthTexture;
        public RenderTexture RuntimeShadowTexturePreview => _runtimeShadowTexture;
        public RenderTexture RuntimeShadowRegistrationTexturePreview => _runtimeShadowRegistrationTexture;
#endif

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
        public void _onVarChange_Color() {
            if (_old_Color != Color) {
                _old_Color = Color;
                MarkRangeDirtyAndNotify(false, false, false);
            }
        }
        public void _onVarChange_Intensity() {
            if (_old_Intensity != Intensity) {
                _old_Intensity = Intensity;
                MarkRangeDirtyAndNotify(false, false, false);
            }
        }
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

#if UNITY_EDITOR && !COMPILER_UDONSHARP
        // Caches editor-observed scalar values after the editor coordinator mirrors them without proxy polling.
        public void CacheEditorObservedValues() {
            _old_Color = Color;
            _old_Intensity = Intensity;
            _old_ShadingStrength = ShadingStrength;
        }
#endif

        // Sends this instance change to the manager when it is active.
        private void NotifyManager(bool rebuildFinalData, bool customTexturesChanged, bool shadowTexturesChanged) {
            bool wasActive = IsActive;
            IsActive = enabled && gameObject.activeInHierarchy && Intensity != 0 && Color != Color.black;
            if (!enabled || !gameObject.activeInHierarchy) return;
            RegisterWithManager();
            if (LightVolumeManager == null) return;
            if (wasActive != IsActive) {
                if (CustomTexture != null || CustomTextureMaterial != null) customTexturesChanged = true;
                if (ShadowMapID >= 0) shadowTexturesChanged = true;
            }
            LightVolumeManager.NotifyPointLightVolumeChanged(this, rebuildFinalData, customTexturesChanged, shadowTexturesChanged);
        }

        // Registers once with the scene's single manager.
        private void RegisterWithManager() {
            IsActive = enabled && gameObject.activeInHierarchy && Intensity != 0 && Color != Color.black;
            if (LightVolumeManager == null || !gameObject.activeInHierarchy || !enabled || _isRegisteredWithManager) return;
            _isRegisteredWithManager = true;
            LightVolumeManager.InitializePointLightVolume(this);
        }

        // Removes this instance from the scene's manager.
        private void UnregisterFromManager() {
            _isRegisteredWithManager = false;
            if (LightVolumeManager == null) return;
            bool customTexturesChanged = IsActive && (CustomTexture != null || CustomTextureMaterial != null);
            bool shadowTexturesChanged = IsActive && ShadowMapID >= 0;
            LightVolumeManager.DeinitializePointLightVolume(this, customTexturesChanged, shadowTexturesChanged);
        }

        private void Start() {
#if !UDONSHARP
            if (LightVolumeManager == null) {
                LightVolumeManager = FindObjectOfType<LightVolumeManager>();
            }
#endif
            RegisterWithManager();
            if (BakeInGame && !_inGameBakeStarted) {
                _inGameBakeStarted = true;
                RuntimeShadowBlurSamplePreset = 2;
                RuntimeShadowSphericalBlur = true;
                RuntimeShadowFacesPerFrame = 6;
                RuntimeShadowDirectOutput = false;
                BakeShadows();
            }
        }

        private void OnEnable() {
            RegisterWithManager();
        }

        private void OnDisable() {
            UnregisterFromManager();
            IsActive = false;
        }

        // Releases runtime shadow resources owned by this point light.
        private void OnDestroy() {
            ReleaseRuntimeShadowTextures();
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
            if (_areaCookieAveragePixels.Length != 1) _areaCookieAveragePixels = new Color32[1];
            if (!request.TryGetData(_areaCookieAveragePixels)) {
                LightVolumeManager.CompleteAreaCookieAverageReadback(this, false, Color.clear);
                return;
            }
            LightVolumeManager.CompleteAreaCookieAverageReadback(this, true, _areaCookieAveragePixels[0]);
        }
#else
        // Receives the area-cookie fallback average and sends it back to the manager
        public void OnAsyncGpuReadbackComplete(AsyncGPUReadbackRequest request) {
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
            RegisterWithManager();
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
            UpdateRotation();
            MarkRangeDirtyAndNotify(true, CustomTexture != null || CustomTextureMaterial != null, false);
        }

        // Sets cubemap or cookie projection mode
        public void SetCustomTexture() {
            SetCustomProjectionMode();
            MarkRangeDirtyAndNotify(true, CustomTexture != null || CustomTextureMaterial != null, false);
        }

        // Sets a texture source for this light's custom projection and schedules manager runtime texture cache refresh
        public void SetCustomTexture(Texture texture, bool isCubemap, bool autoUpdate) {
            CustomTexture = texture;
            CustomTextureMaterial = null;
            ProjectionType = 0; // 0: none
            AutoUpdateCustomTexture = false;
            CustomTextureIsCubemap = false;
            CustomTextureHasDepthSlices = false;

            if (texture != null) {
                ProjectionType = 1; // 1: texture
                AutoUpdateCustomTexture = autoUpdate;

                if (isCubemap) {
                    int textureDimension = (int)texture.dimension;
                    if (textureDimension == 4) CustomTextureIsCubemap = true; // 4: TextureDimension.Cube
                    else if (textureDimension == 5) CustomTextureHasDepthSlices = true; // 5: TextureDimension.Tex2DArray
                }

                SetCustomProjectionMode();
            } else {
                SetParametricMode();
            }
            MarkRangeDirtyAndNotify(true, true, false);
        }

        // Sets a material source for this light's custom projection and schedules manager runtime texture cache refresh
        public void SetCustomMaterial(Material material, bool autoUpdate) {
            CustomTexture = null;
            CustomTextureMaterial = material;
            ProjectionType = 0; // 0: none
            AutoUpdateCustomTexture = false;
            CustomTextureIsCubemap = false;
            CustomTextureHasDepthSlices = false;

            if (material != null) {
                ProjectionType = 2; // 2: material
                AutoUpdateCustomTexture = autoUpdate;
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

        // Sets the light into the point light type
        public void SetPointLight() {
            Vector3 position = transform.position;
            if (LightType == 0 && Position == position && ShadowMapUsesCubemap) return;
            bool shadowTexturesChanged = !ShadowMapUsesCubemap && (ShadowMapID >= 0 || ShadowMapTexture != null || ShadowMapMaterial != null);
            LightType = 0; // 0: point
            ShadowMapUsesCubemap = true;
            Position = position;
            UpdateRotation();
            MarkRangeDirtyAndNotify(false, false, shadowTexturesChanged);
        }

        // Sets the light into the spotlight type with both angle and falloff because angle is required to determine falloff
        public void SetSpotLight(float angleDeg, float falloff) {
            float angle = angleDeg * Mathf.Deg2Rad * 0.5f;
            float outerAngleTan = Mathf.Tan(angle);
            float outerAngleCos = Mathf.Cos(angle);
            float coneFalloff = 1f / (Mathf.Cos(angle * (1.0f - Mathf.Clamp01(falloff))) - outerAngleCos);
            Vector3 position = transform.position;
            Vector3 direction = transform.forward;
            Quaternion rotation = Quaternion.Inverse(transform.rotation);
            if (LightType == 1 && Angle == angle && OuterAngleTan == outerAngleTan && Position == position && (ProjectionMode == 2 ? Rotation == rotation : Direction == direction && OuterAngleCos == outerAngleCos && ConeFalloff == coneFalloff)) return;
            LightType = 1; // 1: spot
            Angle = angle;
            OuterAngleTan = outerAngleTan;
            if (ProjectionMode != 2) { // 2: custom cookie or cubemap
                OuterAngleCos = outerAngleCos;
                ConeFalloff = coneFalloff;
            }
            Position = position;
            UpdateRotation();
            MarkRangeDirtyAndNotify(false, false, false);
        }

        // Sets the light into the spotlight type with a specified angle
        public void SetSpotLight(float angleDeg) {
            float angle = angleDeg * Mathf.Deg2Rad * 0.5f;
            float outerAngleTan = Mathf.Tan(angle);
            float outerAngleCos = Mathf.Cos(angle);
            Vector3 position = transform.position;
            Vector3 direction = transform.forward;
            Quaternion rotation = Quaternion.Inverse(transform.rotation);
            if (LightType == 1 && Angle == angle && OuterAngleTan == outerAngleTan && Position == position && (ProjectionMode == 2 ? Rotation == rotation : Direction == direction && OuterAngleCos == outerAngleCos)) return;
            LightType = 1; // 1: spot
            Angle = angle;
            OuterAngleTan = outerAngleTan;
            if (ProjectionMode != 2) { // 2: custom cookie or cubemap
                OuterAngleCos = outerAngleCos;
            }
            Position = position;
            UpdateRotation();
            MarkRangeDirtyAndNotify(false, false, false);
        }
        
        // Sets the light into the area light type
        public void SetAreaLight() {
            bool shadowTexturesChanged = !ShadowMapUsesCubemap && (ShadowMapID >= 0 || ShadowMapTexture != null || ShadowMapMaterial != null);
            LightType = 2; // 2: area
            ShadowMapUsesCubemap = true;
            Position = transform.position;
            Width = Mathf.Max(Mathf.Abs(transform.lossyScale.x), 0.001f);
            Height = Mathf.Max(Mathf.Abs(transform.lossyScale.y), 0.001f);
            UpdateRotation();
            MarkRangeDirtyAndNotify(true, CustomTexture != null || CustomTextureMaterial != null, shadowTexturesChanged);
        }

        // Sets light source color
        public void SetColor(Color color) {
            if (Color == color) return;
            Color = color;
            _old_Color = color;
            MarkRangeDirtyAndNotify(false, false, false);
        }

        // Sets light source intensity
        public void SetIntensity(float intensity) {
            if (Intensity == intensity) return;
            Intensity = intensity;
            _old_Intensity = intensity;
            MarkRangeDirtyAndNotify(false, false, false);
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

        // Hides every renderer below the exclusion roots, including inactive objects.
        private void ApplyExclusionMask() {
            RestoreExclusionMask();
            int rootCount = ExclusionMask != null ? ExclusionMask.Length : 0;
            if (rootCount == 0) return;

            Renderer[][] rendererGroups = new Renderer[rootCount][];
            int rendererCount = 0;
            for (int i = 0; i < rootCount; i++) {
                GameObject root = ExclusionMask[i];
                if (root == null) continue;
                Renderer[] rootRenderers = root.GetComponentsInChildren<Renderer>(true);
                rendererGroups[i] = rootRenderers;
                rendererCount += rootRenderers.Length;
            }

            _shadowExclusionRenderers = new Renderer[rendererCount];
            _shadowExclusionRendererStates = new bool[rendererCount];
            int rendererIndex = 0;
            for (int i = 0; i < rootCount; i++) {
                Renderer[] rootRenderers = rendererGroups[i];
                if (rootRenderers == null) continue;
                for (int j = 0; j < rootRenderers.Length; j++) {
                    Renderer renderer = rootRenderers[j];
                    if (renderer == null) continue;
                    _shadowExclusionRenderers[rendererIndex] = renderer;
                    _shadowExclusionRendererStates[rendererIndex] = renderer.forceRenderingOff;
                    renderer.forceRenderingOff = true;
                    rendererIndex++;
                }
            }
        }

        // Restores the exact renderer states captured by ApplyExclusionMask.
        private void RestoreExclusionMask() {
            int rendererCount = _shadowExclusionRenderers != null ? _shadowExclusionRenderers.Length : 0;
            for (int i = rendererCount - 1; i >= 0; i--) {
                Renderer renderer = _shadowExclusionRenderers[i];
                if (renderer != null) renderer.forceRenderingOff = _shadowExclusionRendererStates[i];
            }
            _shadowExclusionRenderers = new Renderer[0];
            _shadowExclusionRendererStates = new bool[0];
        }

#if UNITY_EDITOR && !COMPILER_UDONSHARP
        // Editor safety net used by the asset baker's finally block if rendering throws unexpectedly.
        public void EditorRestoreExclusionMask() {
            RestoreExclusionMask();
        }
#endif

        // Runs one runtime shadow bake trigger using the current runtime bake options.
        public void BakeShadows() {
            bool rangeChanged = IsRangeDirty;
            int bakeResolution = Mathf.Max(RuntimeShadowResolution, 16);
            LightVolumeManager manager = LightVolumeManager;
            Material depthEncodeMaterial = RuntimeShadowDepthEncodeMaterial;
            int bakeFacesPerFrame = RuntimeShadowFacesPerFrame;
            if (bakeFacesPerFrame <= 1) bakeFacesPerFrame = 1;
            else if (bakeFacesPerFrame <= 2) bakeFacesPerFrame = 2;
            else if (bakeFacesPerFrame <= 3) bakeFacesPerFrame = 3;
            else bakeFacesPerFrame = 6;
            bool useCubemapShadow = LightType != 1 || ShadowMapUsesCubemap; // 1: spot
            int bakeSliceCount = useCubemapShadow ? 6 : 1;
            bool useSphericalBlur = RuntimeShadowSphericalBlur;
            bool useDirectOutput = RuntimeShadowDirectOutput && manager != null && manager.ShadowTexturesWidth == bakeResolution && manager.ShadowTexturesHeight == bakeResolution;
            bool useBlur = Blur > 0.0001f && RuntimeShadowBlurMaterial != null;

            // Validate runtime shadow bake dependencies and cache hot references for this trigger.
            if (!enabled || !gameObject.activeInHierarchy || Intensity == 0f || Color == Color.black || manager == null || depthEncodeMaterial == null) {
                _runtimeShadowFaceIndex = 0;
                ReleaseIdleRuntimeShadowTextures();
                return;
            }
            Camera runtimeShadowCamera = RuntimeShadowCamera;
            if (runtimeShadowCamera == null) {
                _runtimeShadowFaceIndex = 0;
                ReleaseIdleRuntimeShadowTextures();
                return;
            }
            Transform runtimeShadowCameraTransform = runtimeShadowCamera.transform;
            if (!_runtimeShadowShaderPropertiesInitialized) InitializeRuntimeShadowShaderProperties();
            if (rangeChanged) manager.RecalculatePointLightRange(this);

            // Prepare render targets for the selected runtime shadow output path.
            RenderTextureFormat format = manager.ShadowTextureFormat == ShadowTextureFormatHalf ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGBFloat;
            if (_runtimeShadowFaceIndex >= bakeSliceCount) _runtimeShadowFaceIndex = 0;
            EnsureRuntimeShadowDepthTexture(bakeResolution);
            if (useDirectOutput) {
                // Direct output writes final faces straight into the manager atlas, so keep only a tiny registration source for metadata.
                _runtimeShadowRegistrationTexture = EnsureRuntimeShadowOwnedArrayTexture(_runtimeShadowRegistrationTexture, format, 1, bakeSliceCount, FilterMode.Point, true);
                if (_runtimeShadowTexture != null) {
                    if (ShadowMapTexture == _runtimeShadowTexture) ShadowMapTexture = null;
                    ReleaseRuntimeShadowRenderTexture(_runtimeShadowTexture);
                    _runtimeShadowTexture = null;
                }
            } else {
                // Local output keeps a full source array on this light, then copies completed faces to the manager atlas.
                if (_runtimeShadowRegistrationTexture != null) {
                    ReleaseRuntimeShadowRenderTexture(_runtimeShadowRegistrationTexture);
                    _runtimeShadowRegistrationTexture = null;
                    _runtimeShadowFaceIndex = 0;
                }
                _runtimeShadowTexture = EnsureRuntimeShadowOwnedArrayTexture(_runtimeShadowTexture, format, bakeResolution, bakeSliceCount, FilterMode.Bilinear, true);
            }
            if (useBlur) {
                // Blur needs one scratch array matching the active output layout.
                _runtimeShadowBlurTempTexture = EnsureRuntimeShadowOwnedArrayTexture(_runtimeShadowBlurTempTexture, format, bakeResolution, bakeSliceCount, FilterMode.Bilinear, false);
            } else if (_runtimeShadowBlurTempTexture != null) {
                // No-blur path should not keep scratch VRAM alive between bakes.
                ReleaseRuntimeShadowRenderTexture(_runtimeShadowBlurTempTexture);
                _runtimeShadowBlurTempTexture = null;
            }

            // Read current light transform and safe bake parameters.
            Vector3 bakePosition = transform.position;
            Quaternion bakeRotation = transform.rotation;
            float bakeNearClip = Mathf.Max(NearClip, 0.0001f);
            float bakeFarClip = FarClip > 0f ? FarClip : Mathf.Sqrt(Mathf.Max(SquaredRange, 0.000001f));
            bakeFarClip = Mathf.Max(bakeFarClip, bakeNearClip + 0.0001f);
            BakedFarClip = bakeFarClip;
            bool receiverClipChanged = _runtimeShadowReceiverNearClip != bakeNearClip || _runtimeShadowReceiverFarClip != bakeFarClip;
            _runtimeShadowReceiverNearClip = bakeNearClip;
            _runtimeShadowReceiverFarClip = bakeFarClip;
            if (receiverClipChanged) _runtimeShadowFaceIndex = 0;

            // Select the face range only after clip changes have restarted a partial cubemap cycle.
            bool instantBake = !useCubemapShadow || bakeFacesPerFrame >= bakeSliceCount;
            int firstFace = instantBake ? 0 : _runtimeShadowFaceIndex;
            int faceCount = instantBake ? bakeSliceCount : bakeFacesPerFrame;
            int remainingFaces = bakeSliceCount - firstFace;
            if (faceCount > remainingFaces) faceCount = remainingFaces;

            float bakeBias = Mathf.Max(Bias, 0f);
            float bakeFieldOfView;
            float bakeTanHalfFov;
            if (useCubemapShadow) {
                // Cubemap faces always render with a 90-degree projection.
                bakeFieldOfView = 90f;
                bakeTanHalfFov = 1f;
            } else {
                // Single-slice spot shadows use the light cone projection.
                bakeFieldOfView = Mathf.Clamp(Angle * Mathf.Rad2Deg * 2f, 0.1f, 179.9f);
                bakeTanHalfFov = Mathf.Tan(bakeFieldOfView * 0.5f * Mathf.Deg2Rad);
            }
            bool blurUsesUniformRadius = Mathf.Clamp01(ContactHardening) <= 0f;

            // Publish runtime shadow metadata before writing pixels into the selected output.
            bool shadowDataChanged = ApplyRuntimeShadowSourceInternal(bakePosition, bakeRotation, rangeChanged || receiverClipChanged, useDirectOutput, useCubemapShadow);
            bool rebuildShadowArray = !_runtimeShadowSourceInitialized || manager.ShadowTextures == null || manager.ShadowMapsCount <= 0;
            if (rebuildShadowArray) {
                manager.InitializePointLightVolume(this);
                manager.ReinitializeShadowTextures();
                _runtimeShadowSourceInitialized = true;
            }
            if (rebuildShadowArray || shadowDataChanged) manager.RequestUpdateVolumes();
            if (useDirectOutput && (manager.ShadowTextures == null || ShadowMapID < 0)) {
                _runtimeShadowFaceIndex = 0;
                ReleaseIdleRuntimeShadowTextures();
                return;
            }

            // Resolve the output array and base slice that receive rendered shadow faces.
            RenderTexture outputTexture;
            int outputBaseSlice;
            if (useDirectOutput) {
                // Realtime/direct mode writes directly into the manager-owned shadow texture array.
                outputTexture = manager.ShadowTextures;
                int shadowId = (int)ShadowMapID;
                if (shadowId < 0) outputBaseSlice = 0;
                else if (useCubemapShadow) outputBaseSlice = shadowId * 6;
                else {
                    int cubemapCount = manager.ShadowCubemapsCount;
                    outputBaseSlice = cubemapCount * 6 + shadowId - cubemapCount;
                }
            } else {
                // One-shot/local mode writes into this light's runtime source texture first.
                outputTexture = _runtimeShadowTexture;
                outputBaseSlice = 0;
            }

            // Configure per-bake camera projection and culling settings.
            runtimeShadowCamera.fieldOfView = bakeFieldOfView;
            runtimeShadowCamera.nearClipPlane = bakeNearClip;
            runtimeShadowCamera.farClipPlane = bakeFarClip;
            runtimeShadowCamera.cullingMask = LayerMask;

            // Upload current bake constants to runtime shadow materials.
            depthEncodeMaterial.SetFloat(_runtimeShadowFarClipID, bakeFarClip);
            depthEncodeMaterial.SetFloat(_runtimeShadowNearClipID, bakeNearClip);
            depthEncodeMaterial.SetFloat(_runtimeShadowBiasID, bakeBias);
            depthEncodeMaterial.SetFloat(_runtimeShadowTanHalfFovID, bakeTanHalfFov);
            depthEncodeMaterial.SetTexture(_runtimeShadowDepthTextureID, _runtimeShadowDepthTexture, RenderTextureSubElement.Depth);
            if (useBlur) useBlur = PrepareRuntimeShadowBlurMaterial(blurUsesUniformRadius, bakeTanHalfFov, bakeResolution, useCubemapShadow, useSphericalBlur);

            // Render selected faces into the output array using the shared camera. There are deliberately no
            // early returns between Apply and Restore because Udon does not support try/finally.
            Quaternion previousCameraRotation = runtimeShadowCameraTransform.rotation;
            runtimeShadowCameraTransform.position = bakePosition;
            RenderTexture previousTargetTexture = runtimeShadowCamera.targetTexture;
            runtimeShadowCamera.targetTexture = _runtimeShadowDepthTexture;
            ApplyExclusionMask();

            int face = firstFace;
            if (useCubemapShadow) {
                // Point/cubemap shadows render each requested cubemap face with a fixed face rotation.
                for (int i = 0; i < faceCount; i++) {
                    if (face == 0) runtimeShadowCameraTransform.rotation = bakeRotation * _runtimeShadowFaceRotation0;
                    else if (face == 1) runtimeShadowCameraTransform.rotation = bakeRotation * _runtimeShadowFaceRotation1;
                    else if (face == 2) runtimeShadowCameraTransform.rotation = bakeRotation * _runtimeShadowFaceRotation2;
                    else if (face == 3) runtimeShadowCameraTransform.rotation = bakeRotation * _runtimeShadowFaceRotation3;
                    else if (face == 4) runtimeShadowCameraTransform.rotation = bakeRotation * _runtimeShadowFaceRotation4;
                    else runtimeShadowCameraTransform.rotation = bakeRotation;

                    runtimeShadowCamera.Render();
                    BlitRuntimeShadowMaterialToSlice(_runtimeShadowDepthTexture, depthEncodeMaterial, 0, outputTexture, outputBaseSlice + face);
                    face++;
                }
            } else {
                // Single-slice spot shadows render one projection using the light rotation.
                runtimeShadowCameraTransform.rotation = bakeRotation;
                runtimeShadowCamera.Render();
                BlitRuntimeShadowMaterialToSlice(_runtimeShadowDepthTexture, depthEncodeMaterial, 0, outputTexture, outputBaseSlice);
            }

            RestoreExclusionMask();
            runtimeShadowCamera.targetTexture = previousTargetTexture;
            runtimeShadowCameraTransform.rotation = previousCameraRotation;

            // Finish this trigger and publish local-output slices when this is a real runtime source.
            bool cycleComplete = instantBake || face >= bakeSliceCount;
            _runtimeShadowFaceIndex = cycleComplete ? 0 : face;
            if (useBlur) {
                // Blur is applied only after a full cycle so every face has matching source data.
                if (cycleComplete) BlurRuntimeShadowFaces(bakeSliceCount, blurUsesUniformRadius, useDirectOutput, !useDirectOutput, outputTexture, outputBaseSlice, useSphericalBlur);
            } else if (!useDirectOutput) {
                // Without blur, local-output faces can be copied to the manager immediately.
                int copyFirstFace = cycleComplete ? 0 : firstFace;
                int copyFaceCount = cycleComplete ? bakeSliceCount : faceCount;
                for (int i = 0; i < copyFaceCount; i++) manager.UpdatePointLightShadowTextureSlice(this, copyFirstFace + i);
            }

            if (cycleComplete && !useDirectOutput) ReleaseIdleRuntimeShadowTextures();
        }

        // Creates or validates the camera depth render target.
        private void EnsureRuntimeShadowDepthTexture(int resolution) {
            if (_runtimeShadowDepthTexture != null && _runtimeShadowDepthTexture.width == resolution && _runtimeShadowDepthTexture.height == resolution
#if !COMPILER_UDONSHARP
                && _runtimeShadowDepthTexture.format == RenderTextureFormat.Depth
#endif
                ) return;

            _runtimeShadowFaceIndex = 0;
            ReleaseRuntimeShadowRenderTexture(_runtimeShadowDepthTexture);
            _runtimeShadowDepthTexture = new RenderTexture(resolution, resolution, 32, RenderTextureFormat.Depth, RenderTextureReadWrite.Linear);
            _runtimeShadowDepthTexture.dimension = TextureDimension.Tex2D;
            _runtimeShadowDepthTexture.useMipMap = false;
            _runtimeShadowDepthTexture.autoGenerateMips = false;
            _runtimeShadowDepthTexture.wrapMode = TextureWrapMode.Clamp;
            _runtimeShadowDepthTexture.filterMode = FilterMode.Point;
            _runtimeShadowDepthTexture.anisoLevel = 0;
#if !COMPILER_UDONSHARP
            _runtimeShadowDepthTexture.hideFlags = HideFlags.HideAndDontSave;
#endif
            _runtimeShadowDepthTexture.Create();
        }

        // Reuses or recreates a locally-owned runtime shadow texture array.
        private RenderTexture EnsureRuntimeShadowOwnedArrayTexture(RenderTexture texture, RenderTextureFormat format, int resolution, int sliceCount, FilterMode filterMode, bool resetBakeCycle) {
            if (texture != null && texture.width == resolution && texture.height == resolution && texture.volumeDepth == sliceCount
#if !COMPILER_UDONSHARP
                && texture.format == format
#endif
                ) return texture;

            if (resetBakeCycle) _runtimeShadowFaceIndex = 0;
            ReleaseRuntimeShadowRenderTexture(texture);
            texture = new RenderTexture(resolution, resolution, 0, format, RenderTextureReadWrite.Linear);
            texture.dimension = TextureDimension.Tex2DArray;
            texture.volumeDepth = sliceCount;
            texture.useMipMap = false;
            texture.autoGenerateMips = false;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = filterMode;
            texture.anisoLevel = 0;
#if !COMPILER_UDONSHARP
            texture.hideFlags = HideFlags.HideAndDontSave;
#endif
            texture.Create();
            return texture;
        }

        // Updates this light's runtime shadow source and returns whether shader metadata changed.
        private bool ApplyRuntimeShadowSourceInternal(Vector3 bakePosition, Quaternion bakeRotation, bool rangeChanged, bool useDirectOutput, bool useCubemapShadow) {
            Texture sourceTexture = useDirectOutput ? _runtimeShadowRegistrationTexture : _runtimeShadowTexture;
            bool sourceIsCubemap = sourceTexture != null && sourceTexture.dimension == TextureDimension.Cube;
            bool sourceHasSlices = sourceTexture != null && sourceTexture.dimension == TextureDimension.Tex2DArray && useCubemapShadow;
            bool sourceChanged = ShadowMapID < 0 || ShadowMapTexture != sourceTexture || ShadowMapMaterial != null || AutoUpdateShadowMap || ShadowMapTextureIsCubemap != sourceIsCubemap || ShadowMapTextureHasDepthSlices != sourceHasSlices || ShadowMapUsesCubemap != useCubemapShadow;
            // Runtime shadow metadata must match the exact transform used by this bake. Unity's
            // Vector3/Quaternion operators are approximate, which can otherwise retain a nearby
            // stale origin/rotation and prevent the exact same-origin receiver path from engaging.
            bool bakePositionChanged = ShadowBakePosition.x != bakePosition.x || ShadowBakePosition.y != bakePosition.y || ShadowBakePosition.z != bakePosition.z;
            bool bakeRotationChanged = ShadowBakeRotation.x != bakeRotation.x || ShadowBakeRotation.y != bakeRotation.y || ShadowBakeRotation.z != bakeRotation.z || ShadowBakeRotation.w != bakeRotation.w;
            bool metadataChanged = sourceChanged || rangeChanged || (WorldSpaceShadows && (bakePositionChanged || bakeRotationChanged));

            if (ShadowMapID < 0) ShadowMapID = 0f;
            if (sourceChanged) {
                ShadowMapTexture = sourceTexture;
                ShadowMapMaterial = null;
                AutoUpdateShadowMap = false;
                ShadowMapTextureIsCubemap = sourceIsCubemap;
                ShadowMapTextureHasDepthSlices = sourceHasSlices;
                ShadowMapUsesCubemap = useCubemapShadow;
                _runtimeShadowSourceInitialized = false;
            }
            if (bakePositionChanged) ShadowBakePosition = bakePosition;
            if (bakeRotationChanged) ShadowBakeRotation = bakeRotation;
            return metadataChanged;
        }

        // Applies the selected runtime blur path to the requested shadow slices.
        private void BlurRuntimeShadowFaces(int sliceCount, bool blurUsesUniformRadius, bool useDirectOutput, bool copyToManager, RenderTexture outputTexture, int outputBaseSlice, bool useSphericalBlur) {
            Material blurMaterial = RuntimeShadowBlurMaterial;
            if (outputTexture == null || _runtimeShadowBlurTempTexture == null || blurMaterial == null) return;
            if (useSphericalBlur) {
                // Spherical blur samples across cubemap/spot projection space in one pass, reducing visible seams.
                blurMaterial.SetTexture(_runtimeShadowSourceArrayID, outputTexture);
                blurMaterial.SetFloat(_runtimeShadowSourceBaseSliceID, outputBaseSlice);
                if (!blurUsesUniformRadius) {
                    // Contact hardening uses the unblurred depth source to vary blur width by receiver depth.
                    blurMaterial.SetTexture(_runtimeShadowDepthArrayID, outputTexture);
                    blurMaterial.SetFloat(_runtimeShadowDepthBaseSliceID, outputBaseSlice);
                }

                // Write blurred faces into the scratch array at zero-based slice indices.
                for (int face = 0; face < sliceCount; face++) {
                    blurMaterial.SetInt(_runtimeShadowFaceIndexID, face);
                    BlitRuntimeShadowMaterialToSlice(outputTexture, blurMaterial, 0, _runtimeShadowBlurTempTexture, face);
                }

                // Copy scratch slices back to either local output or the manager atlas base slice.
                int targetBaseSlice = useDirectOutput ? outputBaseSlice : 0;
                for (int face = 0; face < sliceCount; face++) {
                    VRCGraphics.Blit(_runtimeShadowBlurTempTexture, outputTexture, face, targetBaseSlice + face);
                }
            } else {
                // Planar blur is cheaper: horizontal pass into scratch, then vertical pass back to output.
                blurMaterial.SetTexture(_runtimeShadowSourceArrayID, outputTexture);
                blurMaterial.SetFloat(_runtimeShadowSourceBaseSliceID, outputBaseSlice);
                blurMaterial.SetVector(_runtimeShadowBlurDirectionID, Vector2.right);
                if (!blurUsesUniformRadius) {
                    // Contact hardening in planar mode uses the same source depth for the horizontal pass.
                    blurMaterial.SetTexture(_runtimeShadowDepthArrayID, outputTexture);
                    blurMaterial.SetFloat(_runtimeShadowDepthBaseSliceID, outputBaseSlice);
                }

                // Horizontal pass writes each requested face into the scratch array.
                for (int face = 0; face < sliceCount; face++) {
                    blurMaterial.SetInt(_runtimeShadowFaceIndexID, face);
                    BlitRuntimeShadowMaterialToSlice(outputTexture, blurMaterial, 0, _runtimeShadowBlurTempTexture, face);
                }

                blurMaterial.SetTexture(_runtimeShadowSourceArrayID, _runtimeShadowBlurTempTexture);
                blurMaterial.SetFloat(_runtimeShadowSourceBaseSliceID, 0);
                blurMaterial.SetVector(_runtimeShadowBlurDirectionID, Vector2.up);
                if (!blurUsesUniformRadius) {
                    // Vertical pass samples the horizontally blurred depth scratch for contact hardening.
                    blurMaterial.SetTexture(_runtimeShadowDepthArrayID, _runtimeShadowBlurTempTexture);
                    blurMaterial.SetFloat(_runtimeShadowDepthBaseSliceID, 0);
                }

                // Vertical pass writes final blurred faces to local output or direct atlas slices.
                int targetBaseSlice = useDirectOutput ? outputBaseSlice : 0;
                for (int face = 0; face < sliceCount; face++) {
                    blurMaterial.SetInt(_runtimeShadowFaceIndexID, face);
                    BlitRuntimeShadowMaterialToSlice(_runtimeShadowBlurTempTexture, blurMaterial, 0, outputTexture, targetBaseSlice + face);
                }
            }

            LightVolumeManager manager = LightVolumeManager;
            if (copyToManager && manager != null) {
                // Local-output blur must publish the finished faces to the manager atlas after blur completes.
                for (int face = 0; face < sliceCount; face++) manager.UpdatePointLightShadowTextureSlice(this, face);
            }
        }

        // Prepares blur material constants and keyword state.
        private bool PrepareRuntimeShadowBlurMaterial(bool blurUsesUniformRadius, float tanHalfFov, int bakeResolution, bool useCubemapShadow, bool useSphericalBlur) {
            Material blurMaterial = RuntimeShadowBlurMaterial;
            if (blurMaterial == null) return false;

            // Clamp public blur settings only at material upload time.
            float blurRadius = Mathf.Max(Blur, 0f);
            float blurDepth = Mathf.Clamp01(ContactHardening);

            // Convert public bake settings to local shader keyword state.
            int qualityPreset = RuntimeShadowBlurSamplePreset;
            if (qualityPreset <= 0) qualityPreset = 0;
            else if (qualityPreset >= 3) qualityPreset = 3;
            else if (qualityPreset >= 2) qualityPreset = 2;
            else qualityPreset = 1;
            int uniformKeyword = blurUsesUniformRadius ? 1 : 0;
            int directKeyword = !useCubemapShadow ? 1 : 0;
            int sphericalKeyword = useSphericalBlur ? 1 : 0;
            LightVolumeManager sharedMaterialManager = LightVolumeManager;
            bool useSharedBlurState = sharedMaterialManager != null && blurMaterial == sharedMaterialManager.RuntimeShadowBlurMaterial;
            bool keywordStateChanged = true;
            if (useSharedBlurState) keywordStateChanged = sharedMaterialManager.RuntimeShadowBlurQualityPreset != qualityPreset || sharedMaterialManager.RuntimeShadowBlurUniformKeyword != uniformKeyword
                || sharedMaterialManager.RuntimeShadowBlurDirectKeyword != directKeyword || sharedMaterialManager.RuntimeShadowBlurSphericalKeyword != sphericalKeyword;
            if (keywordStateChanged) {
                // Shared manager material tracks keyword state globally; local material always reapplies it.
                blurMaterial.DisableKeyword(ShadowQualityKeywordLow);
                blurMaterial.DisableKeyword(ShadowQualityKeywordMedium);
                blurMaterial.DisableKeyword(ShadowQualityKeywordHigh);
                blurMaterial.DisableKeyword(ShadowQualityKeywordEditor);
                if (qualityPreset == 0) blurMaterial.EnableKeyword(ShadowQualityKeywordLow);
                else if (qualityPreset == 3) {
                    blurMaterial.EnableKeyword(ShadowQualityKeywordHigh);
                    blurMaterial.EnableKeyword(ShadowQualityKeywordEditor);
                }
                else if (qualityPreset == 2) blurMaterial.EnableKeyword(ShadowQualityKeywordHigh);
                else blurMaterial.EnableKeyword(ShadowQualityKeywordMedium);

                if (blurUsesUniformRadius) blurMaterial.EnableKeyword(ShadowBlurKeywordUniform);
                else blurMaterial.DisableKeyword(ShadowBlurKeywordUniform);

                if (!useCubemapShadow) blurMaterial.EnableKeyword(ShadowBlurKeywordDirect);
                else blurMaterial.DisableKeyword(ShadowBlurKeywordDirect);

                if (useSphericalBlur) blurMaterial.EnableKeyword(ShadowBlurKeywordSpherical);
                else blurMaterial.DisableKeyword(ShadowBlurKeywordSpherical);

                if (useSharedBlurState) {
                    sharedMaterialManager.RuntimeShadowBlurQualityPreset = qualityPreset;
                    sharedMaterialManager.RuntimeShadowBlurUniformKeyword = uniformKeyword;
                    sharedMaterialManager.RuntimeShadowBlurDirectKeyword = directKeyword;
                    sharedMaterialManager.RuntimeShadowBlurSphericalKeyword = sphericalKeyword;
                }
            }

            // Upload blur constants after keywords select planar/spherical/direct shader code.
            blurMaterial.SetFloat(_runtimeShadowBlurRadiusID, blurRadius * (Mathf.Max(bakeResolution, 1) / ShadowBlurBaseResolution));
            if (blurUsesUniformRadius) blurMaterial.SetFloat(_runtimeShadowBlurDepthID, 0f);
            else {
                // Contact hardening is exponential so low values stay subtle while high values expand quickly.
                blurMaterial.SetFloat(_runtimeShadowBlurDepthID, (Mathf.Pow(10f, blurDepth) - 1f) * 0.1111111111f);
            }
            blurMaterial.SetFloat(_runtimeShadowInvResolutionID, 1f / bakeResolution);
            // Single-slice spot blur needs projection scale compensation; cubemap blur does not.
            if (!useCubemapShadow) blurMaterial.SetFloat(_runtimeShadowTanHalfFovID, tanHalfFov);
            return true;
        }

        // Initializes all shader property IDs used by runtime shadow materials.
        private void InitializeRuntimeShadowShaderProperties() {
            _runtimeShadowDepthTextureID = VRCShader.PropertyToID("_ShadowDepthTex");
            _runtimeShadowFarClipID = VRCShader.PropertyToID("_ShadowFarClip");
            _runtimeShadowNearClipID = VRCShader.PropertyToID("_ShadowNearClip");
            _runtimeShadowBiasID = VRCShader.PropertyToID("_ShadowBakeBias");
            _runtimeShadowTanHalfFovID = VRCShader.PropertyToID("_ShadowTanHalfFov");
            _runtimeShadowSourceArrayID = VRCShader.PropertyToID("_SourceArrayTex");
            _runtimeShadowDepthArrayID = VRCShader.PropertyToID("_DepthArrayTex");
            _runtimeShadowFaceIndexID = VRCShader.PropertyToID("_FaceIndex");
            _runtimeShadowSourceBaseSliceID = VRCShader.PropertyToID("_SourceBaseSlice");
            _runtimeShadowDepthBaseSliceID = VRCShader.PropertyToID("_DepthBaseSlice");
            _runtimeShadowBlurDirectionID = VRCShader.PropertyToID("_BlurDirection");
            _runtimeShadowBlurRadiusID = VRCShader.PropertyToID("_BlurRadius");
            _runtimeShadowBlurDepthID = VRCShader.PropertyToID("_BlurDepth");
            _runtimeShadowInvResolutionID = VRCShader.PropertyToID("_InvResolution");
            _runtimeShadowShaderPropertiesInitialized = true;
        }

        // Renders one material pass into a destination texture-array slice.
        private void BlitRuntimeShadowMaterialToSlice(Texture sourceTexture, Material material, int pass, RenderTexture destination, int targetSlice) {
            if (material == null || destination == null) return;
#if COMPILER_UDONSHARP
            if (_runtimeShadowMaterialBlitInputTexture == null) {
                _runtimeShadowMaterialBlitInputTexture = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                _runtimeShadowMaterialBlitInputTexture.dimension = TextureDimension.Tex2D;
                _runtimeShadowMaterialBlitInputTexture.useMipMap = false;
                _runtimeShadowMaterialBlitInputTexture.autoGenerateMips = false;
                _runtimeShadowMaterialBlitInputTexture.Create();
            }
            Texture blitSource = _runtimeShadowMaterialBlitInputTexture;
            VRCGraphics.Blit(blitSource, destination, 0, targetSlice);
            VRCGraphics.Blit(blitSource, material, pass, targetSlice);
#else
            RenderTexture previousRenderTexture = RenderTexture.active;
            VRCGraphics.SetRenderTarget(destination, 0, CubemapFace.Unknown, targetSlice);
            VRCGraphics.Blit(sourceTexture, material, pass);
            RenderTexture.active = previousRenderTexture == destination ? null : previousRenderTexture;
#endif
        }

        // Releases one runtime shadow render texture before replacing it.
        private void ReleaseRuntimeShadowRenderTexture(RenderTexture texture) {
            if (texture == null) return;
#if COMPILER_UDONSHARP
            Destroy(texture);
#else
            RenderTexture.active = null;
            texture.Release();
            if (Application.isPlaying) Destroy(texture);
            else DestroyImmediate(texture);
#endif
        }

        // Releases temporary per-trigger bake buffers while keeping the published shadow source alive.
        private void ReleaseIdleRuntimeShadowTextures() {
            ReleaseRuntimeShadowRenderTexture(_runtimeShadowDepthTexture);
            ReleaseRuntimeShadowRenderTexture(_runtimeShadowBlurTempTexture);
            ReleaseRuntimeShadowRenderTexture(_runtimeShadowMaterialBlitInputTexture);
            _runtimeShadowDepthTexture = null;
            _runtimeShadowBlurTempTexture = null;
            _runtimeShadowMaterialBlitInputTexture = null;
        }

        // Releases all locally-owned runtime shadow textures.
        private void ReleaseRuntimeShadowTextures() {
            if (ShadowMapTexture == _runtimeShadowTexture || ShadowMapTexture == _runtimeShadowRegistrationTexture) ShadowMapTexture = null;
            ReleaseRuntimeShadowRenderTexture(_runtimeShadowDepthTexture);
            ReleaseRuntimeShadowRenderTexture(_runtimeShadowTexture);
            ReleaseRuntimeShadowRenderTexture(_runtimeShadowRegistrationTexture);
            ReleaseRuntimeShadowRenderTexture(_runtimeShadowBlurTempTexture);
            ReleaseRuntimeShadowRenderTexture(_runtimeShadowMaterialBlitInputTexture);
            _runtimeShadowDepthTexture = null;
            _runtimeShadowTexture = null;
            _runtimeShadowRegistrationTexture = null;
            _runtimeShadowBlurTempTexture = null;
            _runtimeShadowMaterialBlitInputTexture = null;
            _runtimeShadowFaceIndex = 0;
            _runtimeShadowSourceInitialized = false;
        }

        // Marks this light range dirty and tells the manager which runtime data needs rebuilding.
        private void MarkRangeDirtyAndNotify(bool rebuildFinalData, bool customTexturesChanged, bool shadowTexturesChanged) {
            IsRangeDirty = true;
            NotifyManager(rebuildFinalData, customTexturesChanged, shadowTexturesChanged);
        }

        // Applies the internal custom projection mode without touching texture source fields
        private void SetCustomProjectionMode() {
            ProjectionMode = 2; // 2: custom cookie or cubemap
            if (LightType == 1) OuterAngleTan = Mathf.Tan(Angle); // 1: spot
            UpdateRotation();
        }

        // Applies the internal parametric projection mode without touching texture source fields
        private void SetParametricMode() {
            ProjectionMode = 0; // 0: parametric
            if (LightType == 1) OuterAngleTan = Mathf.Tan(Angle); // 1: spot
            OuterAngleCos = Mathf.Cos(Angle);
            UpdateRotation();
        }

        // Updates data required for shader
        public void UpdateTransform() {

            // Position Update
            Vector3 pos = transform.position;
            if (_prevPosition != pos) {
                _prevPosition = pos;
                UpdatePosition();
            }

            // Rotation Update
            Quaternion rot = transform.rotation;
            if (_prevRotation != rot) {
                _prevRotation = rot;
                UpdateRotation();
            }

            // Scale Update
            Vector3 lscale = transform.lossyScale;
            if (_prevScale != lscale) {
                _prevScale = lscale;
                UpdateScale();
            }
              
        }

        // Force update position
        public void UpdatePosition() {
            Position = transform.position;
            NotifyManager(false, false, false);
        }

        // Resolves the Area Cookie X/Y reflection relative to the quaternion frame sent to shaders.
        private void RefreshAreaCookieMirror(Quaternion transformRotation) {
            Matrix4x4 localToWorldMatrix = transform.localToWorldMatrix;
            Vector3 matrixXAxis = new Vector3(localToWorldMatrix.m00, localToWorldMatrix.m10, localToWorldMatrix.m20);
            Vector3 matrixYAxis = new Vector3(localToWorldMatrix.m01, localToWorldMatrix.m11, localToWorldMatrix.m21);
            bool flipCookieX = Vector3.Dot(matrixXAxis, transformRotation * Vector3.right) < 0f;
            bool flipCookieY = Vector3.Dot(matrixYAxis, transformRotation * Vector3.up) < 0f;
            AreaCookieMirror = (flipCookieY ? 2f : 1f) * (flipCookieX ? -1f : 1f);
        }
        
        // Force update rotation
        public void UpdateRotation() {
            Quaternion rot = transform.rotation;
            if (LightType == 2) { // 2: area
                Rotation = rot;
                RefreshAreaCookieMirror(rot);
            } else if (LightType == 1 && ProjectionMode != 2) { // 1: spot, 2: custom cookie
                Direction = transform.forward;
            } else if (ProjectionMode != 0) { // 0: parametric; non-parametric point/cookie uses inverse rotation
                rot = Quaternion.Inverse(rot);
                Rotation = rot;
            }
            NotifyManager(false, false, false);
        }

        // Force update scale
        public void UpdateScale() {
            Vector3 lscale = transform.lossyScale;
            if (LightType == 2) { // 2: area
                Width = Mathf.Max(Mathf.Abs(lscale.x), 0.001f);
                Height = Mathf.Max(Mathf.Abs(lscale.y), 0.001f);
                UpdateRotation();
            }
            float averageScale = (Mathf.Abs(lscale.x) + Mathf.Abs(lscale.y) + Mathf.Abs(lscale.z)) / 3;
            SquaredScale = averageScale * averageScale;
            MarkRangeDirtyAndNotify(false, false, false);
        }


#if UNITY_EDITOR && !COMPILER_UDONSHARP
        // Returns the persistent source selected by the authoring projection mode.
        public UnityEngine.Object GetProjectionSource() {
            if (LightType == 2) return Cookie; // 2: area
            if (Projection == 1) return FalloffLUT; // 1: LUT
            if (Projection != 2) return null; // 2: custom
            if (LightType == 0) return Cubemap; // 0: point
            if (LightType == 1) return Cookie; // 1: spot
            return null;
        }

        public Texture GetCustomTexture() {
            return GetProjectionSource() as Texture;
        }

        public Material GetCustomTextureMaterial() {
            return GetProjectionSource() as Material;
        }

        public int GetProjectionType() {
            UnityEngine.Object source = GetProjectionSource();
            if (!HasProjectionSource()) return 0;
            if (source is Texture) return 1;
            if (source is Material) return 2;
            return 0;
        }

        public bool HasProjectionSource() {
            UnityEngine.Object source = GetProjectionSource();
            if (source is Material) return true;
            if (!(source is Texture)) return false;
            if (LightType == 2 || Projection == 1) return true;
            return Projection == 2 && (LightType == 0 || LightType == 1);
        }

        public bool UsesCubemapProjection() {
            return LightType == 0 && Projection == 2 && HasProjectionSource();
        }

        private int GetAuthoringProjectionMode() {
            if (!HasProjectionSource()) return 0;
            if (LightType == 2) return 2;
            return Projection == 1 ? 1 : Projection == 2 ? 2 : 0;
        }

        private static bool IsAnimatedEditorSource(UnityEngine.Object source) {
            return source is RenderTexture || source is Material;
        }

        private static bool IsEditorCubemapTexture(Texture texture) {
            if (texture is Cubemap) return true;
            RenderTexture renderTexture = texture as RenderTexture;
            return renderTexture != null && renderTexture.dimension == TextureDimension.Cube;
        }

        private static bool EditorTextureHasDepthSlices(Texture texture) {
            RenderTexture renderTexture = texture as RenderTexture;
            if (renderTexture != null) return renderTexture.dimension == TextureDimension.Tex2DArray && renderTexture.volumeDepth > 1;
            return texture is Texture2DArray;
        }

        public Texture GetShadowMapTexture() {
            return ShadowMap as Texture;
        }

        public Material GetShadowMapMaterial() {
            return ShadowMap as Material;
        }

        public bool HasShadowMapSource() {
            return ShadowMap is Texture || ShadowMap is Material;
        }

        public bool ShouldBakeCubemapShadows() {
            return LightType != 1 || ForceCubemapShadows || Angle >= Mathf.PI; // 1: spot
        }

        public bool UsesCubemapShadows() {
            Texture texture = GetShadowMapTexture();
            if (IsEditorCubemapTexture(texture) || EditorTextureHasDepthSlices(texture)) return true;
            return ShouldBakeCubemapShadows();
        }

        public float GetShadowNearClip() {
            return Mathf.Max(NearClip, 0.0001f);
        }

        public float GetShadowFarClip() {
            float nearClip = GetShadowNearClip();
            float farClip = FarClip > 0f ? FarClip : GetCalculatedShadowFarClip();
            return Mathf.Max(farClip, nearClip + 0.0001f);
        }

        private float GetCalculatedShadowFarClip() {
            Vector3 lossyScale = transform.lossyScale;
            float averageScale = (Mathf.Abs(lossyScale.x) + Mathf.Abs(lossyScale.y) + Mathf.Abs(lossyScale.z)) / 3f;
            float cutoff = LightVolumeManager != null ? LightVolumeManager.LightsBrightnessCutoff : 0.35f;

            if (LightType == 2) {
                float width = Mathf.Max(Mathf.Abs(lossyScale.x), 0.001f);
                float height = Mathf.Max(Mathf.Abs(lossyScale.y), 0.001f);
                return Mathf.Max(Mathf.Sqrt(ComputeEditorAreaLightSquaredRange(width, height, Color, Intensity * Mathf.PI, cutoff)), 0.0001f);
            }
            if (Projection == 1 && HasProjectionSource()) return Mathf.Max(Range * averageScale, 0.0001f);

            float size = Mathf.Max(LightSourceSize * averageScale, 0.0001f);
            float luminance = Mathf.Max(Color.r, Mathf.Max(Color.g, Color.b));
            float squaredRange = Mathf.Max(Mathf.PI * 2f * luminance * Mathf.Abs(Intensity) / (cutoff * cutoff) - 1f, 0f) * size * size;
            return Mathf.Max(Mathf.Sqrt(squaredRange), 0.0001f);
        }

        private static float ComputeEditorAreaLightSquaredRange(float width, float height, Color color, float intensity, float cutoff) {
            float luminance = Mathf.Max(color.r, Mathf.Max(color.g, color.b)) * Mathf.Abs(intensity);
            if (luminance <= 0.000001f) return 0f;

            float minSolidAngle = cutoff / luminance;
            if (minSolidAngle >= Mathf.PI * 2f - 0.0001f) return 0f;
            minSolidAngle = Mathf.Max(minSolidAngle, 0.000001f);

            float area = width * height;
            float shape = 0.25f * (width * width + height * height);
            float tangent = Mathf.Tan(0.25f * minSolidAngle);
            float tangentSquared = Mathf.Max(tangent * tangent, 0.000001f);
            float scaledShape = tangentSquared * shape;
            float discriminant = Mathf.Sqrt(scaledShape * scaledShape + 4f * tangentSquared * area * area);
            return Mathf.Max((discriminant - scaledShape) * 0.125f / tangentSquared, 0f);
        }

        private bool PreserveRuntimeShadowSourceInEditor() {
            if (!Application.isPlaying || !Shadows) return false;
            Texture sourceTexture = GetShadowMapTexture();
            if (BakeInGame) return ShadowMapTexture != sourceTexture;
            return ShadowMapTexture != null && ShadowMapTexture != sourceTexture;
        }

        public bool HasEditorCustomTextureChanges() {
            Texture texture = GetCustomTexture();
            Material material = GetCustomTextureMaterial();
            int mode = GetAuthoringProjectionMode();
            int type = GetProjectionType();
            return CustomTexture != texture || CustomTextureMaterial != material || ProjectionMode != mode || ProjectionType != type
                || CustomTextureIsCubemap != IsEditorCubemapTexture(texture) || CustomTextureHasDepthSlices != EditorTextureHasDepthSlices(texture);
        }

        public bool HasEditorShadowTextureChanges() {
            if (PreserveRuntimeShadowSourceInEditor()) return ShadowMapUsesCubemap != ShouldBakeCubemapShadows();

            Texture texture = Shadows ? GetShadowMapTexture() : null;
            Material material = Shadows ? GetShadowMapMaterial() : null;
            bool usesCubemap = Shadows && UsesCubemapShadows();
            return ShadowMapTexture != texture || ShadowMapMaterial != material || ShadowMapUsesCubemap != usesCubemap || ShadowMapTextureIsCubemap != IsEditorCubemapTexture(texture)
                || ShadowMapTextureHasDepthSlices != EditorTextureHasDepthSlices(texture) || AutoUpdateShadowMap != (Shadows && IsAnimatedEditorSource(ShadowMap));
        }

        // Rebuilds all shader-facing data from the single serialized authoring/runtime component.
        // The editor coordinator calls this only for explicit or coalesced changes; it never polls per object.
        public void EditorApplyAuthoringData(bool customTexturesChanged, bool shadowTexturesChanged, bool notifyManager = true) {
            int safeLightType = Mathf.Clamp(LightType, 0, 2);
            int projectionMode = GetAuthoringProjectionMode();
            float safeSourceSize = Mathf.Max(Mathf.Abs(LightSourceSize), 0.0001f);
            float safeRange = Mathf.Max(Mathf.Abs(Range), 0.0001f);
            float safeAngle = Mathf.Clamp(Angle, 0.05f * Mathf.Deg2Rad, Mathf.PI);
            float safeFalloff = Mathf.Clamp(Falloff, 0.001f, 1f);
            float safeAspect = Mathf.Max(Mathf.Abs(SpotCookieAspect), 0.001f);

            LightType = safeLightType;
            ProjectionMode = projectionMode;
            LightSourceSize = safeSourceSize;
            InverseSquaredRange = 1f / ((projectionMode == 1 ? safeRange : safeSourceSize) * (projectionMode == 1 ? safeRange : safeSourceSize));
            Angle = safeAngle;
            SpotCookieAspect = safeAspect;
            ShadingStrength = Mathf.Clamp01(ShadingStrength);

            Texture customTexture = GetCustomTexture();
            Material customMaterial = GetCustomTextureMaterial();
            CustomTexture = customTexture;
            CustomTextureMaterial = customMaterial;
            ProjectionType = GetProjectionType();
            if (customTexturesChanged) AutoUpdateCustomTexture = IsAnimatedEditorSource(GetProjectionSource());
            CustomTextureIsCubemap = IsEditorCubemapTexture(customTexture);
            CustomTextureHasDepthSlices = EditorTextureHasDepthSlices(customTexture);

            bool preserveRuntimeShadow = PreserveRuntimeShadowSourceInEditor();
            if (!preserveRuntimeShadow) {
                Texture shadowTexture = Shadows ? GetShadowMapTexture() : null;
                Material shadowMaterial = Shadows ? GetShadowMapMaterial() : null;
                bool shadowSourceChanged = ShadowMapTexture != shadowTexture || ShadowMapMaterial != shadowMaterial;
                ShadowMapTexture = shadowTexture;
                ShadowMapMaterial = shadowMaterial;
                AutoUpdateShadowMap = Shadows && IsAnimatedEditorSource(ShadowMap);
                ShadowMapTextureIsCubemap = IsEditorCubemapTexture(shadowTexture);
                ShadowMapTextureHasDepthSlices = EditorTextureHasDepthSlices(shadowTexture);
                ShadowMapUsesCubemap = Shadows && UsesCubemapShadows();
                ShadowMapID = Shadows && (shadowTexture != null || shadowMaterial != null) ? 0f : -1f;
                if (shadowSourceChanged) {
                    ShadowBakePosition = transform.position;
                    ShadowBakeRotation = transform.rotation;
                }
            }

            RuntimeShadowResolution = LightVolumeManager != null ? Mathf.Max(LightVolumeManager.ShadowTexturesWidth, 16) : Mathf.Max(RuntimeShadowResolution, 16);
            RuntimeShadowBlurSamplePreset = 2;
            RuntimeShadowSphericalBlur = true;
            RuntimeShadowFacesPerFrame = 6;
            RuntimeShadowDirectOutput = false;

            Position = transform.position;
            Vector3 lossyScale = transform.lossyScale;
            float averageScale = (Mathf.Abs(lossyScale.x) + Mathf.Abs(lossyScale.y) + Mathf.Abs(lossyScale.z)) / 3f;
            SquaredScale = averageScale * averageScale;
            if (safeLightType == 2) {
                Rotation = transform.rotation;
                Width = Mathf.Max(Mathf.Abs(lossyScale.x), 0.001f);
                Height = Mathf.Max(Mathf.Abs(lossyScale.y), 0.001f);
                RefreshAreaCookieMirror(transform.rotation);
                ShadowMapUsesCubemap = Shadows;
            } else if (safeLightType == 1) {
                OuterAngleTan = Mathf.Tan(safeAngle);
                OuterAngleCos = Mathf.Cos(safeAngle);
                float denominator = Mathf.Cos(safeAngle * (1f - safeFalloff)) - OuterAngleCos;
                ConeFalloff = 1f / Mathf.Max(denominator, 0.000001f);
                if (projectionMode == 2) Rotation = Quaternion.Inverse(transform.rotation);
                else Direction = transform.forward;
            } else if (projectionMode != 0) {
                Rotation = Quaternion.Inverse(transform.rotation);
                ShadowMapUsesCubemap = Shadows;
            }

            _prevPosition = transform.position;
            _prevRotation = transform.rotation;
            _prevScale = lossyScale;
            _old_Color = Color;
            _old_Intensity = Intensity;
            _old_ShadingStrength = ShadingStrength;
            IsRangeDirty = true;
            if (notifyManager) NotifyManager(true, customTexturesChanged, shadowTexturesChanged);
        }
#endif
    }

}
