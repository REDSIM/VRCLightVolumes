using UnityEngine;
using UnityEngine.Rendering;
using System;

#if UDONSHARP
using VRC.SDKBase;
using UdonSharp;
using VRCGraphics = VRC.SDKBase.VRCGraphics;
#if COMPILER_UDONSHARP
using VRC.SDK3.Rendering;
using VRC.Udon.Common.Interfaces;
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
#region Constants
        private const float Version = 3; // Current VRC Light Volumes shader feature version
        private const int MaxLightVolumeCount = 32;
        private const int MaxPointLightCount = 128;
        private const int MaxLightVolumeRotationVectors = MaxLightVolumeCount * 2;
        private const int MaxLightVolumeUvwScaleVectors = MaxLightVolumeCount * 3;
        private const int MaxLightVolumeLegacyUvwVectors = MaxLightVolumeCount * 6;
        private const RenderTextureFormat FixedCustomTexturesFormat = RenderTextureFormat.ARGBHalf;
        private const float DisabledShadingShadowId = 10000f;
        private const int ShadowTextureFormatHalf = 0;
#endregion

#region Inspector And Runtime References

        [Header("Light Volume Atlas")]
        [Tooltip("Combined Texture3D containing all baked Light Volume data. This field is not used at runtime, see LightVolumeAtlas instead. It specifies the base for the post process chain, if given.")]
        public Texture3D LightVolumeAtlasBase;
        [Tooltip("Combined texture containing all Light Volumes' textures.")]
        public Texture LightVolumeAtlas;

        [Header("Point Light Volumes")]
        [Tooltip("Width of each runtime point light projection texture slice.")]
        public int CustomTexturesWidth = 512;
        [Tooltip("Height of each runtime point light projection texture slice.")]
        public int CustomTexturesHeight = 512;
        [Tooltip("The minimum brightness at a point due to lighting from a Point Light Volume, before the light is culled. Larger values will result in better performance, but light attenuation will be less physically correct.")]
        public float LightsBrightnessCutoff = 0.35f;
        [Tooltip("Width of each runtime shadow cubemap face.")]
        public int ShadowTexturesWidth = 256;
        [Tooltip("Height of each runtime shadow cubemap face.")]
        public int ShadowTexturesHeight = 256;
        [Tooltip("Precision used for baked EVSM shadow maps and the runtime shadow texture array. 0 = ARGBHalf, 1 = ARGBFloat.")]
        public int ShadowTextureFormat = 1;
        [Tooltip("EVSM light bleed reduction applied by the shadow receiver shader. 0 disables reduction, 1 is strongest.")]
        public float ShadowBleedReduction = 0.2f;
        [Tooltip("Raw minimum EVSM variance used by the shadow receiver shader. Authoring setup stores this as a 0..1 logarithmic slider.")]
        public float ShadowMinVariance = 1.0f;

        [Header("Visuals")]
        [Tooltip("When enabled, areas outside Light Volumes fall back to light probes. Otherwise, the Light Volume with the smallest weight is used as fallback. It also improves performance.")]
        public bool LightProbesBlending = true;
        [Tooltip("Disables smooth blending with areas outside Light Volumes. Use it if your entire scene's play area is covered by Light Volumes. It also improves performance.")]
        public bool SharpBounds = true;
        [Tooltip("Automatically updates most of the volumes properties in runtime. Enabling/Disabling, Color and Intensity updates automatically even without this option enabled. Position, Rotation and Scale gets updated only for volumes that are marked dynamic. It's more performant to keep it off.")]
        public bool AutoUpdateVolumes = true;
        [Tooltip("Automatically updates dynamic point light cookie and shadow texture sources in runtime. It's more performant to keep it off.")]
        public bool AutoUpdateTextures = true;
        [Tooltip("Limits the maximum number of additive volumes and Point Light Volumes that can affect a single pixel. This also limits individual Point Light Volume speculars in modern compatible shaders. Lower values improve worst-case performance in overlap-heavy areas.")]
        public int AdditiveMaxOverdraw = 4;
        [Tooltip("Disables min/max brightness limits for modern avatar shaders such as lilToon or Poiyomi. This feature prevents avatars from standing out from the scene due to their brightness. Check this only if you're sure your scene lighting is properly configured.")]
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
        [Tooltip("Cubemap shadow maps count stored in ShadowTextures. Cubemap array elements start from the beginning, 6 elements each.")]
        public int ShadowCubemapsCount = 0;
        [Tooltip("Shadow maps count stored in ShadowTextures. Cubemaps use 6 array elements, single projected shadows use 1 array element.")]
        public int ShadowMapsCount = 0;

        // Material used to copy cubemap source faces into the animated projection texture array
        [HideInInspector] public Material CubemapFaceMaterial;
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
        private bool[] _customSingleTextureAreaCookies = new bool[0];
        private bool[] _customSingleMaterialAreaCookies = new bool[0];
        private PointLightVolumeInstance[] _customSingleAreaCookieReceivers = new PointLightVolumeInstance[0];

        private int[] _customCubemapTextureModes = new int[0]; // Texture layouts: 0 = single 2D texture copied to all faces, 1 = Texture2DArray slices 0..5, 2 = native Cubemap faces
        private int[] _pointLightCustomIDs = new int[0];
        private int[] _customSourceTypes = new int[0]; // Source types per point light: 0 = none, 1 = cubemap texture, 2 = cubemap material, 3 = single texture, 4 = single material
        private Color[] _pointLightAreaCookieAverageColors = new Color[0];
        [HideInInspector] public bool HasAutoCustomTextureUpdates = false;

        // SHADOW TEXTURES
        // Counts describe active prefixes, arrays stay reusable to avoid runtime allocations
        private bool _shadowTexturesInitialized = false;
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

        // Dummy RT required by VRCGraphics material blits when a material generates pixels without a real input texture
        private RenderTexture _dummyRT;

#endregion

#region Volume State And Shader Buffers

        private bool _isInitialized = false; // Tracks one-time shader array initialization in runtime while still allowing editor property IDs to refresh
        private bool _isRangeDirty = false; // Global state mirrors and dirty flags

        // Compact shader buffers are the runtime source of truth. These flags only decide whether to upload them.
        private bool _lightVolumeArraysDirty = false;
        private bool _pointLightArraysDirty = false;
        private bool _updateLightVolumeBuffers = false;
        private bool _updatePointLightBuffers = false;
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
        private Vector4[] _pointLightPosition = new Vector4[MaxPointLightCount];
        private Vector4[] _pointLightColor = new Vector4[MaxPointLightCount];
        private Vector4[] _pointLightExtraData = new Vector4[MaxPointLightCount];
        private Vector4[] _pointLightDirection = new Vector4[MaxPointLightCount];
        private Vector4[] _pointLightCustomId = new Vector4[MaxPointLightCount];
        private Vector4[] _pointLightShadowReprojectionData = new Vector4[MaxPointLightCount];
        private Vector4[] _pointLightShadowRotationData = new Vector4[MaxPointLightCount];

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

        // Active registry index buffer for compact shader uploads
        private int[] _enabledIDs = new int[MaxLightVolumeCount];

        // Public API for other UdonSharp scripts
        public int EnabledCount => _enabledCount;
        public int[] EnabledIDs => _enabledIDs;

        private float _prevLightsBrightnessCutoff = 0.35f;
        private Vector4 _customRenderTextureInfo;

#endregion

#region Update Process State

        // Unified delayed update process state
        private bool _volumeDataUpdateRequested = false;
        private bool _isUpdatingVolumes = false;
#if !UDONSHARP || UNITY_EDITOR
        private bool _prevAutoUpdateVolumes = false;
        private bool _prevAutoUpdateTextures = false;
#endif
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
        private int _pointLightShadowBleedReductionID;
        private int _pointLightShadowMinVarianceID;
        private int _lightBrightnessCutoffID;
        // Other
        private int _forceSceneLightingID;
        private int _cubemapMainTexID;
        private int _cubemapSourceTexID;
        private int _cubemapFaceIndexID;

#endregion

#region Shared Data Helpers

        // Computes a bounding sphere radius squared for area lights
        private float ComputeAreaLightSquaredBoundingSphere(float width, float height, Color color, float intensity, float cutoff) {
            float minSolidAngle = Mathf.Clamp(cutoff / (Mathf.Max(color.r, Mathf.Max(color.g, color.b)) * intensity), -Mathf.PI * 2f, Mathf.PI * 2);
            float A = width * height;
            float w2 = width * width;
            float h2 = height * height;
            float B = 0.25f * (w2 + h2);
            float t = Mathf.Tan(0.25f * minSolidAngle);
            float T = t * t;
            float TB = T * B;
            float discriminant = Mathf.Sqrt(TB * TB + 4.0f * T * A * A);
            return (discriminant - TB) * 0.125f / T;
        }

        // Computes a bounding sphere radius squared for point and spot lights
        private float ComputePointLightSquaredBoundingSphere(Color color, float intensity, float sqSize, float cutoff) {
            float L = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
            return Mathf.Max(Mathf.PI * 2 * L * Mathf.Abs(intensity) / (cutoff * cutoff) - 1, 0) * sqSize;
        }

        // Recalculates point light culling range using manager-side math
        private void ComputePointLightRange(PointLightVolumeInstance instance) {
            if (instance == null) return;
            float cutoff = LightsBrightnessCutoff;
            if (instance.LightType == 2) { // 2: area
                instance.SquaredRange = ComputeAreaLightSquaredBoundingSphere(Mathf.Abs(instance.SquaredScale / instance.Width), instance.Height, instance.Color, instance.Intensity * Mathf.PI, cutoff);
            } else if (instance.ProjectionMode == 1) { // 1: LUT
                instance.SquaredRange = Mathf.Abs(instance.SquaredScale / instance.InverseSquaredRange);
            } else {
                instance.SquaredRange = ComputePointLightSquaredBoundingSphere(instance.Color, instance.Intensity, Mathf.Abs(instance.SquaredScale * instance.LightSourceSize * instance.LightSourceSize), cutoff);
            }
            instance.IsRangeDirty = false;
        }

        // Updates one regular volume instance fields from one Transform matrix read
        private void UpdateLightVolumeTransformData(LightVolumeInstance instance, Matrix4x4 localToWorldMatrix) {
            if (instance == null) return;
            instance.InvWorldMatrix = localToWorldMatrix.inverse;
            Quaternion transformRotation = localToWorldMatrix.rotation;
            Quaternion relativeRotation = transformRotation * instance.InvBakedRotation;
            bool isRotated = relativeRotation.w < 0.999999f;
            instance.IsRotated = isRotated;
            if (!isRotated) {
                instance.RelativeRotationRow0 = new Vector3(1, 0, 0);
                instance.RelativeRotationRow1 = new Vector3(0, 1, 0);
                return;
            }
            Matrix4x4 rotationMatrix = Matrix4x4.Rotate(relativeRotation);
            instance.RelativeRotationRow0 = rotationMatrix.GetRow(0);
            instance.RelativeRotationRow1 = rotationMatrix.GetRow(1);
        }

        // Updates one point light instance fields from one Transform matrix read
        private void UpdatePointLightTransformData(PointLightVolumeInstance instance, Matrix4x4 localToWorldMatrix, bool forceRangeUpdate) {
            if (instance == null) return;
            int lightType = instance.LightType;
            int projectionMode = instance.ProjectionMode;
            float oldSquaredScale = forceRangeUpdate ? 0 : instance.SquaredScale;
            Vector3 scale = localToWorldMatrix.lossyScale;
            float scaleX = Mathf.Abs(scale.x);
            float scaleY = Mathf.Abs(scale.y);
            float scaleZ = Mathf.Abs(scale.z);
            instance.Position = localToWorldMatrix.GetPosition();

            if (lightType != 0 || projectionMode != 0) { // 0: point, 0: parametric
                Quaternion transformRotation = localToWorldMatrix.rotation;
                if (lightType == 2) { // 2: area
                    instance.Rotation = transformRotation;
                    instance.Width = Mathf.Max(scaleX, 0.001f);
                    instance.Height = Mathf.Max(scaleY, 0.001f);
                } else if (lightType == 1 && projectionMode != 2) { // 1: spot, 2: custom cookie
                    instance.Direction = transformRotation * Vector3.forward;
                } else {
                    instance.Rotation = Quaternion.Inverse(transformRotation);
                }
            }

            float averageScale = (scaleX + scaleY + scaleZ) * 0.3333333333f;
            float squaredScale = averageScale * averageScale;
            instance.SquaredScale = squaredScale;
            if (forceRangeUpdate || lightType == 2 || Mathf.Abs(oldSquaredScale - squaredScale) > 0.001f) ComputePointLightRange(instance);
        }

        // Finds the current compact shader slot for one registered regular volume
        private int FindLightVolumeFinalIndex(int registryIndex) {
            if (registryIndex < 0) return -1;
            for (int i = 0; i < _enabledCount; i++) {
                if (_enabledIDs[i] == registryIndex) return i;
            }
            return -1;
        }

        // Finds the current compact shader slot for one registered point light
        private int FindPointLightFinalIndex(int registryIndex) {
            if (registryIndex < 0) return -1;
            for (int i = 0; i < _pointLightCount; i++) {
                if (_enabledPointIDs[i] == registryIndex) return i;
            }
            return -1;
        }

