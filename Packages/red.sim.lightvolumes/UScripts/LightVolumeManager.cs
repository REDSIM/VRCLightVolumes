#if !UDONSHARP && (UNITY_EDITOR || COMPILER_UDONSHARP)
#define UDONSHARP
#endif

using UnityEngine;

#if UDONSHARP
using VRC.SDKBase;
using UdonSharp;
#endif

namespace VRCLightVolumes {

    // LightVolumeManager.*.cs companion partials organize the implementation and must not receive separate UdonSharpProgramAsset files.
    // - LightVolumeManager.cs: constants, serialized Inspector/runtime state and shader property IDs.
    // - ~.Core.cs: shared calculations, change notifications, initialization and lifecycle.
    // - ~.Registries.cs: component registration, stable ordering and compact runtime registries.
    // - ~.Buffers.cs: frame update orchestration, data packing and shader-global uploads.
    // - ~.Textures.cs: runtime cookie/custom/shadow texture caches and rendering.
    // - ~.Clustering.cs: camera-relative froxel clustering and cluster-mask generation.
    // - ~.Editor.cs: Editor-only state, atlas post-processors, probe baking and preview helpers.
    // - ~.LegacyEditor.cs: temporary source-compatible editor API retained for existing integrations.
    // - ~.EditorHandle.cs: supporting manager.Editor handle and public post-processor descriptor; this file does not add another Manager partial.

