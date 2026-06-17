using UnityEngine;
using UnityEngine.Rendering;

#if !UDONSHARP
using System.Collections;
#endif

#if UDONSHARP
using UdonSharp;
#endif

#if COMPILER_UDONSHARP
using VRCGraphics = VRC.SDKBase.VRCGraphics;
using VRCShader = VRC.SDKBase.VRCShader;
#else
using VRCGraphics = UnityEngine.Graphics;
using VRCShader = UnityEngine.Shader;
#endif

namespace VRCLightVolumes {
    [DisallowMultipleComponent]
#if UDONSHARP
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class PointLightShadowRuntimeBaker : UdonSharpBehaviour
#else
    public class PointLightShadowRuntimeBaker : MonoBehaviour
#endif
    {
        // Local shader keywords used by the runtime blur material
        private const string ShadowQualityKeywordLow = "VRCLV_RUNTIME_SHADOW_QUALITY_LOW";
        private const string ShadowQualityKeywordMedium = "VRCLV_RUNTIME_SHADOW_QUALITY_MEDIUM";
        private const string ShadowQualityKeywordHigh = "VRCLV_RUNTIME_SHADOW_QUALITY_HIGH";
        private const string ShadowBlurKeywordUniform = "VRCLV_RUNTIME_SHADOW_BLUR_UNIFORM";
        private const string ShadowBlurKeywordDirect = "VRCLV_RUNTIME_SHADOW_BLUR_DIRECT";
        private const float ShadowBlurBaseResolution = 128f;

        [Tooltip("Point Light Volume instance that receives the runtime-baked shadow texture.")]
        public PointLightVolumeInstance TargetPointLightVolume;
        [Tooltip("Bake one full shadow cubemap when this behaviour is enabled, distributed across bake loop ticks.")]
        public bool BakeOnEnable = true;
        [Tooltip("Continuously update shadow faces through a delayed Udon event loop.")]
        public bool Realtime = false;
        [Tooltip("Resolution used by the camera depth target and local EVSM texture array.")]
        [Min(16)] public int Resolution = 128;
        [Tooltip("How many cubemap faces are rendered per bake loop tick.")]
        [Range(1, 6)] public int RealtimeFacesPerFrame = 1;
        [Tooltip("Shadow blur and depth contrast sample preset. 0 = Low (30 blur taps), 1 = Medium (62 blur taps), 2 = High (126 blur taps).")]
        [Range(0, 2)] public int ShadowBlurSamplePreset = 1;
        [HideInInspector] public Camera ShadowCamera;
        [HideInInspector] public Material RuntimeShadowDepthEncodeMaterial;
        [HideInInspector] public Material RuntimeShadowBlurMaterial;

        // Initialization and cached material state. These prevent repeated setup work during bake ticks
        private bool _shadowSourceInitialized = false;
        private bool _shaderPropertiesInitialized = false;
        private bool _runtimeMaterialsInitialized = false;
        private bool _blurUsesUniformRadius = false;
        private bool _hasCompletedFullBake = false;
        private bool _deferBlurUntilFullCycle = false;

        // Delayed bake loop state. Udon uses a scheduled event flag, while regular MonoBehaviour uses a coroutine handle
#if UDONSHARP
        private bool _bakeLoopScheduled = false;
#else
        private Coroutine _bakeLoopCoroutine = null;
#endif

        // Current bake cycle progress and cached values that are reused while processing cubemap faces
        private int _realtimeFaceIndex = 0;
        private int _bakeLoopRemainingFaces = 0;
        private int _configuredCullingMask = 0;
        private int _lastShadowQualityPreset = -1;
        private int _lastUniformBlurKeyword = -1;
        private int _lastDirectBlurKeyword = -1;
        private int _currentOutputBaseSlice = 0;
        private int _completedOutputBaseSlice = -1;
        private int _bakeResolution = 128;
        private int _bakeSliceCount = 6;
        private int _bakeCullingMask = -1;
        private bool _cycleBakeSettingsValid = false;

        // Camera and light bake parameters copied from the target before rendering to avoid repeated component reads
        private float _configuredNearClip = -1f;
        private float _configuredFarClip = -1f;
        private float _configuredFieldOfView = -1f;
        private float _bakeFarClip = 1f;
        private float _bakeNearClip = 0.01f;
        private float _bakeFieldOfView = 90f;
        private float _bakeTanHalfFov = 1f;
        private float _publishedFarClip = 0f;
        private float _bakeBias = 0f;
        private float _bakeBlur = 0f;
        private float _bakeBlurDepth = 0f;
        private float _cycleBakeFarClip = 1f;
        private float _cycleBakeNearClip = 0.01f;
        private float _cycleBakeBias = 0f;
        private int _cycleBakeCullingMask = -1;

        // Output mode flags selected per bake. Direct output writes into the manager array when resolution allows it
        private bool _useDirectOutput = false;
        private bool _cycleUseDirectOutput = false;
        private bool _useCubemapShadow = true;
        private bool _useBlur = false;
        private bool _hasPublishedFarClip = false;
        private Vector3 _cycleBakePosition = Vector3.zero;
        private Quaternion _cycleBakeRotation = Quaternion.identity;

        // Cached scene references used by the render loop
        private PointLightVolumeInstance _target;
        private LightVolumeManager _manager;
        private Transform _targetTransform;
        private Transform _cameraTransform;

        // Runtime render resources. These are reused during active baking and released when idle or destroyed
        private Material _shadowDepthEncodeMaterial;
        private Material _shadowBlurMaterial;
        private Material _sourceRuntimeShadowDepthEncodeMaterial;
        private Material _sourceRuntimeShadowBlurMaterial;
        private RenderTexture _depthTexture;
        private RenderTexture _shadowTexture;
        private RenderTexture _registrationTexture;
        private RenderTexture _blurTempTexture;
        private RenderTexture _currentOutputTexture;
        private RenderTexture _completedOutputTexture;
        private RenderTexture _materialBlitInputTexture;

        // Cubemap face rotations for the texture-array layout used by the EVSM shadow array
        private Quaternion _faceRotation0 = new Quaternion(0f, -0.70710678f, 0f, 0.70710678f);
        private Quaternion _faceRotation1 = new Quaternion(0f, 0.70710678f, 0f, 0.70710678f);
        private Quaternion _faceRotation2 = new Quaternion(0f, -0.70710678f, 0.70710678f, 0f);
        private Quaternion _faceRotation3 = new Quaternion(0f, 0.70710678f, 0.70710678f, 0f);
        private Quaternion _faceRotation4 = new Quaternion(0f, 1f, 0f, 0f);

        // Cached shader property IDs. Udon-side property lookup is avoided after startup
        private int _depthTextureID;
        private int _farClipID;
        private int _nearClipID;
        private int _biasID;
        private int _tanHalfFovID;
        private int _sourceArrayID;
        private int _depthArrayID;
        private int _faceIndexID;
        private int _sourceBaseSliceID;
        private int _depthBaseSliceID;
        private int _blurDirectionID;
        private int _blurRadiusID;
        private int _blurDepthID;
        private int _invResolutionID;

        // Initializes cached references and optionally runs one non-Udon editor bake
        private void Start() {
            InitializeShaderProperties();
            InitializeRuntimeMaterials();
            CacheRuntimeReferences();
            RefreshBakeSettings();
            ConfigureCamera(_bakeFarClip, _bakeNearClip, _bakeCullingMask, _bakeFieldOfView);
#if !UDONSHARP
            if (BakeOnEnable || Realtime) StartBakeLoopCycle();
#endif
        }

        // Starts deferred Udon baking when this behaviour becomes active
        private void OnEnable() {
#if UDONSHARP
            _bakeLoopScheduled = false;
#endif
            // The shared manager texture can be reinitialized while this baker is disabled without changing the RenderTexture reference
            _hasCompletedFullBake = false;
            _deferBlurUntilFullCycle = true;
#if UDONSHARP
            if (BakeOnEnable || Realtime) StartBakeLoopCycle();
#endif
        }

        // Stops pending realtime work from rescheduling itself
        private void OnDisable() {
#if UDONSHARP
            _bakeLoopScheduled = false;
#else
            if (_bakeLoopCoroutine != null) {
                StopCoroutine(_bakeLoopCoroutine);
                _bakeLoopCoroutine = null;
            }
#endif
            _bakeLoopRemainingFaces = 0;
            _cycleBakeSettingsValid = false;
            ReleaseIdleBakeTextures();
        }

        // Releases locally created render textures when this baker is destroyed
        private void OnDestroy() {
            ReleaseRuntimeTextures();
            ReleaseRuntimeMaterials();
        }

        // Bakes all shadow slices immediately
        public void BakeShadows() {
            if (Realtime) StartBakeLoopCycle();
            if (!PrepareBake()) return;

            Vector3 bakePosition = _targetTransform.position;
            Quaternion bakeRotation = _targetTransform.rotation;

            if (_useDirectOutput && !PrepareOutput(bakePosition, _bakeFarClip, _bakeBias, true)) return;
            // Select the active output once; face encode passes write directly into this texture/slice range
            if (_useDirectOutput && _manager != null) {
                _currentOutputTexture = _manager.ShadowTextures;
                int shadowId = _target != null ? (int)_target.ShadowMapID : -1;
                if (shadowId < 0) _currentOutputBaseSlice = 0;
                else if (_useCubemapShadow) _currentOutputBaseSlice = shadowId * 6;
                else {
                    int cubemapCount = _manager.ShadowCubemapsCount;
                    _currentOutputBaseSlice = cubemapCount * 6 + shadowId - cubemapCount;
                }
            } else {
                _currentOutputTexture = _shadowTexture;
                _currentOutputBaseSlice = 0;
            }
            ConfigureCamera(_bakeFarClip, _bakeNearClip, _bakeCullingMask, _bakeFieldOfView);
            if (_useCubemapShadow) RenderDepthFacesToShadowMap(bakePosition, bakeRotation, _bakeFarClip, _bakeBias);
            else RenderDepthSingleToShadowMap(bakePosition, bakeRotation, _bakeFarClip, _bakeBias);
            if (_useBlur && PrepareShadowBlurMaterial()) BlurFaces(0, _bakeSliceCount, _useDirectOutput, false);
            _completedOutputTexture = _currentOutputTexture;
            _completedOutputBaseSlice = _currentOutputBaseSlice;
            _hasCompletedFullBake = true;
            _deferBlurUntilFullCycle = false;
            if (!_useDirectOutput) {
                if (!PrepareOutput(bakePosition, _bakeFarClip, _bakeBias, false)) return;
                // Non-direct output uses a local array, so copy all baked slices into the manager array
                if (_manager != null && _target != null) {
                    for (int i = 0; i < _bakeSliceCount; i++) _manager.UpdatePointLightShadowTextureSlice(_target, i);
                }
            }
            if (!Realtime) ReleaseIdleBakeTextures();
        }

        // Internal delayed event loop used by realtime shadow baking
        public void _RealtimeBakeLoop() {
#if UDONSHARP
            _bakeLoopScheduled = false;
#endif
            if (!enabled || !gameObject.activeInHierarchy) return;
            RunBakeLoopStep();
        }

#if !UDONSHARP
        // Internal coroutine to run distributed bake ticks outside Udon
        private IEnumerator BakeLoopCoroutine() {
            do {
                yield return null;
                RunBakeLoopStep();
            } while (isActiveAndEnabled && (Realtime || _bakeLoopRemainingFaces > 0));

            _bakeLoopCoroutine = null;
        }

#endif

        // Starts one shadow bake loop cycle if one is not already in progress
        private void StartBakeLoopCycle() {
            if (_bakeLoopRemainingFaces <= 0) {
                _bakeLoopRemainingFaces = _bakeSliceCount > 0 ? _bakeSliceCount : 6;
                _realtimeFaceIndex = 0;
                _cycleUseDirectOutput = Realtime;
                _deferBlurUntilFullCycle = !_hasCompletedFullBake;
                _cycleBakeSettingsValid = false;
            }
            ScheduleBakeLoop();
        }

        // Runs one distributed bake tick and schedules the next tick only when a cycle must continue
        private void RunBakeLoopStep() {
            if (_bakeLoopRemainingFaces <= 0) {
                if (!Realtime) return;
                StartBakeLoopCycle();
            }

            bool stepSucceeded = BakeRealtimeStep();
            if (!stepSucceeded && !Realtime) _bakeLoopRemainingFaces = 0;

            if (_bakeLoopRemainingFaces > 0) ScheduleBakeLoop();
            else if (Realtime) StartBakeLoopCycle();
            else ReleaseIdleBakeTextures();
        }

        // Prepares references and render textures shared by full and incremental bakes
        private bool PrepareBake() {
            if (!_shaderPropertiesInitialized) InitializeShaderProperties();
            if (!_runtimeMaterialsInitialized || _sourceRuntimeShadowDepthEncodeMaterial != RuntimeShadowDepthEncodeMaterial || _sourceRuntimeShadowBlurMaterial != RuntimeShadowBlurMaterial) InitializeRuntimeMaterials();
            if (!CacheRuntimeReferences()) return false;
            if (_shadowDepthEncodeMaterial == null) return false;
            if (!_target.enabled || !_target.gameObject.activeInHierarchy) return false;
            if (_target.Intensity == 0f || _target.Color == Color.black) return false;

            RefreshBakeSettings();

            RenderTextureFormat format = _manager != null && _manager.ShadowTextureFormat == 0 ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGBFloat;

            if (!EnsureDepthTexture(_bakeResolution)) return false;
            if (_useDirectOutput) {
                _registrationTexture = EnsureOwnedArrayTexture(_registrationTexture, format, 1, _bakeSliceCount, FilterMode.Point);
                if (_registrationTexture == null) return false;
                if (_shadowTexture != null && !IsShadowTexturePublished()) {
                    ReleaseRuntimeRenderTexture(_shadowTexture);
                    _shadowTexture = null;
                }
            } else {
                _shadowTexture = EnsureOwnedArrayTexture(_shadowTexture, format, _bakeResolution, _bakeSliceCount, FilterMode.Bilinear);
                if (_shadowTexture == null) return false;
            }

            if (_useBlur) {
                bool blurTextureCompatible = _blurTempTexture != null && _blurTempTexture.width == _bakeResolution && _blurTempTexture.height == _bakeResolution && _blurTempTexture.dimension == TextureDimension.Tex2DArray && _blurTempTexture.volumeDepth == _bakeSliceCount && !_blurTempTexture.useMipMap && _blurTempTexture.filterMode == FilterMode.Bilinear;
#if !COMPILER_UDONSHARP
                blurTextureCompatible = blurTextureCompatible && _blurTempTexture.format == format && !_blurTempTexture.autoGenerateMips;
#endif
                if (!blurTextureCompatible) {
                    ReleaseBlurTempTexture();
                    _blurTempTexture = CreateArrayTexture(format, _bakeResolution, _bakeSliceCount, FilterMode.Bilinear);
                }
                if (_blurTempTexture == null) return false;
            } else {
                if (_blurTempTexture != null) ReleaseBlurTempTexture();
            }

            return true;
        }

        // Caches scene references used by the hot path
        private bool CacheRuntimeReferences() {
            if (_target != TargetPointLightVolume) {
                _target = TargetPointLightVolume;
                _manager = _target != null ? _target.LightVolumeManager : null;
                _targetTransform = _target != null ? _target.transform : null;
                _shadowSourceInitialized = false;
                _hasCompletedFullBake = false;
                _deferBlurUntilFullCycle = false;
                _completedOutputTexture = null;
                _completedOutputBaseSlice = -1;
                _cycleBakeSettingsValid = false;
                _publishedFarClip = 0f;
                _hasPublishedFarClip = false;
                if (_target != null) {
#if UDONSHARP
                    _target.SetProgramVariable("IsRangeDirty", true);
#else
                    _target.IsRangeDirty = true;
#endif
                }
            } else if (_target != null && _manager != _target.LightVolumeManager) {
                _manager = _target.LightVolumeManager;
                _shadowSourceInitialized = false;
                _hasCompletedFullBake = false;
                _deferBlurUntilFullCycle = false;
                _completedOutputTexture = null;
                _completedOutputBaseSlice = -1;
                _cycleBakeSettingsValid = false;
#if UDONSHARP
                _target.SetProgramVariable("IsRangeDirty", true);
#else
                _target.IsRangeDirty = true;
#endif
            }

            if (ShadowCamera != null) {
                Transform cameraTransform = ShadowCamera.transform;
                if (_cameraTransform != cameraTransform) {
                    _cameraTransform = cameraTransform;
                    _configuredFarClip = -1f;
                    _configuredFieldOfView = -1f;
                }
            } else {
                _cameraTransform = null;
                _configuredFarClip = -1f;
                _configuredFieldOfView = -1f;
            }

            return _target != null && _targetTransform != null && ShadowCamera != null && _cameraTransform != null;
        }

        // Copies target bake settings into local fields once before the bake hot path runs
        private void RefreshBakeSettings() {
            if (_target == null) return;
            if (_target.IsRangeDirty) RefreshTargetRangeForBake();
            _bakeResolution = Resolution;
            _bakeCullingMask = _target.LayerMask;
            _bakeNearClip = _target.NearClip;
            _bakeBias = _target.Bias;
            _bakeBlur = _target.Blur;
            _bakeBlurDepth = _target.ContactHardening;
            float targetFarClip = _target.FarClip;
            bool useTargetFarClip = targetFarClip > 0f && (!_hasPublishedFarClip || Mathf.Abs(targetFarClip - _publishedFarClip) > 0.0001f);
            _bakeFarClip = useTargetFarClip ? Mathf.Max(targetFarClip, 0.0001f) : Mathf.Sqrt(Mathf.Max(_target.SquaredRange, 0.000001f));
            bool useCubemapShadow = _target.LightType != 1 || _target.ShadowMapUsesCubemap; // 1: spot
            int bakeSliceCount = useCubemapShadow ? 6 : 1;
            if (_useCubemapShadow != useCubemapShadow || _bakeSliceCount != bakeSliceCount) {
                _useCubemapShadow = useCubemapShadow;
                _bakeSliceCount = bakeSliceCount;
                _realtimeFaceIndex = 0;
                if (_bakeLoopRemainingFaces > _bakeSliceCount) _bakeLoopRemainingFaces = _bakeSliceCount;
                _shadowSourceInitialized = false;
                _completedOutputTexture = null;
                _completedOutputBaseSlice = -1;
                _hasCompletedFullBake = false;
                _deferBlurUntilFullCycle = true;
                _cycleBakeSettingsValid = false;
                _configuredFieldOfView = -1f;
            }
            if (_useCubemapShadow) {
                _bakeFieldOfView = 90f;
                _bakeTanHalfFov = 1f;
            } else {
                _bakeFieldOfView = Mathf.Clamp(_target.Angle * Mathf.Rad2Deg * 2f, 0.1f, 179.9f);
                _bakeTanHalfFov = Mathf.Tan(_bakeFieldOfView * 0.5f * Mathf.Deg2Rad);
            }
            _useBlur = _bakeBlur > 0.0001f && _shadowBlurMaterial != null;
            _useDirectOutput = _manager != null && (Realtime || _cycleUseDirectOutput) && _manager.ShadowTexturesWidth == _bakeResolution && _manager.ShadowTexturesHeight == _bakeResolution;
        }

        // Recalculates target range for baker camera settings without notifying the manager from the realtime bake loop
        private void RefreshTargetRangeForBake() {
            if (_target.IsDynamic && _targetTransform != null) {
                Vector3 scale = _targetTransform.lossyScale;
                if (_target.LightType == 2) { // 2: area
                    float width = Mathf.Max(Mathf.Abs(scale.x), 0.001f);
                    float height = Mathf.Max(Mathf.Abs(scale.y), 0.001f);
#if UDONSHARP
                    _target.SetProgramVariable("Width", width);
                    _target.SetProgramVariable("Height", height);
#else
                    _target.Width = width;
                    _target.Height = height;
#endif
                }
                float averageScale = (scale.x + scale.y + scale.z) * 0.3333333333f;
                float squaredScale = averageScale * averageScale;
#if UDONSHARP
                _target.SetProgramVariable("SquaredScale", squaredScale);
#else
                _target.SquaredScale = squaredScale;
#endif
            }
            float cutoff = _manager != null ? _manager.LightsBrightnessCutoff : 0.35f;
            float squaredRange;
            if (_target.LightType == 2) { // 2: area
                float minSolidAngle = Mathf.Clamp(cutoff / (Mathf.Max(_target.Color.r, Mathf.Max(_target.Color.g, _target.Color.b)) * _target.Intensity * Mathf.PI), -Mathf.PI * 2f, Mathf.PI * 2f);
                float width = Mathf.Abs(_target.SquaredScale / _target.Width);
                float height = _target.Height;
                float area = width * height;
                float widthSquared = width * width;
                float heightSquared = height * height;
                float halfExtentSquared = 0.25f * (widthSquared + heightSquared);
                float tangent = Mathf.Tan(0.25f * minSolidAngle);
                float tangentSquared = tangent * tangent;
                float tangentHalfExtent = tangentSquared * halfExtentSquared;
                float discriminant = Mathf.Sqrt(tangentHalfExtent * tangentHalfExtent + 4.0f * tangentSquared * area * area);
                squaredRange = (discriminant - tangentHalfExtent) * 0.125f / tangentSquared;
            } else if (_target.ProjectionMode == 1) { // 1: LUT
                squaredRange = Mathf.Abs(_target.SquaredScale / _target.InverseSquaredRange);
            } else {
                float maxColor = Mathf.Max(_target.Color.r, Mathf.Max(_target.Color.g, _target.Color.b));
                float squaredSize = Mathf.Abs(_target.SquaredScale * _target.LightSourceSize * _target.LightSourceSize);
                squaredRange = Mathf.Max(Mathf.PI * 2f * maxColor * Mathf.Abs(_target.Intensity) / (cutoff * cutoff) - 1f, 0f) * squaredSize;
            }
#if UDONSHARP
            _target.SetProgramVariable("SquaredRange", squaredRange);
            _target.SetProgramVariable("IsRangeDirty", false);
#else
            _target.SquaredRange = squaredRange;
            _target.IsRangeDirty = false;
#endif
        }

        // Creates or validates the camera depth render target
        private bool EnsureDepthTexture(int resolution) {
            if (_depthTexture != null && _depthTexture.width == resolution && _depthTexture.height == resolution && _depthTexture.dimension == TextureDimension.Tex2D && !_depthTexture.useMipMap && _depthTexture.filterMode == FilterMode.Point) {
#if COMPILER_UDONSHARP
                return true;
#else
                if (_depthTexture.format == RenderTextureFormat.Depth && !_depthTexture.autoGenerateMips) return true;
#endif
            }
            ReleaseRuntimeRenderTexture(_depthTexture);
            _depthTexture = new RenderTexture(resolution, resolution, 32, RenderTextureFormat.Depth, RenderTextureReadWrite.Linear);
            _depthTexture.dimension = TextureDimension.Tex2D;
            _depthTexture.useMipMap = false;
            _depthTexture.autoGenerateMips = false;
            _depthTexture.wrapMode = TextureWrapMode.Clamp;
            _depthTexture.filterMode = FilterMode.Point;
            _depthTexture.anisoLevel = 0;
#if !COMPILER_UDONSHARP
            _depthTexture.hideFlags = HideFlags.HideAndDontSave;
#endif
            _depthTexture.Create();
            return _depthTexture != null;
        }

        // Reuses or recreates a locally-owned texture array
        private RenderTexture EnsureOwnedArrayTexture(RenderTexture texture, RenderTextureFormat format, int resolution, int sliceCount, FilterMode filterMode) {
            if (texture != null && texture.width == resolution && texture.height == resolution && texture.dimension == TextureDimension.Tex2DArray && texture.volumeDepth == sliceCount && !texture.useMipMap && texture.filterMode == filterMode) {
#if COMPILER_UDONSHARP
                return texture;
#else
                if (texture.format == format && !texture.autoGenerateMips) return texture;
#endif
            }
            ReleaseRuntimeRenderTexture(texture);
            return CreateArrayTexture(format, resolution, sliceCount, filterMode);
        }

        // Creates a texture array with the requested format, slice count and filtering
        private RenderTexture CreateArrayTexture(RenderTextureFormat format, int resolution, int sliceCount, FilterMode filterMode) {
            RenderTexture texture = new RenderTexture(resolution, resolution, 0, format, RenderTextureReadWrite.Linear);
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

        // Configures the depth camera for one shadow render
        private void ConfigureCamera(float farClip, float nearClip, int cullingMask, float fieldOfView) {
            float safeFarClip = Mathf.Max(farClip, 0.0001f);
            float safeNearClip = Mathf.Max(nearClip, 0.0001f);
            float safeFieldOfView = Mathf.Clamp(fieldOfView, 0.1f, 179.9f);
            if (safeNearClip >= safeFarClip) safeNearClip = safeFarClip * 0.5f;
            if (_configuredFarClip == safeFarClip && _configuredNearClip == safeNearClip && _configuredCullingMask == cullingMask && _configuredFieldOfView == safeFieldOfView) return;

            ShadowCamera.enabled = false;
            ShadowCamera.clearFlags = CameraClearFlags.Depth;
            ShadowCamera.backgroundColor = Color.white;
            ShadowCamera.orthographic = false;
            ShadowCamera.fieldOfView = safeFieldOfView;
            ShadowCamera.aspect = 1f;
            ShadowCamera.nearClipPlane = safeNearClip;
            ShadowCamera.farClipPlane = safeFarClip;
            ShadowCamera.depthTextureMode = DepthTextureMode.None;
            ShadowCamera.renderingPath = RenderingPath.Forward;
            ShadowCamera.allowHDR = false;
            ShadowCamera.allowMSAA = false;
            ShadowCamera.useOcclusionCulling = false;
            ShadowCamera.cullingMask = cullingMask;
            ShadowCamera.stereoTargetEye = StereoTargetEyeMask.None;
            ShadowCamera.ResetReplacementShader();

            _configuredFarClip = safeFarClip;
            _configuredNearClip = safeNearClip;
            _configuredCullingMask = cullingMask;
            _configuredFieldOfView = safeFieldOfView;
        }

        // Registers the output texture and refreshes manager metadata before a bake writes pixels
        private bool PrepareOutput(Vector3 bakePosition, float farClip, float bias, bool useDirectOutput) {
            bool shadowDataChanged = ApplyTargetShadowSourceInternal(bakePosition, farClip, bias, useDirectOutput);
            if (_manager == null) return !useDirectOutput;
            bool rebuildShadowArray = !_shadowSourceInitialized || _manager.ShadowTextures == null || _manager.ShadowMapsCount <= 0;
            if (rebuildShadowArray) {
                // Rebuilding can overwrite the shared shadow slices, so the next realtime cycle must finish all faces before seam-aware blur
                _completedOutputTexture = null;
                _completedOutputBaseSlice = -1;
                _hasCompletedFullBake = false;
                _deferBlurUntilFullCycle = true;
                _manager.InitializePointLightVolume(_target);
                _manager.ReinitializeShadowTextures();
                _shadowSourceInitialized = true;
            }
            if (rebuildShadowArray || shadowDataChanged) {
                _manager.RequestUpdateVolumes();
            }
            if (useDirectOutput) return _manager.ShadowTextures != null && _target.ShadowMapID >= 0;
            return true;
        }

        // Updates the target light shadow source and returns whether shader-side metadata changed
        private bool ApplyTargetShadowSourceInternal(Vector3 bakePosition, float farClip, float bias, bool useDirectOutput) {
            Texture sourceTexture = useDirectOutput ? _registrationTexture : _shadowTexture;
            bool sourceIsCubemap = sourceTexture != null && sourceTexture.dimension == TextureDimension.Cube;
            bool sourceHasSlices = sourceTexture != null && sourceTexture.dimension == TextureDimension.Tex2DArray && _useCubemapShadow;
            bool sourceUsesCubemap = _useCubemapShadow;
            bool sourceChanged = _target.ShadowMapID < 0 || _target.ShadowMapTexture != sourceTexture || _target.ShadowMapMaterial != null || _target.AutoUpdateShadowMap || _target.ShadowMapTextureIsCubemap != sourceIsCubemap || _target.ShadowMapTextureHasDepthSlices != sourceHasSlices || _target.ShadowMapUsesCubemap != sourceUsesCubemap;
            bool bakePositionChanged = _target.ShadowBakePosition != bakePosition;
            Quaternion bakeRotation = _targetTransform.rotation;
            bool bakeRotationChanged = _target.ShadowBakeRotation != bakeRotation;
            bool metadataChanged = sourceChanged || _target.FarClip != farClip || (_target.WorldSpaceShadows && (bakePositionChanged || bakeRotationChanged));

            if (_target.ShadowMapID < 0) {
#if UDONSHARP
                _target.SetProgramVariable("ShadowMapID", 0f);
#else
                _target.ShadowMapID = 0f;
#endif
            }
            if (sourceChanged) {
#if UDONSHARP
                _target.SetProgramVariable("ShadowMapTexture", sourceTexture);
                _target.SetProgramVariable("ShadowMapMaterial", null);
                _target.SetProgramVariable("AutoUpdateShadowMap", false);
                _target.SetProgramVariable("ShadowMapTextureIsCubemap", sourceIsCubemap);
                _target.SetProgramVariable("ShadowMapTextureHasDepthSlices", sourceHasSlices);
                _target.SetProgramVariable("ShadowMapUsesCubemap", sourceUsesCubemap);
#else
                _target.ShadowMapTexture = sourceTexture;
                _target.ShadowMapMaterial = null;
                _target.AutoUpdateShadowMap = false;
                _target.ShadowMapTextureIsCubemap = sourceIsCubemap;
                _target.ShadowMapTextureHasDepthSlices = sourceHasSlices;
                _target.ShadowMapUsesCubemap = sourceUsesCubemap;
#endif
                _shadowSourceInitialized = false;
            }
            if (_target.Bias != bias) {
#if UDONSHARP
                _target.SetProgramVariable("Bias", bias);
#else
                _target.Bias = bias;
#endif
            }
            if (_target.FarClip != farClip) {
#if UDONSHARP
                _target.SetProgramVariable("FarClip", farClip);
#else
                _target.FarClip = farClip;
#endif
            }
            _publishedFarClip = farClip;
            _hasPublishedFarClip = true;
            if (bakePositionChanged) {
#if UDONSHARP
                _target.SetProgramVariable("ShadowBakePosition", bakePosition);
#else
                _target.ShadowBakePosition = bakePosition;
#endif
            }
            if (bakeRotationChanged) {
#if UDONSHARP
                _target.SetProgramVariable("ShadowBakeRotation", bakeRotation);
#else
                _target.ShadowBakeRotation = bakeRotation;
#endif
            }
            return metadataChanged;
        }

        // Renders six point-light depth faces and encodes them into the active output texture
        private void RenderDepthFacesToShadowMap(Vector3 bakePosition, Quaternion bakeRotation, float farClip, float bias) {
            _shadowDepthEncodeMaterial.SetFloat(_farClipID, farClip);
            _shadowDepthEncodeMaterial.SetFloat(_nearClipID, _configuredNearClip);
            _shadowDepthEncodeMaterial.SetFloat(_biasID, bias);
            _shadowDepthEncodeMaterial.SetFloat(_tanHalfFovID, _bakeTanHalfFov);
            _shadowDepthEncodeMaterial.SetTexture(_depthTextureID, _depthTexture, RenderTextureSubElement.Depth);

            Quaternion previousCameraRotation = _cameraTransform.rotation;
            _cameraTransform.position = bakePosition;

            RenderTexture previousTargetTexture = ShadowCamera.targetTexture;
            ShadowCamera.targetTexture = _depthTexture;
            // Face rotation and EVSM encode are inlined here to avoid per-face Udon method calls
            for (int face = 0; face < 6; face++) {
                // Apply the cubemap-array face orientation expected by the shader sampling layout
                if (face == 0) _cameraTransform.rotation = bakeRotation * _faceRotation0;
                else if (face == 1) _cameraTransform.rotation = bakeRotation * _faceRotation1;
                else if (face == 2) _cameraTransform.rotation = bakeRotation * _faceRotation2;
                else if (face == 3) _cameraTransform.rotation = bakeRotation * _faceRotation3;
                else if (face == 4) _cameraTransform.rotation = bakeRotation * _faceRotation4;
                else _cameraTransform.rotation = bakeRotation;

                ShadowCamera.Render();
                BlitMaterialToSlice(_depthTexture, _shadowDepthEncodeMaterial, 0, _currentOutputTexture, _currentOutputBaseSlice + face);
            }
            ShadowCamera.targetTexture = previousTargetTexture;
            _cameraTransform.rotation = previousCameraRotation;
        }

        // Renders one spotlight depth view and encodes it into the active output texture
        private void RenderDepthSingleToShadowMap(Vector3 bakePosition, Quaternion bakeRotation, float farClip, float bias) {
            _shadowDepthEncodeMaterial.SetFloat(_farClipID, farClip);
            _shadowDepthEncodeMaterial.SetFloat(_nearClipID, _configuredNearClip);
            _shadowDepthEncodeMaterial.SetFloat(_biasID, bias);
            _shadowDepthEncodeMaterial.SetFloat(_tanHalfFovID, _bakeTanHalfFov);
            _shadowDepthEncodeMaterial.SetTexture(_depthTextureID, _depthTexture, RenderTextureSubElement.Depth);

            Quaternion previousCameraRotation = _cameraTransform.rotation;
            _cameraTransform.position = bakePosition;
            _cameraTransform.rotation = bakeRotation;

            RenderTexture previousTargetTexture = ShadowCamera.targetTexture;
            ShadowCamera.targetTexture = _depthTexture;
            ShadowCamera.Render();
            BlitMaterialToSlice(_depthTexture, _shadowDepthEncodeMaterial, 0, _currentOutputTexture, _currentOutputBaseSlice);
            ShadowCamera.targetTexture = previousTargetTexture;
            _cameraTransform.rotation = previousCameraRotation;
        }

        // Updates a small number of shadow slices for realtime mode
        private bool BakeRealtimeStep() {
            if (!PrepareBake()) return false;

            // Clamp work size for this tick and avoid processing more slices than the active cycle still needs
            int faceCount = _useCubemapShadow ? RealtimeFacesPerFrame : 1;
            if (faceCount <= 1) faceCount = 1;
            else if (faceCount >= _bakeSliceCount) faceCount = _bakeSliceCount;
            if (_bakeLoopRemainingFaces > 0 && faceCount > _bakeLoopRemainingFaces) faceCount = _bakeLoopRemainingFaces;

            if (!_cycleBakeSettingsValid) {
                _cycleBakePosition = _targetTransform.position;
                _cycleBakeRotation = _targetTransform.rotation;
                _cycleBakeFarClip = _bakeFarClip;
                _cycleBakeNearClip = _bakeNearClip;
                _cycleBakeBias = _bakeBias;
                _cycleBakeCullingMask = _bakeCullingMask;
                _cycleBakeSettingsValid = true;
            }

            Vector3 bakePosition = _cycleBakePosition;
            Quaternion bakeRotation = _cycleBakeRotation;
            float bakeFarClip = _cycleBakeFarClip;
            float bakeNearClip = _cycleBakeNearClip;
            float bakeBias = _cycleBakeBias;
            int bakeCullingMask = _cycleBakeCullingMask;

            if (!PrepareOutput(bakePosition, bakeFarClip, bakeBias, _useDirectOutput)) return false;
            // Direct mode writes into the manager array; fallback mode writes into the baker-local array first
            if (_useDirectOutput && _manager != null) {
                _currentOutputTexture = _manager.ShadowTextures;
                int shadowId = _target != null ? (int)_target.ShadowMapID : -1;
                if (shadowId < 0) _currentOutputBaseSlice = 0;
                else if (_useCubemapShadow) _currentOutputBaseSlice = shadowId * 6;
                else {
                    int cubemapCount = _manager.ShadowCubemapsCount;
                    _currentOutputBaseSlice = cubemapCount * 6 + shadowId - cubemapCount;
                }
            } else {
                _currentOutputTexture = _shadowTexture;
                _currentOutputBaseSlice = 0;
            }
            if (_completedOutputTexture != _currentOutputTexture || _completedOutputBaseSlice != _currentOutputBaseSlice) {
                _completedOutputTexture = _currentOutputTexture;
                _completedOutputBaseSlice = _currentOutputBaseSlice;
                _hasCompletedFullBake = false;
                _deferBlurUntilFullCycle = true;
            }
            ConfigureCamera(bakeFarClip, bakeNearClip, bakeCullingMask, _bakeFieldOfView);
            _shadowDepthEncodeMaterial.SetFloat(_farClipID, bakeFarClip);
            _shadowDepthEncodeMaterial.SetFloat(_nearClipID, _configuredNearClip);
            _shadowDepthEncodeMaterial.SetFloat(_biasID, bakeBias);
            _shadowDepthEncodeMaterial.SetFloat(_tanHalfFovID, _bakeTanHalfFov);
            _shadowDepthEncodeMaterial.SetTexture(_depthTextureID, _depthTexture, RenderTextureSubElement.Depth);
            if (_useBlur) PrepareShadowBlurMaterial();

            Quaternion previousCameraRotation = _cameraTransform.rotation;
            _cameraTransform.position = bakePosition;
            RenderTexture previousTargetTexture = ShadowCamera.targetTexture;
            ShadowCamera.targetTexture = _depthTexture;

            int firstFace = _realtimeFaceIndex;
            int face = firstFace;
            if (_useCubemapShadow) {
                // Render and encode the requested face batch without per-face helper calls
                for (int i = 0; i < faceCount; i++) {
                    // Keep the camera aligned to the face that will be written into the same array slice
                    if (face == 0) _cameraTransform.rotation = bakeRotation * _faceRotation0;
                    else if (face == 1) _cameraTransform.rotation = bakeRotation * _faceRotation1;
                    else if (face == 2) _cameraTransform.rotation = bakeRotation * _faceRotation2;
                    else if (face == 3) _cameraTransform.rotation = bakeRotation * _faceRotation3;
                    else if (face == 4) _cameraTransform.rotation = bakeRotation * _faceRotation4;
                    else _cameraTransform.rotation = bakeRotation;

                    ShadowCamera.Render();
                    BlitMaterialToSlice(_depthTexture, _shadowDepthEncodeMaterial, 0, _currentOutputTexture, _currentOutputBaseSlice + face);

                    face++;
                    if (face >= _bakeSliceCount) face = 0;
                }
            } else {
                _cameraTransform.rotation = bakeRotation;
                ShadowCamera.Render();
                BlitMaterialToSlice(_depthTexture, _shadowDepthEncodeMaterial, 0, _currentOutputTexture, _currentOutputBaseSlice);
                face = 0;
            }
            _realtimeFaceIndex = face;

            ShadowCamera.targetTexture = previousTargetTexture;
            _cameraTransform.rotation = previousCameraRotation;
            // Finish the current one-shot cycle after all required slices, respecting Faces Per Frame for cubemap mode
            if (_bakeLoopRemainingFaces > 0) {
                _bakeLoopRemainingFaces -= faceCount;
                if (_bakeLoopRemainingFaces < 0) _bakeLoopRemainingFaces = 0;
            }

            bool cycleComplete = _bakeLoopRemainingFaces <= 0;
            if (_useBlur && _deferBlurUntilFullCycle) {
                if (cycleComplete) {
                    BlurFaces(0, _bakeSliceCount, _useDirectOutput, !_useDirectOutput);
                    FinishBakeLoopCycle(bakePosition, bakeFarClip, bakeBias);
                }
            } else if (_useBlur) {
                BlurFaces(firstFace, faceCount, _useDirectOutput, !_useDirectOutput);
                if (cycleComplete) {
                    FinishBakeLoopCycle(bakePosition, bakeFarClip, bakeBias);
                }
            } else if (!_useDirectOutput && _manager != null && _target != null) {
                // Without blur, local output slices can be copied to the manager immediately after encoding
                int copyFirstFace = _deferBlurUntilFullCycle && cycleComplete ? 0 : firstFace;
                int copyFaceCount = _deferBlurUntilFullCycle && cycleComplete ? _bakeSliceCount : faceCount;
                face = copyFirstFace;
                for (int i = 0; i < copyFaceCount; i++) {
                    _manager.UpdatePointLightShadowTextureSlice(_target, face);
                    face++;
                    if (face >= _bakeSliceCount) face = 0;
                }
                if (cycleComplete) {
                    FinishBakeLoopCycle(bakePosition, bakeFarClip, bakeBias);
                }
            } else if (cycleComplete) {
                FinishBakeLoopCycle(bakePosition, bakeFarClip, bakeBias);
            }
            return true;
        }

        // Finalizes a completed distributed bake cycle and snapshots direct realtime output when the realtime loop stops
        private void FinishBakeLoopCycle(Vector3 bakePosition, float farClip, float bias) {
            if (_cycleUseDirectOutput && _useDirectOutput && !Realtime && _manager != null && _target != null && _manager.ShadowTextures != null && _target.ShadowMapID >= 0) {
                RenderTextureFormat format = _manager.ShadowTextureFormat == 0 ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGBFloat;
                _shadowTexture = EnsureOwnedArrayTexture(_shadowTexture, format, _bakeResolution, _bakeSliceCount, FilterMode.Bilinear);
                if (_shadowTexture != null) {
                    int shadowId = (int)_target.ShadowMapID;
                    int sourceBaseSlice;
                    if (_useCubemapShadow) sourceBaseSlice = shadowId * 6;
                    else {
                        int cubemapCount = _manager.ShadowCubemapsCount;
                        sourceBaseSlice = cubemapCount * 6 + shadowId - cubemapCount;
                    }
                    for (int i = 0; i < _bakeSliceCount; i++) VRCGraphics.Blit(_manager.ShadowTextures, _shadowTexture, sourceBaseSlice + i, i);
                    ApplyTargetShadowSourceInternal(bakePosition, farClip, bias, false);
                }
            }
            _hasCompletedFullBake = true;
            _deferBlurUntilFullCycle = false;
            if (!Realtime) _cycleUseDirectOutput = false;
            _cycleBakeSettingsValid = false;
        }

        // Applies horizontal blur to all requested slices first, then vertical blur, so seam-aware sampling sees a coherent cubemap
        private void BlurFaces(int firstFace, int faceCount, bool useDirectOutput, bool copyToManager) {
            if (_currentOutputTexture == null || _blurTempTexture == null || _shadowBlurMaterial == null) return;

            // Horizontal pass: read from current output and write coherent faces into the local blur scratch
            _shadowBlurMaterial.SetTexture(_sourceArrayID, _currentOutputTexture);
            _shadowBlurMaterial.SetFloat(_sourceBaseSliceID, _currentOutputBaseSlice);
            _shadowBlurMaterial.SetVector(_blurDirectionID, Vector2.right);
            // Dynamic blur radius samples the same source as depth data; uniform blur skips these uniforms in shader
            if (!_blurUsesUniformRadius) {
                _shadowBlurMaterial.SetTexture(_depthArrayID, _currentOutputTexture);
                _shadowBlurMaterial.SetFloat(_depthBaseSliceID, _currentOutputBaseSlice);
            }

            int face = firstFace;
            // Per-face blur blits stay inline to avoid Udon helper calls in the blur loop
            for (int i = 0; i < faceCount; i++) {
                _shadowBlurMaterial.SetInt(_faceIndexID, face);
                BlitMaterialToSlice(_currentOutputTexture, _shadowBlurMaterial, 0, _blurTempTexture, face);
                face++;
                if (face >= _bakeSliceCount) face = 0;
            }

            // Vertical pass: read from blur scratch and write back to the active output target
            _shadowBlurMaterial.SetTexture(_sourceArrayID, _blurTempTexture);
            _shadowBlurMaterial.SetFloat(_sourceBaseSliceID, 0);
            _shadowBlurMaterial.SetVector(_blurDirectionID, Vector2.up);
            if (!_blurUsesUniformRadius) {
                _shadowBlurMaterial.SetTexture(_depthArrayID, _blurTempTexture);
                _shadowBlurMaterial.SetFloat(_depthBaseSliceID, 0);
            }

            int targetBaseSlice = useDirectOutput ? _currentOutputBaseSlice : 0;
            face = firstFace;
            // Direct output writes final blur into the manager array; local output writes into the baker array
            for (int i = 0; i < faceCount; i++) {
                _shadowBlurMaterial.SetInt(_faceIndexID, face);
                BlitMaterialToSlice(_blurTempTexture, _shadowBlurMaterial, 0, _currentOutputTexture, targetBaseSlice + face);
                face++;
                if (face >= _bakeSliceCount) face = 0;
            }

            if (copyToManager && _manager != null && _target != null) {
                // Local blurred output still needs an explicit copy into the shared manager shadow array
                face = firstFace;
                for (int i = 0; i < faceCount; i++) {
                    _manager.UpdatePointLightShadowTextureSlice(_target, face);
                    face++;
                    if (face >= _bakeSliceCount) face = 0;
                }
            }
        }

        // Prepares blur material constants and keyword state
        private bool PrepareShadowBlurMaterial() {
            if (!_useBlur) return false;
            _blurUsesUniformRadius = _bakeBlurDepth <= 0f;

            int qualityPreset = ShadowBlurSamplePreset;
            if (qualityPreset <= 0) qualityPreset = 0;
            else if (qualityPreset >= 2) qualityPreset = 2;
            else qualityPreset = 1;
            int uniformKeyword = _blurUsesUniformRadius ? 1 : 0;
            int directKeyword = !_useCubemapShadow ? 1 : 0;
            if (_lastShadowQualityPreset != qualityPreset || _lastUniformBlurKeyword != uniformKeyword || _lastDirectBlurKeyword != directKeyword) {
                _shadowBlurMaterial.DisableKeyword(ShadowQualityKeywordLow);
                _shadowBlurMaterial.DisableKeyword(ShadowQualityKeywordMedium);
                _shadowBlurMaterial.DisableKeyword(ShadowQualityKeywordHigh);
                if (qualityPreset == 0) _shadowBlurMaterial.EnableKeyword(ShadowQualityKeywordLow);
                else if (qualityPreset == 2) _shadowBlurMaterial.EnableKeyword(ShadowQualityKeywordHigh);
                else _shadowBlurMaterial.EnableKeyword(ShadowQualityKeywordMedium);

                if (_blurUsesUniformRadius) _shadowBlurMaterial.EnableKeyword(ShadowBlurKeywordUniform);
                else _shadowBlurMaterial.DisableKeyword(ShadowBlurKeywordUniform);

                if (!_useCubemapShadow) _shadowBlurMaterial.EnableKeyword(ShadowBlurKeywordDirect);
                else _shadowBlurMaterial.DisableKeyword(ShadowBlurKeywordDirect);

                _lastShadowQualityPreset = qualityPreset;
                _lastUniformBlurKeyword = uniformKeyword;
                _lastDirectBlurKeyword = directKeyword;
            }
            _shadowBlurMaterial.SetFloat(_blurRadiusID, _bakeBlur * (Mathf.Max(_bakeResolution, 1) / ShadowBlurBaseResolution));
            // Depth <= 0 selects the cheaper uniform blur shader path; otherwise map inspector value logarithmically
            if (_blurUsesUniformRadius) _shadowBlurMaterial.SetFloat(_blurDepthID, 0f);
            else {
                float normalizedBlurDepth = Mathf.Clamp01(_bakeBlurDepth);
                _shadowBlurMaterial.SetFloat(_blurDepthID, (Mathf.Pow(10f, normalizedBlurDepth) - 1f) * 0.1111111111f);
            }
            _shadowBlurMaterial.SetFloat(_invResolutionID, 1f / _bakeResolution);
            return true;
        }

        // Schedules the next Udon bake loop tick
        private void ScheduleBakeLoop() {
            if ((!Realtime && _bakeLoopRemainingFaces <= 0) || !enabled || !gameObject.activeInHierarchy) return;
#if UDONSHARP
            if (_bakeLoopScheduled) return;
            _bakeLoopScheduled = true;
            SendCustomEventDelayedFrames(nameof(_RealtimeBakeLoop), 1);
#else
            if (_bakeLoopCoroutine != null || !isActiveAndEnabled) return;
            _bakeLoopCoroutine = StartCoroutine(BakeLoopCoroutine());
#endif
        }

        // Initializes all shader property IDs used by the runtime materials
        private void InitializeShaderProperties() {
            _depthTextureID = VRCShader.PropertyToID("_ShadowDepthTex");
            _farClipID = VRCShader.PropertyToID("_ShadowFarClip");
            _nearClipID = VRCShader.PropertyToID("_ShadowNearClip");
            _biasID = VRCShader.PropertyToID("_ShadowBakeBias");
            _tanHalfFovID = VRCShader.PropertyToID("_ShadowTanHalfFov");
            _sourceArrayID = VRCShader.PropertyToID("_SourceArrayTex");
            _depthArrayID = VRCShader.PropertyToID("_DepthArrayTex");
            _faceIndexID = VRCShader.PropertyToID("_FaceIndex");
            _sourceBaseSliceID = VRCShader.PropertyToID("_SourceBaseSlice");
            _depthBaseSliceID = VRCShader.PropertyToID("_DepthBaseSlice");
            _blurDirectionID = VRCShader.PropertyToID("_BlurDirection");
            _blurRadiusID = VRCShader.PropertyToID("_BlurRadius");
            _blurDepthID = VRCShader.PropertyToID("_BlurDepth");
            _invResolutionID = VRCShader.PropertyToID("_InvResolution");
            _shaderPropertiesInitialized = true;
        }

        // Caches editor-prepared per-baker material instances so concurrent runtime bakers never share mutable blit state
        private void InitializeRuntimeMaterials() {
            if (_runtimeMaterialsInitialized && _sourceRuntimeShadowDepthEncodeMaterial == RuntimeShadowDepthEncodeMaterial && _sourceRuntimeShadowBlurMaterial == RuntimeShadowBlurMaterial) return;

            ReleaseRuntimeMaterials();
            _sourceRuntimeShadowDepthEncodeMaterial = RuntimeShadowDepthEncodeMaterial;
            _sourceRuntimeShadowBlurMaterial = RuntimeShadowBlurMaterial;

            _shadowDepthEncodeMaterial = RuntimeShadowDepthEncodeMaterial;
            _shadowBlurMaterial = RuntimeShadowBlurMaterial;
            _lastShadowQualityPreset = -1;
            _lastUniformBlurKeyword = -1;
            _lastDirectBlurKeyword = -1;

            _runtimeMaterialsInitialized = true;
        }

        // Renders one material pass into a destination texture-array slice
        private void BlitMaterialToSlice(Texture sourceTexture, Material material, int pass, RenderTexture destination, int targetSlice) {
            if (material == null || destination == null) return;
#if COMPILER_UDONSHARP
            if (_materialBlitInputTexture == null) {
                _materialBlitInputTexture = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                _materialBlitInputTexture.dimension = TextureDimension.Tex2D;
                _materialBlitInputTexture.useMipMap = false;
                _materialBlitInputTexture.autoGenerateMips = false;
                _materialBlitInputTexture.Create();
            }
            Texture blitSource = _materialBlitInputTexture;
            VRCGraphics.Blit(blitSource, destination, 0, targetSlice);
            VRCGraphics.Blit(blitSource, material, pass, targetSlice);
#else
            RenderTexture previousRenderTexture = RenderTexture.active;
            VRCGraphics.SetRenderTarget(destination, 0, CubemapFace.Unknown, targetSlice);
            VRCGraphics.Blit(sourceTexture, material, pass);
            RenderTexture.active = previousRenderTexture;
#endif
        }

        // Releases the local blur scratch texture used by this baker
        private void ReleaseBlurTempTexture() {
            ReleaseRuntimeRenderTexture(_blurTempTexture);
            _blurTempTexture = null;
        }

        // Releases one temporary render texture before replacing it
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

        // Releases one material instance owned by this baker
        private void ReleaseRuntimeMaterial(Material material) {
            if (material == null) return;
#if !COMPILER_UDONSHARP
            if (Application.isPlaying) Destroy(material);
            else DestroyImmediate(material);
#endif
        }

        // Returns true when the target instance uses this baker's local shadow texture as its rebuild source
        private bool IsShadowTexturePublished() {
            PointLightVolumeInstance target = _target != null ? _target : TargetPointLightVolume;
            return target != null && _shadowTexture != null && target.ShadowMapTexture == _shadowTexture;
        }

        // Releases temporary bake buffers after a one-shot cycle finishes or realtime baking stops
        private void ReleaseIdleBakeTextures() {
            ReleaseRuntimeRenderTexture(_depthTexture);
            ReleaseBlurTempTexture();
            ReleaseRuntimeRenderTexture(_materialBlitInputTexture);
            _depthTexture = null;
            _materialBlitInputTexture = null;
            _currentOutputTexture = null;
        }

        // Releases per-baker material instances
        private void ReleaseRuntimeMaterials() {
            ReleaseRuntimeMaterial(_shadowDepthEncodeMaterial);
            ReleaseRuntimeMaterial(_shadowBlurMaterial);
            _shadowDepthEncodeMaterial = null;
            _shadowBlurMaterial = null;
            _sourceRuntimeShadowDepthEncodeMaterial = null;
            _sourceRuntimeShadowBlurMaterial = null;
            _runtimeMaterialsInitialized = false;
        }

        // Releases all locally-owned runtime textures
        private void ReleaseRuntimeTextures() {
            ReleaseRuntimeRenderTexture(_depthTexture);
            if (!IsShadowTexturePublished()) ReleaseRuntimeRenderTexture(_shadowTexture);
            ReleaseRuntimeRenderTexture(_registrationTexture);
            ReleaseBlurTempTexture();
            ReleaseRuntimeRenderTexture(_materialBlitInputTexture);
            _depthTexture = null;
            _shadowTexture = null;
            _registrationTexture = null;
            _materialBlitInputTexture = null;
            _currentOutputTexture = null;
            _completedOutputTexture = null;
            _completedOutputBaseSlice = -1;
            _hasCompletedFullBake = false;
            _deferBlurUntilFullCycle = false;
            _cycleUseDirectOutput = false;
            _cycleBakeSettingsValid = false;
        }

    }
}