#endregion

#region Change Notifications

        // Used by LightVolumeInstance runtime methods
        public void NotifyLightVolumeChanged(LightVolumeInstance lightVolume, bool rebuildFinalData) {
            // Checking, initializing...
            if (lightVolume == null) return;
            int registryIndex = Array.IndexOf((Array)LightVolumeInstances, lightVolume, 0, LightVolumeInstances.Length);
            if (registryIndex < 0) {
                if (!lightVolume.IsActive) return;
                InitializeLightVolume(lightVolume);
                registryIndex = Array.IndexOf((Array)LightVolumeInstances, lightVolume, 0, LightVolumeInstances.Length);
                if (registryIndex < 0) return;
            }

            //+
            int shaderIndex = FindLightVolumeFinalIndex(registryIndex);
            bool isActive = lightVolume.IsActive;
            if (isActive != (shaderIndex >= 0) || rebuildFinalData) {
                RequestUpdateVolumes();
                return;
            }
            if (!isActive) return;

            // Update shader data
            WriteLightVolumeShaderData(shaderIndex, lightVolume);
            _lightVolumeArraysDirty = true;
            ScheduleUpdateProcess();
        }

        // Used by PointLightVolumeInstance runtime methods
        public void NotifyPointLightVolumeChanged(PointLightVolumeInstance pointLightVolume, bool rebuildFinalData, bool customTexturesChanged, bool shadowTexturesChanged) {
            // Checking, initializing...
            if (pointLightVolume == null) return;
            int registryIndex = Array.IndexOf((Array)PointLightVolumeInstances, pointLightVolume, 0, PointLightVolumeInstances.Length);
            if (registryIndex < 0) {
                if (!pointLightVolume.IsActive) return;
                InitializePointLightVolume(pointLightVolume);
                registryIndex = Array.IndexOf((Array)PointLightVolumeInstances, pointLightVolume, 0, PointLightVolumeInstances.Length);
                if (registryIndex < 0) return;
            }

            int shaderIndex = FindPointLightFinalIndex(registryIndex);
            bool isActive = pointLightVolume.IsActive;
            if (isActive != (shaderIndex >= 0) || rebuildFinalData) {
                if (customTexturesChanged) _customTexturesInitialized = false;
                if (shadowTexturesChanged) _shadowTexturesInitialized = false;
                RequestUpdateVolumes();
                return;
            }
            if (!isActive) return;
            if (customTexturesChanged || shadowTexturesChanged) {
                if (customTexturesChanged) _customTexturesInitialized = false;
                if (shadowTexturesChanged) _shadowTexturesInitialized = false;
                RequestUpdateVolumes();
                return;
            }

            // Update shader data
            WritePointLightShaderData(shaderIndex, registryIndex, pointLightVolume, false);
            _pointLightArraysDirty = true;
            ScheduleUpdateProcess();
        }

#endregion

#region Initialization

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
            _pointLightExtraDataID = VRCShader.PropertyToID("_UdonPointLightVolumeExtraData");
            _pointLightDirectionID = VRCShader.PropertyToID("_UdonPointLightVolumeDirection");
            _pointLightCountID = VRCShader.PropertyToID("_UdonPointLightVolumeCount");
            _pointLightCustomIdID = VRCShader.PropertyToID("_UdonPointLightVolumeCustomID");
            _pointLightCubeCountID = VRCShader.PropertyToID("_UdonPointLightVolumeCubeCount");
            _pointLightTextureID = VRCShader.PropertyToID("_UdonPointLightVolumeTexture");
            _pointLightTextureTexelCountID = VRCShader.PropertyToID("_UdonPointLightVolumeTextureTexelCount");
            _pointLightTextureMaxMipID = VRCShader.PropertyToID("_UdonPointLightVolumeTextureMaxMip");
            _pointLightShadowReprojectionDataID = VRCShader.PropertyToID("_UdonPointLightVolumeShadowReprojectionData");
            _pointLightShadowRotationDataID = VRCShader.PropertyToID("_UdonPointLightVolumeShadowRotationData");
            _pointLightShadowCountID = VRCShader.PropertyToID("_UdonPointLightVolumeShadowCount");
            _pointLightShadowCubeCountID = VRCShader.PropertyToID("_UdonPointLightVolumeShadowCubeCount");
            _pointLightShadowTextureID = VRCShader.PropertyToID("_UdonPointLightVolumeShadowTexture");
            _pointLightShadowBleedReductionID = VRCShader.PropertyToID("_UdonPointLightVolumeShadowBleedReduction");
            _pointLightShadowMinVarianceID = VRCShader.PropertyToID("_UdonPointLightVolumeShadowMinVariance");
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
            VRCShader.SetGlobalVectorArray(_pointLightExtraDataID, _pointLightExtraData);
            VRCShader.SetGlobalVectorArray(_pointLightDirectionID, _pointLightDirection);
            VRCShader.SetGlobalVectorArray(_pointLightCustomIdID, _pointLightCustomId);
            VRCShader.SetGlobalVectorArray(_pointLightShadowReprojectionDataID, _pointLightShadowReprojectionData);
            VRCShader.SetGlobalVectorArray(_pointLightShadowRotationDataID, _pointLightShadowRotationData);
            VRCShader.SetGlobalFloat(_pointLightShadowBleedReductionID, Mathf.Clamp01(ShadowBleedReduction));
            VRCShader.SetGlobalFloat(_pointLightShadowMinVarianceID, Mathf.Max(ShadowMinVariance, 0f));
            _isInitialized = true;
        }

        // Writes a fully disabled state to shader globals so stale counts do not survive after all volumes disappear
        private void SetDisabledShaderState() {
            VRCShader.SetGlobalFloat(_lightVolumeCountID, 0);
            VRCShader.SetGlobalFloat(_lightVolumeAdditiveCountID, 0);
            VRCShader.SetGlobalFloat(_pointLightCountID, 0);
            VRCShader.SetGlobalFloat(_pointLightCubeCountID, 0);
            VRCShader.SetGlobalFloat(_pointLightShadowCubeCountID, 0);
            VRCShader.SetGlobalFloat(_pointLightShadowCountID, 0);
            VRCShader.SetGlobalFloat(_pointLightShadowBleedReductionID, Mathf.Clamp01(ShadowBleedReduction));
            VRCShader.SetGlobalFloat(_pointLightShadowMinVarianceID, Mathf.Max(ShadowMinVariance, 0f));
            VRCShader.SetGlobalFloat(_lightVolumeEnabledID, 0);
        }

#endregion

#region Lifecycle

        // Clears runtime state and schedules the first shader data upload
        private void Start() {
            _isInitialized = false;
            RequestUpdateVolumes();
        }

        // Requests a fresh volume update after this manager becomes active
        private void OnEnable() {
            RequestUpdateVolumes();
        }

        // Stops automatic updates and disables shader globals when this manager is disabled
        private void OnDisable() {
            TryInitialize();
#if UDONSHARP
            _isUpdateProcessRunning = false;
#else
            if (_updateCoroutine != null) {
                StopCoroutine(_updateCoroutine);
                _updateCoroutine = null;
            }
#endif
            SetDisabledShaderState();
        }

#if !UDONSHARP || UNITY_EDITOR
        // To make it work when changing values on UdonSharpBehaviour in editor
        private void Update() {
            if (_prevAutoUpdateVolumes != AutoUpdateVolumes) {
                _prevAutoUpdateVolumes = AutoUpdateVolumes;
                if (AutoUpdateVolumes) RequestUpdateVolumes();
            }
            if (_prevAutoUpdateTextures != AutoUpdateTextures) {
                _prevAutoUpdateTextures = AutoUpdateTextures;
                if (AutoUpdateTextures) ScheduleUpdateProcess();
            }
            if (Mathf.Abs(_prevLightsBrightnessCutoff - LightsBrightnessCutoff) > 0.0001f) {
                _prevLightsBrightnessCutoff = LightsBrightnessCutoff;
                _isRangeDirty = true;
                RequestUpdateVolumes();
            }
        }
#endif

