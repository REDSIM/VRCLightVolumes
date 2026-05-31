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
        private const int DynamicUpdateFlagLightVolumes = 1;
        private const int DynamicUpdateFlagPointLights = 2;
        private const int DynamicUpdateFlagFullRebuild = 4;
        private const RenderTextureFormat FixedCustomTexturesFormat = RenderTextureFormat.ARGBHalf;
        private const string CustomRenderTextureInfoProperty = "_CustomRenderTextureInfo";

        [Header("Light Volume Atlas")]
        [Tooltip("Combined Texture3D containing all baked Light Volume data. This field is not used at runtime, see LightVolumeAtlas instead. It specifies the base for the post process chain, if given.")]
        public Texture3D LightVolumeAtlasBase;
        [Tooltip("Combined texture containing all Light Volumes' textures.")]
        public Texture LightVolumeAtlas;

        [Header("Point Light Volumes")]
        [Tooltip("Width of each runtime point light projection texture slice.")]
        public int CustomTexturesWidth = 128;
        [Tooltip("Height of each runtime point light projection texture slice.")]
        public int CustomTexturesHeight = 128;
        [Tooltip("The minimum brightness at a point due to lighting from a Point Light Volume, before the light is culled. Larger values will result in better performance, but light attenuation will be less physically correct.")]
        public float LightsBrightnessCutoff = 0.35f;
        [Tooltip("Width of each runtime shadow cubemap face.")]
        public int ShadowTexturesWidth = 128;
        [Tooltip("Height of each runtime shadow cubemap face.")]
        public int ShadowTexturesHeight = 128;
        [Tooltip("Precision used for baked EVSM shadow cubemaps and the runtime shadow texture array. Half is cheaper, Float reduces EVSM precision artifacts.")]
        public int ShadowTextureFormat = 1;

        [Header("Visuals")]
        [Tooltip("When enabled, areas outside Light Volumes fall back to light probes. Otherwise, the Light Volume with the smallest weight is used as fallback. It also improves performance.")]
        public bool LightProbesBlending = true;
        [Tooltip("Disables smooth blending with areas outside Light Volumes. Use it if your entire scene's play area is covered by Light Volumes. It also improves performance.")]
        public bool SharpBounds = true;
        [Tooltip("Automatically updates most of the volumes properties in runtime. Enabling/Disabling, Color and Intensity updates automatically even without this option enabled. Position, Rotation and Scale gets updated only for volumes that are marked dynamic.")]
        public bool AutoUpdateVolumes = true;
        [Tooltip("Automatically updates dynamic point light cookie and shadow texture sources in runtime.")]
        public bool AutoUpdateTextures = true;
        [Tooltip("Limits the maximum number of additive volumes and point light volumes that can affect a single pixel. If you have many dynamic additive or point light volumes that may overlap, it's good practice to limit overdraw to maintain performance.")]
        public int AdditiveMaxOverdraw = 4;
        [Tooltip("Disables min/max brightness limits for modern avatar shaders such as lilToon or Poiyomi. Check this only if you're sure your scene lighting is properly configured.")]
        public bool ForceSceneLighting = false;

        [Header("Runtime Registries")]
        [Tooltip("All Light Volume instances sorted in decreasing order by weight. You can enable or disable volumes game objects at runtime. Manually disabling unnecessary volumes improves performance.")]
        public LightVolumeInstance[] LightVolumeInstances = new LightVolumeInstance[0];
        [Tooltip("All Point Light Volume instances. You can enable or disable point light volumes game objects at runtime. Manually disabling unnecessary point light volumes improves performance.")]
        public PointLightVolumeInstance[] PointLightVolumeInstances = new PointLightVolumeInstance[0];

        [Header("Runtime Textures")]
        [Tooltip("Runtime texture array used for point light cubemaps, LUTs and cookies.")]
        public RenderTexture CustomTextures;
        [Tooltip("Cubemaps count that stored in CustomTextures. Cubemap array elements starts from the beginning, 6 elements each.")]
        public int CubemapsCount = 0;
        [Tooltip("Runtime texture array that stores per-light shadow maps.")]
        public RenderTexture ShadowTextures;
        [Tooltip("Shadow maps count stored in ShadowTextures. Each cubemap uses 6 array elements.")]
        public int ShadowMapsCount = 0;

        // Material used to copy cubemap source faces into the animated projection texture array
        [HideInInspector] public Material CubemapFaceMaterial;

        // Custom projection texture cache state
        // Counts describe active prefixes, arrays stay reusable to avoid runtime allocations
        private bool _customTexturesInitialized = false;
        private int _customTexturesDepth = 0;
        private int _customCubemapTextureCount = 0;
        private int _customCubemapMaterialCount = 0;
        private int _customSingleTextureCount = 0;
        private int _customSingleMaterialCount = 0;

        // Unique custom projection sources split by source shape and source type
        private Texture[] _customCubemapTextures = new Texture[MaxPointLightCount];
        private Material[] _customCubemapMaterials = new Material[MaxPointLightCount];
        private Texture[] _customSingleTextures = new Texture[MaxPointLightCount];
        private Material[] _customSingleMaterials = new Material[MaxPointLightCount];
        private int[] _customCubemapTextureModes = new int[MaxPointLightCount];
        private bool[] _customCubemapTextureAutoUpdates = new bool[MaxPointLightCount];
        private bool[] _customCubemapMaterialAutoUpdates = new bool[MaxPointLightCount];
        private bool[] _customSingleTextureAutoUpdates = new bool[MaxPointLightCount];
        private bool[] _customSingleMaterialAutoUpdates = new bool[MaxPointLightCount];
        private int[] _pointLightCustomIDs = new int[MaxPointLightCount];
        private int[] _customSourceTypes = new int[MaxPointLightCount];
        private bool _hasAutoCustomTextureUpdates = false;

        // Shadow texture cache state
        // Counts describe active prefixes, arrays stay reusable to avoid runtime allocations
        private bool _shadowTexturesInitialized = false;
        private int _shadowTexturesDepth = 0;
        private int _shadowCubemapTextureCount = 0;
        private int _shadowCubemapMaterialCount = 0;

        // Unique shadow sources and resolved per-point-light shadow IDs
        private Texture[] _shadowCubemapTextures = new Texture[MaxPointLightCount];
        private Material[] _shadowCubemapMaterials = new Material[MaxPointLightCount];
        private int[] _shadowCubemapTextureModes = new int[MaxPointLightCount];
        private bool[] _shadowCubemapTextureAutoUpdates = new bool[MaxPointLightCount];
        private bool[] _shadowCubemapMaterialAutoUpdates = new bool[MaxPointLightCount];
        private int[] _pointLightShadowIDs = new int[MaxPointLightCount];
        private bool[] _shadowSourceIsMaterial = new bool[MaxPointLightCount];
        private bool _hasAutoShadowTextureUpdates = false;

        // Dummy source texture required by VRCGraphics material blits when a material generates pixels without a real input texture
        private RenderTexture _runtimeMaterialBlitInputTexture;

        // Runtime state mirrors and dirty flags
        private bool _isRangeDirty = false;
        // Tracks one-time shader array initialization in runtime while still allowing editor property IDs to refresh
        private bool _isInitialized = false;
        // Prevents serialized registry cleanup from running every frame
        private bool _isRegistrySanitized = false;
        private float _prevLightsBrightnessCutoff = 0.35f;

        private Vector4 _customRenderTextureInfo;

        // Light Volume shader upload buffers
        private int _enabledCount = 0;
        private int _additiveCount = 0;
        private Vector4[] _invLocalEdgeSmooth = new Vector4[MaxLightVolumeCount];
        private Vector4[] _colors = new Vector4[MaxLightVolumeCount];
        private Vector4[] _boundsUvwScale = new Vector4[MaxLightVolumeUvwScaleVectors];
        private Vector4[] _boundsUvw = new Vector4[MaxLightVolumeLegacyUvwVectors];
        private Vector4[] _relativeRotation = new Vector4[MaxLightVolumeRotationVectors];

        // Point Light shader upload buffers
        private int _pointLightCount = 0;
        private int _activeShadowCount = 0;
        private int[] _enabledPointIDs = new int[MaxPointLightCount];
        private Vector4[] _pointLightPosition = new Vector4[MaxPointLightCount];
        private Vector4[] _pointLightColor = new Vector4[MaxPointLightCount];
        private Vector4[] _pointLightDirection = new Vector4[MaxPointLightCount];
        private Vector4[] _pointLightCustomId = new Vector4[MaxPointLightCount];
        private Vector4[] _pointLightShadowReprojectionData = new Vector4[MaxPointLightCount];

        // Matrix upload buffer for active regular volumes
        private Matrix4x4[] _invWorldMatrix = new Matrix4x4[MaxLightVolumeCount];

        // Dynamic volume transform watch cache used by AutoUpdateVolumes
        private int _dynamicLightVolumeCount = 0;
        private int _dynamicPointLightVolumeCount = 0;

        // Dynamic transform references and last uploaded values for cheap change detection
        private LightVolumeInstance[] _dynamicLightVolumeInstances = new LightVolumeInstance[MaxLightVolumeCount];
        private PointLightVolumeInstance[] _dynamicPointLightVolumeInstances = new PointLightVolumeInstance[MaxPointLightCount];
        private Transform[] _dynamicLightVolumeTransforms = new Transform[MaxLightVolumeCount];
        private Transform[] _dynamicPointLightVolumeTransforms = new Transform[MaxPointLightCount];
        private int[] _dynamicLightVolumeShaderIndices = new int[MaxLightVolumeCount];
        private int[] _dynamicPointLightVolumeShaderIndices = new int[MaxPointLightCount];
        private Vector3[] _dynamicLightVolumePositions = new Vector3[MaxLightVolumeCount];
        private Quaternion[] _dynamicLightVolumeRotations = new Quaternion[MaxLightVolumeCount];
        private Vector3[] _dynamicLightVolumeScales = new Vector3[MaxLightVolumeCount];
        private Vector3[] _dynamicPointLightVolumePositions = new Vector3[MaxPointLightCount];
        private Quaternion[] _dynamicPointLightVolumeRotations = new Quaternion[MaxPointLightCount];
        private Vector3[] _dynamicPointLightVolumeScales = new Vector3[MaxPointLightCount];

        // Active registry index buffer for compact shader uploads
        private int[] _enabledIDs = new int[MaxLightVolumeCount];

        // Public API for other UdonSharp scripts
        public int EnabledCount => _enabledCount;
        public int[] EnabledIDs => _enabledIDs;

        // Delayed update loop state
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
        private int _lightVolumeRotationID;
        private int _lightVolumeInvWorldMatrixID;
        private int _lightVolumeUvwScaleID;
        private int _lightVolumeUvwID;
        private int _lightVolumeOcclusionCountID;
        // Point Lights
        private int _pointLightPositionID;
        private int _pointLightColorID;
        private int _pointLightDirectionID;
        private int _pointLightCustomIdID;
        private int _pointLightCountID;
        private int _pointLightCubeCountID;
        private int _pointLightTextureID;
        private int _pointLightShadowReprojectionDataID;
        private int _pointLightShadowCountID;
        private int _pointLightShadowTextureID;
        private int _lightBrightnessCutoffID;
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

        // Finds a Light Volume reference in a registry prefix without using generic Array helpers
        private int FindLightVolumeIndex(LightVolumeInstance[] array, LightVolumeInstance instance, int startIndex, int count) {
            if (array == null) return -1;
            int endIndex = startIndex + count;
            for (int i = startIndex; i < endIndex; i++) {
                if (array[i] == instance) return i;
            }
            return -1;
        }

        // Finds a Point Light Volume reference in a registry prefix without using generic Array helpers
        private int FindPointLightVolumeIndex(PointLightVolumeInstance[] array, PointLightVolumeInstance instance, int startIndex, int count) {
            if (array == null) return -1;
            int endIndex = startIndex + count;
            for (int i = startIndex; i < endIndex; i++) {
                if (array[i] == instance) return i;
            }
            return -1;
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
            _lightVolumeRotationID = VRCShader.PropertyToID("_UdonLightVolumeRotation");
            _lightVolumeUvwScaleID = VRCShader.PropertyToID("_UdonLightVolumeUvwScale");
            _lightVolumeUvwID = VRCShader.PropertyToID("_UdonLightVolumeUvw");
            _lightVolumeOcclusionCountID = VRCShader.PropertyToID("_UdonLightVolumeOcclusionCount");
            // Point Lights
            _pointLightPositionID = VRCShader.PropertyToID("_UdonPointLightVolumePosition");
            _pointLightColorID = VRCShader.PropertyToID("_UdonPointLightVolumeColor");
            _pointLightDirectionID = VRCShader.PropertyToID("_UdonPointLightVolumeDirection");
            _pointLightCountID = VRCShader.PropertyToID("_UdonPointLightVolumeCount");
            _pointLightCustomIdID = VRCShader.PropertyToID("_UdonPointLightVolumeCustomID");
            _pointLightCubeCountID = VRCShader.PropertyToID("_UdonPointLightVolumeCubeCount");
            _pointLightTextureID = VRCShader.PropertyToID("_UdonPointLightVolumeTexture");
            _pointLightShadowReprojectionDataID = VRCShader.PropertyToID("_UdonPointLightVolumeShadowReprojectionData");
            _pointLightShadowCountID = VRCShader.PropertyToID("_UdonPointLightVolumeShadowCount");
            _pointLightShadowTextureID = VRCShader.PropertyToID("_UdonPointLightVolumeShadowTexture");
            _lightBrightnessCutoffID = VRCShader.PropertyToID("_UdonLightBrightnessCutoff");
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
            VRCShader.SetGlobalVectorArray(_lightVolumeRotationID, _relativeRotation);
            VRCShader.SetGlobalVectorArray(_lightVolumeUvwScaleID, _boundsUvwScale);
            VRCShader.SetGlobalVectorArray(_lightVolumeUvwID, _boundsUvw);
            VRCShader.SetGlobalFloat(_lightVolumeOcclusionCountID, 0);
            // Point Lights
            VRCShader.SetGlobalVectorArray(_pointLightPositionID, _pointLightPosition);
            VRCShader.SetGlobalVectorArray(_pointLightColorID, _pointLightColor);
            VRCShader.SetGlobalVectorArray(_pointLightDirectionID, _pointLightDirection);
            VRCShader.SetGlobalVectorArray(_pointLightCustomIdID, _pointLightCustomId);
            VRCShader.SetGlobalVectorArray(_pointLightShadowReprojectionDataID, _pointLightShadowReprojectionData);
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
            VRCShader.SetGlobalFloat(_lightVolumeOcclusionCountID, 0);
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
#if UDONSHARP
            _isUpdateProcessRunning = false;
#else
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
            _volumeDataUpdateRequested = false;
        }

        // Clears manager-owned runtime texture outputs before rebuilding them
        private void ResetRuntimeTextureArrays() {
            ResetCustomTexturesGlobal();
            ResetShadowTexturesGlobal();
            ReleaseRuntimeRenderTexture(_runtimeMaterialBlitInputTexture); // Release the dummy material-blit source alongside generated arrays
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
            int existingIndex = FindLightVolumeIndex(LightVolumeInstances, lightVolume, 0, count);
            if (existingIndex >= 0) {
                lightVolume.LightVolumeManager = this;
                return;
            }
            // Fill the first stale/null slot before growing the registry array
            int emptyIndex = FindLightVolumeIndex(LightVolumeInstances, null, 0, count);
            if (emptyIndex >= 0) {
                LightVolumeInstances[emptyIndex] = lightVolume;
                lightVolume.LightVolumeManager = this;
                return;
            }
            // No empty slot exists, so grow the registry array
            LightVolumeInstance[] targetArray = new LightVolumeInstance[count + 1];
            Array.Copy(LightVolumeInstances, targetArray, count);
            targetArray[count] = lightVolume;
            lightVolume.LightVolumeManager = this;
            LightVolumeInstances = targetArray;
        }

        // Removes Light Volume references from the light volume registry without resizing it
        public void UnregisterLightVolume(LightVolumeInstance lightVolume) {
            if (lightVolume == null) return;
            EnsureRegistryArrays();
            int count = LightVolumeInstances.Length;
            int index = FindLightVolumeIndex(LightVolumeInstances, lightVolume, 0, count);
            // Clear all duplicate registrations left by serialized data or previous versions
            while (index >= 0) {
                LightVolumeInstances[index] = null;
                int nextIndex = index + 1;
                if (nextIndex >= count) break;
                index = FindLightVolumeIndex(LightVolumeInstances, lightVolume, nextIndex, count - nextIndex); // Continue after the cleared slot to catch later duplicates
            }
        }

        // Initializes a Point Light Volume by adding it to the point light volume registry
        public void InitializePointLightVolume(PointLightVolumeInstance pointLightVolume) {
            if (pointLightVolume == null) return;
            EnsureRegistryArrays();
            int count = PointLightVolumeInstances.Length;
            // Reuse an existing slot so repeated OnEnable calls do not duplicate the same point light
            int existingIndex = FindPointLightVolumeIndex(PointLightVolumeInstances, pointLightVolume, 0, count);
            if (existingIndex >= 0) {
                pointLightVolume.LightVolumeManager = this;
                return;
            }
            // Fill the first stale/null slot before growing the registry array
            int emptyIndex = FindPointLightVolumeIndex(PointLightVolumeInstances, null, 0, count);
            if (emptyIndex >= 0) {
                PointLightVolumeInstances[emptyIndex] = pointLightVolume;
                pointLightVolume.LightVolumeManager = this;
                _customTexturesInitialized = false;
                _shadowTexturesInitialized = false;
                return;
            }
            // No empty slot exists, so grow the registry array
            PointLightVolumeInstance[] targetArray = new PointLightVolumeInstance[count + 1];
            Array.Copy(PointLightVolumeInstances, targetArray, count);
            targetArray[count] = pointLightVolume;
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
            int index = FindPointLightVolumeIndex(PointLightVolumeInstances, pointLightVolume, 0, count);
            while (index >= 0) {
                PointLightVolumeInstances[index] = null;
                _customTexturesInitialized = false;
                _shadowTexturesInitialized = false;
                int nextIndex = index + 1;
                if (nextIndex >= count) break;
                index = FindPointLightVolumeIndex(PointLightVolumeInstances, pointLightVolume, nextIndex, count - nextIndex);
            }
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
                    continue;
                }
                // Keep the first occurrence so serialized duplicates do not shift runtime light IDs
                if (FindLightVolumeIndex(LightVolumeInstances, instance, 0, i) >= 0) LightVolumeInstances[i] = null;
            }

            int pointLightCount = PointLightVolumeInstances.Length;
            for (int i = 0; i < pointLightCount; i++) { // Point light registry changes also invalidate projection and shadow texture caches
                PointLightVolumeInstance instance = PointLightVolumeInstances[i];
                if (instance == null) continue;
                instance.LightVolumeManager = this;
                // Inactive point lights should not reserve custom texture or shadow slots
                if (!instance.gameObject.activeInHierarchy) {
                    PointLightVolumeInstances[i] = null;
                    _customTexturesInitialized = false;
                    _shadowTexturesInitialized = false;
                    continue;
                }
                // Keep the first occurrence and force runtime texture IDs to rebuild if a duplicate was removed
                if (FindPointLightVolumeIndex(PointLightVolumeInstances, instance, 0, i) >= 0) {
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

        // Ensures reusable custom texture source arrays can cover the current point light registry
        private void EnsureCustomTextureCacheCapacity(int count) {
            int targetCapacity = count > MaxPointLightCount ? count : MaxPointLightCount;
            int capacity = _pointLightCustomIDs != null ? _pointLightCustomIDs.Length : 0;
            // Keep existing arrays when their reusable capacity already covers the registry
            if (capacity >= targetCapacity && _customCubemapTextures != null && _customCubemapTextures.Length >= targetCapacity && _customCubemapMaterials != null && _customCubemapMaterials.Length >= targetCapacity && _customSingleTextures != null && _customSingleTextures.Length >= targetCapacity && _customSingleMaterials != null && _customSingleMaterials.Length >= targetCapacity && _customCubemapTextureModes != null && _customCubemapTextureModes.Length >= targetCapacity && _customCubemapTextureAutoUpdates != null && _customCubemapTextureAutoUpdates.Length >= targetCapacity && _customCubemapMaterialAutoUpdates != null && _customCubemapMaterialAutoUpdates.Length >= targetCapacity && _customSingleTextureAutoUpdates != null && _customSingleTextureAutoUpdates.Length >= targetCapacity && _customSingleMaterialAutoUpdates != null && _customSingleMaterialAutoUpdates.Length >= targetCapacity && _customSourceTypes != null && _customSourceTypes.Length >= targetCapacity) return;

            // Grow all related arrays together so source IDs keep matching registry indices
            _customCubemapTextures = new Texture[targetCapacity];
            _customCubemapMaterials = new Material[targetCapacity];
            _customSingleTextures = new Texture[targetCapacity];
            _customSingleMaterials = new Material[targetCapacity];
            _customCubemapTextureModes = new int[targetCapacity];
            _customCubemapTextureAutoUpdates = new bool[targetCapacity];
            _customCubemapMaterialAutoUpdates = new bool[targetCapacity];
            _customSingleTextureAutoUpdates = new bool[targetCapacity];
            _customSingleMaterialAutoUpdates = new bool[targetCapacity];
            _pointLightCustomIDs = new int[targetCapacity];
            _customSourceTypes = new int[targetCapacity];
        }

        // Clears reusable custom texture source cache entries without reallocating their arrays
        private void ClearCustomTextureSourceCache() {
            // Clear only previously active source prefixes; the arrays can be much larger than active counts
            for (int i = 0; i < _customCubemapTextureCount; i++) {
                _customCubemapTextures[i] = null;
                _customCubemapTextureModes[i] = 0;
                _customCubemapTextureAutoUpdates[i] = false;
            }
            for (int i = 0; i < _customCubemapMaterialCount; i++) {
                _customCubemapMaterials[i] = null;
                _customCubemapMaterialAutoUpdates[i] = false;
            }
            for (int i = 0; i < _customSingleTextureCount; i++) {
                _customSingleTextures[i] = null;
                _customSingleTextureAutoUpdates[i] = false;
            }
            for (int i = 0; i < _customSingleMaterialCount; i++) {
                _customSingleMaterials[i] = null;
                _customSingleMaterialAutoUpdates[i] = false;
            }

            int idCount = _pointLightCustomIDs != null ? _pointLightCustomIDs.Length : 0;
            // Per-instance ID arrays use registry indices directly, so the whole reusable capacity is reset
            for (int i = 0; i < idCount; i++) {
                _pointLightCustomIDs[i] = -1;
                _customSourceTypes[i] = 0;
            }

            _customCubemapTextureCount = 0;
            _customCubemapMaterialCount = 0;
            _customSingleTextureCount = 0;
            _customSingleMaterialCount = 0;
            CubemapsCount = 0;
            _customTexturesDepth = 0;
            _hasAutoCustomTextureUpdates = false;
        }

        // Clears active custom texture globals when no point light uses a projection source
        private void ResetCustomTexturesGlobal() {
            ReleaseRuntimeRenderTexture(CustomTextures);
            CustomTextures = null;
            ClearCustomTextureSourceCache();
        }

        // Builds deduplicated source arrays and per-instance shader IDs for the runtime cookie texture array
        private void BuildCustomTextureSourceCache() {
            int count = PointLightVolumeInstances != null ? PointLightVolumeInstances.Length : 0;
            EnsureCustomTextureCacheCapacity(count);
            ClearCustomTextureSourceCache();

            int cubemapTextureCount = 0;
            int cubemapMaterialCount = 0;
            int singleTextureCount = 0;
            int singleMaterialCount = 0;

            // Walk the registry once and collect unique texture/material sources in reusable arrays
            for (int i = 0; i < count; i++) { // Start every point light unresolved; supported sources assign a local deduplicated index below
                PointLightVolumeInstance instance = PointLightVolumeInstances[i];
                if (!HasActiveCustomTextureSource(instance)) continue;

                bool usesCubemapProjection = instance.LightType == 0 && instance.ProjectionMode == 2; // 0: point, 2: custom cookie or cubemap
                Texture textureSource = instance.ProjectionType == 1 ? instance.CustomTexture : null; // 1: texture
                if (textureSource != null) {
                    if (usesCubemapProjection) { // Point light cubemap sources reserve six consecutive slices
                        int index = FindTextureIndex(_customCubemapTextures, cubemapTextureCount, textureSource);
                        int textureMode = instance.CustomTextureIsCubemap ? 2 : (instance.CustomTextureHasDepthSlices ? 1 : 0);
                        if (index < 0) { // Append each unique source once so matching lights share the same texture ID
                            index = cubemapTextureCount;
                            _customCubemapTextures[cubemapTextureCount] = textureSource;
                            _customCubemapTextureModes[cubemapTextureCount] = textureMode;
                            cubemapTextureCount++;
                        } else {
                            if (textureMode > _customCubemapTextureModes[index]) _customCubemapTextureModes[index] = textureMode;
                        }
                        _pointLightCustomIDs[i] = index;
                        _customSourceTypes[i] = 1;
                        if (instance.AutoUpdateCustomTexture) {
                            _customCubemapTextureAutoUpdates[index] = true;
                            _hasAutoCustomTextureUpdates = true;
                        }
                    } else { // Spot and LUT/cookie projections use one slice per unique source
                        int index = FindTextureIndex(_customSingleTextures, singleTextureCount, textureSource);
                        if (index < 0) { // Append each unique source once so matching lights share the same texture ID
                            index = singleTextureCount;
                            _customSingleTextures[singleTextureCount] = textureSource;
                            singleTextureCount++;
                        }
                        _pointLightCustomIDs[i] = index;
                        _customSourceTypes[i] = 3;
                        if (instance.AutoUpdateCustomTexture) {
                            _customSingleTextureAutoUpdates[index] = true;
                            _hasAutoCustomTextureUpdates = true;
                        }
                    }
                    continue;
                }

                Material materialSource = instance.ProjectionType == 2 ? instance.CustomTextureMaterial : null; // 2: material
                if (materialSource != null) {
                    if (usesCubemapProjection) { // Cubemap materials are rendered as six generated faces
                        int index = FindMaterialIndex(_customCubemapMaterials, cubemapMaterialCount, materialSource);
                        if (index < 0) { // Append each unique material once so matching lights share the same texture ID
                            index = cubemapMaterialCount;
                            _customCubemapMaterials[cubemapMaterialCount] = materialSource;
                            cubemapMaterialCount++;
                        }
                        _pointLightCustomIDs[i] = index;
                        _customSourceTypes[i] = 2;
                        if (instance.AutoUpdateCustomTexture) {
                            _customCubemapMaterialAutoUpdates[index] = true;
                            _hasAutoCustomTextureUpdates = true;
                        }
                    } else { // Single-slice materials render directly into one projection slice
                        int index = FindMaterialIndex(_customSingleMaterials, singleMaterialCount, materialSource);
                        if (index < 0) { // Append each unique material once so matching lights share the same texture ID
                            index = singleMaterialCount;
                            _customSingleMaterials[singleMaterialCount] = materialSource;
                            singleMaterialCount++;
                        }
                        _pointLightCustomIDs[i] = index;
                        _customSourceTypes[i] = 4;
                        if (instance.AutoUpdateCustomTexture) {
                            _customSingleMaterialAutoUpdates[index] = true;
                            _hasAutoCustomTextureUpdates = true;
                        }
                    }
                }
            }

            _customCubemapTextureCount = cubemapTextureCount;
            _customCubemapMaterialCount = cubemapMaterialCount;
            _customSingleTextureCount = singleTextureCount;
            _customSingleMaterialCount = singleMaterialCount;
            CubemapsCount = cubemapTextureCount + cubemapMaterialCount;
            _customTexturesDepth = CubemapsCount * 6 + singleTextureCount + singleMaterialCount;
            // Convert local per-source indices into final texture-array slice IDs after final counts are known
            AssignPointLightCustomIDs(_customSourceTypes, cubemapTextureCount, singleTextureCount);
        }

        // Checks whether a point light currently contributes to shader data and texture cache membership
        private bool IsActivePointLight(PointLightVolumeInstance instance) {
            return instance != null && instance.gameObject.activeInHierarchy && instance.Intensity != 0 && instance.Color != Color.black;
        }

        // Checks whether a point light needs a custom projection source in the runtime texture array right now
        private bool HasActiveCustomTextureSource(PointLightVolumeInstance instance) {
            if (!IsActivePointLight(instance)) return false;
            if (instance.LightType == 2) return false; // 2: area; area lights do not use projection texture arrays
            if (instance.ProjectionMode == 0) return false; // 0: parametric
            return (instance.ProjectionType == 1 && instance.CustomTexture != null) || (instance.ProjectionType == 2 && instance.CustomTextureMaterial != null); // 1: texture, 2: material
        }

        // Checks whether a point light needs a shadow source in the runtime texture array right now
        private bool HasActiveShadowTextureSource(PointLightVolumeInstance instance) {
            return IsActivePointLight(instance) && instance.ShadowMapID >= 0 && (instance.ShadowMapTexture != null || instance.ShadowMapMaterial != null);
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
            int cubemapTextureCount = _customCubemapTextureCount;
            for (int i = 0; i < cubemapTextureCount; i++) {
                if (onlyAutoUpdates && !_customCubemapTextureAutoUpdates[i]) continue;
                BlitCubemapTexture(_customCubemapTextures[i], _customCubemapTextureModes[i], i * 6, CustomTextures);
            }

            // Cubemap material sources follow cubemap texture sources and are also rendered as six slices
            int cubemapMaterialCount = _customCubemapMaterialCount;
            for (int i = 0; i < cubemapMaterialCount; i++) {
                if (onlyAutoUpdates && !_customCubemapMaterialAutoUpdates[i]) continue;
                BlitCubemapMaterial(_customCubemapMaterials[i], (cubemapTextureCount + i) * 6, CustomTextures, _customTexturesDepth);
            }

            // Single-slice projection textures start after every cubemap slice
            int singleBaseSlice = CubemapsCount * 6;
            int singleTextureCount = _customSingleTextureCount;
            for (int i = 0; i < singleTextureCount; i++) {
                if (onlyAutoUpdates && !_customSingleTextureAutoUpdates[i]) continue;
                Texture sourceTexture = _customSingleTextures[i];
                if (sourceTexture == null) continue;
                VRCGraphics.Blit(sourceTexture, CustomTextures, 0, singleBaseSlice + i);
            }

            // Single-slice projection materials follow regular single-slice textures
            int singleMaterialCount = _customSingleMaterialCount;
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
            for (int i = 0; i < count; i++) {
                if (array[i] == texture) return i;
            }
            return -1;
        }

        // Finds a material reference in a fixed-size prefix of an array
        private int FindMaterialIndex(Material[] array, int count, Material material) {
            if (array == null || material == null) return -1;
            for (int i = 0; i < count; i++) {
                if (array[i] == material) return i;
            }
            return -1;
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

        // Updates one shadow texture-array slice for runtime bakers that already manage their own refresh loop
        public bool UpdatePointLightShadowTextureSlice(PointLightVolumeInstance instance, int sourceSlice) {
            if (instance == null) return false;
            Texture sourceTexture = instance.ShadowMapTexture;
            if (sourceTexture == null) return false;

            if (!_shadowTexturesInitialized || ShadowTextures == null || _shadowTexturesDepth <= 0) ReinitializeShadowTextures();
            if (ShadowTextures == null || _shadowTexturesDepth <= 0) return false;

            int shadowId = (int)instance.ShadowMapID;
            if (shadowId < 0) return false;

            int safeSourceSlice = Mathf.Clamp(sourceSlice, 0, 5);
            int targetSlice = shadowId * 6 + safeSourceSlice;
            if (targetSlice < 0 || targetSlice >= _shadowTexturesDepth) return false;

            if (instance.ShadowMapTextureIsCubemap) {
                BlitCubemapFace(sourceTexture, ShadowTextures, safeSourceSlice, targetSlice);
            } else {
                int directSourceSlice = instance.ShadowMapTextureHasDepthSlices ? safeSourceSlice : 0;
                VRCGraphics.Blit(sourceTexture, ShadowTextures, directSourceSlice, targetSlice);
            }

            return true;
        }

        // Ensures reusable shadow texture source arrays can cover the current point light registry
        private void EnsureShadowTextureCacheCapacity(int count) {
            int targetCapacity = count > MaxPointLightCount ? count : MaxPointLightCount;
            int capacity = _pointLightShadowIDs != null ? _pointLightShadowIDs.Length : 0;
            // Keep existing arrays when their reusable capacity already covers the registry
            if (capacity >= targetCapacity && _shadowCubemapTextures != null && _shadowCubemapTextures.Length >= targetCapacity && _shadowCubemapMaterials != null && _shadowCubemapMaterials.Length >= targetCapacity && _shadowCubemapTextureModes != null && _shadowCubemapTextureModes.Length >= targetCapacity && _shadowCubemapTextureAutoUpdates != null && _shadowCubemapTextureAutoUpdates.Length >= targetCapacity && _shadowCubemapMaterialAutoUpdates != null && _shadowCubemapMaterialAutoUpdates.Length >= targetCapacity && _shadowSourceIsMaterial != null && _shadowSourceIsMaterial.Length >= targetCapacity) return;

            // Grow all related arrays together so shadow IDs keep matching registry indices
            _shadowCubemapTextures = new Texture[targetCapacity];
            _shadowCubemapMaterials = new Material[targetCapacity];
            _shadowCubemapTextureModes = new int[targetCapacity];
            _shadowCubemapTextureAutoUpdates = new bool[targetCapacity];
            _shadowCubemapMaterialAutoUpdates = new bool[targetCapacity];
            _pointLightShadowIDs = new int[targetCapacity];
            _shadowSourceIsMaterial = new bool[targetCapacity];
        }

        // Clears reusable shadow texture source cache entries without reallocating their arrays
        private void ClearShadowTextureSourceCache() {
            // Clear only previously active source prefixes; the arrays can be much larger than active counts
            for (int i = 0; i < _shadowCubemapTextureCount; i++) {
                _shadowCubemapTextures[i] = null;
                _shadowCubemapTextureModes[i] = 0;
                _shadowCubemapTextureAutoUpdates[i] = false;
            }
            for (int i = 0; i < _shadowCubemapMaterialCount; i++) {
                _shadowCubemapMaterials[i] = null;
                _shadowCubemapMaterialAutoUpdates[i] = false;
            }

            int idCount = _pointLightShadowIDs != null ? _pointLightShadowIDs.Length : 0;
            // Per-instance ID arrays use registry indices directly, so the whole reusable capacity is reset
            for (int i = 0; i < idCount; i++) {
                _pointLightShadowIDs[i] = -1;
                _shadowSourceIsMaterial[i] = false;
            }

            _shadowCubemapTextureCount = 0;
            _shadowCubemapMaterialCount = 0;
            ShadowMapsCount = 0;
            _shadowTexturesDepth = 0;
            _hasAutoShadowTextureUpdates = false;
        }

        // Clears active shadow texture globals when no point light uses a shadow source
        private void ResetShadowTexturesGlobal() {
            ReleaseRuntimeRenderTexture(ShadowTextures);
            ShadowTextures = null;
            ClearShadowTextureSourceCache();
        }

        // Builds deduplicated source arrays and per-instance shader IDs for the runtime shadow texture array
        private void BuildShadowTextureSourceCache() {
            int count = PointLightVolumeInstances != null ? PointLightVolumeInstances.Length : 0;
            EnsureShadowTextureCacheCapacity(count);
            ClearShadowTextureSourceCache();

            int cubemapTextureCount = 0;
            int cubemapMaterialCount = 0;

            // Walk the registry once and collect unique shadow sources in reusable arrays
            for (int i = 0; i < count; i++) {
                // Start every point light unresolved; only valid shadow sources receive a shadow texture ID
                PointLightVolumeInstance instance = PointLightVolumeInstances[i];
                if (!IsActivePointLight(instance)) continue;
                if (instance.ShadowMapID < 0 || (instance.ShadowMapTexture == null && instance.ShadowMapMaterial == null)) {
                    instance.ShadowMapID = -1;
                    continue;
                }
                // Prefer texture shadows over material shadows when both fields are assigned
                Texture textureSource = instance.ShadowMapTexture;
                if (textureSource != null) { // Shadow textures are deduplicated before being copied into the runtime array
                    int index = FindTextureIndex(_shadowCubemapTextures, cubemapTextureCount, textureSource);
                    int textureMode = instance.ShadowMapTextureIsCubemap ? 2 : (instance.ShadowMapTextureHasDepthSlices ? 1 : 0);
                    if (index < 0) { // Append each unique texture once; matching lights reuse the same shadow ID
                        index = cubemapTextureCount;
                        _shadowCubemapTextures[cubemapTextureCount] = textureSource;
                        _shadowCubemapTextureModes[cubemapTextureCount] = textureMode;
                        cubemapTextureCount++;
                    } else { // Keep the most expressive mode when the same texture appears with different source metadata
                        if (textureMode > _shadowCubemapTextureModes[index]) _shadowCubemapTextureModes[index] = textureMode;
                    }
                    if (instance.AutoUpdateShadowMap) {
                        _shadowCubemapTextureAutoUpdates[index] = true;
                        _hasAutoShadowTextureUpdates = true;
                    }
                    _pointLightShadowIDs[i] = index;
                    continue;
                }
                // Material shadows are rendered after texture shadows, but share the same final cubemap array
                Material materialSource = instance.ShadowMapMaterial;
                if (materialSource != null) {
                    int index = FindMaterialIndex(_shadowCubemapMaterials, cubemapMaterialCount, materialSource);
                    if (index < 0) { // Append each unique material once; matching lights reuse the same shadow ID
                        index = cubemapMaterialCount;
                        _shadowCubemapMaterials[cubemapMaterialCount] = materialSource;
                        cubemapMaterialCount++;
                    }
                    if (instance.AutoUpdateShadowMap) {
                        _shadowCubemapMaterialAutoUpdates[index] = true;
                        _hasAutoShadowTextureUpdates = true;
                    }
                    _pointLightShadowIDs[i] = index;
                    _shadowSourceIsMaterial[i] = true;
                }
            }

            _shadowCubemapTextureCount = cubemapTextureCount;
            _shadowCubemapMaterialCount = cubemapMaterialCount;
            ShadowMapsCount = cubemapTextureCount + cubemapMaterialCount;
            _shadowTexturesDepth = ShadowMapsCount * 6;
            // Material shadow sources are stored after texture sources in the final array
            for (int i = 0; i < count; i++) {
                int index = _pointLightShadowIDs[i];
                if (index < 0) continue;
                if (_shadowSourceIsMaterial[i]) index += cubemapTextureCount;
                _pointLightShadowIDs[i] = index;
                PointLightVolumeInstance instance = PointLightVolumeInstances[i];
                if (instance != null) instance.ShadowMapID = index;
            }
        }

        // Copies unique shadow cubemap sources into the runtime array
        private void BlitShadowTextures(bool onlyAutoUpdates) {
            // Shadow texture sources occupy the first shadow slices, six slices per cubemap
            int cubemapTextureCount = _shadowCubemapTextureCount;
            for (int i = 0; i < cubemapTextureCount; i++) {
                if (onlyAutoUpdates && !_shadowCubemapTextureAutoUpdates[i]) continue;
                BlitCubemapTexture(_shadowCubemapTextures[i], _shadowCubemapTextureModes[i], i * 6, ShadowTextures);
            }
            // Shadow material sources follow texture sources and are rendered as six generated slices
            int cubemapMaterialCount = _shadowCubemapMaterialCount;
            for (int i = 0; i < cubemapMaterialCount; i++) {
                if (onlyAutoUpdates && !_shadowCubemapMaterialAutoUpdates[i]) continue;
                BlitCubemapMaterial(_shadowCubemapMaterials[i], (cubemapTextureCount + i) * 6, ShadowTextures, _shadowTexturesDepth);
            }
        }

        // Creates or recreates the runtime texture array so it matches an explicit texture layout
        private bool EnsureRuntimeCustomTextures(int width, int height, int depth) {
            if (width <= 0 || height <= 0 || depth <= 0) return false;
            bool recreate = ShouldRecreateRuntimeTextureArray(CustomTextures, width, height, depth, FixedCustomTexturesFormat, false, FilterMode.Trilinear);
            if (!recreate) return CustomTextures != null;

            ReleaseRuntimeRenderTexture(CustomTextures);

            CustomTextures = CreateRuntimeTextureArray(width, height, depth, FixedCustomTexturesFormat, FilterMode.Trilinear, false);
#if !COMPILER_UDONSHARP
            CustomTextures.name = "LightVolumeManager_CustomTextures";
#endif
            _customTexturesDepth = depth;
            return true;
        }

        // Creates or recreates the runtime shadow texture array so it matches an explicit texture layout
        private bool EnsureRuntimeShadowTextures(int width, int height, int depth) {
            if (width <= 0 || height <= 0 || depth <= 0) return false;
            RenderTextureFormat renderTextureFormat = ShadowTextureFormat == 0 ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGBFloat;
            bool recreate = ShouldRecreateRuntimeTextureArray(ShadowTextures, width, height, depth, renderTextureFormat, false, FilterMode.Bilinear);
            if (!recreate) return ShadowTextures != null;

            ReleaseRuntimeRenderTexture(ShadowTextures);

            ShadowTextures = CreateRuntimeTextureArray(width, height, depth, renderTextureFormat, FilterMode.Bilinear, false);
#if !COMPILER_UDONSHARP
            ShadowTextures.name = "LightVolumeManager_ShadowTextures";
#endif
            ShadowMapsCount = depth / 6;
            _shadowTexturesDepth = depth;
            return true;
        }

        // Checks if a runtime texture array must be recreated for the requested layout
        private bool ShouldRecreateRuntimeTextureArray(RenderTexture texture, int width, int height, int depth, RenderTextureFormat format, bool useMipMap, FilterMode filterMode) {
            if (texture == null || texture.width != width || texture.height != height || texture.volumeDepth != depth) return true;
#if !COMPILER_UDONSHARP
            if (texture.format != format) return true;
            if (texture.autoGenerateMips) return true;
#endif
            if (texture.useMipMap != useMipMap || texture.filterMode != filterMode) return true;
            return false;
        }

        // Releases a runtime render texture before replacing it
        private void ReleaseRuntimeRenderTexture(RenderTexture texture) {
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

        // Creates a runtime texture array with the shared Light Volumes settings
        private RenderTexture CreateRuntimeTextureArray(int width, int height, int depth, RenderTextureFormat format, FilterMode filterMode, bool useMipMap) {
            RenderTexture texture = new RenderTexture(width, height, 0, format, RenderTextureReadWrite.Linear);
            texture.dimension = TextureDimension.Tex2DArray;
            texture.volumeDepth = depth;
            texture.useMipMap = useMipMap;
#if COMPILER_UDONSHARP
            texture.autoGenerateMips = useMipMap;
#else
            texture.autoGenerateMips = false;
#endif
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

        // Updates moved dynamic volumes in-place and returns which shader buffer groups need uploading
        private int UpdateAutoUpdatedVolumeChanges() {
            if (LightVolumeInstances == null || PointLightVolumeInstances == null) return DynamicUpdateFlagFullRebuild;
            int updateFlags = 0;

            // Compare regular dynamic volumes against the transform values captured during the last upload
            for (int i = 0; i < _dynamicLightVolumeCount; i++) {
                LightVolumeInstance instance = _dynamicLightVolumeInstances[i];
                Transform instanceTransform = _dynamicLightVolumeTransforms[i];
                if (instance == null || instanceTransform == null) return DynamicUpdateFlagFullRebuild;
                if (!instance.gameObject.activeInHierarchy || !instance.IsDynamic) return DynamicUpdateFlagFullRebuild;

                Vector3 position = instanceTransform.position;
                Quaternion rotation = instanceTransform.rotation;
                Vector3 scale = instanceTransform.lossyScale;
                if (position == _dynamicLightVolumePositions[i] && rotation == _dynamicLightVolumeRotations[i] && scale == _dynamicLightVolumeScales[i]) continue;
                if (instance.Intensity == 0 || instance.Color == Color.black) return DynamicUpdateFlagFullRebuild;

                int shaderIndex = _dynamicLightVolumeShaderIndices[i];
                if (shaderIndex < 0 || shaderIndex >= _enabledCount) return DynamicUpdateFlagFullRebuild;
                int registryIndex = _enabledIDs[shaderIndex];
                if (registryIndex < 0 || registryIndex >= LightVolumeInstances.Length || LightVolumeInstances[registryIndex] != instance) return DynamicUpdateFlagFullRebuild;

                instance.UpdateTransform();
                _dynamicLightVolumePositions[i] = position;
                _dynamicLightVolumeRotations[i] = rotation;
                _dynamicLightVolumeScales[i] = scale;
                WriteLightVolumeTransformShaderData(shaderIndex, instance);
                updateFlags |= DynamicUpdateFlagLightVolumes;
            }

            // Compare dynamic point lights against the transform values captured during the last upload
            for (int i = 0; i < _dynamicPointLightVolumeCount; i++) {
                PointLightVolumeInstance instance = _dynamicPointLightVolumeInstances[i];
                Transform instanceTransform = _dynamicPointLightVolumeTransforms[i];
                if (instance == null || instanceTransform == null) return DynamicUpdateFlagFullRebuild;
                if (!instance.gameObject.activeInHierarchy || !instance.IsDynamic) return DynamicUpdateFlagFullRebuild;

                Vector3 position = instanceTransform.position;
                Quaternion rotation = instanceTransform.rotation;
                Vector3 scale = instanceTransform.lossyScale;
                bool positionChanged = position != _dynamicPointLightVolumePositions[i];
                bool rotationChanged = rotation != _dynamicPointLightVolumeRotations[i];
                bool scaleChanged = scale != _dynamicPointLightVolumeScales[i];
                if (!positionChanged && !rotationChanged && !scaleChanged) continue;
                if (instance.Intensity == 0 || instance.Color == Color.black) return DynamicUpdateFlagFullRebuild;

                int shaderIndex = _dynamicPointLightVolumeShaderIndices[i];
                if (shaderIndex < 0 || shaderIndex >= _pointLightCount) return DynamicUpdateFlagFullRebuild;
                int registryIndex = _enabledPointIDs[shaderIndex];
                if (registryIndex < 0 || registryIndex >= PointLightVolumeInstances.Length || PointLightVolumeInstances[registryIndex] != instance) return DynamicUpdateFlagFullRebuild;

                if (positionChanged) {
                    instance.Position = position;
                }
                if (rotationChanged) instance.UpdateRotation();

                if (scaleChanged) {
                    if (instance.LightType == 2) { // 2: area
                        instance.Width = Mathf.Max(Mathf.Abs(scale.x), 0.001f);
                        instance.Height = Mathf.Max(Mathf.Abs(scale.y), 0.001f);
                        instance.UpdateRotation();
                    }
                    float squaredScale = (scale.x + scale.y + scale.z) * 0.3333333333f;
                    instance.SquaredScale = squaredScale * squaredScale;
                    instance.IsRangeDirty = true;
                }

                _dynamicPointLightVolumePositions[i] = position;
                _dynamicPointLightVolumeRotations[i] = rotation;
                _dynamicPointLightVolumeScales[i] = scale;
                WritePointLightShaderData(shaderIndex, registryIndex, instance, false);
                updateFlags |= DynamicUpdateFlagPointLights;
            }

            return updateFlags;
        }

        // Uploads only shader arrays affected by an incremental dynamic transform update
        private void UploadAutoUpdatedVolumeChanges(int updateFlags) {
            if ((updateFlags & DynamicUpdateFlagLightVolumes) != 0 && _enabledCount != 0) {
                VRCShader.SetGlobalMatrixArray(_lightVolumeInvWorldMatrixID, _invWorldMatrix);
                VRCShader.SetGlobalVectorArray(_lightVolumeRotationID, _relativeRotation);
                VRCShader.SetGlobalVectorArray(_lightVolumeColorID, _colors);
            }
            if ((updateFlags & DynamicUpdateFlagPointLights) != 0 && _pointLightCount != 0) {
                VRCShader.SetGlobalVectorArray(_pointLightColorID, _pointLightColor);
                VRCShader.SetGlobalVectorArray(_pointLightPositionID, _pointLightPosition);
                VRCShader.SetGlobalVectorArray(_pointLightDirectionID, _pointLightDirection);
                VRCShader.SetGlobalVectorArray(_pointLightCustomIdID, _pointLightCustomId);
                if (_activeShadowCount > 0) VRCShader.SetGlobalVectorArray(_pointLightShadowReprojectionDataID, _pointLightShadowReprojectionData);
            }
        }

#if UDONSHARP
        // Internal method to auto update volumes and runtime textures every frame while needed
        public void UpdateVolumesProcess() {
            if (!enabled || !gameObject.activeInHierarchy) {
                _volumeDataUpdateRequested = false;
                _isUpdateProcessRunning = false;
                return;
            }

            bool updateVolumes = _volumeDataUpdateRequested;
            _volumeDataUpdateRequested = false;
            int dynamicUpdateFlags = 0;
            // AutoUpdateVolumes updates moved dynamic instances in-place unless an explicit rebuild was requested
            if (!updateVolumes && AutoUpdateVolumes) {
                dynamicUpdateFlags = UpdateAutoUpdatedVolumeChanges();
                if ((dynamicUpdateFlags & DynamicUpdateFlagFullRebuild) != 0) updateVolumes = true;
            }

            if (updateVolumes) {
                UpdateVolumes();
            } else if (dynamicUpdateFlags != 0) {
                UploadAutoUpdatedVolumeChanges(dynamicUpdateFlags);
            } else if (!_customTexturesInitialized || !_shadowTexturesInitialized) {
                // Texture caches may need initialization even when volume transforms did not change
                EnsureRuntimeTextureCaches();
            }

            bool updateTextures = AutoUpdateTextures && (_hasAutoCustomTextureUpdates || _hasAutoShadowTextureUpdates);
            if (AutoUpdateTextures && _hasAutoCustomTextureUpdates) UpdateAutoCustomTextures();
            if (AutoUpdateTextures && _hasAutoShadowTextureUpdates) UpdateAutoShadowTextures();

            // Keep the delayed loop alive only while there is monitoring or texture work left to do
            if (((AutoUpdateVolumes && (_dynamicLightVolumeCount > 0 || _dynamicPointLightVolumeCount > 0)) || _volumeDataUpdateRequested || updateTextures || !_customTexturesInitialized || !_shadowTexturesInitialized) && enabled && gameObject.activeInHierarchy) {
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

                bool updateVolumes = _volumeDataUpdateRequested;
                _volumeDataUpdateRequested = false;
                int dynamicUpdateFlags = 0;
                // AutoUpdateVolumes updates moved dynamic instances in-place unless an explicit rebuild was requested
                if (!updateVolumes && AutoUpdateVolumes) {
                    dynamicUpdateFlags = UpdateAutoUpdatedVolumeChanges();
                    if ((dynamicUpdateFlags & DynamicUpdateFlagFullRebuild) != 0) updateVolumes = true;
                }

                if (updateVolumes) {
                    UpdateVolumes();
                } else if (dynamicUpdateFlags != 0) {
                    UploadAutoUpdatedVolumeChanges(dynamicUpdateFlags);
                } else if (!_customTexturesInitialized || !_shadowTexturesInitialized) {
                    // Texture caches may need initialization even when volume transforms did not change
                    EnsureRuntimeTextureCaches();
                }

                updateTextures = AutoUpdateTextures && (_hasAutoCustomTextureUpdates || _hasAutoShadowTextureUpdates);
                if (AutoUpdateTextures && _hasAutoCustomTextureUpdates) UpdateAutoCustomTextures();
                if (AutoUpdateTextures && _hasAutoShadowTextureUpdates) UpdateAutoShadowTextures();
            } while (isActiveAndEnabled && ((AutoUpdateVolumes && (_dynamicLightVolumeCount > 0 || _dynamicPointLightVolumeCount > 0)) || _volumeDataUpdateRequested || updateTextures || !_customTexturesInitialized || !_shadowTexturesInitialized));

            _updateCoroutine = null;
        }
#endif

        // Writes one regular Light Volume into the compact shader upload buffers
        private void WriteLightVolumeShaderData(int shaderIndex, LightVolumeInstance instance) {
            int i2 = shaderIndex * 2;
            int i3 = shaderIndex * 3;
            int i6 = shaderIndex * 6;

            _invWorldMatrix[shaderIndex] = instance.InvWorldMatrix;
            _invLocalEdgeSmooth[shaderIndex] = instance.InvLocalEdgeSmoothing;

            Vector4 c = instance.Color.linear * instance.Intensity;
            c.w = instance.IsRotated ? 1 : 0;
            _colors[shaderIndex] = c;

            _relativeRotation[i2] = instance.RelativeRotationRow0;
            _relativeRotation[i2 + 1] = instance.RelativeRotationRow1;

            _boundsUvwScale[i3] = instance.BoundsUvwMin0;
            _boundsUvwScale[i3 + 1] = instance.BoundsUvwMin1;
            _boundsUvwScale[i3 + 2] = instance.BoundsUvwMin2;
            Vector4 uvwMin0 = instance.BoundsUvwMin0;
            Vector4 uvwMin1 = instance.BoundsUvwMin1;
            Vector4 uvwMin2 = instance.BoundsUvwMin2;
            float uvwScaleX = uvwMin0.w;
            float uvwScaleY = uvwMin1.w;
            float uvwScaleZ = uvwMin2.w;
            _boundsUvw[i6] = new Vector4(uvwMin0.x, uvwMin0.y, uvwMin0.z, 0);
            _boundsUvw[i6 + 1] = new Vector4(uvwMin0.x + uvwScaleX, uvwMin0.y + uvwScaleY, uvwMin0.z + uvwScaleZ, 0);
            _boundsUvw[i6 + 2] = new Vector4(uvwMin1.x, uvwMin1.y, uvwMin1.z, 0);
            _boundsUvw[i6 + 3] = new Vector4(uvwMin1.x + uvwScaleX, uvwMin1.y + uvwScaleY, uvwMin1.z + uvwScaleZ, 0);
            _boundsUvw[i6 + 4] = new Vector4(uvwMin2.x, uvwMin2.y, uvwMin2.z, 0);
            _boundsUvw[i6 + 5] = new Vector4(uvwMin2.x + uvwScaleX, uvwMin2.y + uvwScaleY, uvwMin2.z + uvwScaleZ, 0);
        }

        // Writes only dynamic transform-dependent Light Volume data for incremental AutoUpdateVolumes
        private void WriteLightVolumeTransformShaderData(int shaderIndex, LightVolumeInstance instance) {
            int i2 = shaderIndex * 2;
            _invWorldMatrix[shaderIndex] = instance.InvWorldMatrix;
            Vector4 c = _colors[shaderIndex];
            c.w = instance.IsRotated ? 1 : 0;
            _colors[shaderIndex] = c;
            _relativeRotation[i2] = instance.RelativeRotationRow0;
            _relativeRotation[i2 + 1] = instance.RelativeRotationRow1;
        }

        // Writes one Point Light Volume into the compact shader upload buffers
        private void WritePointLightShaderData(int shaderIndex, int sourceIndex, PointLightVolumeInstance instance, bool countActiveShadow) {
            if (instance.IsRangeDirty) instance.UpdateRange();

            Vector4 pos = new Vector4(instance.Position.x, instance.Position.y, instance.Position.z, 0);
            float angleData;
            if (instance.LightType == 2) { // 2: area
                pos.w = instance.Width;
                angleData = 2f + instance.Height;
            } else {
                float typeSign = instance.LightType == 1 ? -1f : 1f; // 1: spot
                if (instance.ProjectionMode == 1) pos.w = typeSign * instance.InverseSquaredRange / Mathf.Max(instance.SquaredScale, 0.000001f); // 1: LUT
                else pos.w = typeSign * instance.LightSourceSize * instance.LightSourceSize * instance.SquaredScale;
                if (instance.LightType == 1 && instance.ProjectionMode == 2) angleData = instance.OuterAngleTan; // 1: spot, 2: custom cookie
                else angleData = instance.OuterAngleCos;
            }
            _pointLightPosition[shaderIndex] = pos;

            Vector4 c = instance.Color.linear * instance.Intensity;
            c.w = angleData;
            _pointLightColor[shaderIndex] = c;

            if (instance.LightType == 1 && instance.ProjectionMode != 2) { // 1: spot, 2: custom cookie
                _pointLightDirection[shaderIndex].x = instance.Direction.x;
                _pointLightDirection[shaderIndex].y = instance.Direction.y;
                _pointLightDirection[shaderIndex].z = instance.Direction.z;
                _pointLightDirection[shaderIndex].w = instance.ConeFalloff;
            } else {
                _pointLightDirection[shaderIndex].x = instance.Rotation.x;
                _pointLightDirection[shaderIndex].y = instance.Rotation.y;
                _pointLightDirection[shaderIndex].z = instance.Rotation.z;
                _pointLightDirection[shaderIndex].w = instance.Rotation.w;
            }

            int resolvedCustomId = _pointLightCustomIDs != null && sourceIndex < _pointLightCustomIDs.Length ? _pointLightCustomIDs[sourceIndex] : -1;
            float shaderCustomId = 0;
            if (resolvedCustomId >= 0) {
                if (instance.ProjectionMode == 1) shaderCustomId = resolvedCustomId + 1; // 1: LUT
                else if (instance.ProjectionMode == 2) shaderCustomId = -resolvedCustomId - 1; // 2: custom cookie or cubemap
            }
            _pointLightCustomId[shaderIndex].x = shaderCustomId;

            int resolvedShadowId = _pointLightShadowIDs != null && sourceIndex < _pointLightShadowIDs.Length ? _pointLightShadowIDs[sourceIndex] : -1;
            bool hasShadow = ShadowMapsCount > 0 && resolvedShadowId >= 0 && resolvedShadowId < ShadowMapsCount;
            if (countActiveShadow && hasShadow) _activeShadowCount++;
            float shadowFarClip = 0;
            if (hasShadow) shadowFarClip = instance.FarClip > 0 ? instance.FarClip : Mathf.Sqrt(Mathf.Max(instance.SquaredRange, 0.000001f));
            bool useLocalSpaceShadows = hasShadow && !instance.WorldSpaceShadows;
            float shadowMapID = hasShadow ? (useLocalSpaceShadows ? -resolvedShadowId - 1 : resolvedShadowId + 1) : 0;
            _pointLightCustomId[shaderIndex].y = shadowMapID;
            _pointLightCustomId[shaderIndex].z = instance.SquaredRange;
            _pointLightCustomId[shaderIndex].w = shadowFarClip;
            if (useLocalSpaceShadows) {
                Quaternion shadowRotation = Quaternion.Inverse(instance.transform.rotation);
                _pointLightShadowReprojectionData[shaderIndex].x = shadowRotation.x;
                _pointLightShadowReprojectionData[shaderIndex].y = shadowRotation.y;
                _pointLightShadowReprojectionData[shaderIndex].z = shadowRotation.z;
                _pointLightShadowReprojectionData[shaderIndex].w = shadowRotation.w;
            } else {
                _pointLightShadowReprojectionData[shaderIndex].x = instance.ShadowBakePosition.x;
                _pointLightShadowReprojectionData[shaderIndex].y = instance.ShadowBakePosition.y;
                _pointLightShadowReprojectionData[shaderIndex].z = instance.ShadowBakePosition.z;
                _pointLightShadowReprojectionData[shaderIndex].w = hasShadow ? 1 : 0;
            }
        }

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

            // Start a new transform baseline for AutoUpdateVolumes before scanning active instances
            _dynamicLightVolumeCount = 0;
            _dynamicPointLightVolumeCount = 0;

            // Search for enabled volumes and count additive volumes
            _enabledCount = 0;
            _additiveCount = 0;
            for (int i = 0; i < LightVolumeInstances.Length && _enabledCount < MaxLightVolumeCount; i++) {
                LightVolumeInstance instance = LightVolumeInstances[i];
                if (instance == null) continue;
                if (!instance.gameObject.activeInHierarchy) {
                    instance.LightVolumeManager = this;
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
                    if (instance.IsDynamic) {
                        // Store dynamic transform state only after the instance has refreshed its own cached data
                        Transform instanceTransform = instance.transform;
                        _dynamicLightVolumeInstances[_dynamicLightVolumeCount] = instance;
                        _dynamicLightVolumeTransforms[_dynamicLightVolumeCount] = instanceTransform;
                        _dynamicLightVolumeShaderIndices[_dynamicLightVolumeCount] = _enabledCount;
                        _dynamicLightVolumePositions[_dynamicLightVolumeCount] = instanceTransform.position;
                        _dynamicLightVolumeRotations[_dynamicLightVolumeCount] = instanceTransform.rotation;
                        _dynamicLightVolumeScales[_dynamicLightVolumeCount] = instanceTransform.lossyScale;
                        _dynamicLightVolumeCount++;
                    }
                    if (instance.IsAdditive) _additiveCount++;
                    _enabledIDs[_enabledCount] = i;
                    _enabledCount++;
                }
            }

            // Fill arrays with enabled volume data
            for (int i = 0; i < _enabledCount; i++) {
                WriteLightVolumeShaderData(i, LightVolumeInstances[_enabledIDs[i]]);
            }

            // Search for enabled point light volumes
            _pointLightCount = 0;
            for (int i = 0; i < PointLightVolumeInstances.Length && _pointLightCount < MaxPointLightCount; i++) {
                PointLightVolumeInstance instance = PointLightVolumeInstances[i];
                if (instance == null) continue;
                if (!instance.gameObject.activeInHierarchy) {
                    instance.LightVolumeManager = this;
                    PointLightVolumeInstances[i] = null;
                    continue;
                }
                if (_isRangeDirty) { // If brightness cutoff changed, force every light range to recalculate
                    instance.UpdateRange();
                }
                // Source membership changes invalidate only the matching texture cache
                bool hasCustomTextureSource = HasActiveCustomTextureSource(instance);
                if (_pointLightCustomIDs == null || i >= _pointLightCustomIDs.Length || hasCustomTextureSource != (_pointLightCustomIDs[i] >= 0)) _customTexturesInitialized = false;
                bool hasShadowTextureSource = HasActiveShadowTextureSource(instance);
                if (_pointLightShadowIDs == null || i >= _pointLightShadowIDs.Length || hasShadowTextureSource != (_pointLightShadowIDs[i] >= 0)) _shadowTexturesInitialized = false;
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
                    if (instance.IsDynamic) {
                        // Store dynamic transform state only after the instance has refreshed its own cached data
                        Transform instanceTransform = instance.transform;
                        _dynamicPointLightVolumeInstances[_dynamicPointLightVolumeCount] = instance;
                        _dynamicPointLightVolumeTransforms[_dynamicPointLightVolumeCount] = instanceTransform;
                        _dynamicPointLightVolumeShaderIndices[_dynamicPointLightVolumeCount] = _pointLightCount;
                        _dynamicPointLightVolumePositions[_dynamicPointLightVolumeCount] = instanceTransform.position;
                        _dynamicPointLightVolumeRotations[_dynamicPointLightVolumeCount] = instanceTransform.rotation;
                        _dynamicPointLightVolumeScales[_dynamicPointLightVolumeCount] = instanceTransform.lossyScale;
                        _dynamicPointLightVolumeCount++;
                    }
                    _enabledPointIDs[_pointLightCount] = i;
                    _pointLightCount++;
                }
            }

            _isRangeDirty = false; // Reset range dirtiness
            // Rebuild texture caches after point light source membership has been checked
            EnsureRuntimeTextureCaches();

            // Fill arrays with enabled point light data
            _activeShadowCount = 0;
            for (int i = 0; i < _pointLightCount; i++) {
                int sourceIndex = _enabledPointIDs[i];
                WritePointLightShaderData(i, sourceIndex, PointLightVolumeInstances[sourceIndex], true);
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
            VRCShader.SetGlobalFloat(_lightVolumeOcclusionCountID, 0);

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
                VRCShader.SetGlobalVectorArray(_lightVolumeUvwID, _boundsUvw);

                // Volume transform matrices
                VRCShader.SetGlobalMatrixArray(_lightVolumeInvWorldMatrixID, _invWorldMatrix);

                // Volume relative rotations
                VRCShader.SetGlobalVectorArray(_lightVolumeRotationID, _relativeRotation);

                // Volume color correction data
                VRCShader.SetGlobalVectorArray(_lightVolumeColorID, _colors);

            }

            // Point Lights
            VRCShader.SetGlobalFloat(_pointLightCountID, _pointLightCount);
            VRCShader.SetGlobalFloat(_pointLightCubeCountID, CubemapsCount);
            int shadowCount = _activeShadowCount > 0 ? ShadowMapsCount : 0;
            VRCShader.SetGlobalFloat(_pointLightShadowCountID, shadowCount);
            if (_pointLightCount != 0) { // Skip point light array uploads when no point lights are active
                VRCShader.SetGlobalVectorArray(_pointLightColorID, _pointLightColor);
                VRCShader.SetGlobalVectorArray(_pointLightPositionID, _pointLightPosition);
                VRCShader.SetGlobalVectorArray(_pointLightDirectionID, _pointLightDirection);
                VRCShader.SetGlobalVectorArray(_pointLightCustomIdID, _pointLightCustomId);
                if (_activeShadowCount > 0) { // Shadow arrays are uploaded only when at least one enabled point light uses shadows
                    VRCShader.SetGlobalVectorArray(_pointLightShadowReprojectionDataID, _pointLightShadowReprojectionData);
                }
                VRCShader.SetGlobalFloat(_lightBrightnessCutoffID, LightsBrightnessCutoff);
            }
            if (CustomTextures != null) {
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
