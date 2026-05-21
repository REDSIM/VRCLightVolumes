using UnityEngine;
using UnityEngine.Rendering;
using System;

#if UDONSHARP
using VRC.SDKBase;
using UdonSharp;
using VRCGraphics = VRC.SDKBase.VRCGraphics;
#if COMPILER_UDONSHARP
using VRCShader = VRC.SDKBase.VRCShader;
#else
using VRCShader = UnityEngine.Shader;
#endif
#else
using System.Collections;
using VRCGraphics = UnityEngine.Graphics;
using VRCShader = UnityEngine.Shader;
#endif

namespace VRCLightVolumes {
    [DisallowMultipleComponent]
#if UDONSHARP
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LightVolumeManager : UdonSharpBehaviour
#else
    public class LightVolumeManager : MonoBehaviour
#endif
    {
        public const float Version = 3; // Current VRC Light Volumes shader feature version
        private const int MaxLightVolumeCount = 32;
        private const int MaxPointLightCount = 128;
        private const int MaxLightVolumeRotationVectors = MaxLightVolumeCount * 2;
        private const int MaxLightVolumeUvwScaleVectors = MaxLightVolumeCount * 3;
        private const int MaxLightVolumeLegacyUvwVectors = MaxLightVolumeCount * 6;
        private const RenderTextureFormat FixedCustomTexturesFormat = RenderTextureFormat.ARGBHalf;
        private const RenderTextureFormat FixedShadowTexturesFormat = RenderTextureFormat.RHalf;
        private const string CustomRenderTextureInfoProperty = "_CustomRenderTextureInfo";

        [Tooltip("Combined texture containing all Light Volumes' textures.")]
        public Texture LightVolumeAtlas;
        [Tooltip("Combined Texture3D containing all baked Light Volume data. This field is not used at runtime, see LightVolumeAtlas instead. It specifies the base for the post process chain, if given.")]
        public Texture3D LightVolumeAtlasBase;
        [Tooltip("When enabled, areas outside Light Volumes fall back to light probes. Otherwise, the Light Volume with the smallest weight is used as fallback. It also improves performance.")]
        public bool LightProbesBlending = true;
        [Tooltip("Disables smooth blending with areas outside Light Volumes. Use it if your entire scene's play area is covered by Light Volumes. It also improves performance.")]
        public bool SharpBounds = true;
        [Tooltip("Automatically updates most of the volumes properties in runtime. Enabling/Disabling, Color and Intensity updates automatically even without this option enabled. Position, Rotation and Scale gets updated only for volumes that are marked dynamic.")]
        public bool AutoUpdateVolumes = true;
        [Tooltip("Automatically updates dynamic point light cookie and shadow texture sources in runtime.")]
        public bool AutoUpdateTextures = true;
        [Tooltip("Limits the maximum number of additive volumes that can affect a single pixel. If you have many dynamic additive volumes that may overlap, it's good practice to limit overdraw to maintain performance.")]
        public int AdditiveMaxOverdraw = 4;
        [Tooltip("Disables min/max brightness limits for modern avatar shaders such as lilToon or Poiyomi. Check this only if you're sure your scene lighting is properly configured.")]
        public bool ForceSceneLighting = false;
        [Tooltip("The minimum brightness at a point due to lighting from a Point Light Volume, before the light is culled. Larger values will result in better performance, but light attenuation will be less physically correct.")]
        public float LightsBrightnessCutoff = 0.35f;
        [Tooltip("All Light Volume instances sorted in decreasing order by weight. You can enable or disable volumes game objects at runtime. Manually disabling unnecessary volumes improves performance.")]
        public LightVolumeInstance[] LightVolumeInstances = new LightVolumeInstance[0];
        [Tooltip("All Point Light Volume instances. You can enable or disable point light volumes game objects at runtime. Manually disabling unnecessary point light volumes improves performance.")]
        public PointLightVolumeInstance[] PointLightVolumeInstances = new PointLightVolumeInstance[0];
        [Tooltip("Runtime texture array used for point light cubemaps, LUTs and cookies.")]
        public RenderTexture CustomTextures;
        [Tooltip("Cubemaps count that stored in CustomTextures. Cubemap array elements starts from the beginning, 6 elements each.")]
        public int CubemapsCount = 0;
        [Tooltip("Width of each runtime point light projection texture slice.")]
        public int CustomTexturesWidth = 128;
        [Tooltip("Height of each runtime point light projection texture slice.")]
        public int CustomTexturesHeight = 128;
        [Tooltip("Runtime texture array that stores per-light shadow maps.")]
        public RenderTexture ShadowTextures;
        [Tooltip("Shadow maps count stored in ShadowTextures. Each cubemap uses 6 array elements.")]
        public int ShadowMapsCount = 0;
        [Tooltip("Width of each runtime shadow cubemap face.")]
        public int ShadowTexturesWidth = 128;
        [Tooltip("Height of each runtime shadow cubemap face.")]
        public int ShadowTexturesHeight = 128;

        // Material used to copy cubemap source faces into the animated projection texture array
        [HideInInspector] public Material CubemapFaceMaterial;

        // Custom texture source cache and resolved per-instance IDs
        private bool _customTexturesInitialized = false;
        private int _customTexturesDepth = 0;
        private Texture[] _customCubemapTextures = new Texture[0];
        private Material[] _customCubemapMaterials = new Material[0];
        private Texture[] _customSingleTextures = new Texture[0];
        private Material[] _customSingleMaterials = new Material[0];
        private int[] _customCubemapTextureModes = new int[0];
        private bool[] _customCubemapTextureAutoUpdates = new bool[0];
        private bool[] _customCubemapMaterialAutoUpdates = new bool[0];
        private bool[] _customSingleTextureAutoUpdates = new bool[0];
        private bool[] _customSingleMaterialAutoUpdates = new bool[0];
        private int[] _pointLightCustomIDs = new int[0];
        private bool _hasAutoCustomTextureUpdates = false;

        // Shadow texture source cache and resolved per-instance IDs
        private bool _shadowTexturesInitialized = false;
        private int _shadowTexturesDepth = 0;
        private Texture[] _shadowCubemapTextures = new Texture[0];
        private Material[] _shadowCubemapMaterials = new Material[0];
        private int[] _shadowCubemapTextureModes = new int[0];
        private bool[] _shadowCubemapTextureAutoUpdates = new bool[0];
        private bool[] _shadowCubemapMaterialAutoUpdates = new bool[0];
        private int[] _pointLightShadowIDs = new int[0];
        private bool _hasAutoShadowTextureUpdates = false;

        // Dummy source texture required by VRCGraphics material blits when a material generates pixels without a real input texture
        private RenderTexture _runtimeMaterialBlitInputTexture;

        private bool _isRangeDirty = false;
        // Tracks one-time shader array initialization in runtime while still allowing editor property IDs to refresh
        private bool _isInitialized = false;
        // Prevents serialized registry cleanup from running every frame
        private bool _isRegistrySanitized = false;
        private float _prevLightsBrightnessCutoff = 0.35f;

        private Vector4 _customRenderTextureInfo;

        // Light Volumes data
        private int _enabledCount = 0;
        private int _additiveCount = 0;
        private Vector4[] _invLocalEdgeSmooth = new Vector4[MaxLightVolumeCount];
        private Vector4[] _colors = new Vector4[MaxLightVolumeCount];
        private Vector4[] _boundsUvwScale = new Vector4[MaxLightVolumeUvwScaleVectors];
        private Vector4[] _relativeRotationQuaternion = new Vector4[MaxLightVolumeCount];

        // Point Lights data
        private int _pointLightCount = 0;
        private int _activeShadowCount = 0;
        private int[] _enabledPointIDs = new int[MaxPointLightCount];
        private Vector4[] _pointLightPosition = new Vector4[MaxPointLightCount];
        private Vector4[] _pointLightColor = new Vector4[MaxPointLightCount];
        private Vector4[] _pointLightDirection = new Vector4[MaxPointLightCount];
        private Vector4[] _pointLightCustomId = new Vector4[MaxPointLightCount];
        private Vector4[] _pointLightShadowData = new Vector4[MaxPointLightCount];
        private Vector4[] _pointLightShadowReprojectionData = new Vector4[MaxPointLightCount];

        // Legacy support data
        private Matrix4x4[] _invWorldMatrix = new Matrix4x4[MaxLightVolumeCount];
        private Vector4[] _boundsUvw = new Vector4[MaxLightVolumeLegacyUvwVectors];
        private Vector4[] _relativeRotation = new Vector4[MaxLightVolumeRotationVectors];

        // Other
        private int[] _enabledIDs = new int[MaxLightVolumeCount];
        private Vector4[] _boundsScale = new Vector4[3];
        private Vector4[] _bounds = new Vector4[6]; // Legacy

        // Public API for other UdonSharp scripts
        public int EnabledCount => _enabledCount;
        public int[] EnabledIDs => _enabledIDs;

        private bool _volumeDataUpdateRequested = false;
        private bool _isUpdatingVolumes = false;
        private bool _old_AutoUpdateVolumes = false;
        private bool _old_AutoUpdateTextures = false;
#if UDONSHARP
        private bool _isUpdateProcessRunning = false; // Flag that specifies if the delayed update process is already scheduled
#else
        private Coroutine _updateCoroutine = null; // Coroutine that auto-updates volumes or runtime textures when needed (Non-Udon only)
#endif

#region Shader Property IDs
        // Light Volumes
        private int _lightVolumeInvLocalEdgeSmoothID;
        private int _lightVolumeColorID;
        private int _lightVolumeCountID;
        private int _lightVolumeAdditiveCountID;
        private int _lightVolumeAdditiveMaxOverdrawID;
        private int _lightVolumeEnabledID;
        private int _lightVolumeVersionID;
        private int _lightVolumeProbesBlendID;
        private int _lightVolumeSharpBoundsID;
        private int _lightVolumeID;
        private int _lightVolumeRotationQuaternionID;
        private int _lightVolumeInvWorldMatrixID;
        private int _lightVolumeUvwScaleID;
        // Point Lights
        private int _pointLightPositionID;
        private int _pointLightColorID;
        private int _pointLightDirectionID;
        private int _pointLightCustomIdID;
        private int _pointLightCountID;
        private int _pointLightCubeCountID;
        private int _pointLightTextureID;
        private int _pointLightShadowDataID;
        private int _pointLightShadowReprojectionDataID;
        private int _pointLightShadowCountID;
        private int _pointLightShadowTextureID;
        private int _pointLightShadowResolutionID;
        private int _lightBrightnessCutoffID;
        // Legacy support
        private int _areaLightBrightnessCutoffID;
        private int _lightVolumeRotationID;
        private int _lightVolumeUvwID;
        // Other
        private int _forceSceneLightingID;
        private int _cubemapMainTexID;
        private int _cubemapSourceTexID;
        private int _cubemapFaceIndexID;
        
        // Restores registry arrays when serialized data or external Udon calls provide null references
        private void EnsureRegistryArrays() {
            if (LightVolumeInstances == null) LightVolumeInstances = new LightVolumeInstance[0];
            if (PointLightVolumeInstances == null) PointLightVolumeInstances = new PointLightVolumeInstance[0];
        }

        // Initializes shader property IDs and global shader arrays when needed
        private void TryInitialize() {
#if !UNITY_EDITOR
            if (_isInitialized) return;
#endif
            // Light Volumes
            _lightVolumeInvLocalEdgeSmoothID = VRCShader.PropertyToID("_UdonLightVolumeInvLocalEdgeSmooth");
            _lightVolumeInvWorldMatrixID = VRCShader.PropertyToID("_UdonLightVolumeInvWorldMatrix");
            _lightVolumeColorID = VRCShader.PropertyToID("_UdonLightVolumeColor");
            _lightVolumeCountID = VRCShader.PropertyToID("_UdonLightVolumeCount");
            _lightVolumeAdditiveCountID = VRCShader.PropertyToID("_UdonLightVolumeAdditiveCount");
            _lightVolumeAdditiveMaxOverdrawID = VRCShader.PropertyToID("_UdonLightVolumeAdditiveMaxOverdraw");
            _lightVolumeEnabledID = VRCShader.PropertyToID("_UdonLightVolumeEnabled");
            _lightVolumeVersionID = VRCShader.PropertyToID("_UdonLightVolumeVersion");
            _lightVolumeProbesBlendID = VRCShader.PropertyToID("_UdonLightVolumeProbesBlend");
            _lightVolumeSharpBoundsID = VRCShader.PropertyToID("_UdonLightVolumeSharpBounds");
            _lightVolumeID = VRCShader.PropertyToID("_UdonLightVolume");
            _lightVolumeRotationQuaternionID = VRCShader.PropertyToID("_UdonLightVolumeRotationQuaternion");
            _lightVolumeUvwScaleID = VRCShader.PropertyToID("_UdonLightVolumeUvwScale");
            // Point Lights
            _pointLightPositionID = VRCShader.PropertyToID("_UdonPointLightVolumePosition");
            _pointLightColorID = VRCShader.PropertyToID("_UdonPointLightVolumeColor");
            _pointLightDirectionID = VRCShader.PropertyToID("_UdonPointLightVolumeDirection");
            _pointLightCountID = VRCShader.PropertyToID("_UdonPointLightVolumeCount");
            _pointLightCustomIdID = VRCShader.PropertyToID("_UdonPointLightVolumeCustomID");
            _pointLightCubeCountID = VRCShader.PropertyToID("_UdonPointLightVolumeCubeCount");
            _pointLightTextureID = VRCShader.PropertyToID("_UdonPointLightVolumeTexture");
            _pointLightShadowDataID = VRCShader.PropertyToID("_UdonPointLightVolumeShadowData");
            _pointLightShadowReprojectionDataID = VRCShader.PropertyToID("_UdonPointLightVolumeShadowReprojectionData");
            _pointLightShadowCountID = VRCShader.PropertyToID("_UdonPointLightVolumeShadowCount");
            _pointLightShadowTextureID = VRCShader.PropertyToID("_UdonPointLightVolumeShadowTexture");
            _pointLightShadowResolutionID = VRCShader.PropertyToID("_UdonPointLightVolumeShadowResolution");
            _lightBrightnessCutoffID = VRCShader.PropertyToID("_UdonLightBrightnessCutoff");
            // Legacy support
            _areaLightBrightnessCutoffID = VRCShader.PropertyToID("_UdonAreaLightBrightnessCutoff");
            _lightVolumeRotationID = VRCShader.PropertyToID("_UdonLightVolumeRotation");
            _lightVolumeUvwID = VRCShader.PropertyToID("_UdonLightVolumeUvw");
            // Other
            _forceSceneLightingID = VRCShader.PropertyToID("_UdonForceSceneLighting");
            _cubemapMainTexID = VRCShader.PropertyToID("_MainTex");
            _cubemapSourceTexID = VRCShader.PropertyToID("_CubeTex");
            _cubemapFaceIndexID = VRCShader.PropertyToID("_FaceIndex");

#if UNITY_EDITOR
            if (_isInitialized) return;
#endif

            // Light Volumes
            VRCShader.SetGlobalVectorArray(_lightVolumeInvLocalEdgeSmoothID, _invLocalEdgeSmooth);
            VRCShader.SetGlobalVectorArray(_lightVolumeColorID, _colors);
            VRCShader.SetGlobalMatrixArray(_lightVolumeInvWorldMatrixID, _invWorldMatrix);
            VRCShader.SetGlobalVectorArray(_lightVolumeRotationQuaternionID, _relativeRotationQuaternion);
            VRCShader.SetGlobalVectorArray(_lightVolumeUvwScaleID, _boundsUvwScale);
            // Point Lights
            VRCShader.SetGlobalVectorArray(_pointLightPositionID, _pointLightPosition);
            VRCShader.SetGlobalVectorArray(_pointLightColorID, _pointLightColor);
            VRCShader.SetGlobalVectorArray(_pointLightDirectionID, _pointLightDirection);
            VRCShader.SetGlobalVectorArray(_pointLightCustomIdID, _pointLightCustomId);
            VRCShader.SetGlobalVectorArray(_pointLightShadowDataID, _pointLightShadowData);
            VRCShader.SetGlobalVectorArray(_pointLightShadowReprojectionDataID, _pointLightShadowReprojectionData);
            // Legacy support
            VRCShader.SetGlobalVectorArray(_lightVolumeRotationID, _relativeRotation);
            VRCShader.SetGlobalVectorArray(_lightVolumeUvwID, _boundsUvw);

            _isInitialized = true;
        }

        #endregion

        // Writes a fully disabled state to shader globals so stale counts do not survive after all volumes disappear
        private void SetDisabledShaderState() {
            VRCShader.SetGlobalFloat(_lightVolumeCountID, 0);
            VRCShader.SetGlobalFloat(_lightVolumeAdditiveCountID, 0);
            VRCShader.SetGlobalFloat(_pointLightCountID, 0);
            VRCShader.SetGlobalFloat(_pointLightCubeCountID, 0);
            VRCShader.SetGlobalFloat(_pointLightShadowCountID, 0);
            VRCShader.SetGlobalFloat(_lightVolumeEnabledID, 0);
        }

        // To make it work when changing values on UdonSharpBehaviour in editor
#if !UDONSHARP || UNITY_EDITOR
        private void Update() {
            if (_old_AutoUpdateVolumes != AutoUpdateVolumes) {
                _old_AutoUpdateVolumes = AutoUpdateVolumes;
                if (AutoUpdateVolumes) RequestUpdateVolumes();
            }
            if (_old_AutoUpdateTextures != AutoUpdateTextures) {
                _old_AutoUpdateTextures = AutoUpdateTextures;
                if (AutoUpdateTextures) RequestUpdateVolumes();
            }
        }
#endif

        // Clears runtime texture outputs and disables shader globals when this manager is disabled
        private void OnDisable() {
            TryInitialize();
            ResetCustomTexturesGlobal();
            ResetShadowTexturesGlobal();
#if !UDONSHARP
            if (_updateCoroutine != null) {
                StopCoroutine(_updateCoroutine);
                _updateCoroutine = null;
            }
            DestroyCubemapFaceRuntimeMaterial();
#endif
            SetDisabledShaderState();
        }

        // Requests a fresh volume update after this manager becomes active
        private void OnEnable() {
            RequestUpdateVolumes();
        }

        // Rebuilds runtime caches and forces the first shader data upload
        private void Start() {
            _isInitialized = false;
            _isRegistrySanitized = false;
            ResetRuntimeTextureArrays();
            ReinitializeCustomTextures();
            ReinitializeShadowTextures();
            UpdateVolumes(); // Force the first volume update at Start even if auto update is disabled
        }

        // Clears manager-owned runtime texture outputs before rebuilding them
        private void ResetRuntimeTextureArrays() {
            ResetCustomTexturesGlobal();
            ResetShadowTexturesGlobal();
#if !COMPILER_UDONSHARP
            ReleaseRuntimeRenderTexture(_runtimeMaterialBlitInputTexture); // Release the dummy material-blit source alongside generated arrays
#endif
            _runtimeMaterialBlitInputTexture = null;
            _customTexturesInitialized = false;
            _shadowTexturesInitialized = false;
        }

        // Initializes a Light Volume by adding it to the light volume registry. Called automatically at runtime when the object spawns
        public void InitializeLightVolume(LightVolumeInstance lightVolume) {
            if (lightVolume == null) return;
            EnsureRegistryArrays();
            int count = LightVolumeInstances.Length;
            // Reuse an existing slot so repeated OnEnable calls do not duplicate the same volume
            int existingIndex = Array.IndexOf((Array)LightVolumeInstances, lightVolume, 0, count);
            if (existingIndex >= 0) {
                lightVolume.LightVolumeManager = this;
                lightVolume.IsInitialized = true;
                return;
            }
            // Fill the first stale/null slot before growing the registry array
            int emptyIndex = Array.IndexOf((Array)LightVolumeInstances, null, 0, count);
            if (emptyIndex >= 0) {
                LightVolumeInstances[emptyIndex] = lightVolume;
                lightVolume.LightVolumeManager = this;
                lightVolume.IsInitialized = true;
                return;
            }
            // No empty slot exists, so grow the registry array
            LightVolumeInstance[] targetArray = new LightVolumeInstance[count + 1];
            Array.Copy(LightVolumeInstances, targetArray, count);
            targetArray[count] = lightVolume;
            lightVolume.IsInitialized = true;
            lightVolume.LightVolumeManager = this;
            LightVolumeInstances = targetArray;
        }

        // Removes Light Volume references from the light volume registry without resizing it
        public void UnregisterLightVolume(LightVolumeInstance lightVolume) {
            if (lightVolume == null) return;
            EnsureRegistryArrays();
            int count = LightVolumeInstances.Length;
            int index = Array.IndexOf((Array)LightVolumeInstances, lightVolume, 0, count);
            // Clear all duplicate registrations left by serialized data or previous versions
            while (index >= 0) {
                LightVolumeInstances[index] = null;
                int nextIndex = index + 1;
                if (nextIndex >= count) break;
                index = Array.IndexOf((Array)LightVolumeInstances, lightVolume, nextIndex, count - nextIndex); // Continue after the cleared slot to catch later duplicates
            }
            lightVolume.IsInitialized = false;
        }

        // Initializes a Point Light Volume by adding it to the point light volume registry
        public void InitializePointLightVolume(PointLightVolumeInstance pointLightVolume) {
            if (pointLightVolume == null) return;
            EnsureRegistryArrays();
            int count = PointLightVolumeInstances.Length;
            // Reuse an existing slot so repeated OnEnable calls do not duplicate the same point light
            int existingIndex = Array.IndexOf((Array)PointLightVolumeInstances, pointLightVolume, 0, count);
            if (existingIndex >= 0) {
                pointLightVolume.LightVolumeManager = this;
                pointLightVolume.IsInitialized = true;
                return;
            }
            // Fill the first stale/null slot before growing the registry array
            int emptyIndex = Array.IndexOf((Array)PointLightVolumeInstances, null, 0, count);
            if (emptyIndex >= 0) {
                PointLightVolumeInstances[emptyIndex] = pointLightVolume;
                pointLightVolume.LightVolumeManager = this;
                pointLightVolume.IsInitialized = true;
                _customTexturesInitialized = false;
                _shadowTexturesInitialized = false;
                return;
            }
            // No empty slot exists, so grow the registry array
            PointLightVolumeInstance[] targetArray = new PointLightVolumeInstance[count + 1];
            Array.Copy(PointLightVolumeInstances, targetArray, count);
            targetArray[count] = pointLightVolume;
            pointLightVolume.IsInitialized = true;
            pointLightVolume.LightVolumeManager = this;
            PointLightVolumeInstances = targetArray;
            _customTexturesInitialized = false;
            _shadowTexturesInitialized = false;
        }

        // Removes Point Light Volume references from the point light volume registry without resizing it
        public void UnregisterPointLightVolume(PointLightVolumeInstance pointLightVolume) {
            if (pointLightVolume == null) return;
            EnsureRegistryArrays();
            int count = PointLightVolumeInstances.Length;
            // Clear all duplicate point light registrations and mark texture caches dirty when the registry changes
            int index = Array.IndexOf((Array)PointLightVolumeInstances, pointLightVolume, 0, count);
            while (index >= 0) {
                PointLightVolumeInstances[index] = null;
                _customTexturesInitialized = false;
                _shadowTexturesInitialized = false;
                int nextIndex = index + 1;
                if (nextIndex >= count) break;
                index = Array.IndexOf((Array)PointLightVolumeInstances, pointLightVolume, nextIndex, count - nextIndex);
            }
            pointLightVolume.IsInitialized = false;
        }

        // Removes stale inactive and duplicate references left in serialized arrays
        private void SanitizeRegistries() {
            int lightVolumeCount = LightVolumeInstances.Length;
            for (int i = 0; i < lightVolumeCount; i++) {
                LightVolumeInstance instance = LightVolumeInstances[i];
                if (instance == null) continue;
                instance.LightVolumeManager = this;
                if (!instance.gameObject.activeInHierarchy) {
                    LightVolumeInstances[i] = null;
                    instance.IsInitialized = false;
                    continue;
                }
                instance.IsInitialized = true;
                // Keep the first occurrence so serialized duplicates do not shift runtime light IDs
                if (Array.IndexOf((Array)LightVolumeInstances, instance, 0, i) >= 0) LightVolumeInstances[i] = null;
            }

            int pointLightCount = PointLightVolumeInstances.Length;
            for (int i = 0; i < pointLightCount; i++) { // Point light registry changes also invalidate projection and shadow texture caches
                PointLightVolumeInstance instance = PointLightVolumeInstances[i];
                if (instance == null) continue;
                instance.LightVolumeManager = this;
                // Inactive point lights should not reserve custom texture or shadow slots
                if (!instance.gameObject.activeInHierarchy) {
                    PointLightVolumeInstances[i] = null;
                    instance.IsInitialized = false;
                    _customTexturesInitialized = false;
                    _shadowTexturesInitialized = false;
                    continue;
                }
                instance.IsInitialized = true;
                // Keep the first occurrence and force runtime texture IDs to rebuild if a duplicate was removed
                if (Array.IndexOf((Array)PointLightVolumeInstances, instance, 0, i) >= 0) {
                    PointLightVolumeInstances[i] = null;
                    _customTexturesInitialized = false;
                    _shadowTexturesInitialized = false;
                }
            }

            _isRegistrySanitized = true;
        }

        // Keeps runtime texture arrays initialized after registry or source changes
        private void EnsureRuntimeTextureCaches() {
            if (!_customTexturesInitialized) ReinitializeCustomTextures();
            if (!_shadowTexturesInitialized) ReinitializeShadowTextures();
        }

        // Rebuilds the runtime cookie texture array and assigns stable shader-side IDs to all point light instances
        public void ReinitializeCustomTextures() {
            EnsureRegistryArrays();
            BuildCustomTextureSourceCache();
            if (_customTexturesDepth <= 0) {
                ResetCustomTexturesGlobal();
                _customTexturesInitialized = true;
                return;
            }
            if (!EnsureRuntimeCustomTextures(CustomTexturesWidth, CustomTexturesHeight, _customTexturesDepth)) return;
            ApplyCustomTextures(CustomTextures);
            BlitCustomTextures(false);
            _customTexturesInitialized = true;
        }

        // Updates only custom texture sources marked for per-frame refresh
        public void UpdateAutoCustomTextures() {
            if (!_customTexturesInitialized) {
                ReinitializeCustomTextures();
                return;
            }
            if (_customTexturesDepth <= 0) return;
            if (CustomTextures == null) {
                ReinitializeCustomTextures();
                return;
            }
            BlitCustomTextures(true);
        }

        // Checks whether any cookie or shadow texture source needs per-frame refresh
        public bool HasAutoTextureUpdates() {
            return AutoUpdateTextures && (_hasAutoCustomTextureUpdates || _hasAutoShadowTextureUpdates);
        }

        // Clears active custom texture globals when no point light uses a projection source
        private void ResetCustomTexturesGlobal() {
#if !COMPILER_UDONSHARP
            ReleaseRuntimeRenderTexture(CustomTextures);
#endif
            CustomTextures = null;
            _customTexturesDepth = 0;
            CubemapsCount = 0;
            _customCubemapTextures = new Texture[0];
            _customCubemapMaterials = new Material[0];
            _customSingleTextures = new Texture[0];
            _customSingleMaterials = new Material[0];
            _customCubemapTextureModes = new int[0];
            _customCubemapTextureAutoUpdates = new bool[0];
            _customCubemapMaterialAutoUpdates = new bool[0];
            _customSingleTextureAutoUpdates = new bool[0];
            _customSingleMaterialAutoUpdates = new bool[0];
            _pointLightCustomIDs = new int[0];
            _hasAutoCustomTextureUpdates = false;
        }

        // Builds deduplicated source arrays and per-instance shader IDs for the runtime cookie texture array
        private void BuildCustomTextureSourceCache() {
            int count = PointLightVolumeInstances != null ? PointLightVolumeInstances.Length : 0;
            Texture[] cubemapTextures = new Texture[count];
            Material[] cubemapMaterials = new Material[count];
            Texture[] singleTextures = new Texture[count];
            Material[] singleMaterials = new Material[count];
            int[] cubemapTextureModes = new int[count];
            bool[] cubemapTextureAutoUpdates = new bool[count];
            bool[] cubemapMaterialAutoUpdates = new bool[count];
            bool[] singleTextureAutoUpdates = new bool[count];
            bool[] singleMaterialAutoUpdates = new bool[count];

            int cubemapTextureCount = 0;
            int cubemapMaterialCount = 0;
            int singleTextureCount = 0;
            int singleMaterialCount = 0;

            _hasAutoCustomTextureUpdates = false;
            _pointLightCustomIDs = new int[count];
            int[] customSourceTypes = new int[count]; // 0: none, 1: cubemap texture, 2: cubemap material, 3: single texture, 4: single material
            for (int i = 0; i < count; i++) { // Start every point light unresolved; supported sources assign a local deduplicated index below
                _pointLightCustomIDs[i] = -1;
                PointLightVolumeInstance instance = PointLightVolumeInstances[i];
                if (instance == null) continue;
                if (instance.PositionData.w >= 0 && instance.AngleData > 1.5f) continue; // Area lights do not use projection texture arrays
                if (instance.ProjectionMode == 0) continue; // 0: parametric, no custom projection source

                bool usesCubemapProjection = instance.PositionData.w >= 0 && instance.AngleData <= 1.5f && instance.ProjectionMode == 2; // 2: custom cookie or cubemap
                Texture textureSource = instance.ProjectionType == 1 ? instance.CustomTexture : null; // 1: texture
                if (textureSource != null) {
                    if (usesCubemapProjection) { // Point light cubemap sources reserve six consecutive slices
                        int index = FindTextureIndex(cubemapTextures, cubemapTextureCount, textureSource);
                        int textureMode = instance.CustomTextureIsCubemap ? 2 : (instance.CustomTextureHasDepthSlices ? 1 : 0);
                        if (index < 0) { // Append each unique source once so matching lights share the same texture ID
                            index = cubemapTextureCount;
                            cubemapTextures[cubemapTextureCount] = textureSource;
                            cubemapTextureModes[cubemapTextureCount] = textureMode;
                            cubemapTextureCount++;
                        } else {
                            if (textureMode > cubemapTextureModes[index]) cubemapTextureModes[index] = textureMode;
                        }
                        _pointLightCustomIDs[i] = index;
                        customSourceTypes[i] = 1;
                        if (instance.AutoUpdateCustomTexture) {
                            cubemapTextureAutoUpdates[index] = true;
                            _hasAutoCustomTextureUpdates = true;
                        }
                    } else { // Spot and LUT/cookie projections use one slice per unique source
                        int index = FindTextureIndex(singleTextures, singleTextureCount, textureSource);
                        if (index < 0) { // Append each unique source once so matching lights share the same texture ID
                            index = singleTextureCount;
                            singleTextures[singleTextureCount] = textureSource;
                            singleTextureCount++;
                        }
                        _pointLightCustomIDs[i] = index;
                        customSourceTypes[i] = 3;
                        if (instance.AutoUpdateCustomTexture) {
                            singleTextureAutoUpdates[index] = true;
                            _hasAutoCustomTextureUpdates = true;
                        }
                    }
                    continue;
                }

                Material materialSource = instance.ProjectionType == 2 ? instance.CustomTextureMaterial : null; // 2: material
                if (materialSource != null) {
                    if (usesCubemapProjection) { // Cubemap materials are rendered as six generated faces
                        int index = FindMaterialIndex(cubemapMaterials, cubemapMaterialCount, materialSource);
                        if (index < 0) { // Append each unique material once so matching lights share the same texture ID
                            index = cubemapMaterialCount;
                            cubemapMaterials[cubemapMaterialCount] = materialSource;
                            cubemapMaterialCount++;
                        }
                        _pointLightCustomIDs[i] = index;
                        customSourceTypes[i] = 2;
                        if (instance.AutoUpdateCustomTexture) {
                            cubemapMaterialAutoUpdates[index] = true;
                            _hasAutoCustomTextureUpdates = true;
                        }
                    } else { // Single-slice materials render directly into one projection slice
                        int index = FindMaterialIndex(singleMaterials, singleMaterialCount, materialSource);
                        if (index < 0) { // Append each unique material once so matching lights share the same texture ID
                            index = singleMaterialCount;
                            singleMaterials[singleMaterialCount] = materialSource;
                            singleMaterialCount++;
                        }
                        _pointLightCustomIDs[i] = index;
                        customSourceTypes[i] = 4;
                        if (instance.AutoUpdateCustomTexture) {
                            singleMaterialAutoUpdates[index] = true;
                            _hasAutoCustomTextureUpdates = true;
                        }
                    }
                }
            }

            // Trim temporary arrays to actual source counts so update loops only touch valid entries
            _customCubemapTextures = CopyTextureArray(cubemapTextures, cubemapTextureCount);
            _customCubemapMaterials = CopyMaterialArray(cubemapMaterials, cubemapMaterialCount);
            _customSingleTextures = CopyTextureArray(singleTextures, singleTextureCount);
            _customSingleMaterials = CopyMaterialArray(singleMaterials, singleMaterialCount);
            _customCubemapTextureModes = CopyIntArray(cubemapTextureModes, cubemapTextureCount);
            _customCubemapTextureAutoUpdates = CopyBoolArray(cubemapTextureAutoUpdates, cubemapTextureCount);
            _customCubemapMaterialAutoUpdates = CopyBoolArray(cubemapMaterialAutoUpdates, cubemapMaterialCount);
            _customSingleTextureAutoUpdates = CopyBoolArray(singleTextureAutoUpdates, singleTextureCount);
            _customSingleMaterialAutoUpdates = CopyBoolArray(singleMaterialAutoUpdates, singleMaterialCount);

            CubemapsCount = cubemapTextureCount + cubemapMaterialCount;
            _customTexturesDepth = CubemapsCount * 6 + singleTextureCount + singleMaterialCount;
            AssignPointLightCustomIDs(customSourceTypes, cubemapTextureCount, singleTextureCount);
        }

        // Converts local source indices collected while building the cache into final shader custom IDs
        private void AssignPointLightCustomIDs(int[] customSourceTypes, int cubemapTextureCount, int singleTextureCount) {
            int count = _pointLightCustomIDs != null ? _pointLightCustomIDs.Length : 0;
            for (int i = 0; i < count; i++) {
                int index = _pointLightCustomIDs[i];
                if (index < 0) continue;
                int sourceType = customSourceTypes[i];
                if (sourceType == 2) _pointLightCustomIDs[i] = cubemapTextureCount + index;
                else if (sourceType == 3) _pointLightCustomIDs[i] = CubemapsCount + index;
                else if (sourceType == 4) _pointLightCustomIDs[i] = CubemapsCount + singleTextureCount + index;
            }
        }

        // Copies unique custom texture sources into the runtime array
        private void BlitCustomTextures(bool onlyAutoUpdates) {

            // Cubemap texture sources occupy the first custom texture slices, six slices per source
            int cubemapTextureCount = _customCubemapTextures != null ? _customCubemapTextures.Length : 0;
            for (int i = 0; i < cubemapTextureCount; i++) {
                if (onlyAutoUpdates && !_customCubemapTextureAutoUpdates[i]) continue;
                BlitCubemapTexture(_customCubemapTextures[i], _customCubemapTextureModes[i], i * 6, CustomTextures);
            }

            // Cubemap material sources follow cubemap texture sources and are also rendered as six slices
            int cubemapMaterialCount = _customCubemapMaterials != null ? _customCubemapMaterials.Length : 0;
            for (int i = 0; i < cubemapMaterialCount; i++) {
                if (onlyAutoUpdates && !_customCubemapMaterialAutoUpdates[i]) continue;
                BlitCubemapMaterial(_customCubemapMaterials[i], (cubemapTextureCount + i) * 6, CustomTextures, _customTexturesDepth);
            }

            // Single-slice projection textures start after every cubemap slice
            int singleBaseSlice = CubemapsCount * 6;
            int singleTextureCount = _customSingleTextures != null ? _customSingleTextures.Length : 0;
            for (int i = 0; i < singleTextureCount; i++) {
                if (onlyAutoUpdates && !_customSingleTextureAutoUpdates[i]) continue;
                Texture sourceTexture = _customSingleTextures[i];
                if (sourceTexture == null) continue;
                VRCGraphics.Blit(sourceTexture, CustomTextures, 0, singleBaseSlice + i);
            }

            // Single-slice projection materials follow regular single-slice textures
            int singleMaterialCount = _customSingleMaterials != null ? _customSingleMaterials.Length : 0;
            for (int i = 0; i < singleMaterialCount; i++) {
                if (onlyAutoUpdates && !_customSingleMaterialAutoUpdates[i]) continue;
                Material sourceMaterial = _customSingleMaterials[i];
                if (sourceMaterial == null) continue;
                BlitMaterialSlice(sourceMaterial, 0, singleBaseSlice + singleTextureCount + i, false, CustomTextures, _customTexturesDepth);
            }

        }

        // Finds a texture reference in a fixed-size prefix of an array
        private int FindTextureIndex(Texture[] array, int count, Texture texture) {
            if (array == null || texture == null) return -1;
            return Array.IndexOf((Array)array, texture, 0, count);
        }

        // Finds a material reference in a fixed-size prefix of an array
        private int FindMaterialIndex(Material[] array, int count, Material material) {
            if (array == null || material == null) return -1;
            return Array.IndexOf((Array)array, material, 0, count);
        }

        // Copies a texture array prefix to an exact-sized array
        private Texture[] CopyTextureArray(Texture[] source, int count) {
            if (count <= 0) return new Texture[0];
            Texture[] destination = new Texture[count];
            Array.Copy(source, destination, count);
            return destination;
        }

        // Copies a material array prefix to an exact-sized array
        private Material[] CopyMaterialArray(Material[] source, int count) {
            if (count <= 0) return new Material[0];
            Material[] destination = new Material[count];
            Array.Copy(source, destination, count);
            return destination;
        }

        // Copies an int array prefix to an exact-sized array
        private int[] CopyIntArray(int[] source, int count) {
            if (count <= 0) return new int[0];
            int[] destination = new int[count];
            Array.Copy(source, destination, count);
            return destination;
        }

        // Copies a bool array prefix to an exact-sized array
        private bool[] CopyBoolArray(bool[] source, int count) {
            if (count <= 0) return new bool[0];
            bool[] destination = new bool[count];
            Array.Copy(source, destination, count);
            return destination;
        }

        // Rebuilds the runtime shadow texture array and assigns stable shader-side IDs to all shadowed point light instances
        public void ReinitializeShadowTextures() {
            EnsureRegistryArrays();
            BuildShadowTextureSourceCache();
            if (_shadowTexturesDepth <= 0) { // No shadow sources are active, so clear the global array instead of keeping stale data
                ResetShadowTexturesGlobal();
                _shadowTexturesInitialized = true;
                return;
            }
            if (!EnsureRuntimeShadowTextures(ShadowTexturesWidth, ShadowTexturesHeight, _shadowTexturesDepth)) return;
            ApplyShadowTextures(ShadowTextures);
            BlitShadowTextures(false);
            _shadowTexturesInitialized = true;
        }

        // Updates only shadow cubemap sources marked for per-frame refresh
        public void UpdateAutoShadowTextures() {
            if (!_shadowTexturesInitialized) {
                ReinitializeShadowTextures();
                return;
            }
            if (_shadowTexturesDepth <= 0) return; // Nothing is allocated when no point light contributes a shadow source
            if (ShadowTextures == null) {
                ReinitializeShadowTextures();
                return;
            }
            BlitShadowTextures(true);
        }

        // Clears active shadow texture globals when no point light uses a shadow source
        private void ResetShadowTexturesGlobal() {
#if !COMPILER_UDONSHARP
            ReleaseRuntimeRenderTexture(ShadowTextures);
#endif
            ShadowTextures = null;
            ShadowMapsCount = 0;
            _shadowTexturesDepth = 0;
            _shadowCubemapTextures = new Texture[0];
            _shadowCubemapMaterials = new Material[0];
            _shadowCubemapTextureModes = new int[0];
            _shadowCubemapTextureAutoUpdates = new bool[0];
            _shadowCubemapMaterialAutoUpdates = new bool[0];
            _pointLightShadowIDs = new int[0];
            _hasAutoShadowTextureUpdates = false;
        }

        // Builds deduplicated source arrays and per-instance shader IDs for the runtime shadow texture array
        private void BuildShadowTextureSourceCache() {
            int count = PointLightVolumeInstances != null ? PointLightVolumeInstances.Length : 0;
            Texture[] cubemapTextures = new Texture[count];
            Material[] cubemapMaterials = new Material[count];
            int[] cubemapTextureModes = new int[count];
            bool[] cubemapTextureAutoUpdates = new bool[count];
            bool[] cubemapMaterialAutoUpdates = new bool[count];

            int cubemapTextureCount = 0;
            int cubemapMaterialCount = 0;

            _hasAutoShadowTextureUpdates = false;
            _pointLightShadowIDs = new int[count];
            bool[] shadowSourceIsMaterial = new bool[count];
            for (int i = 0; i < count; i++) {
                // Start every point light unresolved; only valid shadow sources receive a shadow texture ID
                _pointLightShadowIDs[i] = -1;
                PointLightVolumeInstance instance = PointLightVolumeInstances[i];
                if (instance == null || instance.ShadowMapID < 0 || (instance.ShadowMapTexture == null && instance.ShadowMapMaterial == null)) {
                    if (instance != null) instance.ShadowMapID = -1;
                    continue;
                }
                // Prefer texture shadows over material shadows when both fields are assigned
                Texture textureSource = instance.ShadowMapTexture;
                if (textureSource != null) { // Shadow textures are deduplicated before being copied into the runtime array
                    int index = FindTextureIndex(cubemapTextures, cubemapTextureCount, textureSource);
                    int textureMode = instance.ShadowMapTextureIsCubemap ? 2 : (instance.ShadowMapTextureHasDepthSlices ? 1 : 0);
                    if (index < 0) { // Append each unique texture once; matching lights reuse the same shadow ID
                        index = cubemapTextureCount;
                        cubemapTextures[cubemapTextureCount] = textureSource;
                        cubemapTextureModes[cubemapTextureCount] = textureMode;
                        cubemapTextureCount++;
                    } else { // Keep the most expressive mode when the same texture appears with different source metadata
                        if (textureMode > cubemapTextureModes[index]) cubemapTextureModes[index] = textureMode;
                    }
                    if (instance.AutoUpdateShadowMap) {
                        cubemapTextureAutoUpdates[index] = true;
                        _hasAutoShadowTextureUpdates = true;
                    }
                    _pointLightShadowIDs[i] = index;
                    continue;
                }
                // Material shadows are rendered after texture shadows, but share the same final cubemap array
                Material materialSource = instance.ShadowMapMaterial;
                if (materialSource != null) {
                    int index = FindMaterialIndex(cubemapMaterials, cubemapMaterialCount, materialSource);
                    if (index < 0) { // Append each unique material once; matching lights reuse the same shadow ID
                        index = cubemapMaterialCount;
                        cubemapMaterials[cubemapMaterialCount] = materialSource;
                        cubemapMaterialCount++;
                    }
                    if (instance.AutoUpdateShadowMap) {
                        cubemapMaterialAutoUpdates[index] = true;
                        _hasAutoShadowTextureUpdates = true;
                    }
                    _pointLightShadowIDs[i] = index;
                    shadowSourceIsMaterial[i] = true;
                }
            }

            // Trim temporary arrays to actual source counts so update loops only touch valid entries
            _shadowCubemapTextures = CopyTextureArray(cubemapTextures, cubemapTextureCount);
            _shadowCubemapMaterials = CopyMaterialArray(cubemapMaterials, cubemapMaterialCount);
            _shadowCubemapTextureModes = CopyIntArray(cubemapTextureModes, cubemapTextureCount);
            _shadowCubemapTextureAutoUpdates = CopyBoolArray(cubemapTextureAutoUpdates, cubemapTextureCount);
            _shadowCubemapMaterialAutoUpdates = CopyBoolArray(cubemapMaterialAutoUpdates, cubemapMaterialCount);

            ShadowMapsCount = cubemapTextureCount + cubemapMaterialCount;
            _shadowTexturesDepth = ShadowMapsCount * 6;
            // Material shadow sources are stored after texture sources in the final array
            for (int i = 0; i < count; i++) {
                int index = _pointLightShadowIDs[i];
                if (index < 0) continue;
                if (shadowSourceIsMaterial[i]) index += cubemapTextureCount;
                _pointLightShadowIDs[i] = index;
                PointLightVolumeInstance instance = PointLightVolumeInstances[i];
                if (instance != null) instance.ShadowMapID = index;
            }
        }

        // Copies unique shadow cubemap sources into the runtime array
        private void BlitShadowTextures(bool onlyAutoUpdates) {
            // Shadow texture sources occupy the first shadow slices, six slices per cubemap
            int cubemapTextureCount = _shadowCubemapTextures != null ? _shadowCubemapTextures.Length : 0;
            for (int i = 0; i < cubemapTextureCount; i++) {
                if (onlyAutoUpdates && !_shadowCubemapTextureAutoUpdates[i]) continue;
                BlitCubemapTexture(_shadowCubemapTextures[i], _shadowCubemapTextureModes[i], i * 6, ShadowTextures);
            }
            // Shadow material sources follow texture sources and are rendered as six generated slices
            int cubemapMaterialCount = _shadowCubemapMaterials != null ? _shadowCubemapMaterials.Length : 0;
            for (int i = 0; i < cubemapMaterialCount; i++) {
                if (onlyAutoUpdates && !_shadowCubemapMaterialAutoUpdates[i]) continue;
                BlitCubemapMaterial(_shadowCubemapMaterials[i], (cubemapTextureCount + i) * 6, ShadowTextures, _shadowTexturesDepth);
            }
        }

        // Creates or recreates the runtime texture array so it matches an explicit texture layout
        private bool EnsureRuntimeCustomTextures(int width, int height, int depth) {
            if (width <= 0 || height <= 0 || depth <= 0) return false;
            bool recreate = ShouldRecreateRuntimeTextureArray(CustomTextures, width, height, depth);
            if (!recreate) return CustomTextures != null;

#if !COMPILER_UDONSHARP
            ReleaseRuntimeRenderTexture(CustomTextures);
#endif

            CustomTextures = CreateRuntimeTextureArray(width, height, depth, FixedCustomTexturesFormat, FilterMode.Trilinear);
#if !COMPILER_UDONSHARP
            CustomTextures.name = "LightVolumeManager_CustomTextures";
#endif
            _customTexturesDepth = depth;
            return true;
        }

        // Creates or recreates the runtime shadow texture array so it matches an explicit texture layout
        private bool EnsureRuntimeShadowTextures(int width, int height, int depth) {
            if (width <= 0 || height <= 0 || depth <= 0) return false;
            bool recreate = ShouldRecreateRuntimeTextureArray(ShadowTextures, width, height, depth);
            if (!recreate) return ShadowTextures != null;

#if !COMPILER_UDONSHARP
            ReleaseRuntimeRenderTexture(ShadowTextures);
#endif

            ShadowTextures = CreateRuntimeTextureArray(width, height, depth, FixedShadowTexturesFormat, FilterMode.Point);
#if !COMPILER_UDONSHARP
            ShadowTextures.name = "LightVolumeManager_ShadowTextures";
#endif
            ShadowMapsCount = depth / 6;
            _shadowTexturesDepth = depth;
            return true;
        }

        // Checks if a runtime texture array must be recreated for the requested layout
        private bool ShouldRecreateRuntimeTextureArray(RenderTexture texture, int width, int height, int depth) {
            if (texture == null || texture.width != width || texture.height != height || texture.volumeDepth != depth) return true;
            return false;
        }

#if !COMPILER_UDONSHARP
        // Releases a runtime render texture before replacing it
        private void ReleaseRuntimeRenderTexture(RenderTexture texture) {
            if (texture == null) return;
            RenderTexture.active = null;
            texture.Release();
        }
#endif

        // Creates a runtime texture array with the shared Light Volumes settings
        private RenderTexture CreateRuntimeTextureArray(int width, int height, int depth, RenderTextureFormat format, FilterMode filterMode) {
            RenderTexture texture = new RenderTexture(width, height, 0, format, RenderTextureReadWrite.Linear);
            texture.dimension = TextureDimension.Tex2DArray;
            texture.volumeDepth = depth;
            texture.useMipMap = false;
            texture.autoGenerateMips = false;
            texture.enableRandomWrite = false;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = filterMode;
            texture.anisoLevel = 0;
#if !COMPILER_UDONSHARP
            texture.hideFlags = HideFlags.HideAndDontSave;
#endif
            texture.Create();
            return texture;
        }

        // Copies one cubemap face into one texture array slice using the shared face unwrap shader
        private void BlitCubemapFace(Texture sourceTexture, RenderTexture destination, int sourceFace, int targetSlice) {
            if (!EnsureCubemapFaceMaterial()) return;

            CubemapFaceMaterial.SetTexture(_cubemapSourceTexID, sourceTexture);
            CubemapFaceMaterial.SetInt(_cubemapFaceIndexID, Mathf.Clamp(sourceFace, 0, 5));

            Texture blitSource = sourceTexture;
#if UDONSHARP
            blitSource = GetMaterialBlitInputTexture();
#endif
            BlitMaterialToSlice(blitSource, CubemapFaceMaterial, destination, targetSlice);
        }

        // Writes a six-face cubemap texture source into consecutive destination array slices
        private void BlitCubemapTexture(Texture sourceTexture, int textureMode, int firstSlice, RenderTexture destination) {
            if (sourceTexture == null) return;
            for (int i = 0; i < 6; i++) {
                int targetSlice = firstSlice + i;
                if (textureMode == 2) {
                    BlitCubemapFace(sourceTexture, destination, i, targetSlice);
                } else {
                    int sourceSlice = 0;
                    if (textureMode == 1) sourceSlice = i;
                    VRCGraphics.Blit(sourceTexture, destination, sourceSlice, targetSlice);
                }
            }
        }

        // Writes a six-face cubemap material source into consecutive destination array slices
        private void BlitCubemapMaterial(Material sourceMaterial, int firstSlice, RenderTexture destination, int destinationDepth) {
            if (sourceMaterial == null) return;
            for (int i = 0; i < 6; i++) {
                int targetSlice = firstSlice + i;
                BlitMaterialSlice(sourceMaterial, i, targetSlice, true, destination, destinationDepth);
            }
        }

        // Runs a material-only update into one texture array slice
        private void BlitMaterialSlice(Material sourceMaterial, int faceIndex, int targetSlice, bool isCubemapUpdate, RenderTexture destination, int textureDepth) {
            if (sourceMaterial == null) return;
            if (destination == null) return;
            Texture blitSource = null;
#if UDONSHARP
            blitSource = GetMaterialBlitInputTexture();
#endif
            SetMaterialBlitProperties(sourceMaterial, faceIndex, targetSlice, isCubemapUpdate, destination, textureDepth);
            if (blitSource != null) sourceMaterial.SetTexture(_cubemapMainTexID, blitSource);

            BlitMaterialToSlice(blitSource, sourceMaterial, destination, targetSlice);
        }

        // Applies Light Volumes material-blit target info before a material-only blit
        private void SetMaterialBlitProperties(Material sourceMaterial, int faceIndex, int targetSlice, bool isCubemapUpdate, RenderTexture destination, int textureDepth) {
            int width = 1;
            int height = 1;
            int depth = textureDepth;
            if (destination != null) {
                width = destination.width;
                height = destination.height;
                if (depth <= 0) depth = destination.volumeDepth;
            }
            if (depth <= 0) depth = 1;

            int safeFaceIndex = Mathf.Clamp(faceIndex, 0, 5);
            float infoSlice = (float)targetSlice;
            float infoDepth = (float)depth;
            if (isCubemapUpdate) {
                infoSlice = (float)safeFaceIndex;
                infoDepth = 1.0f;
            }

            _customRenderTextureInfo = new Vector4((float)width, (float)height, infoDepth, infoSlice);
            sourceMaterial.SetVector(CustomRenderTextureInfoProperty, _customRenderTextureInfo);
        }

#if UDONSHARP
        // Returns a stable source texture used only to bind the active destination for material-only Udon blits
        private Texture GetMaterialBlitInputTexture() {
            if (_runtimeMaterialBlitInputTexture != null) return _runtimeMaterialBlitInputTexture;
            _runtimeMaterialBlitInputTexture = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            _runtimeMaterialBlitInputTexture.dimension = TextureDimension.Tex2D;
            _runtimeMaterialBlitInputTexture.useMipMap = false;
            _runtimeMaterialBlitInputTexture.autoGenerateMips = false;
            _runtimeMaterialBlitInputTexture.Create();
            return _runtimeMaterialBlitInputTexture;
        }
#endif

        // Renders one material pass into a destination texture-array slice using the active runtime API
        private void BlitMaterialToSlice(Texture sourceTexture, Material material, RenderTexture destination, int targetSlice) {
#if UDONSHARP
            // Udon VRCGraphics needs a separate destination-binding blit before rendering the material into the selected slice
            VRCGraphics.Blit(sourceTexture, destination, 0, targetSlice);
            VRCGraphics.Blit(sourceTexture, material, 0, targetSlice);
#else
            // Unity Graphics can bind the target slice directly, so the material pass can render in one blit
            RenderTexture previousRenderTexture = RenderTexture.active;
            VRCGraphics.SetRenderTarget(destination, 0, CubemapFace.Unknown, targetSlice);
            VRCGraphics.Blit(sourceTexture, material, 0);
            RenderTexture.active = previousRenderTexture;
#endif
        }

        // Applies the active cookie texture array to the manager and shader globals
        private void ApplyCustomTextures(RenderTexture texture) {
            CustomTextures = texture;
            if (texture == null) return;
            TryInitialize();
            if (!_isInitialized) return;
            VRCShader.SetGlobalTexture(_pointLightTextureID, texture);
        }

        // Applies the active shadow texture array to the manager and shader globals
        private void ApplyShadowTextures(RenderTexture texture) {
            ShadowTextures = texture;
            if (texture == null) return;
            TryInitialize();
            if (!_isInitialized) return;
            VRCShader.SetGlobalTexture(_pointLightShadowTextureID, texture);
        }

        // Finds or lazily creates the cubemap face material outside Udon
        private bool EnsureCubemapFaceMaterial() {
            if (CubemapFaceMaterial != null) return true;
#if !COMPILER_UDONSHARP
            Shader shader = Shader.Find("Hidden/CubeFace");
            if (shader == null) return false;
            CubemapFaceMaterial = new Material(shader);
            CubemapFaceMaterial.hideFlags = HideFlags.HideAndDontSave;
            return true;
#else
            return false;
#endif
        }

#if !COMPILER_UDONSHARP
        // Destroys the editor/runtime material instance used by non-Udon execution
        private void DestroyCubemapFaceRuntimeMaterial() {
            if (CubemapFaceMaterial == null) return;
            if (CubemapFaceMaterial.hideFlags != HideFlags.HideAndDontSave) return;
            if (Application.isPlaying) Destroy(CubemapFaceMaterial);
            else DestroyImmediate(CubemapFaceMaterial);
            CubemapFaceMaterial = null;
        }
#endif

        // Requests to update volumes next frame
        public void RequestUpdateVolumes() {
            if (_isUpdatingVolumes) return;
            _volumeDataUpdateRequested = true;
#if UDONSHARP
            if (_isUpdateProcessRunning) return; // Prevent multiple update processes
            _isUpdateProcessRunning = true;
            SendCustomEventDelayedFrames(nameof(UpdateVolumesProcess), 1);
#else
            if (_updateCoroutine != null || !isActiveAndEnabled) return;
            _updateCoroutine = StartCoroutine(UpdateVolumesCoroutine());
#endif
        }

#if UDONSHARP
        // Internal method to auto update volumes and runtime textures every frame while needed
        public void UpdateVolumesProcess() {
            if (!enabled || !gameObject.activeInHierarchy) {
                _volumeDataUpdateRequested = false;
                _isUpdateProcessRunning = false;
                return;
            }

            bool updateVolumes = _volumeDataUpdateRequested || AutoUpdateVolumes;
            _volumeDataUpdateRequested = false;

            if (updateVolumes) {
                UpdateVolumes();
            } else if (!_customTexturesInitialized || !_shadowTexturesInitialized) {
                EnsureRuntimeTextureCaches();
            }

            bool updateTextures = AutoUpdateTextures && (_hasAutoCustomTextureUpdates || _hasAutoShadowTextureUpdates);
            if (AutoUpdateTextures && _hasAutoCustomTextureUpdates) UpdateAutoCustomTextures();
            if (AutoUpdateTextures && _hasAutoShadowTextureUpdates) UpdateAutoShadowTextures();

            if ((AutoUpdateVolumes || _volumeDataUpdateRequested || updateTextures || !_customTexturesInitialized || !_shadowTexturesInitialized) && enabled && gameObject.activeInHierarchy) {
                SendCustomEventDelayedFrames(nameof(UpdateVolumesProcess), 1);
            } else {
                _isUpdateProcessRunning = false;
            }
        }
#else
        // Internal coroutine to auto update volumes and runtime textures every frame while needed
        private IEnumerator UpdateVolumesCoroutine() {
            bool updateTextures;
            do {
                yield return null;

                bool updateVolumes = _volumeDataUpdateRequested || AutoUpdateVolumes;
                _volumeDataUpdateRequested = false;

                if (updateVolumes) {
                    UpdateVolumes();
                } else if (!_customTexturesInitialized || !_shadowTexturesInitialized) {
                    EnsureRuntimeTextureCaches();
                }

                updateTextures = AutoUpdateTextures && (_hasAutoCustomTextureUpdates || _hasAutoShadowTextureUpdates);
                if (AutoUpdateTextures && _hasAutoCustomTextureUpdates) UpdateAutoCustomTextures();
                if (AutoUpdateTextures && _hasAutoShadowTextureUpdates) UpdateAutoShadowTextures();
            } while (isActiveAndEnabled && (AutoUpdateVolumes || _volumeDataUpdateRequested || updateTextures || !_customTexturesInitialized || !_shadowTexturesInitialized));

            _updateCoroutine = null;
        }
#endif

        // Recalculates all volume data and uploads it to shader globals
        public void UpdateVolumes() {
            if (_isUpdatingVolumes) return;
            _isUpdatingVolumes = true;
            TryInitialize();

            // Uploads whether Force Scene Lighting is enabled in the scene
            VRCShader.SetGlobalInteger(_forceSceneLightingID, ForceSceneLighting ? 1 : 0);

            if (!enabled || !gameObject.activeInHierarchy) {
                SetDisabledShaderState();
                _isUpdatingVolumes = false;
                return;
            }

            EnsureRuntimeTextureCaches();
            EnsureRegistryArrays();

            // Recalculate all light ranges if LightsBrightnessCutoff changed
            if (_prevLightsBrightnessCutoff != LightsBrightnessCutoff) {
                _prevLightsBrightnessCutoff = LightsBrightnessCutoff;
                _isRangeDirty = true;
            }

            if (!_isRegistrySanitized) SanitizeRegistries();

            // Search for enabled volumes and count additive volumes
            _enabledCount = 0;
            _additiveCount = 0;
            for (int i = 0; i < LightVolumeInstances.Length && _enabledCount < MaxLightVolumeCount; i++) {
                LightVolumeInstance instance = LightVolumeInstances[i];
                if (instance == null) continue;
                if (!instance.gameObject.activeInHierarchy) {
                    instance.LightVolumeManager = this;
                    instance.IsInitialized = false;
                    LightVolumeInstances[i] = null;
                    continue;
                }
                if (instance.Intensity != 0 && instance.Color != Color.black) {
#if UDONSHARP
    #if COMPILER_UDONSHARP
                    if (instance.IsDynamic) instance.UpdateTransform();
    #else
                    if (Application.isPlaying) {
                        if (instance.IsDynamic) instance.UpdateTransform();
                    } else {
                        instance.UpdateTransform();
                    }
    #endif
#else
                    if (Application.isPlaying) {
                        if (instance.IsDynamic) instance.UpdateTransform();
                    } else {
                        instance.UpdateTransform();
                    }
#endif
                    if (instance.IsAdditive) _additiveCount++;
                    _enabledIDs[_enabledCount] = i;
                    _enabledCount++;
                }
            }

            // Fill arrays with enabled volume data
            for (int i = 0; i < _enabledCount; i++) {

                int enabledId = _enabledIDs[i];
                int i2 = i * 2;
                int i3 = i * 3;
                int i6 = i * 6;

                LightVolumeInstance instance = LightVolumeInstances[enabledId];

                // Set volume transform data
                _invWorldMatrix[i] = instance.InvWorldMatrix;
                _invLocalEdgeSmooth[i] = instance.InvLocalEdgeSmoothing; // Set volume edge smoothing

                Vector4 c = instance.Color.linear * instance.Intensity; // Apply volume color and intensity
                c.w = instance.IsRotated ? 1 : 0; // Color alpha stores whether the volume is rotated
                _colors[i] = c;

                // Set volume relative rotation
                _relativeRotationQuaternion[i] = instance.RelativeRotation;
                _relativeRotation[i2] = instance.RelativeRotationRow0; // Legacy
                _relativeRotation[i2 + 1] = instance.RelativeRotationRow1; // Legacy

                // Set volume UVW bounds
                _boundsScale[0] = instance.BoundsUvwMin0;
                _boundsScale[1] = instance.BoundsUvwMin1;
                _boundsScale[2] = instance.BoundsUvwMin2;
                // Legacy
                _bounds[0] = instance.BoundsUvwMin0;
                _bounds[1] = instance.BoundsUvwMax0;
                _bounds[2] = instance.BoundsUvwMin1;
                _bounds[3] = instance.BoundsUvwMax1;
                _bounds[4] = instance.BoundsUvwMin2;
                _bounds[5] = instance.BoundsUvwMax2;

                Array.Copy(_boundsScale, 0, _boundsUvwScale, i3, 3);
                Array.Copy(_bounds, 0, _boundsUvw, i6, 6); // Legacy

            }

            // Search for enabled point light volumes
            _pointLightCount = 0;
            for (int i = 0; i < PointLightVolumeInstances.Length && _pointLightCount < MaxPointLightCount; i++) {
                PointLightVolumeInstance instance = PointLightVolumeInstances[i];
                if (instance == null) continue;
                if (!instance.gameObject.activeInHierarchy) {
                    instance.LightVolumeManager = this;
                    instance.IsInitialized = false;
                    PointLightVolumeInstances[i] = null;
                    continue;
                }
                if (_isRangeDirty) { // If brightness cutoff changed, force every light range to recalculate
                    instance.UpdateRange();
                }
                if (instance.Intensity != 0 && instance.Color != Color.black) {
#if UDONSHARP
    #if COMPILER_UDONSHARP
                    if (instance.IsDynamic) instance.UpdateTransform();
    #else
                    if (Application.isPlaying) {
                        if (instance.IsDynamic) instance.UpdateTransform();
                    } else {
                        instance.UpdateTransform();
                    }
    #endif
#else
                    if (Application.isPlaying) {
                        if (instance.IsDynamic) instance.UpdateTransform();
                    } else {
                        instance.UpdateTransform();
                    }
#endif
                    _enabledPointIDs[_pointLightCount] = i;
                    _pointLightCount++;
                }
            }

            _isRangeDirty = false; // Reset range dirtiness

            // Fill arrays with enabled point light data
            _activeShadowCount = 0;
            for (int i = 0; i < _pointLightCount; i++) {
                PointLightVolumeInstance instance = PointLightVolumeInstances[_enabledPointIDs[i]];

                // Recalculate squared range of the light if dirty
                if (_isRangeDirty || instance.IsRangeDirty) {
                    instance.UpdateRange();
                }

                // Convert stored point/spot range encoding into the value expected by shaders
                Vector4 pos = instance.PositionData;
                if (instance.PositionData.w < 0 || instance.AngleData <= 1.5f) {
                    if (instance.ProjectionMode == 1) pos.w /= instance.SquaredScale; // 1: LUT
                    else pos.w *= instance.SquaredScale;
                }
                _pointLightPosition[i] = pos;

                // Pack light color, intensity, and angle into the shader color vector
                Vector4 c = instance.Color.linear * instance.Intensity;
                c.w = instance.AngleData;
                _pointLightColor[i] = c;

                // Resolve custom texture and shadow IDs from manager caches using the original registry index
                _pointLightDirection[i] = instance.DirectionData;
                int sourceIndex = _enabledPointIDs[i];
                int resolvedCustomId = _pointLightCustomIDs != null && sourceIndex < _pointLightCustomIDs.Length ? _pointLightCustomIDs[sourceIndex] : -1;
                float shaderCustomId = 0;
                if (resolvedCustomId >= 0) { // Positive IDs are LUT slices; negative IDs are custom cookie/cubemap projections
                    if (instance.ProjectionMode == 1) shaderCustomId = resolvedCustomId + 1; // 1: LUT
                    else if (instance.ProjectionMode == 2) shaderCustomId = -resolvedCustomId - 1; // 2: custom cookie or cubemap
                }
                _pointLightCustomId[i].x = shaderCustomId;
                float projectionSourceType = 0; // 0: static texture, 1: render texture, 2: material
                if (instance.ProjectionType == 2 && instance.CustomTextureMaterial != null) { // 2: material
                    projectionSourceType = 2; // Material projections are generated directly into the runtime array
                } else if (instance.ProjectionType == 1 && instance.CustomTexture != null && instance.CustomTextureIsRenderTexture) { // 1: texture
                    projectionSourceType = 1; // RenderTexture projections can be refreshed from their source every frame
                }
                // Pack projection type and range data into the remaining custom ID channels
                _pointLightCustomId[i].y = projectionSourceType;
                _pointLightCustomId[i].z = instance.SquaredRange;
                int resolvedShadowId = _pointLightShadowIDs != null && sourceIndex < _pointLightShadowIDs.Length ? _pointLightShadowIDs[sourceIndex] : -1;
                bool hasShadow = ShadowMapsCount > 0 && resolvedShadowId >= 0 && resolvedShadowId < ShadowMapsCount;
                if (hasShadow) _activeShadowCount++;
                bool useLocalSpaceShadows = hasShadow && !instance.WorldSpaceShadows;
                float shadowMapID = hasShadow ? (useLocalSpaceShadows ? -resolvedShadowId - 1 : resolvedShadowId + 1) : 0;
                float shadowBias = hasShadow ? Mathf.Max(instance.ShadowBias, 0) : 0;
                // Pack shadow map ID, bias, smoothness, and per-light sharpness for the shader
                _pointLightCustomId[i].w = 0;
                _pointLightShadowData[i].x = shadowMapID;
                _pointLightShadowData[i].y = shadowBias;
                _pointLightShadowData[i].z = hasShadow ? Mathf.Max(instance.ShadowBiasSmoothness, 0) : 0;
                _pointLightShadowData[i].w = hasShadow ? Mathf.Clamp01(instance.ShadowSharpness) : 0;
                if (useLocalSpaceShadows) { // Local-space shadows need bake rotation relative to the current light rotation
                    Quaternion shadowRotation = instance.ShadowBakeRotation * Quaternion.Inverse(instance.transform.rotation);
                    _pointLightShadowReprojectionData[i].x = shadowRotation.x;
                    _pointLightShadowReprojectionData[i].y = shadowRotation.y;
                    _pointLightShadowReprojectionData[i].z = shadowRotation.z;
                    _pointLightShadowReprojectionData[i].w = shadowRotation.w;
                } else { // World-space shadows use the baked world position directly
                    _pointLightShadowReprojectionData[i].x = instance.ShadowBakePosition.x;
                    _pointLightShadowReprojectionData[i].y = instance.ShadowBakePosition.y;
                    _pointLightShadowReprojectionData[i].z = instance.ShadowBakePosition.z;
                    _pointLightShadowReprojectionData[i].w = hasShadow ? 1 : 0;
                }
            }

            bool isAtlas = LightVolumeAtlas != null;

            // Upload Light Volumes version
            VRCShader.SetGlobalFloat(_lightVolumeVersionID, Version);

            // Disable the Light Volumes system if no atlas or no volumes are active
            if ((!isAtlas || _enabledCount == 0) && _pointLightCount == 0) {
                SetDisabledShaderState();
                _isUpdatingVolumes = false;
                return;
            }

            // Upload the 3D atlas texture and its parameters
            if (isAtlas) {
                VRCShader.SetGlobalTexture(_lightVolumeID, LightVolumeAtlas);
            }

            // Regular Light Volumes
            VRCShader.SetGlobalFloat(_lightVolumeCountID, _enabledCount);
            VRCShader.SetGlobalFloat(_lightVolumeAdditiveCountID, _additiveCount);

            // Upload whether Light Probes Blending is enabled in the scene
            VRCShader.SetGlobalFloat(_lightVolumeProbesBlendID, LightProbesBlending ? 1 : 0);
            VRCShader.SetGlobalFloat(_lightVolumeSharpBoundsID, SharpBounds ? 1 : 0);

            // Upload maximum additive overdraw
            VRCShader.SetGlobalFloat(_lightVolumeAdditiveMaxOverdrawID, AdditiveMaxOverdraw);

            if (_enabledCount != 0) {
                // All light volume inverse edge smoothing data
                VRCShader.SetGlobalVectorArray(_lightVolumeInvLocalEdgeSmoothID, _invLocalEdgeSmooth);

                // All light volume UVW data
                VRCShader.SetGlobalVectorArray(_lightVolumeUvwScaleID, _boundsUvwScale);

                // Volume transform matrices
                VRCShader.SetGlobalMatrixArray(_lightVolumeInvWorldMatrixID, _invWorldMatrix);

                // Volume relative rotations
                VRCShader.SetGlobalVectorArray(_lightVolumeRotationQuaternionID, _relativeRotationQuaternion);

                // Volume color correction data
                VRCShader.SetGlobalVectorArray(_lightVolumeColorID, _colors);

                // Legacy data upload
                VRCShader.SetGlobalVectorArray(_lightVolumeUvwID, _boundsUvw);
                VRCShader.SetGlobalVectorArray(_lightVolumeRotationID, _relativeRotation);
            }

            // Point Lights
            VRCShader.SetGlobalFloat(_pointLightCountID, _pointLightCount);
            VRCShader.SetGlobalFloat(_pointLightCubeCountID, CubemapsCount);
            int shadowCount = _activeShadowCount > 0 ? ShadowMapsCount : 0;
            VRCShader.SetGlobalFloat(_pointLightShadowCountID, shadowCount);
            VRCShader.SetGlobalVector(_pointLightShadowResolutionID, new Vector4(ShadowTexturesWidth, ShadowTexturesHeight, 0, 0));
            if (_pointLightCount != 0) { // Skip point light array uploads when no point lights are active
                VRCShader.SetGlobalVectorArray(_pointLightColorID, _pointLightColor);
                VRCShader.SetGlobalVectorArray(_pointLightPositionID, _pointLightPosition);
                VRCShader.SetGlobalVectorArray(_pointLightDirectionID, _pointLightDirection);
                VRCShader.SetGlobalVectorArray(_pointLightCustomIdID, _pointLightCustomId);
                if (_activeShadowCount > 0) { // Shadow arrays are uploaded only when at least one enabled point light uses shadows
                    VRCShader.SetGlobalVectorArray(_pointLightShadowDataID, _pointLightShadowData);
                    VRCShader.SetGlobalVectorArray(_pointLightShadowReprojectionDataID, _pointLightShadowReprojectionData);
                }
                VRCShader.SetGlobalFloat(_lightBrightnessCutoffID, LightsBrightnessCutoff);
                VRCShader.SetGlobalFloat(_areaLightBrightnessCutoffID, LightsBrightnessCutoff); // Legacy
            }
            if(CustomTextures != null) {
                VRCShader.SetGlobalTexture(_pointLightTextureID, CustomTextures);
            }
            if (_activeShadowCount > 0 && ShadowTextures != null) {
                VRCShader.SetGlobalTexture(_pointLightShadowTextureID, ShadowTextures);
            }

            // Upload whether Light Volumes are enabled in the scene. Uses the version number when enabled
            VRCShader.SetGlobalFloat(_lightVolumeEnabledID, 1);
            _isUpdatingVolumes = false;
        }
    }
}