#if !COMPILER_UDONSHARP && (!UDONSHARP || UNITY_EDITOR)
        // Releases generated native resources when the manager object is destroyed
        private void OnDestroy() {
            if (CustomTextures != null && CustomTextures.hideFlags == HideFlags.HideAndDontSave) {
                ReleaseRuntimeRenderTexture(CustomTextures);
                CustomTextures = null;
            }
            if (ShadowTextures != null && ShadowTextures.hideFlags == HideFlags.HideAndDontSave) {
                ReleaseRuntimeRenderTexture(ShadowTextures);
                ShadowTextures = null;
            }
            if (_dummyRT != null) {
                ReleaseRuntimeRenderTexture(_dummyRT);
                _dummyRT = null;
            }
            DestroyCubemapFaceRuntimeMaterial();
        }
#endif

#endregion

#region Runtime Registries

        // Initializes a Light Volume by adding it to the light volume registry. Called automatically at runtime when the object spawns
        public void InitializeLightVolume(LightVolumeInstance lightVolume) {
            if (lightVolume == null) return;
            int count = LightVolumeInstances.Length;
            // Reuse an existing slot so repeated OnEnable calls do not duplicate the same volume
            int existingIndex = Array.IndexOf((Array)LightVolumeInstances, lightVolume, 0, count);
            if (existingIndex >= 0) {
                lightVolume.LightVolumeManager = this;
                RequestUpdateVolumes();
                return;
            }
            // Fill the first stale/null slot before growing the registry array
            int emptyIndex = Array.IndexOf((Array)LightVolumeInstances, null, 0, count);
            if (emptyIndex >= 0) {
                LightVolumeInstances[emptyIndex] = lightVolume;
                lightVolume.LightVolumeManager = this;
                RequestUpdateVolumes();
                return;
            }
            // No empty slot exists, so grow the registry array
            LightVolumeInstance[] targetArray = new LightVolumeInstance[count + 1];
            Array.Copy(LightVolumeInstances, targetArray, count);
            targetArray[count] = lightVolume;
            lightVolume.LightVolumeManager = this;
            LightVolumeInstances = targetArray;
            RequestUpdateVolumes();
        }

        // Deinitializes a Light Volume by removing its registry reference without resizing the array
        public void DeinitializeLightVolume(LightVolumeInstance lightVolume) {
            if (lightVolume == null) return;
            int count = LightVolumeInstances.Length;
            int index = Array.IndexOf((Array)LightVolumeInstances, lightVolume, 0, count);
            if (index < 0) return;
            LightVolumeInstances[index] = null;
            RequestUpdateVolumes();
        }

        // Initializes a Point Light Volume by adding it to the point light volume registry
        public void InitializePointLightVolume(PointLightVolumeInstance pointLightVolume) {
            if (pointLightVolume == null) return;
            int count = PointLightVolumeInstances.Length;
            bool invalidateCustomTextures = _customTexturesInitialized && pointLightVolume.IsActive && (pointLightVolume.CustomTexture != null || pointLightVolume.CustomTextureMaterial != null);
            bool invalidateShadowTextures = _shadowTexturesInitialized && pointLightVolume.IsActive && pointLightVolume.ShadowMapID >= 0;
            // Reuse an existing slot so repeated OnEnable calls do not duplicate the same point light
            int existingIndex = Array.IndexOf((Array)PointLightVolumeInstances, pointLightVolume, 0, count);
            if (existingIndex >= 0) {
                pointLightVolume.LightVolumeManager = this;
                if (invalidateCustomTextures) _customTexturesInitialized = false;
                if (invalidateShadowTextures) _shadowTexturesInitialized = false;
                RequestUpdateVolumes();
                return;
            }
            // Fill the first stale/null slot before growing the registry array
            int emptyIndex = Array.IndexOf((Array)PointLightVolumeInstances, null, 0, count);
            if (emptyIndex >= 0) {
                PointLightVolumeInstances[emptyIndex] = pointLightVolume;
                pointLightVolume.LightVolumeManager = this;
                if (invalidateCustomTextures) _customTexturesInitialized = false;
                if (invalidateShadowTextures) _shadowTexturesInitialized = false;
                RequestUpdateVolumes();
                return;
            }
            // No empty slot exists, so grow the registry array
            PointLightVolumeInstance[] targetArray = new PointLightVolumeInstance[count + 1];
            Array.Copy(PointLightVolumeInstances, targetArray, count);
            targetArray[count] = pointLightVolume;
            pointLightVolume.LightVolumeManager = this;
            PointLightVolumeInstances = targetArray;
            if (invalidateCustomTextures) _customTexturesInitialized = false;
            if (invalidateShadowTextures) _shadowTexturesInitialized = false;
            RequestUpdateVolumes();
        }

        // Deinitializes a Point Light Volume by removing its registry reference without resizing the array
        public void DeinitializePointLightVolume(PointLightVolumeInstance pointLightVolume, bool customTexturesChanged, bool shadowTexturesChanged) {
            if (pointLightVolume == null) return;
            int count = PointLightVolumeInstances.Length;
            int index = Array.IndexOf((Array)PointLightVolumeInstances, pointLightVolume, 0, count);
            if (index < 0) return;
            PointLightVolumeInstances[index] = null;
            if (customTexturesChanged) _customTexturesInitialized = false;
            if (shadowTexturesChanged) _shadowTexturesInitialized = false;
            RequestUpdateVolumes();
        }

#endregion