    [AddComponentMenu("VRC Light Volumes/Light Volume Manager (U# Script)")]
    [DisallowMultipleComponent]
#if UDONSHARP
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public partial class LightVolumeManager : UdonSharpBehaviour
#else
    public partial class LightVolumeManager : MonoBehaviour
#endif
    {
#region Constants
        private const float Version = 3; // Current VRC Light Volumes shader feature version
        private const int MaxLightVolumeCount = 32;
        private const int MaxPointLightCount = 128;
        private const int DefaultRegistryOrder = 2147483647;
        private const int MaxLightVolumeRotationVectors = MaxLightVolumeCount * 2;
        private const int MaxLightVolumeUvwScaleVectors = MaxLightVolumeCount * 3;
        private const int MaxLightVolumeLegacyUvwVectors = MaxLightVolumeCount * 6;
        private const RenderTextureFormat FixedCustomTexturesFormat = RenderTextureFormat.ARGBHalf;
        private const RenderTextureFormat ClusterMaskFormat = RenderTextureFormat.ARGBInt;
        private const float DisabledShadingShadowId = 10000f;
        private const int PointLightUpdateColorRange = 1;
        private const int PointLightUpdateFull = 2;
        private const int PointLightUploadPosition = 1;
        private const int PointLightUploadColor = 2;
        private const int PointLightUploadExtraData = 4;
        private const int PointLightUploadDirection = 8;
        private const int PointLightUploadCustomId = 16;
        private const int PointLightUploadShadowReprojection = 32;
        private const int PointLightUploadShadowRotation = 64;
        private const int ShadowTextureFormatHalf = 0;
        private const string RuntimeShadowCameraName = "Runtime Shadow Camera";
        private const string ClusteringShaderName = "Hidden/VRCLV/FroxelClusteringBuild";
        private const int MaxFroxelTileShift = 4;
        private const int MaxFroxelSize = 256;
        private const int MaxFroxelAtlasSize = 4096;
        private const int MinFroxelCoarse = 2;
        private const int MaxFroxelCoarse = 8;
        private const float DefaultFroxelFov = 90f;
        private const float DefaultFroxelAspect = 1.7777778f;
        private const int ClusterAxisScale = 255;
        private const int ClusterAxisStride = 256;
        private const int ClusterShapeStride = 65536;
        private const float ClusterAxisPad = 0.020002667f; // tan(0.02 radians)
        private const float ClusterMaxTangent = 254f;
#endregion

#region Inspector And Runtime References

        [Header("Light Volume Atlas")]
        [Tooltip("Combined Texture3D containing all baked Light Volume data. This field is not used at runtime, see LightVolumeAtlas instead. It specifies the base for the post process chain, if given.")]
        public Texture3D LightVolumeAtlasBase;
        [Tooltip("Combined texture containing all Light Volumes' textures.")]
        public Texture LightVolumeAtlas;

        [Header("Point Light Volumes")]
        [Tooltip("Resolution used for Point Light cookie, LUT and cubemap projection textures.")]
        public int CustomTexturesWidth = 512;
        [Tooltip("Height of each runtime point light projection texture slice.")]
        public int CustomTexturesHeight = 512;
        [Tooltip("The minimum brightness at a point due to lighting from a Point Light Volume, before the light is culled. Larger values will result in better performance, but light attenuation will be less physically correct.")]
        public float LightsBrightnessCutoff = 0.35f;
        [Tooltip("Resolution used for each shadow map face. A cubemap shadow uses six faces at this resolution.")]
        public int ShadowTexturesWidth = 256;
        [Tooltip("Height of each runtime shadow cubemap face.")]
        public int ShadowTexturesHeight = 256;
        [Tooltip("Precision used for baked EVSM shadow maps and the runtime shadow texture array. 0 = ARGBHalf, 1 = ARGBFloat.")]
        public int ShadowTextureFormat = 1;
        [Tooltip("EVSM light bleed reduction applied by the shadow receiver shader. 0 disables reduction, 1 is strongest.")]
        public float ShadowBleedReduction = 0.2f;
        [Tooltip("EVSM variance bias used by the shadow receiver shader. Authoring setup stores this as a 0..1 logarithmic slider.")]
        public float ShadowMinVariance = 0.0001f;

        [Tooltip("Builds camera-relative Coarse-to-Fine froxel clusters so shaders only evaluate Point Light Volumes that can affect the current pixel.")]
        public bool Clustering = true;
        [Tooltip("Fine froxels per camera degree on each screen axis. 1.0 = one froxel per degree; total count is multiplied by Slices Count.")]
        [Range(0.05f, 3f)] public float FroxelDensity = 1f;
        [Tooltip("Count of exponentially distributed depth slices between the main camera near and far clip planes. This does not change the angular resolution; memory and build cost scale with the slice count.")]
        [Range(8, MaxFroxelSize)] public int FroxelSlices = 100;
        [Tooltip("Power-of-two reduction of the intermediate Coarse grid relative to the Fine grid on every axis. Values are resolved to 2, 4 or 8 so every Fine froxel has one exact parent and the shader can use bit shifts instead of integer division.")]
        [Range(MinFroxelCoarse, MaxFroxelCoarse)] public int FroxelCoarse = 4;
        [Tooltip("Uses the non-clustered loop below this active Point Light Volume count because building and sampling the cluster mask is unlikely to amortize.")]
        [Range(1, MaxPointLightCount)] public int ClusteringMinLights = 8;

        [Tooltip("When enabled, areas outside Light Volumes fall back to light probes. Otherwise, the Light Volume with the smallest weight is used as fallback. It also improves performance.")]
        public bool LightProbesBlending = true;
        [Tooltip("Disables smooth blending with areas outside Light Volumes. Use it if your entire scene's play area is covered by Light Volumes. It also improves performance.")]
        public bool SharpBounds = true;
        [Tooltip("Automatically updates most volume properties at runtime. Enabling/disabling, Color and Intensity update automatically even without this option enabled. Position, Rotation and Scale get updated only for volumes that are marked dynamic. It's more performant to keep it off.")]
        public bool AutoUpdateVolumes = true;
        [Tooltip("Automatically updates dynamic point light cookie and shadow texture sources at runtime. It's more performant to keep it off.")]
        public bool AutoUpdateTextures = true;
        [Tooltip("Limits the maximum number of additive volumes and Point Light Volumes that can affect a single pixel. This also limits individual Point Light Volume speculars in modern compatible shaders. Lower values improve worst-case performance in overlap-heavy areas.")]
        public int AdditiveMaxOverdraw = 4;
        [Tooltip("Enables the Force Scene Lighting shader override on startup, disabling min/max brightness limits in compatible avatar shaders. When disabled, the existing global override is left unchanged. Use SetForceSceneLighting for manual runtime control.")]
        public bool ForceSceneLighting = false;

        // Persistent authoring settings live on the Udon proxy as well. Keeping them here removes the editor-only Setup component without adding runtime work; heavy asset references are cleared from the temporary build scene by the build preprocessor.
        private const int BakingModeProgressive = 0;
        private const int BakingModeBakery = 1;
        [Tooltip("Selects the lightmapper used to bake Light Volumes. Bakery usually gives better results and works faster.")]
        [HideInInspector] public int BakingMode = BakingModeProgressive; // 0 = Progressive, 1 = Bakery, 2 = Custom Lightmapper
        [Tooltip("Light from Bakery light sources with this bitmask will affect Light Volumes.")]
        [HideInInspector] public int VolumeBitmask = 1;
        [Tooltip("Light from Bakery light sources with this bitmask will affect light probes.")]
        [HideInInspector] public int ProbeBitmask = 1;
        [Tooltip("Removes baked noise in Light Volumes, but may slightly reduce sharpness. Recommended to keep enabled.")]
        [HideInInspector] public bool Denoise = true;
        [Tooltip("Dilates valid probe data into invalid probes, such as probes inside geometry, to reduce light leaking.")]
        [HideInInspector] public bool DilateInvalidProbes = true;
        [Tooltip("Number of dilation passes. More passes can reduce leaking, but increase bake time.")]
        [HideInInspector] public int DilationIterations = 1;
        [Tooltip("Fraction of backface hits required to mark a probe invalid for dilation. 0 marks every probe invalid; 1 keeps every probe valid.")]
        [HideInInspector] public float DilationBackfaceBias = 0.1f;
        [Tooltip("Reduces ringing and burned-looking Bakery light probes, at the cost of slightly lower contrast.")]
        [HideInInspector] public bool FixLightProbesL1 = true;
        [Tooltip("Downscales each Light Volume before atlas packing. Useful for lower-resolution mobile atlases or reducing aliasing.")]
        [HideInInspector] public int DownscaleVolumes = 0; // 0 = None, 1 = x2, 2 = x4, 3 = x8
        // The mobile slider is authoring-only. ShadowMinVariance remains the resolved raw runtime value.
        [Tooltip("Logarithmic EVSM variance bias slider used for PC builds. The receiver shader scales this by warped depth, matching the EVSM derivative. Higher values reduce edge noise, but can detach contact shadows.")]
        [HideInInspector] public float ShadowMinVarianceDesktop = 0f;
        [Tooltip("Logarithmic EVSM variance bias slider used for Android and iOS builds. Higher values reduce Half precision edge noise on Quest and Mobile, but can detach contact shadows.")]
        [HideInInspector] public float ShadowMinVarianceMobile = 1f;
        // Serializable RT/material/name projection for editor atlas processors. Delegate callbacks remain in transient editor state and must re-register after reload.
        [HideInInspector] public RenderTexture[] AtlasPostProcessorTargets = new RenderTexture[0];
        [HideInInspector] public Material[] AtlasPostProcessorMaterials = new Material[0];
        [HideInInspector] public string[] AtlasPostProcessorTextureNames = new string[0];

        [Header("Runtime Registries")]
        [Tooltip("All Light Volume instances in stable registration order. The Manager selects the highest-priority active volumes for shader upload. You can disable unnecessary volume GameObjects at runtime to improve performance.")]
        public LightVolumeInstance[] LightVolumeInstances = new LightVolumeInstance[0];
        [Tooltip("All Point Light Volume instances. You can enable or disable point light volume GameObjects at runtime. Manually disabling unnecessary point light volumes improves performance.")]
        public PointLightVolumeInstance[] PointLightVolumeInstances = new PointLightVolumeInstance[0];

        [Tooltip("Runtime texture array used for point light cubemaps, LUTs and cookies.")]
        public RenderTexture CustomTextures;
        [Tooltip("Cubemap count stored in CustomTextures. Cubemap array elements start from the beginning, 6 elements each.")]
        public int CubemapsCount = 0;
        [Tooltip("Runtime texture array that stores per-light shadow maps.")]
        public RenderTexture ShadowTextures;
        [Tooltip("Cubemap shadow maps count stored in ShadowTextures. Cubemap array elements start from the beginning, 6 elements each.")]
        public int ShadowCubemapsCount = 0;
        [Tooltip("Shadow maps count stored in ShadowTextures. Cubemaps use 6 array elements, single projected shadows use 1 array element.")]
        public int ShadowMapsCount = 0;

        // Material used to copy cubemap source faces into the animated projection texture array
        [HideInInspector] public Material CubemapFaceMaterial;
        // Shared disabled camera used by all runtime point light shadow bakes
        [HideInInspector] public Camera RuntimeShadowCamera;
        // Shared material used by all runtime point light shadow bakes to encode camera depth
        [HideInInspector] public Material RuntimeShadowDepthEncodeMaterial;
        // Shared material used by all runtime point light shadow bakes to blur encoded shadows
        [HideInInspector] public Material RuntimeShadowBlurMaterial;
        // Cached quality keyword state for the shared runtime shadow blur material
        [HideInInspector] public int RuntimeShadowBlurQualityPreset = -1;
        // Cached uniform-radius keyword state for the shared runtime shadow blur material
        [HideInInspector] public int RuntimeShadowBlurUniformKeyword = -1;
        // Cached direct-output keyword state for the shared runtime shadow blur material
        [HideInInspector] public int RuntimeShadowBlurDirectKeyword = -1;
        // Cached spherical blur keyword state for the shared runtime shadow blur material
        [HideInInspector] public int RuntimeShadowBlurSphericalKeyword = -1;
        // Shared material used to build the Coarse and Fine clustered-light masks in packed 2D integer atlases
        [HideInInspector] public Material ClusteringMaterial;
#endregion

#region Runtime Texture Cache

        // PROJECTION TEXTURES
        // Counts describe active prefixes, arrays stay reusable to avoid runtime allocations
        private bool _customTexturesInitialized = false;
        private int _customTextureArrayDepth = 0;
        private int _customCubemapTextureCount = 0;
        private int _customCubemapMaterialCount = 0;
        private int _customSingleTextureCount = 0;
        private int _customSingleMaterialCount = 0;
        private bool _customTexturesUseMipMap = false;

        // Unique custom projection sources split by source shape and source type
        private Texture[] _customCubemapTextures = new Texture[0];
        private Material[] _customCubemapMaterials = new Material[0];
        private Texture[] _customSingleTextures = new Texture[0];
        private Material[] _customSingleMaterials = new Material[0];

        private bool[] _customCubemapTextureAutoUpdates = new bool[0];
        private bool[] _customCubemapMaterialAutoUpdates = new bool[0];
        private bool[] _customSingleTextureAutoUpdates = new bool[0];
        private bool[] _customSingleMaterialAutoUpdates = new bool[0];
        private PointLightVolumeInstance[] _customSingleAreaCookieReceivers = new PointLightVolumeInstance[0];
        private int[] _customSingleAreaCookieReceiverIndices = new int[0];

        private int[] _customCubemapTextureModes = new int[0]; // Texture layouts: 0 = single 2D texture copied to all faces, 1 = Texture2DArray slices 0..5, 2 = native Cubemap faces
        private int[] _pointLightCustomIDs = new int[0];
        private int[] _customSourceTypes = new int[0]; // Source types per point light: 0 = none, 1 = cubemap texture, 2 = cubemap material, 3 = single texture, 4 = single material
        private Color[] _pointLightAreaCookieAverageColors = new Color[0];
        private bool _areaCookieAverageReadbackScheduled = false;
        private bool _areaCookieAverageReadbackForceAll = false;
        [HideInInspector] public bool HasAutoCustomTextureUpdates = false;

        // SHADOW TEXTURES
        // Counts describe active prefixes, arrays stay reusable to avoid runtime allocations
        private bool _shadowTexturesInitialized = false;
        // A failed atlas allocation must not be retried from the per-frame auto-update loop. Explicit source/configuration work calls ReinitializeShadowTextures and clears the latch.
        private bool _shadowTextureAllocationFailed = false;
        private int _shadowTextureArrayDepth = 0;
        private int _shadowCubemapTextureCount = 0;
        private int _shadowCubemapMaterialCount = 0;
        private int _shadowSingleTextureCount = 0;
        private int _shadowSingleMaterialCount = 0;

        // Unique shadow sources and resolved per-point-light shadow IDs
        private Texture[] _shadowCubemapTextures = new Texture[0];
        private Material[] _shadowCubemapMaterials = new Material[0];
        private Texture[] _shadowSingleTextures = new Texture[0];
        private Material[] _shadowSingleMaterials = new Material[0];
        private int[] _shadowCubemapTextureModes = new int[0]; // Texture layouts: 0 = single 2D texture copied to all faces, 1 = Texture2DArray slices 0..5, 2 = native Cubemap faces
        private bool[] _shadowCubemapTextureAutoUpdates = new bool[0];
        private bool[] _shadowCubemapMaterialAutoUpdates = new bool[0];
        private bool[] _shadowSingleTextureAutoUpdates = new bool[0];
        private bool[] _shadowSingleMaterialAutoUpdates = new bool[0];
        private int[] _pointLightShadowIDs = new int[0];
        private int[] _shadowSourceTypes = new int[0]; // Source types per point light: 0 = none, 1 = cubemap texture, 2 = cubemap material, 3 = single texture, 4 = single material
        [HideInInspector] public bool HasAutoShadowTextureUpdates = false;
#if !UNITY_EDITOR && !COMPILER_UDONSHARP
        // Standalone non-Udon execution still owns these runtime values directly.
        private Material _generatedClusteringMaterial;
        private Vector4 _editorFroxelDepthParams;
#endif
#if UDONSHARP
        private RenderTexture _dummyRT; // Small source texture used only for Udon destination-binding blits
#endif

#endregion

#region Volume State And Shader Buffers

        private bool _isInitialized = false; // Tracks one-time shader array initialization at runtime while still allowing editor property IDs to refresh
        private bool _isRangeDirty = false; // Global state mirrors and dirty flags

        // Compact shader buffers are the runtime source of truth. These flags only decide whether to upload them.
        private bool _lightVolumeArraysDirty = false;
        private bool _updateAllLightVolumeBuffers = false;
        private bool _updateLightVolumeBuffers = false;
        private bool _updateLightVolumeEdgeBuffer = false;
        private int _pointLightArrayUploadMask = 0;
        private bool _updateNeedsVolumeRebuild = false;

        // Light Volume shader upload buffers
        private int _enabledCount = 0;
        private int _additiveCount = 0;
        private Vector4[] _invLocalEdgeSmooth = new Vector4[MaxLightVolumeCount];
        private Vector4[] _colors = new Vector4[MaxLightVolumeCount];
        private Vector4[] _boundsUvwScale = new Vector4[MaxLightVolumeUvwScaleVectors];
        private Vector4[] _boundsUvw = new Vector4[MaxLightVolumeLegacyUvwVectors];
        private Vector4[] _relativeRotation = new Vector4[MaxLightVolumeRotationVectors];

        // Point Light Volume shader upload buffers
        private int _pointLightCount = 0;
        private int _activeShadowCount = 0;
        private int[] _enabledPointIDs = new int[MaxPointLightCount];
        // Rebuilt with compact buffers. Every lookup validates the hint against _enabledPointIDs before using it, so registry mutations can only cause a slower fallback, never a wrong slot.
        private int[] _pointLightRegistryToShaderIndex = new int[0];
        // End-of-frame notification queue. A zero update mask is also the slot's not-queued marker. Fixed-size storage keeps animated setters allocation-free.
        private int[] _dirtyPointLightShaderIndices = new int[MaxPointLightCount];
        private int[] _dirtyPointLightUpdateFlags = new int[MaxPointLightCount];
        private int _dirtyPointLightCount = 0;
        private Vector4[] _pointLightPosition = new Vector4[MaxPointLightCount];
        private Vector4[] _pointLightColor = new Vector4[MaxPointLightCount];
        private Vector4[] _pointLightExtraData = new Vector4[MaxPointLightCount];
        private Vector4[] _pointLightDirection = new Vector4[MaxPointLightCount];
        private Vector4[] _pointLightCustomId = new Vector4[MaxPointLightCount];
        private Vector4[] _clusteringLights = new Vector4[MaxPointLightCount / 2];
        private Vector4[] _pointLightShadowReprojectionData = new Vector4[MaxPointLightCount];
        private Vector4[] _pointLightShadowRotationData = new Vector4[MaxPointLightCount];
        private bool _clusteringLightsDirty = true;

        // Matrix upload buffer for active regular volumes
        private Matrix4x4[] _invWorldMatrix = new Matrix4x4[MaxLightVolumeCount];

        // Dynamic transform watch arrays point directly at final shader slots.
        private int _dynamicLightVolumeCount = 0;
        private int _dynamicPointLightVolumeCount = 0;
        private LightVolumeInstance[] _dynamicLightVolumeInstances = new LightVolumeInstance[MaxLightVolumeCount];
        private PointLightVolumeInstance[] _dynamicPointLightVolumeInstances = new PointLightVolumeInstance[MaxPointLightCount];
        private Transform[] _dynamicLightVolumeTransforms = new Transform[MaxLightVolumeCount];
        private Transform[] _dynamicPointLightVolumeTransforms = new Transform[MaxPointLightCount];
        private int[] _dynamicLightVolumeShaderIndices = new int[MaxLightVolumeCount];
        private int[] _dynamicPointLightVolumeShaderIndices = new int[MaxPointLightCount];
        private Matrix4x4[] _dynamicLightVolumeMatrices = new Matrix4x4[MaxLightVolumeCount];
        private Matrix4x4[] _dynamicPointLightVolumeMatrices = new Matrix4x4[MaxPointLightCount];

        // Active registry index buffers for compact shader uploads
        private int[] _selectedLightVolumeIDs = new int[MaxLightVolumeCount];
        private float[] _selectionLightVolumeWeights = new float[MaxLightVolumeCount];
        private int[] _selectionLightVolumeOrders = new int[MaxLightVolumeCount];
        private int[] _enabledIDs = new int[MaxLightVolumeCount];

        // Public API for other UdonSharp scripts
        public int EnabledCount => _enabledCount;
        public int[] EnabledIDs => _enabledIDs;

        private float _prevLightsBrightnessCutoff = 0.35f;

        // Froxel clustering resources are generated at runtime and never serialized into the world.
        private RenderTexture _clusterMask;
        private RenderTexture _coarseClusterMask;
        private RenderTexture _clusteringSource;
        private bool _clusteringActive = false;
        private bool _clusteringUnsupported = false;
        private bool _clusteringAllocationFailed = false;
        private bool _froxelLayoutValid = false;
        private bool _froxelDepthValid = false;
        private bool _froxelProjectionValid = false;
        private bool _clusterMaskDirty = true;
        private bool _clusterMaskValid = false;
        private bool _clusterGeometryUploadPending = false;
        private float _froxelLayoutFov;
        private float _froxelLayoutAspect;
        private float _froxelLayoutDensity;
        private int _froxelLayoutSlices;
        private int _froxelLayoutCoarse;
        private float _froxelNearClip;
        private float _froxelFarClip;
        private float _froxelHorizontalPadding;
        private float _froxelVerticalPadding;
        private float _froxelTanHalfHorizontal;
        private float _froxelTanHalfVertical;
        private int _fineAtlasWidth;
        private int _fineAtlasHeight;
        private int _coarseAtlasWidth;
        private int _coarseAtlasHeight;
        private Vector4 _fineGridParams;
        private Vector4 _coarseGridParams;
        private Vector4 _coarseReductionParams;
        private Vector3 _froxelCameraPosition;
        private Vector3 _froxelCameraRight;
        private Vector3 _froxelCameraUp;
        private Vector3 _froxelCameraForward;

#endregion


#region Update Process State

        // Unified delayed update process state
        private bool _volumeDataUpdateRequested = false;
        private bool _isUpdatingVolumes = false;
#if UDONSHARP
        private bool _isUpdateProcessRunning = false; // True while the single delayed update process is scheduled or running
#else
        private Coroutine _updateCoroutine = null; // Coroutine that auto-updates volume data and runtime textures when needed (Non-Udon only)
#endif

#endregion

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
        private int _pointLightExtraDataID;
        private int _pointLightDirectionID;
        private int _pointLightCustomIdID;
        private int _pointLightCountID;
        private int _pointLightCubeCountID;
        private int _pointLightTextureID;
        private int _pointLightTextureTexelCountID;
        private int _pointLightTextureMaxMipID;
        private int _pointLightShadowReprojectionDataID;
        private int _pointLightShadowRotationDataID;
        private int _pointLightShadowCountID;
        private int _pointLightShadowCubeCountID;
        private int _pointLightShadowTextureID;
        private int _pointLightShadowReceiverParamsID;
        private int _clusteringLightsID;
        private int _lightBrightnessCutoffID;
        // Froxel Clustering
        private int _clusteringEnabledID;
        private int _clusterMaskID;
        private int _froxelGridID;
        private int _froxelDepthID;
        private int _froxelDepthStepID;
        private int _coarseClusterMaskID;
        private int _froxelCoarseGridID;
        private int _froxelFineGridID;
        private int _froxelPassID;
        private int _froxelCoarseID;
        private int _froxelProjectionID;
        private int _froxelRightID;
        private int _froxelUpID;
        private int _froxelForwardID;
        // Other
        private int _forceSceneLightingID;
        private int _cubemapMainTexID;
        private int _cubemapSourceTexID;
        private int _cubemapFaceIndexID;

#endregion
    }
}