#region Runtime Texture Caches

        // Rebuilds the runtime cookie texture array and assigns stable shader-side IDs to all point light instances
        public void ReinitializeCustomTextures() {
            BuildCustomTextureSourceCache();
            if (_customTextureArrayDepth <= 0) {
                if (CustomTextures != null) {
                    ReleaseRuntimeRenderTexture(CustomTextures);
                    CustomTextures = null;
                }
                _customTexturesInitialized = true;
                return;
            }
            if (!EnsureRuntimeCustomTextures(CustomTexturesWidth, CustomTexturesHeight, _customTextureArrayDepth)) return;
            ApplyCustomTextures(CustomTextures);
            BlitCustomTextures(false);
            _customTexturesInitialized = true;
            if (AutoUpdateTextures && HasAutoCustomTextureUpdates) ScheduleUpdateProcess();
        }

        // Updates only custom texture sources marked for per-frame refresh
        public void UpdateAutoCustomTextures() {
            if (CustomTextures == null) {
                ReinitializeCustomTextures();
                return;
            }
            BlitCustomTextures(true);
        }

        // Builds deduplicated source arrays and per-instance shader IDs for the runtime cookie texture array
        private void BuildCustomTextureSourceCache() {

            int count = PointLightVolumeInstances.Length;

            // Prepare reusable custom texture source cache arrays for a full rebuild
            if (_pointLightCustomIDs.Length < count || _customSourceTypes.Length < count || _customSingleMaterialAreaCookies.Length < count || _customSingleAreaCookieReceivers.Length < count || _pointLightAreaCookieAverageColors.Length < count) {
                _customCubemapTextures = new Texture[count];
                _customCubemapMaterials = new Material[count];
                _customSingleTextures = new Texture[count];
                _customSingleMaterials = new Material[count];
                _customCubemapTextureModes = new int[count];
                _customCubemapTextureAutoUpdates = new bool[count];
                _customCubemapMaterialAutoUpdates = new bool[count];
                _customSingleTextureAutoUpdates = new bool[count];
                _customSingleMaterialAutoUpdates = new bool[count];
                _customSingleTextureAreaCookies = new bool[count];
                _customSingleMaterialAreaCookies = new bool[count];
                _customSingleAreaCookieReceivers = new PointLightVolumeInstance[count];
                _pointLightCustomIDs = new int[count];
                _customSourceTypes = new int[count];
                Color[] oldAreaCookieAverageColors = _pointLightAreaCookieAverageColors;
                _pointLightAreaCookieAverageColors = new Color[count];
                int averageCopyCount = Mathf.Min(oldAreaCookieAverageColors.Length, count);
                for (int i = 0; i < averageCopyCount; i++) _pointLightAreaCookieAverageColors[i] = oldAreaCookieAverageColors[i];
            } else {
                for (int i = 0; i < _customCubemapTextureCount; i++) _customCubemapTextures[i] = null;
                for (int i = 0; i < _customCubemapMaterialCount; i++) _customCubemapMaterials[i] = null;
                for (int i = 0; i < _customSingleTextureCount; i++) {
                    _customSingleTextures[i] = null;
                    _customSingleTextureAreaCookies[i] = false;
                }
                for (int i = 0; i < _customSingleMaterialCount; i++) {
                    _customSingleMaterials[i] = null;
                    _customSingleMaterialAreaCookies[i] = false;
                }
            }
            int previousSingleSourceCount = _customSingleTextureCount + _customSingleMaterialCount;
            for (int i = 0; i < previousSingleSourceCount; i++) _customSingleAreaCookieReceivers[i] = null;
            HasAutoCustomTextureUpdates = false;
            _customTexturesUseMipMap = false;

            // Projection source counters
            int cubemapTextureCount = 0;
            int cubemapMaterialCount = 0;
            int singleTextureCount = 0;
            int singleMaterialCount = 0;

            // Iterate through registry and collect unique texture/material sources in reusable arrays
            for (int i = 0; i < count; i++) {

                _pointLightCustomIDs[i] = -1;
                PointLightVolumeInstance instance = PointLightVolumeInstances[i];
                if (instance == null || !instance.IsActive) continue;

                int projectionType = instance.ProjectionType;
                if (projectionType == 0) continue; // 0: parametric projection has no custom source

                int lightType = instance.LightType;

                int projectionMode = instance.ProjectionMode;
                if (projectionMode == 0) continue; // 0: parametric projection has no custom source

                bool usesCubemapProjection = lightType == 0 && projectionMode == 2; // 0: point, 2: custom cookie or cubemap
                bool usesAreaCookieProjection = lightType == 2 && projectionMode == 2; // 2: area, 2: custom cookie

                if (projectionType == 1) { // TEXTURE PROJECTION

                    Texture textureSource = instance.CustomTexture;
                    if (textureSource == null) continue;
                    bool autoUpdate = instance.AutoUpdateCustomTexture;
                    if (usesAreaCookieProjection) _customTexturesUseMipMap = true;

                    if (usesCubemapProjection) { // TEXTURE CUBEMAP PROJECTION

                        int index = Array.IndexOf((Array)_customCubemapTextures, textureSource, 0, cubemapTextureCount);
                        if (index < 0) { // Append each unique source once so matching lights share the same texture ID
                            index = cubemapTextureCount;
                            _customCubemapTextures[cubemapTextureCount] = textureSource;
                            _customCubemapTextureModes[cubemapTextureCount] = instance.CustomTextureIsCubemap ? 2 : (instance.CustomTextureHasDepthSlices ? 1 : 0); // Texture layout: 0 = single 2D texture, 1 = Texture2DArray face slices, 2 = native Cubemap.
                            _customCubemapTextureAutoUpdates[cubemapTextureCount] = autoUpdate;
                            cubemapTextureCount++;
                        } else if (autoUpdate) {
                            _customCubemapTextureAutoUpdates[index] = true;
                        }
                        _pointLightCustomIDs[i] = index;
                        _customSourceTypes[i] = 1; // 1: cubemap texture source, already indexed from the start of the cubemap source block

                    } else { // TEXTURE COOKIE PROJECTION

                        int index = Array.IndexOf((Array)_customSingleTextures, textureSource, 0, singleTextureCount);
                        if (index < 0) { // Append each unique source once so matching lights share the same texture ID
                            index = singleTextureCount;
                            _customSingleTextures[singleTextureCount] = textureSource;
                            _customSingleTextureAutoUpdates[singleTextureCount] = autoUpdate;
                            singleTextureCount++;
                        } else if (autoUpdate) {
                            _customSingleTextureAutoUpdates[index] = true;
                        }
                        if (usesAreaCookieProjection) {
                            _customSingleTextureAreaCookies[index] = true;
                        }
                        _pointLightCustomIDs[i] = index;
                        _customSourceTypes[i] = 3; // 3: single texture source, offset after all cubemap sources during final ID assignment

                    }
                    if (autoUpdate) HasAutoCustomTextureUpdates = true;

                } else if (projectionType == 2) { // MATERIAL PROJECTION

                    Material materialSource = instance.CustomTextureMaterial;
                    if (materialSource == null) continue;
                    bool autoUpdate = instance.AutoUpdateCustomTexture;
                    if (usesAreaCookieProjection) _customTexturesUseMipMap = true;

                    if (usesCubemapProjection) { // MATERIAL CUBEMAP PROJECTION

                        int index = Array.IndexOf((Array)_customCubemapMaterials, materialSource, 0, cubemapMaterialCount);
                        if (index < 0) { // Append each unique material once so matching lights share the same texture ID
                            index = cubemapMaterialCount;
                            _customCubemapMaterials[cubemapMaterialCount] = materialSource;
                            _customCubemapMaterialAutoUpdates[cubemapMaterialCount] = autoUpdate;
                            cubemapMaterialCount++;
                        } else if (autoUpdate) {
                            _customCubemapMaterialAutoUpdates[index] = true;
                        }
                        _pointLightCustomIDs[i] = index;
                        _customSourceTypes[i] = 2; // 2: cubemap material source, offset after cubemap texture sources during final ID assignment

                    } else { // MATERIAL SINGLE SLICE PROJECTION

                        int index = Array.IndexOf((Array)_customSingleMaterials, materialSource, 0, singleMaterialCount);
                        if (index < 0) { // Append each unique material once so matching lights share the same texture ID
                            index = singleMaterialCount;
                            _customSingleMaterials[singleMaterialCount] = materialSource;
                            _customSingleMaterialAutoUpdates[singleMaterialCount] = autoUpdate;
                            singleMaterialCount++;
                        } else if (autoUpdate) {
                            _customSingleMaterialAutoUpdates[index] = true;
                        }
                        if (usesAreaCookieProjection) {
                            _customSingleMaterialAreaCookies[index] = true;
                        }
                        _pointLightCustomIDs[i] = index;
                        _customSourceTypes[i] = 4; // 4: single material source, offset after cubemap and single texture sources during final ID assignment

                    }
                    if (autoUpdate) HasAutoCustomTextureUpdates = true;

                }

            }

            _customCubemapTextureCount = cubemapTextureCount;
            _customCubemapMaterialCount = cubemapMaterialCount;
            _customSingleTextureCount = singleTextureCount;
            _customSingleMaterialCount = singleMaterialCount;
            int cubemapsCount = cubemapTextureCount + cubemapMaterialCount;
            CubemapsCount = cubemapsCount;
            _customTextureArrayDepth = cubemapsCount * 6 + singleTextureCount + singleMaterialCount;

            // Convert local source indices into final texture-array source IDs after final counts are known
            for (int i = 0; i < count; i++) {
                int index = _pointLightCustomIDs[i];
                if (index < 0) continue;
                int sourceType = _customSourceTypes[i];
                // SourceType 1 already uses the local cubemap texture index as the final ID; 2/3/4 need offsets.
                if (sourceType == 2) _pointLightCustomIDs[i] = cubemapTextureCount + index; // 2: cubemap materials follow cubemap textures
                else if (sourceType == 3) _pointLightCustomIDs[i] = cubemapsCount + index; // 3: single textures follow every six-slice cubemap source
                else if (sourceType == 4) _pointLightCustomIDs[i] = cubemapsCount + singleTextureCount + index; // 4: single materials follow single textures
                if (sourceType < 3) continue;

                int singleSourceIndex = _pointLightCustomIDs[i] - cubemapsCount;
                if (singleSourceIndex < 0 || _customSingleAreaCookieReceivers[singleSourceIndex] != null) continue;
                PointLightVolumeInstance instance = PointLightVolumeInstances[i];
                if (instance != null && instance.IsActive && instance.LightType == 2 && instance.ProjectionMode == 2) _customSingleAreaCookieReceivers[singleSourceIndex] = instance; // 2: area light, 2: custom cookie
            }

        }

        // Copies custom projection sources into the runtime array. autoUpdatePass copies only sources cached for Auto Update Textures
        private void BlitCustomTextures(bool autoUpdatePass) {
            // Blit each cubemap texture source into 6 array slices
            int cubemapTextureCount = _customCubemapTextureCount;
            for (int i = 0; i < cubemapTextureCount; i++) {
                if (autoUpdatePass && !_customCubemapTextureAutoUpdates[i]) continue;
                BlitCubemapTexture(_customCubemapTextures[i], _customCubemapTextureModes[i], i * 6, CustomTextures);
            }

            // Blit each cubemap material source into 6 array slices
            int cubemapMaterialCount = _customCubemapMaterialCount;
            for (int i = 0; i < cubemapMaterialCount; i++) {
                if (autoUpdatePass && !_customCubemapMaterialAutoUpdates[i]) continue;
                BlitCubemapMaterial(_customCubemapMaterials[i], (cubemapTextureCount + i) * 6, CustomTextures);
            }

            // Blit each 1-slice texture source into 1 array slice after cubemap sources
            int singleBaseSlice = CubemapsCount * 6;
            int singleTextureCount = _customSingleTextureCount;
            for (int i = 0; i < singleTextureCount; i++) {
                if (autoUpdatePass && !_customSingleTextureAutoUpdates[i]) continue;
                Texture sourceTexture = _customSingleTextures[i];
                if (sourceTexture == null) continue;
                int targetSlice = singleBaseSlice + i;
                VRCGraphics.Blit(sourceTexture, CustomTextures, 0, targetSlice);
                if (_customSingleTextureAreaCookies[i]) RequestAreaCookieAverageReadback(i, _customSingleAreaCookieReceivers[i]);
            }

            // Blit each 1-slice material source into 1 array slice after texture sources
            int singleMaterialCount = _customSingleMaterialCount;
            for (int i = 0; i < singleMaterialCount; i++) {
                if (autoUpdatePass && !_customSingleMaterialAutoUpdates[i]) continue;
                Material sourceMaterial = _customSingleMaterials[i];
                if (sourceMaterial == null) continue;
                int targetSlice = singleBaseSlice + singleTextureCount + i;
                BlitMaterialSlice(sourceMaterial, 0, targetSlice, false, CustomTextures);
                if (_customSingleMaterialAreaCookies[i]) RequestAreaCookieAverageReadback(singleTextureCount + i, _customSingleAreaCookieReceivers[singleTextureCount + i]);
            }
        }

        // Requests one area cookie average from the final texture array slice used for old-shader fallback
        private void RequestAreaCookieAverageReadback(int sourceIndex, PointLightVolumeInstance receiver) {
            if (!_customTexturesUseMipMap || CustomTextures == null || receiver == null) return;
            int singleBaseSlice = CubemapsCount * 6;
            int targetSlice = singleBaseSlice + sourceIndex;
            int mipIndex = CustomTextures.mipmapCount - 1;
            int customId = CubemapsCount + sourceIndex;
            receiver.AreaCookieAverageCustomId = customId;
#if COMPILER_UDONSHARP
            VRCAsyncGPUReadback.Request(CustomTextures, mipIndex, 0, 1, 0, 1, targetSlice, 1, TextureFormat.RGBA32, (IUdonEventReceiver)receiver);
#else
            AsyncGPUReadback.Request(CustomTextures, mipIndex, 0, 1, 0, 1, targetSlice, 1, TextureFormat.RGBA32, receiver.OnAsyncGpuReadbackComplete);
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
#endif
        }

        // Caches the readback color and patches the live shader buffer; no volume update is guaranteed after async readback
        public void UploadAreaCookieAverageColor(int customId, Color color) {
            if (customId < 0) return;

            float alpha = color.a;
            color.r *= alpha;
            color.g *= alpha;
            color.b *= alpha;
            color.a = 1f;

            int sourceCount = _pointLightCustomIDs.Length;
            if (_customSourceTypes.Length < sourceCount) sourceCount = _customSourceTypes.Length;
            if (_pointLightAreaCookieAverageColors.Length < sourceCount) sourceCount = _pointLightAreaCookieAverageColors.Length;
            for (int i = 0; i < sourceCount; i++) {
                if (_pointLightCustomIDs[i] != customId || _customSourceTypes[i] < 3) continue;
                _pointLightAreaCookieAverageColors[i] = color;
            }

            int pointLightCount = _pointLightCount;
            PointLightVolumeInstance[] pointInstances = PointLightVolumeInstances;
            int pointInstanceCount = pointInstances.Length;
            bool updatedColor = false;
            for (int shaderIndex = 0; shaderIndex < pointLightCount; shaderIndex++) {
                int sourceIndex = _enabledPointIDs[shaderIndex];
                if (sourceIndex < 0 || sourceIndex >= _pointLightCustomIDs.Length || _pointLightCustomIDs[sourceIndex] != customId) continue;
                if (sourceIndex >= _customSourceTypes.Length || _customSourceTypes[sourceIndex] < 3) continue; // 3/4: single texture/material cookie sources
                if (sourceIndex >= pointInstanceCount) continue;
                PointLightVolumeInstance sourceInstance = pointInstances[sourceIndex];
                if (sourceInstance == null || sourceInstance.LightType != 2 || sourceInstance.ProjectionMode != 2) continue; // 2: area light, 2: custom cookie
                Vector4 shaderColor = _pointLightColor[shaderIndex];
                Vector4 extraData = _pointLightExtraData[shaderIndex];
                float fallbackR = extraData.x * color.r;
                float fallbackG = extraData.y * color.g;
                float fallbackB = extraData.z * color.b;
                if (shaderColor.x == fallbackR && shaderColor.y == fallbackG && shaderColor.z == fallbackB) continue;
                shaderColor.x = fallbackR;
                shaderColor.y = fallbackG;
                shaderColor.z = fallbackB;
                _pointLightColor[shaderIndex] = shaderColor;
                updatedColor = true;
            }
            if (updatedColor) VRCShader.SetGlobalVectorArray(_pointLightColorID, _pointLightColor);
        }

        // Rebuilds the runtime shadow texture array and assigns stable shader-side IDs to all shadowed point light instances
        public void ReinitializeShadowTextures() {
            BuildShadowTextureSourceCache();
            if (_shadowTextureArrayDepth <= 0) { // No shadow sources are active, so release the stale runtime texture array
                if (ShadowTextures != null) {
                    ReleaseRuntimeRenderTexture(ShadowTextures);
                    ShadowTextures = null;
                }
                _shadowTexturesInitialized = true;
                return;
            }
            if (!EnsureRuntimeShadowTextures(ShadowTexturesWidth, ShadowTexturesHeight, _shadowTextureArrayDepth)) return;
            ApplyShadowTextures(ShadowTextures);
            BlitShadowTextures(false);
            _shadowTexturesInitialized = true;
            if (AutoUpdateTextures && HasAutoShadowTextureUpdates) ScheduleUpdateProcess();
        }

        // Updates only shadow cubemap sources marked for per-frame refresh
        public void UpdateAutoShadowTextures() {
            if (ShadowTextures == null) {
                ReinitializeShadowTextures();
                return;
            }
            BlitShadowTextures(true);
        }

        // Updates one shadow texture-array slice for runtime bakers that already manage their own refresh loop
        public void UpdatePointLightShadowTextureSlice(PointLightVolumeInstance instance, int sourceSlice) {
            if (instance == null) return;
            Texture sourceTexture = instance.ShadowMapTexture;
            if (sourceTexture == null) return;

            if (!_shadowTexturesInitialized || ShadowTextures == null || _shadowTextureArrayDepth <= 0) ReinitializeShadowTextures();
            if (ShadowTextures == null || _shadowTextureArrayDepth <= 0) return;

            int shadowId = (int)instance.ShadowMapID;
            if (shadowId < 0) return;

            bool isCubemapShadow = shadowId < ShadowCubemapsCount;
            sourceSlice = isCubemapShadow ? Mathf.Clamp(sourceSlice, 0, 5) : 0;
            int targetSlice = isCubemapShadow ? shadowId * 6 + sourceSlice : ShadowCubemapsCount * 6 + shadowId - ShadowCubemapsCount;
            if (targetSlice >= _shadowTextureArrayDepth) return;

            if (instance.ShadowMapTextureIsCubemap) {
                BlitCubemapFace(sourceTexture, ShadowTextures, sourceSlice, targetSlice);
            } else {
                VRCGraphics.Blit(sourceTexture, ShadowTextures, instance.ShadowMapTextureHasDepthSlices ? sourceSlice : 0, targetSlice);
            }
        }

        // Builds deduplicated source arrays and per-instance shader IDs for the runtime shadow texture array
        private void BuildShadowTextureSourceCache() {

            int count = PointLightVolumeInstances.Length;

            // Prepare reusable shadow texture source cache arrays for a full rebuild
            if (_pointLightShadowIDs.Length < count || _shadowSourceTypes.Length < count) {
                _shadowCubemapTextures = new Texture[count];
                _shadowCubemapMaterials = new Material[count];
                _shadowSingleTextures = new Texture[count];
                _shadowSingleMaterials = new Material[count];
                _shadowCubemapTextureModes = new int[count];
                _shadowCubemapTextureAutoUpdates = new bool[count];
                _shadowCubemapMaterialAutoUpdates = new bool[count];
                _shadowSingleTextureAutoUpdates = new bool[count];
                _shadowSingleMaterialAutoUpdates = new bool[count];
                _pointLightShadowIDs = new int[count];
                _shadowSourceTypes = new int[count];
            } else {
                for (int i = 0; i < _shadowCubemapTextureCount; i++) _shadowCubemapTextures[i] = null;
                for (int i = 0; i < _shadowCubemapMaterialCount; i++) _shadowCubemapMaterials[i] = null;
                for (int i = 0; i < _shadowSingleTextureCount; i++) _shadowSingleTextures[i] = null;
                for (int i = 0; i < _shadowSingleMaterialCount; i++) _shadowSingleMaterials[i] = null;
            }

            int cubemapTextureCount = 0;
            int cubemapMaterialCount = 0;
            int singleTextureCount = 0;
            int singleMaterialCount = 0;
            HasAutoShadowTextureUpdates = false;

            // Iterate the registry once and collect unique shadow sources in reusable arrays
            for (int i = 0; i < count; i++) {

                // Start every point light unresolved. Only valid shadow sources receive a shadow texture ID
                _pointLightShadowIDs[i] = -1;
                PointLightVolumeInstance instance = PointLightVolumeInstances[i];
                if (instance == null || !instance.IsActive) continue;
                if (instance.ShadowMapID < 0 || (instance.ShadowMapTexture == null && instance.ShadowMapMaterial == null)) {
                    instance.ShadowMapID = -1;
                    continue;
                }

                Texture textureSource = instance.ShadowMapTexture;
                bool usesCubemapShadow = instance.ShadowMapUsesCubemap;

                if (textureSource != null) { // Texture shadows mode

                    bool autoUpdate = instance.AutoUpdateShadowMap;
                    if (usesCubemapShadow) { // TEXTURE CUBEMAP SHADOW

                        int index = Array.IndexOf((Array)_shadowCubemapTextures, textureSource, 0, cubemapTextureCount);
                        if (index < 0) { // First use of this texture: append it and reset this source's auto-update flag for the new cache build
                            index = cubemapTextureCount;
                            _shadowCubemapTextures[cubemapTextureCount] = textureSource;
                            _shadowCubemapTextureModes[cubemapTextureCount] = instance.ShadowMapTextureIsCubemap ? 2 : (instance.ShadowMapTextureHasDepthSlices ? 1 : 0); // Texture layout: 0 = single 2D texture, 1 = Texture2DArray face slices, 2 = native Cubemap.
                            _shadowCubemapTextureAutoUpdates[cubemapTextureCount] = autoUpdate;
                            cubemapTextureCount++;
                        } else if (autoUpdate) { // Shared texture source: at least one auto-updated user already makes the shared source auto-updated
                            _shadowCubemapTextureAutoUpdates[index] = true;
                        }
                        _pointLightShadowIDs[i] = index;
                        _shadowSourceTypes[i] = 1; // 1: cubemap texture source, already indexed from the start of the cubemap source block

                    } else { // TEXTURE SINGLE SHADOW

                        int index = Array.IndexOf((Array)_shadowSingleTextures, textureSource, 0, singleTextureCount);
                        if (index < 0) { // First use of this texture: append it and reset this source's auto-update flag for the new cache build
                            index = singleTextureCount;
                            _shadowSingleTextures[singleTextureCount] = textureSource;
                            _shadowSingleTextureAutoUpdates[singleTextureCount] = autoUpdate;
                            singleTextureCount++;
                        } else if (autoUpdate) { // Shared texture source: at least one auto-updated user already makes the shared source auto-updated
                            _shadowSingleTextureAutoUpdates[index] = true;
                        }
                        _pointLightShadowIDs[i] = index;
                        _shadowSourceTypes[i] = 3; // 3: single texture source, offset after all cubemap sources during final ID assignment

                    }
                    if (autoUpdate) HasAutoShadowTextureUpdates = true;

                } else if (instance.ShadowMapMaterial != null) { // Material shadows mode

                    Material materialSource = instance.ShadowMapMaterial;
                    bool autoUpdate = instance.AutoUpdateShadowMap;
                    if (usesCubemapShadow) { // MATERIAL CUBEMAP SHADOW

                        int index = Array.IndexOf((Array)_shadowCubemapMaterials, materialSource, 0, cubemapMaterialCount);
                        if (index < 0) { // First use of this material: append it and reset this source's auto-update flag for the new cache build
                            index = cubemapMaterialCount;
                            _shadowCubemapMaterials[cubemapMaterialCount] = materialSource;
                            _shadowCubemapMaterialAutoUpdates[cubemapMaterialCount] = autoUpdate;
                            cubemapMaterialCount++;
                        } else if (autoUpdate) { // Shared material source: at least one auto-updated user already makes the shared source auto-updated
                            _shadowCubemapMaterialAutoUpdates[index] = true;
                        }
                        _pointLightShadowIDs[i] = index;
                        _shadowSourceTypes[i] = 2; // 2: cubemap material source, offset after cubemap texture sources during final ID assignment

                    } else { // MATERIAL SINGLE SHADOW

                        int index = Array.IndexOf((Array)_shadowSingleMaterials, materialSource, 0, singleMaterialCount);
                        if (index < 0) { // First use of this material: append it and reset this source's auto-update flag for the new cache build
                            index = singleMaterialCount;
                            _shadowSingleMaterials[singleMaterialCount] = materialSource;
                            _shadowSingleMaterialAutoUpdates[singleMaterialCount] = autoUpdate;
                            singleMaterialCount++;
                        } else if (autoUpdate) { // Shared material source: at least one auto-updated user already makes the shared source auto-updated
                            _shadowSingleMaterialAutoUpdates[index] = true;
                        }
                        _pointLightShadowIDs[i] = index;
                        _shadowSourceTypes[i] = 4; // 4: single material source, offset after cubemap and single texture sources during final ID assignment

                    }
                    if (autoUpdate) HasAutoShadowTextureUpdates = true;

                }

            }

            // Updating counts
            _shadowCubemapTextureCount = cubemapTextureCount;
            _shadowCubemapMaterialCount = cubemapMaterialCount;
            _shadowSingleTextureCount = singleTextureCount;
            _shadowSingleMaterialCount = singleMaterialCount;
            int cubemapsCount = cubemapTextureCount + cubemapMaterialCount;
            ShadowCubemapsCount = cubemapsCount;
            ShadowMapsCount = cubemapsCount + singleTextureCount + singleMaterialCount;
            _shadowTextureArrayDepth = cubemapsCount * 6 + singleTextureCount + singleMaterialCount;

            // Convert local source indices into final shadow-map IDs after final counts are known
            for (int i = 0; i < count; i++) {
                int index = _pointLightShadowIDs[i];
                if (index < 0) continue;
                int sourceType = _shadowSourceTypes[i];
                // SourceType 1 already uses the local cubemap texture index as the final ID; 2/3/4 need offsets.
                if (sourceType == 2) _pointLightShadowIDs[i] = cubemapTextureCount + index; // 2: cubemap materials follow cubemap textures
                else if (sourceType == 3) _pointLightShadowIDs[i] = cubemapsCount + index; // 3: single textures follow every six-slice cubemap source
                else if (sourceType == 4) _pointLightShadowIDs[i] = cubemapsCount + singleTextureCount + index; // 4: single materials follow single textures
                PointLightVolumeInstance instance = PointLightVolumeInstances[i];
                instance.ShadowMapID = _pointLightShadowIDs[i];
            }

        }

        // Copies shadow sources into the runtime array. autoUpdatePass copies only sources cached for Auto Update Textures
        private void BlitShadowTextures(bool autoUpdatePass) {
            // Shadow texture sources occupy the first shadow slices, six slices per cubemap
            int cubemapTextureCount = _shadowCubemapTextureCount;
            for (int i = 0; i < cubemapTextureCount; i++) {
                if (autoUpdatePass && !_shadowCubemapTextureAutoUpdates[i]) continue;
                BlitCubemapTexture(_shadowCubemapTextures[i], _shadowCubemapTextureModes[i], i * 6, ShadowTextures);
            }
            // Shadow material sources follow texture sources and are rendered as six generated slices
            int cubemapMaterialCount = _shadowCubemapMaterialCount;
            for (int i = 0; i < cubemapMaterialCount; i++) {
                if (autoUpdatePass && !_shadowCubemapMaterialAutoUpdates[i]) continue;
                BlitCubemapMaterial(_shadowCubemapMaterials[i], (cubemapTextureCount + i) * 6, ShadowTextures);
            }
            // Single shadow textures follow cubemap sources and occupy one array slice each
            int singleBaseSlice = ShadowCubemapsCount * 6;
            int singleTextureCount = _shadowSingleTextureCount;
            for (int i = 0; i < singleTextureCount; i++) {
                if (autoUpdatePass && !_shadowSingleTextureAutoUpdates[i]) continue;
                Texture sourceTexture = _shadowSingleTextures[i];
                if (sourceTexture == null) continue;
                VRCGraphics.Blit(sourceTexture, ShadowTextures, 0, singleBaseSlice + i);
            }
            // Single shadow materials follow single texture sources and occupy one array slice each
            int singleMaterialCount = _shadowSingleMaterialCount;
            for (int i = 0; i < singleMaterialCount; i++) {
                if (autoUpdatePass && !_shadowSingleMaterialAutoUpdates[i]) continue;
                Material sourceMaterial = _shadowSingleMaterials[i];
                if (sourceMaterial == null) continue;
                BlitMaterialSlice(sourceMaterial, 0, singleBaseSlice + singleTextureCount + i, false, ShadowTextures);
            }
        }

#endregion

#region Runtime Texture Rendering

        // Creates or recreates the runtime texture array so it matches an explicit texture layout
        private bool EnsureRuntimeCustomTextures(int width, int height, int depth) {
            if (width <= 0 || height <= 0 || depth <= 0) return false;
            bool useMipMap = _customTexturesUseMipMap;
            bool recreate = ShouldRecreateRuntimeTextureArray(CustomTextures, width, height, depth, FixedCustomTexturesFormat, useMipMap, FilterMode.Trilinear);
            if (!recreate) return CustomTextures != null;
            ReleaseRuntimeRenderTexture(CustomTextures);
            CustomTextures = CreateRuntimeTextureArray(width, height, depth, FixedCustomTexturesFormat, FilterMode.Trilinear, useMipMap);
#if !COMPILER_UDONSHARP
            CustomTextures.name = "CustomTextures";
#endif
            _customTextureArrayDepth = depth;
            return true;
        }

        // Creates or recreates the runtime shadow texture array so it matches an explicit texture layout
        private bool EnsureRuntimeShadowTextures(int width, int height, int depth) {
            if (width <= 0 || height <= 0 || depth <= 0) return false;
            RenderTextureFormat renderTextureFormat = ShadowTextureFormat == ShadowTextureFormatHalf ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGBFloat;
            bool recreate = ShouldRecreateRuntimeTextureArray(ShadowTextures, width, height, depth, renderTextureFormat, false, FilterMode.Bilinear);
            if (!recreate) return ShadowTextures != null;
            ReleaseRuntimeRenderTexture(ShadowTextures);
            ShadowTextures = CreateRuntimeTextureArray(width, height, depth, renderTextureFormat, FilterMode.Bilinear, false);
#if !COMPILER_UDONSHARP
            ShadowTextures.name = "ShadowTextures";
#endif
            _shadowTextureArrayDepth = depth;
            return true;
        }

        // Checks if a runtime texture array must be recreated for the requested layout
        private bool ShouldRecreateRuntimeTextureArray(RenderTexture texture, int width, int height, int depth, RenderTextureFormat format, bool useMipMap, FilterMode filterMode) {
            return texture == null || texture.width != width || texture.height != height || texture.volumeDepth != depth || texture.useMipMap != useMipMap || texture.autoGenerateMips != useMipMap || texture.filterMode != filterMode || texture.format != format;
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
            texture.autoGenerateMips = useMipMap;
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
                if (textureMode == 2) { // Native Cubemap: unwrap the matching cubemap face into this destination slice
                    BlitCubemapFace(sourceTexture, destination, i, targetSlice);
                } else {
                    int sourceSlice = 0;
                    if (textureMode == 1) sourceSlice = i; // Texture2DArray: slices 0..5 already contain the cubemap faces
                    VRCGraphics.Blit(sourceTexture, destination, sourceSlice, targetSlice);
                }
            }
        }

        // Writes a six-face cubemap material source into consecutive destination array slices
        private void BlitCubemapMaterial(Material sourceMaterial, int firstSlice, RenderTexture destination) {
            if (sourceMaterial == null) return;
            for (int i = 0; i < 6; i++) BlitMaterialSlice(sourceMaterial, i, firstSlice + i, true, destination);
        }

        // Runs a material-only update into one texture array slice
        private void BlitMaterialSlice(Material sourceMaterial, int faceIndex, int targetSlice, bool isCubemapUpdate, RenderTexture destination) {
            if (sourceMaterial == null || destination == null) return;
            float infoSlice = targetSlice;
            float infoDepth = destination.volumeDepth;
            if (isCubemapUpdate) {
                infoSlice = Mathf.Clamp(faceIndex, 0, 5);
                infoDepth = 1.0f;
            }
            _customRenderTextureInfo = new Vector4(destination.width, destination.height, infoDepth, infoSlice);
            sourceMaterial.SetVector("_CustomRenderTextureInfo", _customRenderTextureInfo);
#if UDONSHARP
            Texture blitSource = GetMaterialBlitInputTexture();
            sourceMaterial.SetTexture(_cubemapMainTexID, blitSource);
#else
            Texture blitSource = null;
#endif
            BlitMaterialToSlice(blitSource, sourceMaterial, destination, targetSlice);
        }

#if UDONSHARP
        // Returns a stable dummy RT used only to bind the active destination for material-only Udon blits
        private Texture GetMaterialBlitInputTexture() {
            if (_dummyRT != null) return _dummyRT;
            _dummyRT = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            _dummyRT.dimension = TextureDimension.Tex2D;
            _dummyRT.useMipMap = false;
            _dummyRT.autoGenerateMips = false;
            _dummyRT.Create();
            return _dummyRT;
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
            VRCShader.SetGlobalFloat(_pointLightTextureTexelCountID, texture.width * texture.height);
            VRCShader.SetGlobalFloat(_pointLightTextureMaxMipID, Mathf.Max(texture.mipmapCount - 1, 0));
        }

        // Returns the resolved custom projection texture ID for a point light instance
        public int GetPointLightCustomID(PointLightVolumeInstance instance) {
            if (instance == null || PointLightVolumeInstances == null) return -1;
            int index = Array.IndexOf((Array)PointLightVolumeInstances, instance, 0, PointLightVolumeInstances.Length);
            if (index < 0 || index >= _pointLightCustomIDs.Length) return -1;
            return _pointLightCustomIDs[index];
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

#if !COMPILER_UDONSHARP && (!UDONSHARP || UNITY_EDITOR)
        // Destroys the editor/runtime material instance used by non-Udon execution
        private void DestroyCubemapFaceRuntimeMaterial() {
            if (CubemapFaceMaterial == null) return;
            if (CubemapFaceMaterial.hideFlags != HideFlags.HideAndDontSave) return;
            if (Application.isPlaying) Destroy(CubemapFaceMaterial);
            else DestroyImmediate(CubemapFaceMaterial);
            CubemapFaceMaterial = null;
        }

#endif

#endregion

#region Update Process

        // Requests to update volumes next frame
        public void RequestUpdateVolumes() {
            if (_isUpdatingVolumes) return;
            _volumeDataUpdateRequested = true;
            ScheduleUpdateProcess();
        }

        // Schedules the unified delayed update process when it is not already running
        private void ScheduleUpdateProcess() {
#if UDONSHARP
            if (_isUpdateProcessRunning) return;
            _isUpdateProcessRunning = true;
            SendCustomEventDelayedFrames(nameof(UpdateProcess), 1);
#else
            if (_updateCoroutine != null || !isActiveAndEnabled) return;
            _updateCoroutine = StartCoroutine(UpdateCoroutine());
#endif
        }

        // Updates moved dynamic volumes in-place and marks which shader buffer groups need uploading.
        private void UpdateAutoUpdatedVolumeChanges() {
            int enabledCount = _enabledCount;
            int pointLightCount = _pointLightCount;

            // Regular Light Volumes
            for (int i = 0; i < _dynamicLightVolumeCount; i++) {
                LightVolumeInstance instance = _dynamicLightVolumeInstances[i];
                Transform instanceTransform = _dynamicLightVolumeTransforms[i];
                int shaderIndex = _dynamicLightVolumeShaderIndices[i];
                if (instance == null || instanceTransform == null || shaderIndex >= enabledCount) {
                    _updateNeedsVolumeRebuild = true;
                    return;
                }

                Matrix4x4 localToWorldMatrix = instanceTransform.localToWorldMatrix;
                Matrix4x4 previousMatrix = _dynamicLightVolumeMatrices[i];
                if (localToWorldMatrix.Equals(previousMatrix)) continue;

                UpdateLightVolumeTransformData(instance, localToWorldMatrix);
                _dynamicLightVolumeMatrices[i] = localToWorldMatrix;
                WriteLightVolumeTransformShaderData(shaderIndex, instance);
                _updateLightVolumeBuffers = true;
            }

            // Point Light Volumes
            for (int i = 0; i < _dynamicPointLightVolumeCount; i++) {
                PointLightVolumeInstance instance = _dynamicPointLightVolumeInstances[i];
                Transform instanceTransform = _dynamicPointLightVolumeTransforms[i];
                int shaderIndex = _dynamicPointLightVolumeShaderIndices[i];
                if (instance == null || instanceTransform == null || shaderIndex >= pointLightCount) {
                    _updateNeedsVolumeRebuild = true;
                    return;
                }

                Matrix4x4 localToWorldMatrix = instanceTransform.localToWorldMatrix;
                Matrix4x4 previousMatrix = _dynamicPointLightVolumeMatrices[i];
                if (localToWorldMatrix.Equals(previousMatrix)) continue;

                UpdatePointLightTransformData(instance, localToWorldMatrix, false);
                _dynamicPointLightVolumeMatrices[i] = localToWorldMatrix;
                WritePointLightShaderData(shaderIndex, _enabledPointIDs[shaderIndex], instance, false);
                _updatePointLightBuffers = true;
            }
        }

        // Uploads only shader arrays affected by an incremental dynamic transform update
        private void UploadAutoUpdatedVolumeChanges() {
            if (_updateLightVolumeBuffers && _enabledCount != 0) {
                VRCShader.SetGlobalMatrixArray(_lightVolumeInvWorldMatrixID, _invWorldMatrix);
                VRCShader.SetGlobalVectorArray(_lightVolumeRotationID, _relativeRotation);
                VRCShader.SetGlobalVectorArray(_lightVolumeColorID, _colors);
            }
            if (_updatePointLightBuffers && _pointLightCount != 0) {
                VRCShader.SetGlobalVectorArray(_pointLightColorID, _pointLightColor);
                VRCShader.SetGlobalVectorArray(_pointLightExtraDataID, _pointLightExtraData);
                VRCShader.SetGlobalVectorArray(_pointLightPositionID, _pointLightPosition);
                VRCShader.SetGlobalVectorArray(_pointLightDirectionID, _pointLightDirection);
                VRCShader.SetGlobalVectorArray(_pointLightCustomIdID, _pointLightCustomId);
                if (_activeShadowCount > 0) {
                    VRCShader.SetGlobalVectorArray(_pointLightShadowReprojectionDataID, _pointLightShadowReprojectionData);
                    VRCShader.SetGlobalVectorArray(_pointLightShadowRotationDataID, _pointLightShadowRotationData);
                }
            }
            _updateLightVolumeBuffers = false;
            _updatePointLightBuffers = false;
        }

#if UDONSHARP
        // Internal method to auto update volume data and runtime textures every frame while needed
        public void UpdateProcess() {
            if (!enabled || !gameObject.activeInHierarchy) {
                _isUpdateProcessRunning = false;
                return;
            }
            bool keepUpdating;
#else
        // Internal coroutine to auto update volume data and runtime textures every frame while needed
        private IEnumerator UpdateCoroutine() {
            bool keepUpdating;
            do {
                yield return null;
#endif

            // Volume section: full rebuilds, direct dirty uploads and dynamic transform polling
            bool updateVolumes = _volumeDataUpdateRequested;
            _volumeDataUpdateRequested = false;
            _updateLightVolumeBuffers = _lightVolumeArraysDirty;
            _updatePointLightBuffers = _pointLightArraysDirty;
            _updateNeedsVolumeRebuild = false;
            _lightVolumeArraysDirty = false;
            _pointLightArraysDirty = false;

            if (!updateVolumes && AutoUpdateVolumes && (_dynamicLightVolumeCount != 0 || _dynamicPointLightVolumeCount != 0)) {
                UpdateAutoUpdatedVolumeChanges();
                if (_updateNeedsVolumeRebuild) updateVolumes = true;
            }

            if (updateVolumes) {
                UpdateVolumes();
            } else if (_updateLightVolumeBuffers || _updatePointLightBuffers) {
                UploadAutoUpdatedVolumeChanges();
            }

            // Texture section: auto-updates only cached texture sources, without touching point light components
            if (AutoUpdateTextures) {
                if (!_customTexturesInitialized) ReinitializeCustomTextures();
                if (!_shadowTexturesInitialized) ReinitializeShadowTextures();
                if (HasAutoCustomTextureUpdates) UpdateAutoCustomTextures();
                if (HasAutoShadowTextureUpdates) UpdateAutoShadowTextures();
            }

            keepUpdating = AutoUpdateVolumes && (_dynamicLightVolumeCount != 0 || _dynamicPointLightVolumeCount != 0) || AutoUpdateTextures && (HasAutoCustomTextureUpdates || HasAutoShadowTextureUpdates);

            // Keep the delayed loop alive only for continuous monitoring; one-shot requests schedule their own tick.
#if UDONSHARP
            if (keepUpdating) SendCustomEventDelayedFrames(nameof(UpdateProcess), 1);
            else _isUpdateProcessRunning = false;
#else
            } while (isActiveAndEnabled && keepUpdating);
            _updateCoroutine = null;
#endif
        }

#endregion

#region Shader Buffer Rebuild And Upload

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
            Vector4 uvwScale = new Vector4(uvwMin0.w, uvwMin1.w, uvwMin2.w, 0);
            uvwMin0.w = 0;
            uvwMin1.w = 0;
            uvwMin2.w = 0;

            _boundsUvw[i6] = uvwMin0;
            _boundsUvw[i6 + 1] = uvwMin0 + uvwScale;
            _boundsUvw[i6 + 2] = uvwMin1;
            _boundsUvw[i6 + 3] = uvwMin1 + uvwScale;
            _boundsUvw[i6 + 4] = uvwMin2;
            _boundsUvw[i6 + 5] = uvwMin2 + uvwScale;
        }

        // Writes only transform-dependent regular Light Volume data for incremental AutoUpdateVolumes
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
            if (instance.IsRangeDirty) ComputePointLightRange(instance);

            // Caching point light instance data
            int lightType = instance.LightType;
            int projectionMode = instance.ProjectionMode;
            float squaredScale = instance.SquaredScale;
            float squaredRange = instance.SquaredRange;
            Vector4 pos = instance.Position;

            // Point light type
            bool isSpot = lightType == 1; // 1: spot light
            bool isArea = lightType == 2; // 2: area light
            bool isLut = projectionMode == 1; // 1: LUT projection
            bool isCustomCookie = projectionMode == 2; // 2: custom cookie or cubemap projection
            int resolvedCustomId = sourceIndex < _pointLightCustomIDs.Length ? _pointLightCustomIDs[sourceIndex] : -1;

            float angleData;
            if (isArea) {
                float height = instance.Height;
                pos.w = instance.Width;
                angleData = 2f + height;
            } else {
                float typeSign = isSpot ? -1f : 1f;
                if (isLut) pos.w = typeSign * instance.InverseSquaredRange / Mathf.Max(squaredScale, 0.000001f);
                else {
                    float lightSourceSize = instance.LightSourceSize;
                    pos.w = typeSign * lightSourceSize * lightSourceSize * squaredScale;
                }
                if (isSpot && isCustomCookie) angleData = instance.OuterAngleTan;
                else angleData = instance.OuterAngleCos;
            }
            _pointLightPosition[shaderIndex] = pos;

            Vector4 lightColor = instance.Color.linear * instance.Intensity;
            Vector4 extraData = lightColor;
            if (isSpot && isCustomCookie) extraData.x = Mathf.Max(Mathf.Abs(instance.SpotCookieAspect), 0.001f);
            extraData.w = 0f;
            Vector4 color = lightColor;
            int customSourceType = sourceIndex < _customSourceTypes.Length ? _customSourceTypes[sourceIndex] : 0;
            if (isArea && isCustomCookie && resolvedCustomId >= 0 && customSourceType >= 3) {
                Color averageColor = sourceIndex < _pointLightAreaCookieAverageColors.Length ? _pointLightAreaCookieAverageColors[sourceIndex] : Color.clear;
                if (averageColor.a <= 0f) averageColor = Color.white;
                color.x = extraData.x * averageColor.r;
                color.y = extraData.y * averageColor.g;
                color.z = extraData.z * averageColor.b;
            }
            color.w = angleData;
            _pointLightColor[shaderIndex] = color;

            if (isSpot && !isCustomCookie) {
                Vector4 direction = instance.Direction;
                direction.w = instance.ConeFalloff;
                _pointLightDirection[shaderIndex] = direction;
            } else {
                Quaternion r = instance.Rotation;
                _pointLightDirection[shaderIndex] = new Vector4(r.x, r.y, r.z, r.w);
            }

            float shaderCustomId = 0;
            if (resolvedCustomId >= 0) {
                if (isLut) shaderCustomId = resolvedCustomId + 1;
                else if (isCustomCookie) shaderCustomId = -resolvedCustomId - 1;
            }

            int resolvedShadowId = sourceIndex < _pointLightShadowIDs.Length ? _pointLightShadowIDs[sourceIndex] : -1;
            float shadingStrength = Mathf.Clamp01(instance.ShadingStrength);
            bool hasShading = shadingStrength > 0f;
            bool hasShadow = hasShading && ShadowMapsCount > 0 && resolvedShadowId >= 0 && resolvedShadowId < ShadowMapsCount;
            if (countActiveShadow && hasShadow) _activeShadowCount++;
            float shadowFarClip = 0;
            float shadowNearClip = 0;
            if (hasShadow) {
                float farClip = instance.FarClip;
                shadowFarClip = farClip > 0 ? farClip : Mathf.Sqrt(Mathf.Max(squaredRange, 0.000001f));
                shadowNearClip = Mathf.Max(instance.NearClip, 0.0001f);
                if (shadowNearClip >= shadowFarClip) shadowNearClip = shadowFarClip * 0.5f;
            }
            extraData.w = shadowNearClip;
            _pointLightExtraData[shaderIndex] = extraData;
            bool useLocalSpaceShadows = hasShadow && !instance.WorldSpaceShadows;
            float shadowMapID = DisabledShadingShadowId;
            if (hasShading) {
                shadowMapID = hasShadow ? (useLocalSpaceShadows ? -resolvedShadowId - 1f : resolvedShadowId + 1f) : 0f;
                float shadingFade = 1f - shadingStrength;
                if (shadingFade > 0f) shadowMapID += shadowMapID < 0f ? -shadingFade : shadingFade;
            }
            _pointLightCustomId[shaderIndex] = new Vector4(shaderCustomId, shadowMapID, squaredRange, shadowFarClip);

            Vector3 shadowBakePosition = instance.ShadowBakePosition;
            float shadowTanAngle = isSpot ? instance.OuterAngleTan : 0f;
            if (isSpot && shadowTanAngle <= 0f) shadowTanAngle = Mathf.Tan(instance.Angle);
            _pointLightShadowReprojectionData[shaderIndex] = new Vector4(shadowBakePosition.x, shadowBakePosition.y, shadowBakePosition.z, shadowTanAngle);

            Quaternion shadowRotation = useLocalSpaceShadows ? Quaternion.Inverse(instance.transform.rotation) : Quaternion.Inverse(instance.ShadowBakeRotation);
            _pointLightShadowRotationData[shaderIndex] = new Vector4(shadowRotation.x, shadowRotation.y, shadowRotation.z, shadowRotation.w);

        }

        // Recalculates all volume data immediately. Automatic runtime paths should call RequestUpdateVolumes instead
        public void UpdateVolumes() {

            if (_isUpdatingVolumes) return;
            _isUpdatingVolumes = true;
#if !COMPILER_UDONSHARP
            try {
#endif
            TryInitialize();

            // Defines if Force Scene Lighting Feature is enabled in scene. 0 if disabled.
            // Lore: https://x.com/lil_xyzw/status/1961487430256922928?s=20
            VRCShader.SetGlobalInteger(_forceSceneLightingID, ForceSceneLighting ? 1 : 0);

            if (!enabled || !gameObject.activeInHierarchy) {
                SetDisabledShaderState();
                _updateLightVolumeBuffers = false;
                _updatePointLightBuffers = false;
                _updateNeedsVolumeRebuild = false;
                _isUpdatingVolumes = false;
                return;
            }

            bool isAtlas = LightVolumeAtlas != null;

#if !COMPILER_UDONSHARP
            // Editor tests and inspector edits can change fields directly without going through instance notify methods.
            if (!Application.isPlaying) {
                int editorLightVolumeCount = LightVolumeInstances.Length;
                for (int i = 0; i < editorLightVolumeCount; i++) {
                    LightVolumeInstance instance = LightVolumeInstances[i];
                    if (instance == null) continue;
                    instance.IsActive = instance.gameObject.activeInHierarchy && instance.Intensity != 0 && instance.Color != Color.black;
                }
                int editorPointLightCount = PointLightVolumeInstances.Length;
                for (int i = 0; i < editorPointLightCount; i++) {
                    PointLightVolumeInstance instance = PointLightVolumeInstances[i];
                    if (instance == null) continue;
                    bool wasActive = instance.IsActive;
                    instance.IsActive = instance.gameObject.activeInHierarchy && instance.Intensity != 0 && instance.Color != Color.black;
                    if (wasActive == instance.IsActive) continue;
                    if (instance.CustomTexture != null || instance.CustomTextureMaterial != null) _customTexturesInitialized = false;
                    if (instance.ShadowMapID >= 0) _shadowTexturesInitialized = false;
                }
            }
#endif

            // Rebuild runtime texture caches before point light shader IDs are written
            if (!_customTexturesInitialized) ReinitializeCustomTextures();
            if (!_shadowTexturesInitialized) ReinitializeShadowTextures();

            // Rebuild regular Light Volume shader buffers and dynamic transform cache
            _enabledCount = 0;
            _additiveCount = 0;
            _dynamicLightVolumeCount = 0;
            if (isAtlas) {
                int lightVolumeRegistryCount = LightVolumeInstances.Length;
                for (int i = 0; i < lightVolumeRegistryCount && _enabledCount < MaxLightVolumeCount; i++) {
                    LightVolumeInstance instance = LightVolumeInstances[i];
                    if (instance == null) continue;
                    instance.LightVolumeManager = this;
                    if (!instance.IsActive) continue;
                    if (instance.IsDynamic) {
                        Transform instanceTransform = instance.transform;
                        Matrix4x4 localToWorldMatrix = instanceTransform.localToWorldMatrix;
                        UpdateLightVolumeTransformData(instance, localToWorldMatrix);
                        if (_dynamicLightVolumeCount < MaxLightVolumeCount) {
                            _dynamicLightVolumeInstances[_dynamicLightVolumeCount] = instance;
                            _dynamicLightVolumeTransforms[_dynamicLightVolumeCount] = instanceTransform;
                            _dynamicLightVolumeShaderIndices[_dynamicLightVolumeCount] = _enabledCount;
                            _dynamicLightVolumeMatrices[_dynamicLightVolumeCount] = localToWorldMatrix;
                            _dynamicLightVolumeCount++;
                        }
                    }
#if !COMPILER_UDONSHARP
                    else if (!Application.isPlaying) UpdateLightVolumeTransformData(instance, instance.transform.localToWorldMatrix);
#endif
                    _enabledIDs[_enabledCount] = i;
                    if (instance.IsAdditive) _additiveCount++;
                    WriteLightVolumeShaderData(_enabledCount, instance);
                    _enabledCount++;
                }
            }
            _lightVolumeArraysDirty = false;

            // Rebuild Point Light Volume shader buffers and dynamic transform cache
            if (_prevLightsBrightnessCutoff != LightsBrightnessCutoff) {
                _prevLightsBrightnessCutoff = LightsBrightnessCutoff;
                _isRangeDirty = true;
            }
            _pointLightCount = 0;
            _activeShadowCount = 0;
            _dynamicPointLightVolumeCount = 0;
            int pointLightRegistryCount = PointLightVolumeInstances.Length;
            for (int i = 0; i < pointLightRegistryCount && _pointLightCount < MaxPointLightCount; i++) {
                PointLightVolumeInstance instance = PointLightVolumeInstances[i];
                if (instance == null) continue;
                instance.LightVolumeManager = this;
                if (!instance.IsActive) continue;
                if (instance.IsDynamic) {
                    Transform instanceTransform = instance.transform;
                    Matrix4x4 localToWorldMatrix = instanceTransform.localToWorldMatrix;
                    UpdatePointLightTransformData(instance, localToWorldMatrix, true);
                    if (_dynamicPointLightVolumeCount < MaxPointLightCount) {
                        _dynamicPointLightVolumeInstances[_dynamicPointLightVolumeCount] = instance;
                        _dynamicPointLightVolumeTransforms[_dynamicPointLightVolumeCount] = instanceTransform;
                        _dynamicPointLightVolumeShaderIndices[_dynamicPointLightVolumeCount] = _pointLightCount;
                        _dynamicPointLightVolumeMatrices[_dynamicPointLightVolumeCount] = localToWorldMatrix;
                        _dynamicPointLightVolumeCount++;
                    }
                }
#if !COMPILER_UDONSHARP
                else if (!Application.isPlaying) UpdatePointLightTransformData(instance, instance.transform.localToWorldMatrix, true);
#endif
                if (_isRangeDirty || instance.IsRangeDirty) ComputePointLightRange(instance);
                _enabledPointIDs[_pointLightCount] = i;
                WritePointLightShaderData(_pointLightCount, i, instance, true);
                _pointLightCount++;
            }
            _pointLightArraysDirty = false;
            _isRangeDirty = false;

            // Upload scalar shader globals and disable the system if no shader-visible data remains
            int lightVolumeCount = isAtlas ? _enabledCount : 0;
            int additiveCount = isAtlas ? _additiveCount : 0;
            VRCShader.SetGlobalFloat(_lightVolumeVersionID, Version);
            if (lightVolumeCount == 0 && _pointLightCount == 0) {
                SetDisabledShaderState();
            } else {
                if (isAtlas) VRCShader.SetGlobalTexture(_lightVolumeID, LightVolumeAtlas);

                VRCShader.SetGlobalFloat(_lightVolumeCountID, lightVolumeCount);
                VRCShader.SetGlobalFloat(_lightVolumeAdditiveCountID, additiveCount);
                VRCShader.SetGlobalFloat(_lightVolumeOcclusionCountID, 0);
                VRCShader.SetGlobalFloat(_lightVolumeProbesBlendID, LightProbesBlending ? 1 : 0);
                VRCShader.SetGlobalFloat(_lightVolumeSharpBoundsID, SharpBounds ? 1 : 0);
                VRCShader.SetGlobalFloat(_lightVolumeAdditiveMaxOverdrawID, AdditiveMaxOverdraw);

                // Upload regular Light Volume arrays
                if (lightVolumeCount != 0) {
                    VRCShader.SetGlobalVectorArray(_lightVolumeInvLocalEdgeSmoothID, _invLocalEdgeSmooth);
                    VRCShader.SetGlobalVectorArray(_lightVolumeUvwScaleID, _boundsUvwScale);
                    VRCShader.SetGlobalVectorArray(_lightVolumeUvwID, _boundsUvw);
                    VRCShader.SetGlobalMatrixArray(_lightVolumeInvWorldMatrixID, _invWorldMatrix);
                    VRCShader.SetGlobalVectorArray(_lightVolumeRotationID, _relativeRotation);
                    VRCShader.SetGlobalVectorArray(_lightVolumeColorID, _colors);
                }

                // Upload Point Light Volume arrays and runtime texture references
                VRCShader.SetGlobalFloat(_pointLightCountID, _pointLightCount);
                VRCShader.SetGlobalFloat(_pointLightCubeCountID, CubemapsCount);
                int shadowCount = _activeShadowCount > 0 ? ShadowMapsCount : 0;
                VRCShader.SetGlobalFloat(_pointLightShadowCubeCountID, _activeShadowCount > 0 ? ShadowCubemapsCount : 0);
                VRCShader.SetGlobalFloat(_pointLightShadowCountID, shadowCount);
                VRCShader.SetGlobalFloat(_pointLightShadowBleedReductionID, Mathf.Clamp01(ShadowBleedReduction));
                VRCShader.SetGlobalFloat(_pointLightShadowMinVarianceID, Mathf.Max(ShadowMinVariance, 0f));
                if (_pointLightCount != 0) {
                    VRCShader.SetGlobalVectorArray(_pointLightColorID, _pointLightColor);
                    VRCShader.SetGlobalVectorArray(_pointLightExtraDataID, _pointLightExtraData);
                    VRCShader.SetGlobalVectorArray(_pointLightPositionID, _pointLightPosition);
                    VRCShader.SetGlobalVectorArray(_pointLightDirectionID, _pointLightDirection);
                    VRCShader.SetGlobalVectorArray(_pointLightCustomIdID, _pointLightCustomId);
                    if (_activeShadowCount > 0) {
                        VRCShader.SetGlobalVectorArray(_pointLightShadowReprojectionDataID, _pointLightShadowReprojectionData);
                        VRCShader.SetGlobalVectorArray(_pointLightShadowRotationDataID, _pointLightShadowRotationData);
                    }
                    VRCShader.SetGlobalFloat(_lightBrightnessCutoffID, LightsBrightnessCutoff);
                }
                if (CustomTextures != null) {
                    VRCShader.SetGlobalTexture(_pointLightTextureID, CustomTextures);
                    VRCShader.SetGlobalFloat(_pointLightTextureTexelCountID, CustomTextures.width * CustomTextures.height);
                    VRCShader.SetGlobalFloat(_pointLightTextureMaxMipID, Mathf.Max(CustomTextures.mipmapCount - 1, 0));
                }
                if (_activeShadowCount > 0 && ShadowTextures != null) VRCShader.SetGlobalTexture(_pointLightShadowTextureID, ShadowTextures);

                VRCShader.SetGlobalFloat(_lightVolumeEnabledID, 1);
            }

            // Finish volume update state
            _updateLightVolumeBuffers = false;
            _updatePointLightBuffers = false;
            _updateNeedsVolumeRebuild = false;
            _isUpdatingVolumes = false;
#if !COMPILER_UDONSHARP
            } finally {
                _isUpdatingVolumes = false;
            }
#endif
        }

#endregion
    }
}
