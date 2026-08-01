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
    [AddComponentMenu("VRC Light Volumes/Light Volume Manager (U# Script)")]
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
        private const int DefaultRegistryOrder = 2147483647;
        private const int MaxLightVolumeRotationVectors = MaxLightVolumeCount * 2;
        private const int MaxLightVolumeUvwScaleVectors = MaxLightVolumeCount * 3;
        private const int MaxLightVolumeLegacyUvwVectors = MaxLightVolumeCount * 6;
        private const RenderTextureFormat FixedCustomTexturesFormat = RenderTextureFormat.ARGBHalf;
        private const RenderTextureFormat ClusterMaskFormat = RenderTextureFormat.ARGBInt;
        private const float DisabledShadingShadowId = 10000f;
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
        [Tooltip("EVSM variance bias used by the shadow receiver shader. Authoring setup stores this as a 0..1 logarithmic slider.")]
        public float ShadowMinVariance = 1.0f;

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

        // Persistent authoring settings live on the Udon proxy as well. Keeping them here removes
        // the editor-only Setup component without adding runtime work; heavy asset references are
        // cleared from the temporary build scene by the build preprocessor.
        private const int BakingModeProgressive = 0;
        private const int BakingModeBakery = 1;
        private const int BakingModeCustomLightmapper = 2;
        [HideInInspector] public int BakingMode = BakingModeProgressive; // 0 = Progressive, 1 = Bakery, 2 = Custom Lightmapper
        [HideInInspector] public int VolumeBitmask = 1;
        [HideInInspector] public int ProbeBitmask = 1;
        [HideInInspector] public bool Denoise = true;
        [HideInInspector] public bool DilateInvalidProbes = true;
        [HideInInspector] public int DilationIterations = 1;
        [HideInInspector] public float DilationBackfaceBias = 0.1f;
        [HideInInspector] public bool FixLightProbesL1 = true;
        [HideInInspector] public int DownscaleVolumes = 0; // 0 = None, 1 = x2, 2 = x4, 3 = x8
        // The mobile slider is authoring-only. ShadowMinVariance remains the resolved raw runtime value.
        [HideInInspector] public float ShadowMinVarianceDesktop = 0f;
        [HideInInspector] public float ShadowMinVarianceMobile = 1f;
        // Serializable RT/material/name projection for editor atlas processors.
        // Delegate callbacks remain in transient editor state and must re-register after reload.
        [HideInInspector] public RenderTexture[] AtlasPostProcessorTargets = new RenderTexture[0];
        [HideInInspector] public Material[] AtlasPostProcessorMaterials = new Material[0];
        [HideInInspector] public string[] AtlasPostProcessorTextureNames = new string[0];

        [Header("Runtime Registries")]
        [Tooltip("All Light Volume instances sorted in decreasing order by weight. You can enable or disable volume GameObjects at runtime. Manually disabling unnecessary volumes improves performance.")]
        public LightVolumeInstance[] LightVolumeInstances = new LightVolumeInstance[0];
        [Tooltip("All Point Light Volume instances. You can enable or disable point light volume GameObjects at runtime. Manually disabling unnecessary point light volumes improves performance.")]
        public PointLightVolumeInstance[] PointLightVolumeInstances = new PointLightVolumeInstance[0];

#if UNITY_EDITOR && !COMPILER_UDONSHARP
        [Serializable]
        public struct PostProcessor {
            public RenderTexture RT;
            public Material Mat;
            public string TextureName;
            public Action Update;
            public Action<Texture> UpdateWithInput;
        }

        // Preserves the pre-migration integration surface without adding delegate fields to the Udon heap.
        public PostProcessor[] AtlasPostProcessors {
            get => GetAtlasPostProcessors();
            set => SetAtlasPostProcessors(value);
        }

        // Lets the editor assembly persist proxy changes without making this Udon assembly depend on editor tools.
        public static event Action<LightVolumeManager> AtlasPostProcessorsChanged;

        public bool IsBakeryMode => BakingMode == BakingModeBakery;
#endif

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
#if UNITY_EDITOR && !COMPILER_UDONSHARP
        // UdonSharp's play-mode proxy formatter reflects every C# instance field, including
        // non-serialized fields, while COMPILER_UDONSHARP excludes this state from the Udon heap.
        // Keep all editor-only caches outside the behaviour instance layout so proxy copies stay exact.
        private sealed class EditorState {
            public PointLightVolumeInstance[] CustomSourceOwners = Array.Empty<PointLightVolumeInstance>();
            public Texture[] CustomSourceTextures = Array.Empty<Texture>();
            public Material[] CustomSourceMaterials = Array.Empty<Material>();
            public int[] CustomSourceStates = Array.Empty<int>();
            public int CustomTextureWidth = -1;
            public int CustomTextureHeight = -1;
            public PointLightVolumeInstance[] ShadowSourceOwners = Array.Empty<PointLightVolumeInstance>();
            public Texture[] ShadowSourceTextures = Array.Empty<Texture>();
            public Material[] ShadowSourceMaterials = Array.Empty<Material>();
            public int[] ShadowSourceStates = Array.Empty<int>();
            public int ShadowTextureWidth = -1;
            public int ShadowTextureHeight = -1;
            public int ShadowTextureFormat = -1;
            public Material GeneratedClusteringMaterial;
            public Vector4 FroxelDepthParams;
            public PostProcessor[] AtlasPostProcessors;
            public RenderTexture[] PostProcessorProjectionTargets;
            public Material[] PostProcessorProjectionMaterials;
            public string[] PostProcessorProjectionTextureNames;

            public EditorState() { }
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<LightVolumeManager, EditorState> EditorStates
            = new System.Runtime.CompilerServices.ConditionalWeakTable<LightVolumeManager, EditorState>();

        private EditorState EditorData => EditorStates.GetOrCreateValue(this);

        // Preserve the existing call sites while keeping these names out of the reflected instance-field layout.
        private Material _generatedClusteringMaterial {
            get => EditorData.GeneratedClusteringMaterial;
            set => EditorData.GeneratedClusteringMaterial = value;
        }

        private Vector4 _editorFroxelDepthParams {
            get => EditorData.FroxelDepthParams;
            set => EditorData.FroxelDepthParams = value;
        }

        // Registers a Custom Render Texture post processor for the Light Volume 3D atlas.
        public void RegisterPostProcessorCRT(CustomRenderTexture texture) {
            if (texture == null) return;
            RegisterPostProcessor(new PostProcessor {
                RT = texture,
                Mat = texture.material,
                TextureName = "_MainTex",
                Update = texture.Update
            });
        }

        // Backwards-compatible alias retained from LightVolumeSetup.
        public void UnregisterPostProcessorCRT(CustomRenderTexture texture) {
            UnregisterPostProcessor(texture);
        }

        public void UnregisterPostProcessor(RenderTexture texture) {
            if (texture == null) return;
            UnregisterPostProcessor(new PostProcessor { RT = texture });
        }

        public void UnregisterPostProcessor(PostProcessor processor) {
            PostProcessor[] processors = GetAtlasPostProcessors();
            int removeCount = 0;
            RenderTexture removedTarget = processor.RT;
            for (int i = 0; i < processors.Length; i++) {
                if (!IsSamePostProcessor(processors[i], processor)) continue;
                if (removedTarget == null) removedTarget = processors[i].RT;
                removeCount++;
            }
            if (removeCount == 0) return;

            PostProcessor[] remaining = new PostProcessor[processors.Length - removeCount];
            for (int i = 0, write = 0; i < processors.Length; i++) {
                if (IsSamePostProcessor(processors[i], processor)) continue;
                remaining[write++] = processors[i];
            }
            SetAtlasPostProcessors(remaining);
            Debug.Log($"[LightVolumeManager] Unregistered post processor: {(removedTarget != null ? removedTarget.name : "")}");
            RefreshAtlasPostProcessors();
        }

        public void RegisterPostProcessor(PostProcessor processor) {
            if (processor.RT == null || processor.Mat == null && processor.Update == null && processor.UpdateWithInput == null) return;
            if (string.IsNullOrEmpty(processor.TextureName)) processor.TextureName = "_MainTex";

            PostProcessor[] processors = GetAtlasPostProcessors();
            int index = FindPostProcessor(processors, processor);
            if (index < 0) {
                Array.Resize(ref processors, processors.Length + 1);
                processors[processors.Length - 1] = processor;
                SetAtlasPostProcessors(processors);
                Debug.Log($"[LightVolumeManager] Registered post processor: {processor.RT.name}");
                RefreshAtlasPostProcessors();
                return;
            }

            bool changed = !HasSamePostProcessorValues(processors[index], processor);
            processors[index] = processor;
            int duplicateCount = 0;
            for (int i = 0; i < processors.Length; i++)
                if (i != index && IsSamePostProcessor(processors[i], processor)) duplicateCount++;
            if (!changed && duplicateCount == 0) return;

            if (duplicateCount > 0) {
                PostProcessor[] unique = new PostProcessor[processors.Length - duplicateCount];
                for (int i = 0, write = 0; i < processors.Length; i++) {
                    if (i != index && IsSamePostProcessor(processors[i], processor)) continue;
                    unique[write++] = processors[i];
                }
                processors = unique;
            }
            SetAtlasPostProcessors(processors);
            Debug.Log($"[LightVolumeManager] Updated post processor: {processor.RT.name}");
            RefreshAtlasPostProcessors();
        }

        // Re-runs the chain after the base atlas is rebuilt.
        public void RefreshAtlasPostProcessors() {
            Texture output = UpdatePostProcessorChain(GetAtlasPostProcessors(), LightVolumeAtlasBase);
            LightVolumeAtlas = output;
            AtlasPostProcessorsChanged?.Invoke(this);
            UpdateVolumes();
        }

        private PostProcessor[] GetAtlasPostProcessors() {
            EditorState state = EditorData;
            if (state.AtlasPostProcessors != null &&
                state.PostProcessorProjectionTargets == AtlasPostProcessorTargets &&
                state.PostProcessorProjectionMaterials == AtlasPostProcessorMaterials &&
                state.PostProcessorProjectionTextureNames == AtlasPostProcessorTextureNames)
                return state.AtlasPostProcessors;

            RenderTexture[] targets = AtlasPostProcessorTargets ?? Array.Empty<RenderTexture>();
            Material[] materials = AtlasPostProcessorMaterials;
            string[] textureNames = AtlasPostProcessorTextureNames;
            PostProcessor[] processors = new PostProcessor[targets.Length];
            for (int i = 0; i < processors.Length; i++) {
                RenderTexture target = targets[i];
                processors[i] = new PostProcessor {
                    RT = target,
                    Mat = materials != null && i < materials.Length ? materials[i] : null,
                    TextureName = textureNames != null && i < textureNames.Length && !string.IsNullOrEmpty(textureNames[i]) ? textureNames[i] : "_MainTex"
                };
            }
            state.AtlasPostProcessors = processors;
            CapturePostProcessorProjection(state);
            return processors;
        }

        private void SetAtlasPostProcessors(PostProcessor[] processors) {
            processors = processors ?? Array.Empty<PostProcessor>();
            RenderTexture[] targets = new RenderTexture[processors.Length];
            Material[] materials = new Material[processors.Length];
            string[] textureNames = new string[processors.Length];
            for (int i = 0; i < processors.Length; i++) {
                targets[i] = processors[i].RT;
                materials[i] = processors[i].Mat;
                textureNames[i] = string.IsNullOrEmpty(processors[i].TextureName) ? "_MainTex" : processors[i].TextureName;
            }

            AtlasPostProcessorTargets = targets;
            AtlasPostProcessorMaterials = materials;
            AtlasPostProcessorTextureNames = textureNames;
            EditorState state = EditorData;
            state.AtlasPostProcessors = processors;
            CapturePostProcessorProjection(state);
        }

        private void CapturePostProcessorProjection(EditorState state) {
            state.PostProcessorProjectionTargets = AtlasPostProcessorTargets;
            state.PostProcessorProjectionMaterials = AtlasPostProcessorMaterials;
            state.PostProcessorProjectionTextureNames = AtlasPostProcessorTextureNames;
        }

        private static int FindPostProcessor(PostProcessor[] processors, PostProcessor requested) {
            for (int i = 0; i < processors.Length; i++)
                if (IsSamePostProcessor(processors[i], requested)) return i;
            return -1;
        }

        private static bool IsSamePostProcessor(PostProcessor existing, PostProcessor requested) {
            return requested.RT != null && existing.RT == requested.RT ||
                   requested.Update != null && existing.Update == requested.Update ||
                   requested.UpdateWithInput != null && existing.UpdateWithInput == requested.UpdateWithInput;
        }

        private static bool HasSamePostProcessorValues(PostProcessor first, PostProcessor second) {
            return first.RT == second.RT &&
                   first.Mat == second.Mat &&
                   first.TextureName == second.TextureName &&
                   first.Update == second.Update &&
                   first.UpdateWithInput == second.UpdateWithInput;
        }

        private static Texture UpdatePostProcessorChain(PostProcessor[] processors, Texture baseTexture) {
            if (baseTexture == null || processors == null || processors.Length == 0) return baseTexture;

            Texture output = baseTexture;
            bool hasValidProcessor = false;
            for (int i = 0; i < processors.Length; i++) {
                PostProcessor processor = processors[i];
                if (processor.RT == null || processor.Mat == null && processor.Update == null && processor.UpdateWithInput == null) continue;

                SetupPostProcessorTexture(processor.RT, baseTexture);
                Texture input = output;
                if (processor.Mat != null)
                    processor.Mat.SetTexture(string.IsNullOrEmpty(processor.TextureName) ? "_MainTex" : processor.TextureName, input);
                output = processor.RT;
                hasValidProcessor = true;
                if (processor.UpdateWithInput != null) processor.UpdateWithInput(input);
                else processor.Update?.Invoke();
            }
            return hasValidProcessor ? output : baseTexture;
        }

        private static void SetupPostProcessorTexture(RenderTexture texture, Texture source) {
            RenderTexture.active = null;
            texture.Release();
            texture.dimension = TextureDimension.Tex3D;
            texture.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat;
            texture.enableRandomWrite = false;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = 0;
            texture.width = Mathf.Max(source.width, 1);
            texture.height = Mathf.Max(source.height, 1);
            texture.volumeDepth = Mathf.Max(GetTextureDepth(source), 1);
            if (texture is CustomRenderTexture customTexture)
                customTexture.updateMode = CustomRenderTextureUpdateMode.Realtime;
            texture.Create();
        }

        private static int GetTextureDepth(Texture texture) {
            if (texture is Texture3D texture3D) return texture3D.depth;
            if (texture is Texture2DArray textureArray) return textureArray.depth;
            if (texture is RenderTexture renderTexture) return renderTexture.volumeDepth;
            if (texture is Cubemap) return 6;
            return 1;
        }

#endif
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
        private bool _pointLightArraysDirty = false;
        private bool _updateLightVolumeBuffers = false;
        private bool _updatePointLightBuffers = false;
        private bool _updatePointLightPositionBuffer = false;
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
        private int[] _enabledIDs = new int[MaxLightVolumeCount];

        // Public API for other UdonSharp scripts
        public int EnabledCount => _enabledCount;
        public int[] EnabledIDs => _enabledIDs;

        private float _prevLightsBrightnessCutoff = 0.35f;
        private Vector4 _customRenderTextureInfo;

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

#if UNITY_EDITOR && !COMPILER_UDONSHARP
        // Editor-only getters for the custom inspector. They add no serialized fields,
        // asset references, or variables to either the Udon program or a player build.
        public RenderTexture FineClusterMaskPreview => _clusterMask;
        public RenderTexture CoarseClusterMaskPreview => _coarseClusterMask;
        public Material ClusteringMaterialPreview => GetClusteringMaterial();
        public bool RuntimeInitializedPreview => _isInitialized;
        public int ActivePointLightCountPreview => _pointLightCount;
        public int ActiveShadowCountPreview => _activeShadowCount;
        public bool ClusteringActivePreview => _clusteringActive;
        public bool ClusteringUnsupportedPreview => _clusteringUnsupported;
        public bool ClusteringAllocationFailedPreview => _clusteringAllocationFailed;
        public bool ClusterMaskValidPreview => _clusterMaskValid;
#endif

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

#region Shared Data Helpers

        // Precomputes the normalized EVSM receiver constants used by current shaders.
        private Vector4 GetPointLightShadowReceiverParams() {
            float varianceBias = Mathf.Max(ShadowMinVariance, 0f) * 0.01f;
            float bleedReduction = Mathf.Min(Mathf.Clamp01(ShadowBleedReduction), 0.999f);
            float bleedScale = 1f / (1f - bleedReduction);
            return new Vector4(varianceBias * 5.54f, -bleedReduction * bleedScale, bleedScale, varianceBias * 5f);
        }

        // Octahedrally packs a shape axis and 8-bit shape code into one exactly representable 24-bit float integer.
        private float EncodeClusterShape(Vector3 axis, int shapeCode) {
            float axisLengthSq = axis.sqrMagnitude;
            if (axisLengthSq < 0.000001f) axis = Vector3.forward;

            // Oct projection is scale invariant; L1-normalize directly and avoid an Udon sqrt.
            float inverseL1Length = 1f / (Mathf.Abs(axis.x) + Mathf.Abs(axis.y) + Mathf.Abs(axis.z));
            float octX = axis.x * inverseL1Length;
            float octY = axis.y * inverseL1Length;
            float octZ = axis.z * inverseL1Length;
            if (octZ < 0f) {
                float unfoldedX = octX;
                octX = (1f - Mathf.Abs(octY)) * (unfoldedX >= 0f ? 1f : -1f);
                octY = (1f - Mathf.Abs(unfoldedX)) * (octY >= 0f ? 1f : -1f);
            }

            int encodedX = Mathf.Clamp(Mathf.RoundToInt((octX * 0.5f + 0.5f) * ClusterAxisScale), 0, ClusterAxisScale);
            int encodedY = Mathf.Clamp(Mathf.RoundToInt((octY * 0.5f + 0.5f) * ClusterAxisScale), 0, ClusterAxisScale);
            return encodedX + encodedY * ClusterAxisStride + shapeCode * ClusterShapeStride;
        }

        // Packs two lights per vector as radius + shape. Shape 0 is point, 1 is one-sided area and 2..255 is a conservative spot cone.
        private void WriteClusteringLight(int shaderIndex, float squaredRange, int lightType, float outerTangent, Vector3 shapeAxis) {
            int shapeCode = 0;
            if (lightType == 1 && outerTangent > 0f) { // 1: spot; wider-than-hemisphere cones fall back to their range sphere
                // tan(angle + padding) avoids two Udon transcendental calls while covering the packed-axis error.
                float paddingDenominator = 1f - outerTangent * ClusterAxisPad;
                if (paddingDenominator > 0f) {
                    float expandedTangent = (outerTangent + ClusterAxisPad) / paddingDenominator;
                    if (expandedTangent <= ClusterMaxTangent) {
                        int tangentLevel = Mathf.Clamp(Mathf.CeilToInt(expandedTangent / (1f + expandedTangent) * 255f), 1, 254);
                        shapeCode = tangentLevel + 1;
                    }
                }
            } else if (lightType == 2) { // 2: one-sided area
                shapeCode = 1;
            }

            float packedShape = shapeCode == 0 ? 0f : EncodeClusterShape(shapeAxis, shapeCode);
            float range = Mathf.Sqrt(Mathf.Max(squaredRange, 0f));
            int packedIndex = shaderIndex >> 1;
            Vector4 packedData = _clusteringLights[packedIndex];
            if ((shaderIndex & 1) == 0) {
                if (packedData.x == range && packedData.y == packedShape) return;
                packedData.x = range;
                packedData.y = packedShape;
            } else {
                if (packedData.z == range && packedData.w == packedShape) return;
                packedData.z = range;
                packedData.w = packedShape;
            }
            _clusteringLights[packedIndex] = packedData;
            _clusteringLightsDirty = true;
            _clusterGeometryUploadPending = true;
        }

        // Resolves the Area Cookie X/Y reflection relative to the quaternion frame sent to shaders.
        private float GetAreaCookieMirror(Matrix4x4 localToWorldMatrix, Quaternion transformRotation) {
            Vector3 matrixXAxis = new Vector3(localToWorldMatrix.m00, localToWorldMatrix.m10, localToWorldMatrix.m20);
            Vector3 matrixYAxis = new Vector3(localToWorldMatrix.m01, localToWorldMatrix.m11, localToWorldMatrix.m21);
            bool flipCookieX = Vector3.Dot(matrixXAxis, transformRotation * Vector3.right) < 0f;
            bool flipCookieY = Vector3.Dot(matrixYAxis, transformRotation * Vector3.up) < 0f;
            return (flipCookieY ? 2f : 1f) * (flipCookieX ? -1f : 1f);
        }

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

        // Makes the manager's canonical range math available to runtime shadow bakers before they encode depth.
        public void RecalculatePointLightRange(PointLightVolumeInstance instance) {
            ComputePointLightRange(instance);
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

        // Updates one point light instance from its current transform data
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
                // A reflected matrix has no quaternion representation. Keep the physical light rotation and
                // carry Area Cookie-only X/Y reflection in its custom projection descriptor.
                Quaternion transformRotation = instance.transform.rotation;
                if (lightType == 2) { // 2: area
                    instance.Rotation = transformRotation;
                    instance.Width = Mathf.Max(scaleX, 0.001f);
                    instance.Height = Mathf.Max(scaleY, 0.001f);
                    instance.AreaCookieMirror = GetAreaCookieMirror(localToWorldMatrix, transformRotation);
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

        // Selects the highest-priority active volumes without changing their authoring registry.
        private int SelectLightVolumesByWeight() {
            int selectedCount = 0;
            int registryCount = LightVolumeInstances.Length;
            for (int registryIndex = 0; registryIndex < registryCount; registryIndex++) {
                LightVolumeInstance candidate = LightVolumeInstances[registryIndex];
                if (candidate == null || !candidate.IsActive) continue;

                int insertIndex = selectedCount;
                for (int selectedIndex = 0; selectedIndex < selectedCount; selectedIndex++) {
                    LightVolumeInstance selected = LightVolumeInstances[_selectedLightVolumeIDs[selectedIndex]];
                    bool higherWeight = candidate.RegistryWeight > selected.RegistryWeight;
                    bool earlierEqualWeight = candidate.RegistryWeight == selected.RegistryWeight &&
                                              candidate.RegistryOrder < selected.RegistryOrder;
                    if (!higherWeight && !earlierEqualWeight) continue;
                    insertIndex = selectedIndex;
                    break;
                }
                if (insertIndex >= MaxLightVolumeCount) continue;

                int shiftStart = selectedCount < MaxLightVolumeCount ? selectedCount : MaxLightVolumeCount - 1;
                for (int selectedIndex = shiftStart; selectedIndex > insertIndex; selectedIndex--)
                    _selectedLightVolumeIDs[selectedIndex] = _selectedLightVolumeIDs[selectedIndex - 1];
                _selectedLightVolumeIDs[insertIndex] = registryIndex;
                if (selectedCount < MaxLightVolumeCount) selectedCount++;
            }
            return selectedCount;
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

#if !COMPILER_UDONSHARP && UDONSHARP && UNITY_EDITOR
        // Returns true when the editor C# proxy must not write runtime shader data while backed Udon drives Play Mode.
        private bool ShouldSkipEditorProxyRuntimeUpdate() {
            return Application.isPlaying && GetComponent("VRC.Udon.UdonBehaviour") != null;
        }
#endif

        // Used by LightVolumeInstance runtime methods
        public void NotifyLightVolumeChanged(LightVolumeInstance lightVolume, bool rebuildFinalData) {
#if !COMPILER_UDONSHARP && UDONSHARP && UNITY_EDITOR
            if (ShouldSkipEditorProxyRuntimeUpdate()) return;
#endif
            // Checking, initializing...
            if (lightVolume == null) return;
            if (LightVolumeInstances == null) LightVolumeInstances = new LightVolumeInstance[0];
            int registryIndex = FindLightVolumeRegistryIndex(lightVolume);
            if (registryIndex < 0) {
                if (!lightVolume.IsActive) return;
                InitializeLightVolume(lightVolume);
                registryIndex = FindLightVolumeRegistryIndex(lightVolume);
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
#if !COMPILER_UDONSHARP && UDONSHARP && UNITY_EDITOR
            if (ShouldSkipEditorProxyRuntimeUpdate()) return;
#endif
            // Checking, initializing...
            if (pointLightVolume == null) return;
            if (PointLightVolumeInstances == null) PointLightVolumeInstances = new PointLightVolumeInstance[0];
            int registryIndex = FindPointLightRegistryIndex(pointLightVolume);
            if (registryIndex < 0) {
                if (!pointLightVolume.IsActive) return;
                InitializePointLightVolume(pointLightVolume);
                registryIndex = FindPointLightRegistryIndex(pointLightVolume);
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

        // Sets the Force Scene Lighting shader override explicitly for manual runtime control.
        public void SetForceSceneLighting(bool enabled) {
#if !COMPILER_UDONSHARP && UDONSHARP && UNITY_EDITOR
            if (ShouldSkipEditorProxyRuntimeUpdate()) return;
#endif
            ForceSceneLighting = enabled;
            TryInitialize();
            // Lore: https://x.com/lil_xyzw/status/1961487430256922928?s=20
            VRCShader.SetGlobalInteger(_forceSceneLightingID, enabled ? 1 : 0);
        }

#if UDONSHARP
        // External runtime writes can enable texture auto-updates after the delayed process stopped.
        public void _onVarChange_AutoUpdateTextures() {
            if (AutoUpdateTextures) ScheduleUpdateProcess();
        }
#endif

#if UDONSHARP || UNITY_EDITOR
        // Applies Inspector-authored scalar settings without rebuilding volume registries or texture caches.
        public void _ApplyEditorSettings() {
            TryInitialize();
            VRCShader.SetGlobalFloat(_lightVolumeProbesBlendID, LightProbesBlending ? 1f : 0f);
            VRCShader.SetGlobalFloat(_lightVolumeSharpBoundsID, SharpBounds ? 1f : 0f);
            VRCShader.SetGlobalFloat(_lightVolumeAdditiveMaxOverdrawID, AdditiveMaxOverdraw);
            VRCShader.SetGlobalFloat(_lightBrightnessCutoffID, LightsBrightnessCutoff);
            VRCShader.SetGlobalVector(_pointLightShadowReceiverParamsID, GetPointLightShadowReceiverParams());
            if (AutoUpdateTextures) ScheduleUpdateProcess();
        }
#endif

        // Enables or disables camera-relative froxel clustering at runtime.
        public void SetClustering(bool enabled) {
#if !COMPILER_UDONSHARP && UDONSHARP && UNITY_EDITOR
            if (ShouldSkipEditorProxyRuntimeUpdate()) return;
#endif
            if (Clustering == enabled) {
                // An explicit retry may recover from a layout-specific allocation failure.
                if (enabled && (_clusteringUnsupported || _clusteringAllocationFailed)) {
                    _clusteringUnsupported = false;
                    _clusteringAllocationFailed = false;
                    _froxelLayoutValid = false;
                }
                return;
            }
            Clustering = enabled;
            _clusteringUnsupported = false;
            _clusteringAllocationFailed = false;
            _froxelLayoutValid = false;
            TryInitialize();
            DisableClustering();
        }

#endregion

#region Initialization

        // Initializes shader property IDs and global shader arrays when needed
        private void TryInitialize() {
            if (_isInitialized) return;
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
            _pointLightShadowReceiverParamsID = VRCShader.PropertyToID("_UdonPointLightVolumeShadowReceiverParams");
            _clusteringLightsID = VRCShader.PropertyToID("_UdonClusteringLights");
            _lightBrightnessCutoffID = VRCShader.PropertyToID("_UdonLightBrightnessCutoff");
            // Froxel Clustering
            _clusteringEnabledID = VRCShader.PropertyToID("_UdonClusteringEnabled");
            _clusterMaskID = VRCShader.PropertyToID("_UdonClusterMask");
            _froxelGridID = VRCShader.PropertyToID("_UdonFroxelGrid");
            _froxelDepthID = VRCShader.PropertyToID("_UdonFroxelDepth");
            _froxelDepthStepID = VRCShader.PropertyToID("_UdonFroxelDepthStep");
            _coarseClusterMaskID = VRCShader.PropertyToID("_UdonCoarseClusterMask");
            _froxelCoarseGridID = VRCShader.PropertyToID("_UdonFroxelCoarseGrid");
            _froxelFineGridID = VRCShader.PropertyToID("_UdonFroxelFineGrid");
            _froxelPassID = VRCShader.PropertyToID("_UdonFroxelPass");
            _froxelCoarseID = VRCShader.PropertyToID("_UdonFroxelCoarse");
            _froxelProjectionID = VRCShader.PropertyToID("_UdonFroxelProjection");
            _froxelRightID = VRCShader.PropertyToID("_UdonFroxelRight");
            _froxelUpID = VRCShader.PropertyToID("_UdonFroxelUp");
            _froxelForwardID = VRCShader.PropertyToID("_UdonFroxelForward");
            // Other
            _forceSceneLightingID = VRCShader.PropertyToID("_UdonForceSceneLighting");
            _cubemapMainTexID = VRCShader.PropertyToID("_MainTex");
            _cubemapSourceTexID = VRCShader.PropertyToID("_CubeTex");
            _cubemapFaceIndexID = VRCShader.PropertyToID("_FaceIndex");

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
            VRCShader.SetGlobalVector(_pointLightShadowReceiverParamsID, GetPointLightShadowReceiverParams());
            _clusteringLightsDirty = true;
            VRCShader.SetGlobalFloat(_clusteringEnabledID, 0f);
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
            VRCShader.SetGlobalVector(_pointLightShadowReceiverParamsID, GetPointLightShadowReceiverParams());
            VRCShader.SetGlobalFloat(_clusteringEnabledID, 0f);
            _clusteringActive = false;
            VRCShader.SetGlobalFloat(_lightVolumeEnabledID, 0);
        }

#endregion

#region Lifecycle

        // Clears runtime state and schedules the first shader data upload
        private void Start() {
            _isInitialized = false;
            if (ForceSceneLighting) SetForceSceneLighting(true);
            RequestUpdateVolumes();
        }

        // Requests a fresh volume update after this manager becomes active
        private void OnEnable() {
            _isInitialized = false;
            _clusteringUnsupported = false;
            _clusteringAllocationFailed = false;
            _froxelLayoutValid = false;
            _froxelDepthValid = false;
            _froxelProjectionValid = false;
            _clusterMaskDirty = true;
            _clusterMaskValid = false;
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
            DisableClustering();
            SetDisabledShaderState();
        }

#if UDONSHARP
        // Updates cached dynamic transforms and camera-relative clustering after runtime motion has settled.
        public override void PostLateUpdate() {
            UpdateDynamicVolumeTransforms();
            UpdateClustering();
        }
#else
        // Updates cached dynamic transforms and camera-relative clustering after standalone motion has settled.
        private void LateUpdate() {
            if (!Application.isPlaying) return;
            UpdateDynamicVolumeTransforms();
            Camera camera = Camera.main;
            if (camera == null) camera = Camera.current;
            UpdateClusteringFromCamera(camera);
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
            if (_clusterMask != null) {
                ReleaseRuntimeRenderTexture(_clusterMask);
                _clusterMask = null;
            }
            if (_coarseClusterMask != null) {
                ReleaseRuntimeRenderTexture(_coarseClusterMask);
                _coarseClusterMask = null;
            }
            if (_clusteringSource != null) {
                ReleaseRuntimeRenderTexture(_clusteringSource);
                _clusteringSource = null;
            }
#if UDONSHARP
            if (_dummyRT != null) {
                ReleaseRuntimeRenderTexture(_dummyRT);
                _dummyRT = null;
            }
#endif
            DestroyCubemapFaceRuntimeMaterial();
            DestroyClusteringMaterial();
        }
#endif

#endregion

#region Runtime Registries

        // Removes stale serialized registry slots left by older manager versions. New runtime
        // deinitialization keeps both registries dense, so this normally exits without allocating.
        public bool SanitizeRegistries() {
            bool changed = false;

            if (LightVolumeInstances == null) {
                LightVolumeInstances = new LightVolumeInstance[0];
                changed = true;
            } else {
                int count = LightVolumeInstances.Length;
                int validCount = 0;
                for (int i = 0; i < count; i++) {
                    if (LightVolumeInstances[i] != null) validCount++;
                }
                if (validCount != count) {
                    LightVolumeInstance[] targetArray = new LightVolumeInstance[validCount];
                    int targetIndex = 0;
                    for (int i = 0; i < count; i++) {
                        LightVolumeInstance instance = LightVolumeInstances[i];
                        if (instance == null) continue;
                        targetArray[targetIndex++] = instance;
                    }
                    LightVolumeInstances = targetArray;
                    changed = true;
                }
            }

            bool pointLightRegistryChanged = false;
            if (PointLightVolumeInstances == null) {
                PointLightVolumeInstances = new PointLightVolumeInstance[0];
                pointLightRegistryChanged = true;
            } else {
                int count = PointLightVolumeInstances.Length;
                int validCount = 0;
                for (int i = 0; i < count; i++) {
                    if (PointLightVolumeInstances[i] != null) validCount++;
                }
                if (validCount != count) {
                    PointLightVolumeInstance[] targetArray = new PointLightVolumeInstance[validCount];
                    int targetIndex = 0;
                    for (int i = 0; i < count; i++) {
                        PointLightVolumeInstance instance = PointLightVolumeInstances[i];
                        if (instance == null) continue;
                        targetArray[targetIndex++] = instance;
                    }
                    PointLightVolumeInstances = targetArray;
                    pointLightRegistryChanged = true;
                }
            }

            if (pointLightRegistryChanged) {
                changed = true;
                if (_customTextureArrayDepth > 0) _customTexturesInitialized = false;
                if (_shadowTextureArrayDepth > 0) _shadowTexturesInitialized = false;
            }
            return changed;
        }

        // Uses the stable authoring order as an O(1) hint and falls back after runtime compaction.
        private int FindLightVolumeRegistryIndex(LightVolumeInstance lightVolume) {
            if (lightVolume == null || LightVolumeInstances == null) return -1;
            int count = LightVolumeInstances.Length;
            int hint = lightVolume.RegistryOrder;
            if (hint >= 0 && hint < count && LightVolumeInstances[hint] == lightVolume) return hint;
            return Array.IndexOf((Array)LightVolumeInstances, lightVolume, 0, count);
        }

        // Uses the stable authoring order as an O(1) hint and falls back after runtime reordering.
        private int FindPointLightRegistryIndex(PointLightVolumeInstance pointLightVolume) {
            if (pointLightVolume == null || PointLightVolumeInstances == null) return -1;
            int count = PointLightVolumeInstances.Length;
            int hint = pointLightVolume.RegistryOrder;
            if (hint >= 0 && hint < count && PointLightVolumeInstances[hint] == pointLightVolume) return hint;
            return Array.IndexOf((Array)PointLightVolumeInstances, pointLightVolume, 0, count);
        }

        // Initializes a Light Volume by adding it to the light volume registry. Called automatically at runtime when the object spawns
        public void InitializeLightVolume(LightVolumeInstance lightVolume) {
            if (lightVolume == null) return;
            if (LightVolumeInstances == null) LightVolumeInstances = new LightVolumeInstance[0];
            int count = LightVolumeInstances.Length;
            int existingIndex = FindLightVolumeRegistryIndex(lightVolume);
            if (existingIndex >= 0 && lightVolume.RegistryOrder != DefaultRegistryOrder) {
                lightVolume.LightVolumeManager = this;
                RequestUpdateVolumes();
                return;
            }
            existingIndex = -1;
            int nextRegistryOrder = -1;
            for (int i = 0; i < count; i++) {
                LightVolumeInstance existingLightVolume = LightVolumeInstances[i];
                if (existingLightVolume == null) continue;
                if (existingLightVolume.RegistryOrder == DefaultRegistryOrder) existingLightVolume.RegistryOrder = i;
                if (existingLightVolume.RegistryOrder > nextRegistryOrder) nextRegistryOrder = existingLightVolume.RegistryOrder;
                if (existingLightVolume == lightVolume) existingIndex = i;
            }
            if (lightVolume.RegistryOrder == DefaultRegistryOrder) lightVolume.RegistryOrder = nextRegistryOrder + 1;

            // Reuse an existing slot so repeated OnEnable calls do not duplicate the same volume
            if (existingIndex >= 0) {
                lightVolume.LightVolumeManager = this;
                RequestUpdateVolumes();
                return;
            }
            // Keep the runtime registry in stable authoring order; shader priority is resolved separately.
            int targetOrder = lightVolume.RegistryOrder;
            int firstEmptyIndex = -1;
            int lastFilledIndex = -1;
            int insertIndex = count;
            for (int i = 0; i < count; i++) {
                LightVolumeInstance existingLightVolume = LightVolumeInstances[i];
                if (existingLightVolume == null) {
                    if (firstEmptyIndex < 0) firstEmptyIndex = i;
                    continue;
                }
                lastFilledIndex = i;
                if (insertIndex == count && existingLightVolume.RegistryOrder > targetOrder) insertIndex = i;
            }
            if (firstEmptyIndex >= 0) {
                if (insertIndex == count) {
                    if (firstEmptyIndex < lastFilledIndex) {
                        for (int i = firstEmptyIndex; i < lastFilledIndex; i++) LightVolumeInstances[i] = LightVolumeInstances[i + 1];
                        LightVolumeInstances[lastFilledIndex] = lightVolume;
                    } else {
                        LightVolumeInstances[firstEmptyIndex] = lightVolume;
                    }
                } else if (firstEmptyIndex < insertIndex) {
                    for (int i = firstEmptyIndex; i < insertIndex - 1; i++) LightVolumeInstances[i] = LightVolumeInstances[i + 1];
                    LightVolumeInstances[insertIndex - 1] = lightVolume;
                } else {
                    for (int i = firstEmptyIndex; i > insertIndex; i--) LightVolumeInstances[i] = LightVolumeInstances[i - 1];
                    LightVolumeInstances[insertIndex] = lightVolume;
                }
                lightVolume.LightVolumeManager = this;
                RequestUpdateVolumes();
                return;
            }
            // No empty slot exists, so grow the registry array and insert by stable authoring order.
            LightVolumeInstance[] targetArray = new LightVolumeInstance[count + 1];
            for (int i = 0; i < insertIndex; i++) targetArray[i] = LightVolumeInstances[i];
            targetArray[insertIndex] = lightVolume;
            for (int i = insertIndex; i < count; i++) targetArray[i + 1] = LightVolumeInstances[i];
            lightVolume.LightVolumeManager = this;
            LightVolumeInstances = targetArray;
            RequestUpdateVolumes();
        }

        // Deinitializes a Light Volume and keeps the serialized registry dense.
        public void DeinitializeLightVolume(LightVolumeInstance lightVolume) {
            if (lightVolume == null || LightVolumeInstances == null) return;
            int index = FindLightVolumeRegistryIndex(lightVolume);
            if (index < 0) return;
            int count = LightVolumeInstances.Length;
            LightVolumeInstance[] targetArray = new LightVolumeInstance[count - 1];
            for (int i = 0; i < index; i++) targetArray[i] = LightVolumeInstances[i];
            for (int i = index + 1; i < count; i++) targetArray[i - 1] = LightVolumeInstances[i];
            LightVolumeInstances = targetArray;
            if (enabled && gameObject.activeInHierarchy) RequestUpdateVolumes();
        }

        // Refreshes shader selection after a runtime weight change without reordering the registry.
        public void ReorderLightVolume(LightVolumeInstance lightVolume) {
            if (lightVolume == null) return;
            int index = FindLightVolumeRegistryIndex(lightVolume);
            if (index < 0) {
                if (lightVolume.IsActive) InitializeLightVolume(lightVolume);
                return;
            }
            RequestUpdateVolumes();
        }

        // Initializes a Point Light Volume by adding it to the point light volume registry
        public void InitializePointLightVolume(PointLightVolumeInstance pointLightVolume) {
            if (pointLightVolume == null) return;
#if !COMPILER_UDONSHARP
            if (RuntimeShadowCamera == null && pointLightVolume.BakeInGame) EnsureRuntimeShadowCamera();
#endif
            if (pointLightVolume.RuntimeShadowCamera == null) pointLightVolume.RuntimeShadowCamera = RuntimeShadowCamera;
            if (pointLightVolume.RuntimeShadowDepthEncodeMaterial == null && RuntimeShadowDepthEncodeMaterial != null) pointLightVolume.RuntimeShadowDepthEncodeMaterial = RuntimeShadowDepthEncodeMaterial;
            if (pointLightVolume.RuntimeShadowBlurMaterial == null && RuntimeShadowBlurMaterial != null) pointLightVolume.RuntimeShadowBlurMaterial = RuntimeShadowBlurMaterial;
            if (PointLightVolumeInstances == null) PointLightVolumeInstances = new PointLightVolumeInstance[0];
            int count = PointLightVolumeInstances.Length;
            bool invalidateCustomTextures = _customTexturesInitialized && pointLightVolume.IsActive && (pointLightVolume.CustomTexture != null || pointLightVolume.CustomTextureMaterial != null);
            bool invalidateShadowTextures = _shadowTexturesInitialized && pointLightVolume.IsActive && (pointLightVolume.ShadowMapTexture != null || pointLightVolume.ShadowMapMaterial != null || pointLightVolume.ShadowMapID >= 0);
            int existingIndex = FindPointLightRegistryIndex(pointLightVolume);
            if (existingIndex >= 0 && pointLightVolume.RegistryOrder != DefaultRegistryOrder) {
                pointLightVolume.LightVolumeManager = this;
                if (invalidateCustomTextures) _customTexturesInitialized = false;
                if (invalidateShadowTextures) _shadowTexturesInitialized = false;
                RequestUpdateVolumes();
                return;
            }
            existingIndex = -1;
            int nextRegistryOrder = -1;
            for (int i = 0; i < count; i++) {
                PointLightVolumeInstance existingPointLightVolume = PointLightVolumeInstances[i];
                if (existingPointLightVolume == null) continue;
                if (existingPointLightVolume.RegistryOrder == DefaultRegistryOrder) existingPointLightVolume.RegistryOrder = i;
                if (existingPointLightVolume.RegistryOrder > nextRegistryOrder) nextRegistryOrder = existingPointLightVolume.RegistryOrder;
                if (existingPointLightVolume == pointLightVolume) existingIndex = i;
            }
            if (pointLightVolume.RegistryOrder == DefaultRegistryOrder) pointLightVolume.RegistryOrder = nextRegistryOrder + 1;

            // Reuse an existing slot so repeated OnEnable calls do not duplicate the same point light
            if (existingIndex >= 0) {
                pointLightVolume.LightVolumeManager = this;
                if (invalidateCustomTextures) _customTexturesInitialized = false;
                if (invalidateShadowTextures) _shadowTexturesInitialized = false;
                RequestUpdateVolumes();
                return;
            }
            // Insert by weight first and stable registry order second so enable/disable history does not change shader priority
            float targetWeight = pointLightVolume.RegistryWeight;
            int targetOrder = pointLightVolume.RegistryOrder;
            int firstEmptyIndex = -1;
            int lastFilledIndex = -1;
            int insertIndex = count;
            bool registryIndicesChanged = false;
            for (int i = 0; i < count; i++) {
                PointLightVolumeInstance existingPointLightVolume = PointLightVolumeInstances[i];
                if (existingPointLightVolume == null) {
                    if (firstEmptyIndex < 0) firstEmptyIndex = i;
                    continue;
                }
                lastFilledIndex = i;
                if (insertIndex == count && (existingPointLightVolume.RegistryWeight < targetWeight || existingPointLightVolume.RegistryWeight == targetWeight && existingPointLightVolume.RegistryOrder > targetOrder)) insertIndex = i;
            }
            if (firstEmptyIndex >= 0) {
                if (insertIndex == count) {
                    if (firstEmptyIndex < lastFilledIndex) {
                        registryIndicesChanged = true;
                        for (int i = firstEmptyIndex; i < lastFilledIndex; i++) PointLightVolumeInstances[i] = PointLightVolumeInstances[i + 1];
                        PointLightVolumeInstances[lastFilledIndex] = pointLightVolume;
                    } else {
                        PointLightVolumeInstances[firstEmptyIndex] = pointLightVolume;
                    }
                } else if (firstEmptyIndex < insertIndex) {
                    if (firstEmptyIndex < insertIndex - 1) registryIndicesChanged = true;
                    for (int i = firstEmptyIndex; i < insertIndex - 1; i++) PointLightVolumeInstances[i] = PointLightVolumeInstances[i + 1];
                    PointLightVolumeInstances[insertIndex - 1] = pointLightVolume;
                } else {
                    registryIndicesChanged = true;
                    for (int i = firstEmptyIndex; i > insertIndex; i--) PointLightVolumeInstances[i] = PointLightVolumeInstances[i - 1];
                    PointLightVolumeInstances[insertIndex] = pointLightVolume;
                }
                pointLightVolume.LightVolumeManager = this;
                if (registryIndicesChanged) {
                    if (_customTextureArrayDepth > 0) invalidateCustomTextures = true;
                    if (_shadowTextureArrayDepth > 0) invalidateShadowTextures = true;
                }
                if (invalidateCustomTextures) _customTexturesInitialized = false;
                if (invalidateShadowTextures) _shadowTexturesInitialized = false;
                RequestUpdateVolumes();
                return;
            }
            // No empty slot exists, so grow the registry array and insert by weight and stable order
            PointLightVolumeInstance[] targetArray = new PointLightVolumeInstance[count + 1];
            for (int i = 0; i < insertIndex; i++) targetArray[i] = PointLightVolumeInstances[i];
            targetArray[insertIndex] = pointLightVolume;
            for (int i = insertIndex; i < count; i++) targetArray[i + 1] = PointLightVolumeInstances[i];
            pointLightVolume.LightVolumeManager = this;
            PointLightVolumeInstances = targetArray;
            if (insertIndex < count) {
                if (_customTextureArrayDepth > 0) invalidateCustomTextures = true;
                if (_shadowTextureArrayDepth > 0) invalidateShadowTextures = true;
            }
            if (invalidateCustomTextures) _customTexturesInitialized = false;
            if (invalidateShadowTextures) _shadowTexturesInitialized = false;
            RequestUpdateVolumes();
        }

        // Deinitializes a Point Light Volume and keeps the serialized registry dense.
        public void DeinitializePointLightVolume(PointLightVolumeInstance pointLightVolume, bool customTexturesChanged, bool shadowTexturesChanged) {
            if (pointLightVolume == null || PointLightVolumeInstances == null) return;
            int index = FindPointLightRegistryIndex(pointLightVolume);
            if (index < 0) return;
            if (pointLightVolume.LightType == 2 && pointLightVolume.ProjectionMode == 2 && (pointLightVolume.CustomTexture != null || pointLightVolume.CustomTextureMaterial != null)) {
                pointLightVolume.AreaLightFallbackColor = index < _pointLightAreaCookieAverageColors.Length ? _pointLightAreaCookieAverageColors[index] : Color.clear;
            }
            int count = PointLightVolumeInstances.Length;
            PointLightVolumeInstance[] targetArray = new PointLightVolumeInstance[count - 1];
            for (int i = 0; i < index; i++) targetArray[i] = PointLightVolumeInstances[i];
            for (int i = index + 1; i < count; i++) targetArray[i - 1] = PointLightVolumeInstances[i];
            PointLightVolumeInstances = targetArray;
            if (index < count - 1) {
                if (_customTextureArrayDepth > 0) customTexturesChanged = true;
                if (_shadowTextureArrayDepth > 0) shadowTexturesChanged = true;
            }
            if (customTexturesChanged) _customTexturesInitialized = false;
            if (shadowTexturesChanged) _shadowTexturesInitialized = false;
            if (enabled && gameObject.activeInHierarchy) RequestUpdateVolumes();
        }

        // Repositions a registered Point Light Volume after its runtime sort weight changes
        public void ReorderPointLightVolume(PointLightVolumeInstance pointLightVolume) {
            if (pointLightVolume == null) return;
            int index = FindPointLightRegistryIndex(pointLightVolume);
            if (index < 0) {
                if (pointLightVolume.IsActive) InitializePointLightVolume(pointLightVolume);
                return;
            }
            PointLightVolumeInstances[index] = null;
            if (_customTextureArrayDepth > 0) _customTexturesInitialized = false;
            if (_shadowTextureArrayDepth > 0) _shadowTexturesInitialized = false;
            InitializePointLightVolume(pointLightVolume);
        }

#endregion

#region Runtime Texture Caches

#if UNITY_EDITOR && !COMPILER_UDONSHARP
        // Captures the effective custom source state and reports direct edits that bypassed the normal notify API.
        private bool CaptureEditorCustomSourceState() {
            EditorState editorState = EditorData;
            int count = PointLightVolumeInstances.Length;
            bool changed = editorState.CustomSourceOwners.Length != count || editorState.CustomTextureWidth != CustomTexturesWidth || editorState.CustomTextureHeight != CustomTexturesHeight;
            editorState.CustomTextureWidth = CustomTexturesWidth;
            editorState.CustomTextureHeight = CustomTexturesHeight;
            if (changed) {
                editorState.CustomSourceOwners = new PointLightVolumeInstance[count];
                editorState.CustomSourceTextures = new Texture[count];
                editorState.CustomSourceMaterials = new Material[count];
                editorState.CustomSourceStates = new int[count];
            }

            for (int i = 0; i < count; i++) {
                PointLightVolumeInstance instance = PointLightVolumeInstances[i];
                Texture texture = null;
                Material material = null;
                int state = 0;
                if (instance != null && instance.IsActive && instance.ProjectionMode != 0) {
                    if (instance.ProjectionType == 1 && instance.CustomTexture != null) texture = instance.CustomTexture;
                    else if (instance.ProjectionType == 2 && instance.CustomTextureMaterial != null) material = instance.CustomTextureMaterial;
                    if (texture != null || material != null) {
                        state = 1 | (instance.LightType & 3) << 1 | (instance.ProjectionMode & 3) << 3 | (instance.AutoUpdateCustomTexture ? 1 << 5 : 0);
                        if (texture != null) {
                            state |= 1 << 6;
                            if (instance.CustomTextureIsCubemap) state |= 1 << 7;
                            if (instance.CustomTextureHasDepthSlices) state |= 1 << 8;
                        }
                    }
                }
                if (editorState.CustomSourceOwners[i] != instance || editorState.CustomSourceTextures[i] != texture || editorState.CustomSourceMaterials[i] != material || editorState.CustomSourceStates[i] != state) changed = true;
                editorState.CustomSourceOwners[i] = instance;
                editorState.CustomSourceTextures[i] = texture;
                editorState.CustomSourceMaterials[i] = material;
                editorState.CustomSourceStates[i] = state;
            }
            return changed;
        }

        // Captures only source/layout inputs; shading and receiver metadata never rebuild the shared atlas.
        private bool CaptureEditorShadowSourceState() {
            EditorState editorState = EditorData;
            int count = PointLightVolumeInstances.Length;
            bool changed = editorState.ShadowSourceOwners.Length != count || editorState.ShadowTextureWidth != ShadowTexturesWidth || editorState.ShadowTextureHeight != ShadowTexturesHeight
                || editorState.ShadowTextureFormat != ShadowTextureFormat;
            editorState.ShadowTextureWidth = ShadowTexturesWidth;
            editorState.ShadowTextureHeight = ShadowTexturesHeight;
            editorState.ShadowTextureFormat = ShadowTextureFormat;
            if (changed) {
                editorState.ShadowSourceOwners = new PointLightVolumeInstance[count];
                editorState.ShadowSourceTextures = new Texture[count];
                editorState.ShadowSourceMaterials = new Material[count];
                editorState.ShadowSourceStates = new int[count];
            }

            for (int i = 0; i < count; i++) {
                PointLightVolumeInstance instance = PointLightVolumeInstances[i];
                Texture texture = null;
                Material material = null;
                int state = 0;
                if (instance != null && instance.IsActive && instance.ShadowMapID >= 0) {
                    if (instance.ShadowMapTexture != null) texture = instance.ShadowMapTexture;
                    else if (instance.ShadowMapMaterial != null) material = instance.ShadowMapMaterial;
                    if (texture != null || material != null) {
                        bool usesCubemap = instance.LightType != 1 || instance.ShadowMapUsesCubemap;
                        state = 1 | (usesCubemap ? 1 << 1 : 0) | (instance.AutoUpdateShadowMap ? 1 << 2 : 0);
                        if (texture != null) {
                            state |= 1 << 3;
                            if (instance.ShadowMapTextureIsCubemap) state |= 1 << 4;
                            if (instance.ShadowMapTextureHasDepthSlices) state |= 1 << 5;
                        }
                    }
                }
                if (editorState.ShadowSourceOwners[i] != instance || editorState.ShadowSourceTextures[i] != texture || editorState.ShadowSourceMaterials[i] != material || editorState.ShadowSourceStates[i] != state) changed = true;
                editorState.ShadowSourceOwners[i] = instance;
                editorState.ShadowSourceTextures[i] = texture;
                editorState.ShadowSourceMaterials[i] = material;
                editorState.ShadowSourceStates[i] = state;
            }
            return changed;
        }
#endif

        // Rebuilds the runtime cookie texture array and assigns stable shader-side IDs to all point light instances
        public void ReinitializeCustomTextures() {
#if UNITY_EDITOR && !COMPILER_UDONSHARP
            if (!Application.isPlaying) CaptureEditorCustomSourceState();
#endif
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
#if !COMPILER_UDONSHARP && UNITY_EDITOR
            if (_customTexturesUseMipMap && !Application.isPlaying && (!CustomTextures.useMipMap || CustomTextures.autoGenerateMips)) {
                ReinitializeCustomTextures();
                return;
            }
#endif
            BlitCustomTextures(true);
        }

        // Builds deduplicated source arrays and per-instance shader IDs for the runtime cookie texture array
        private void BuildCustomTextureSourceCache() {

            int count = PointLightVolumeInstances.Length;

            // Prepare reusable custom texture source cache arrays for a full rebuild
            if (_pointLightCustomIDs.Length < count || _customSourceTypes.Length < count || _customSingleAreaCookieReceivers.Length < count || _customSingleAreaCookieReceiverIndices.Length < count || _pointLightAreaCookieAverageColors.Length < count) {
                _customCubemapTextures = new Texture[count];
                _customCubemapMaterials = new Material[count];
                _customSingleTextures = new Texture[count];
                _customSingleMaterials = new Material[count];
                _customCubemapTextureModes = new int[count];
                _customCubemapTextureAutoUpdates = new bool[count];
                _customCubemapMaterialAutoUpdates = new bool[count];
                _customSingleTextureAutoUpdates = new bool[count];
                _customSingleMaterialAutoUpdates = new bool[count];
                _customSingleAreaCookieReceivers = new PointLightVolumeInstance[count];
                _customSingleAreaCookieReceiverIndices = new int[count];
                _pointLightCustomIDs = new int[count];
                _customSourceTypes = new int[count];
                _pointLightAreaCookieAverageColors = new Color[count];
            } else {
                for (int i = 0; i < _customCubemapTextureCount; i++) _customCubemapTextures[i] = null;
                for (int i = 0; i < _customCubemapMaterialCount; i++) _customCubemapMaterials[i] = null;
                for (int i = 0; i < _customSingleTextureCount; i++) _customSingleTextures[i] = null;
                for (int i = 0; i < _customSingleMaterialCount; i++) _customSingleMaterials[i] = null;
            }
            // The registry can be compacted or reordered independently of this reusable array.
            // Rebuild its index view from the per-instance cache below so a removed light's
            // fallback color can never leak into the light that takes over its old index.
            for (int i = 0; i < _pointLightAreaCookieAverageColors.Length; i++) _pointLightAreaCookieAverageColors[i] = Color.clear;
            int previousSingleSourceCount = _customSingleTextureCount + _customSingleMaterialCount;
            for (int i = 0; i < previousSingleSourceCount; i++) {
                _customSingleAreaCookieReceivers[i] = null;
                _customSingleAreaCookieReceiverIndices[i] = -1;
            }
            HasAutoCustomTextureUpdates = false;
            _customTexturesUseMipMap = false;

            // Projection source counters
            int cubemapTextureCount = 0;
            int cubemapMaterialCount = 0;
            int singleTextureCount = 0;
            int singleMaterialCount = 0;
            bool pointLutUsesFirstSingleTexture = false;
            bool pointLutUsesFirstSingleMaterial = false;

            // Iterate through registry and collect unique texture/material sources in reusable arrays
            for (int i = 0; i < count; i++) {

                _pointLightCustomIDs[i] = -1;
                _customSourceTypes[i] = 0;
                PointLightVolumeInstance instance = PointLightVolumeInstances[i];
                if (instance == null || !instance.IsActive) continue;

                int projectionType = instance.ProjectionType;
                if (projectionType == 0) continue; // 0: parametric projection has no custom source

                int lightType = instance.LightType;

                int projectionMode = instance.ProjectionMode;
                if (projectionMode == 0) continue; // 0: parametric projection has no custom source

                bool usesCubemapProjection = lightType == 0 && projectionMode == 2; // 0: point, 2: custom cookie or cubemap
                bool usesAreaCookieProjection = lightType == 2 && projectionMode == 2; // 2: area, 2: custom cookie
                bool usesPointLutProjection = lightType == 0 && projectionMode == 1; // 0: point, 1: LUT

                if (projectionType == 1) { // TEXTURE PROJECTION

                    Texture textureSource = instance.CustomTexture;
                    if (textureSource == null) continue;
                    bool autoUpdate = instance.AutoUpdateCustomTexture;
                    if (usesAreaCookieProjection) _customTexturesUseMipMap = true;

                    if (usesCubemapProjection) { // TEXTURE CUBEMAP PROJECTION

                        int index = -1;
                        for (int j = 0; j < cubemapTextureCount; j++) {
                            if (_customCubemapTextures[j] == textureSource && _customCubemapTextureAutoUpdates[j] == autoUpdate) {
                                index = j;
                                break;
                            }
                        }
                        if (index < 0) { // Append each unique source/update-mode pair once so matching lights share the same texture ID
                            index = cubemapTextureCount;
                            _customCubemapTextures[cubemapTextureCount] = textureSource;
                            _customCubemapTextureModes[cubemapTextureCount] = instance.CustomTextureIsCubemap ? 2 : (instance.CustomTextureHasDepthSlices ? 1 : 0); // Texture layout: 0 = single 2D texture, 1 = Texture2DArray face slices, 2 = native Cubemap.
                            _customCubemapTextureAutoUpdates[cubemapTextureCount] = autoUpdate;
                            cubemapTextureCount++;
                        }
                        _pointLightCustomIDs[i] = index;
                        _customSourceTypes[i] = 1; // 1: cubemap texture source, already indexed from the start of the cubemap source block

                    } else { // TEXTURE COOKIE PROJECTION

                        int index = -1;
                        for (int j = 0; j < singleTextureCount; j++) {
                            if (_customSingleTextures[j] == textureSource && _customSingleTextureAutoUpdates[j] == autoUpdate) {
                                index = j;
                                break;
                            }
                        }
                        if (index < 0) { // Append each unique source/update-mode pair once so matching lights share the same texture ID
                            index = singleTextureCount;
                            _customSingleTextures[singleTextureCount] = textureSource;
                            _customSingleTextureAutoUpdates[singleTextureCount] = autoUpdate;
                            singleTextureCount++;
                        }
                        if (usesPointLutProjection && index == 0) pointLutUsesFirstSingleTexture = true;
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

                        int index = -1;
                        for (int j = 0; j < cubemapMaterialCount; j++) {
                            if (_customCubemapMaterials[j] == materialSource && _customCubemapMaterialAutoUpdates[j] == autoUpdate) {
                                index = j;
                                break;
                            }
                        }
                        if (index < 0) { // Append each unique material/update-mode pair once so matching lights share the same texture ID
                            index = cubemapMaterialCount;
                            _customCubemapMaterials[cubemapMaterialCount] = materialSource;
                            _customCubemapMaterialAutoUpdates[cubemapMaterialCount] = autoUpdate;
                            cubemapMaterialCount++;
                        }
                        _pointLightCustomIDs[i] = index;
                        _customSourceTypes[i] = 2; // 2: cubemap material source, offset after cubemap texture sources during final ID assignment

                    } else { // MATERIAL SINGLE SLICE PROJECTION

                        int index = -1;
                        for (int j = 0; j < singleMaterialCount; j++) {
                            if (_customSingleMaterials[j] == materialSource && _customSingleMaterialAutoUpdates[j] == autoUpdate) {
                                index = j;
                                break;
                            }
                        }
                        if (index < 0) { // Append each unique material/update-mode pair once so matching lights share the same texture ID
                            index = singleMaterialCount;
                            _customSingleMaterials[singleMaterialCount] = materialSource;
                            _customSingleMaterialAutoUpdates[singleMaterialCount] = autoUpdate;
                            singleMaterialCount++;
                        }
                        if (usesPointLutProjection && index == 0) pointLutUsesFirstSingleMaterial = true;
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
            int singleSourceIDOffset = cubemapsCount == 0 && (pointLutUsesFirstSingleTexture || singleTextureCount == 0 && pointLutUsesFirstSingleMaterial) ? 1 : 0; // v2 point LUT shaders treat custom ID 0 as parametric.
            _customTextureArrayDepth = cubemapsCount * 6 + singleSourceIDOffset + singleTextureCount + singleMaterialCount;

            // Convert local source indices into final texture-array source IDs and refresh area-cookie fallback source cache after final counts are known
            for (int i = 0; i < count; i++) {
                PointLightVolumeInstance instance = PointLightVolumeInstances[i];
                if (instance == null) {
                    if (i < _pointLightAreaCookieAverageColors.Length) _pointLightAreaCookieAverageColors[i] = Color.clear;
                    continue;
                }
                if (!instance.IsActive) continue;

                int index = _pointLightCustomIDs[i];
                if (index < 0) {
                    if (i < _pointLightAreaCookieAverageColors.Length) _pointLightAreaCookieAverageColors[i] = Color.clear;
                    if (instance.AreaCookieAverageReadbackPending) {
                        instance.AreaCookieAverageCustomId = -1;
                        instance.AreaCookieAverageReadbackDirty = true;
                    }
                    continue;
                }
                int sourceType = _customSourceTypes[i];
                // SourceType 1 already uses the local cubemap texture index as the final ID; 2/3/4 need offsets.
                if (sourceType == 2) index += cubemapTextureCount; // 2: cubemap materials follow cubemap textures
                else if (sourceType == 3) index += cubemapsCount + singleSourceIDOffset; // 3: single textures follow every six-slice cubemap source
                else if (sourceType == 4) index += cubemapsCount + singleSourceIDOffset + singleTextureCount; // 4: single materials follow single textures
                _pointLightCustomIDs[i] = index;

                if ((sourceType != 3 && sourceType != 4) || instance.LightType != 2 || instance.ProjectionMode != 2) { // 2: area light, 2: custom cookie, 3/4: single texture/material
                    if (i < _pointLightAreaCookieAverageColors.Length) _pointLightAreaCookieAverageColors[i] = Color.clear;
                    if (instance.AreaCookieAverageReadbackPending) {
                        instance.AreaCookieAverageCustomId = -1;
                        instance.AreaCookieAverageReadbackDirty = true;
                    }
                    continue;
                }

                int singleSourceIndex = index - cubemapsCount - singleSourceIDOffset;
                if (singleSourceIndex >= 0 && singleSourceIndex < _customSingleAreaCookieReceivers.Length && _customSingleAreaCookieReceivers[singleSourceIndex] == null) {
                    _customSingleAreaCookieReceivers[singleSourceIndex] = instance;
                    _customSingleAreaCookieReceiverIndices[singleSourceIndex] = i;
                }

                if (i < _pointLightAreaCookieAverageColors.Length) _pointLightAreaCookieAverageColors[i] = instance.AreaLightFallbackColor;
                instance.AreaCookieAverageReadbackDirty = true;
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
            int singleTextureCount = _customSingleTextureCount;
            int singleMaterialCount = _customSingleMaterialCount;
            int singleBaseSlice = _customTextureArrayDepth - singleTextureCount - singleMaterialCount;
            for (int i = 0; i < singleTextureCount; i++) {
                if (autoUpdatePass && !_customSingleTextureAutoUpdates[i]) continue;
                Texture sourceTexture = _customSingleTextures[i];
                if (sourceTexture == null) continue;
                int targetSlice = singleBaseSlice + i;
                VRCGraphics.Blit(sourceTexture, CustomTextures, 0, targetSlice);
            }

            // Blit each 1-slice material source into 1 array slice after texture sources
            for (int i = 0; i < singleMaterialCount; i++) {
                if (autoUpdatePass && !_customSingleMaterialAutoUpdates[i]) continue;
                Material sourceMaterial = _customSingleMaterials[i];
                if (sourceMaterial == null) continue;
                int targetSlice = singleBaseSlice + singleTextureCount + i;
                BlitMaterialSlice(sourceMaterial, 0, targetSlice, false, CustomTextures);
            }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
            // Edit-mode fallback readbacks need the freshly blitted last mip immediately.
            if (_customTexturesUseMipMap && CustomTextures != null && !Application.isPlaying && !CustomTextures.autoGenerateMips) CustomTextures.GenerateMips();
            if (!Application.isPlaying) {
                RequestAreaCookieAverageReadbacks(autoUpdatePass);
                return;
            }
#endif
            if (!_customTexturesUseMipMap || CustomTextures == null) return;
            if (!autoUpdatePass) _areaCookieAverageReadbackForceAll = true;
            if (_areaCookieAverageReadbackScheduled) return;
            _areaCookieAverageReadbackScheduled = true;
#if UDONSHARP
            SendCustomEventDelayedFrames(nameof(_RequestAreaCookieAverageReadbacks), 1);
#else
            StartCoroutine(DelayedAreaCookieAverageReadbacks());
#endif
        }

#if !UDONSHARP
        // Delays runtime readbacks by one frame in regular MonoBehaviour builds.
        private IEnumerator DelayedAreaCookieAverageReadbacks() {
            yield return null;
            _RequestAreaCookieAverageReadbacks();
        }
#endif

        // Runs delayed area-cookie fallback readbacks.
        public void _RequestAreaCookieAverageReadbacks() {
            _areaCookieAverageReadbackScheduled = false;
            bool autoUpdatePass = !_areaCookieAverageReadbackForceAll;
            _areaCookieAverageReadbackForceAll = false;
            RequestAreaCookieAverageReadbacks(autoUpdatePass);
        }

        // Requests area-cookie fallback readbacks for all slices touched by the last custom texture blit pass.
        private void RequestAreaCookieAverageReadbacks(bool autoUpdatePass) {
            int singleTextureCount = _customSingleTextureCount;
            int singleMaterialCount = _customSingleMaterialCount;
            for (int i = 0; i < singleTextureCount; i++) {
                if (autoUpdatePass && !_customSingleTextureAutoUpdates[i]) continue;
                PointLightVolumeInstance receiver = _customSingleAreaCookieReceivers[i];
                if (receiver != null) RequestAreaCookieAverageReadback(i, receiver, _customSingleAreaCookieReceiverIndices[i], autoUpdatePass);
            }

            for (int i = 0; i < singleMaterialCount; i++) {
                if (autoUpdatePass && !_customSingleMaterialAutoUpdates[i]) continue;
                int sourceIndex = singleTextureCount + i;
                PointLightVolumeInstance receiver = _customSingleAreaCookieReceivers[sourceIndex];
                if (receiver != null) RequestAreaCookieAverageReadback(sourceIndex, receiver, _customSingleAreaCookieReceiverIndices[sourceIndex], autoUpdatePass);
            }
        }

        // Requests one area cookie average from the final texture array slice used for old-shader fallback
        private void RequestAreaCookieAverageReadback(int sourceIndex, PointLightVolumeInstance receiver, int receiverIndex, bool forceReadback) {
            if (!_customTexturesUseMipMap || CustomTextures == null || receiver == null) return;
            int singleBaseSlice = _customTextureArrayDepth - _customSingleTextureCount - _customSingleMaterialCount;
            int singleSourceIDOffset = singleBaseSlice - CubemapsCount * 6;
            int targetSlice = singleBaseSlice + sourceIndex;
            int mipIndex = CustomTextures.mipmapCount - 1;
            int customId = CubemapsCount + singleSourceIDOffset + sourceIndex;

            if (!forceReadback && !receiver.AreaCookieAverageReadbackDirty) {
                if (receiverIndex >= 0 && receiverIndex < _pointLightAreaCookieAverageColors.Length && _pointLightAreaCookieAverageColors[receiverIndex].a > 0f) {
                    UploadAreaCookieAverageColor(customId, _pointLightAreaCookieAverageColors[receiverIndex]);
                    return;
                }
            }

            if (receiver.AreaCookieAverageReadbackPending) {
                if (receiver.AreaCookieAverageCustomId == customId) return;
                receiver.AreaCookieAverageReadbackDirty = true;
                return;
            }

            receiver.AreaCookieAverageCustomId = customId;
            receiver.AreaCookieAverageReadbackPending = true;
            receiver.AreaCookieAverageReadbackDirty = false;
#if COMPILER_UDONSHARP
            VRCAsyncGPUReadback.Request(CustomTextures, mipIndex, 0, 1, 0, 1, targetSlice, 1, TextureFormat.RGBA32, (IUdonEventReceiver)receiver);
#else
#if UNITY_EDITOR
            if (!Application.isPlaying) {
                AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(CustomTextures, mipIndex, 0, 1, 0, 1, targetSlice, 1, TextureFormat.RGBA32);
                request.WaitForCompletion();
                receiver.OnAsyncGpuReadbackComplete(request);
                return;
            }
#endif
            AsyncGPUReadback.Request(CustomTextures, mipIndex, 0, 1, 0, 1, targetSlice, 1, TextureFormat.RGBA32, receiver.OnAsyncGpuReadbackComplete);
#endif
        }

        // Completes one area-cookie average readback and retries if the source cache changed while it was in flight.
        public void CompleteAreaCookieAverageReadback(PointLightVolumeInstance receiver, bool success, Color color) {
            if (receiver == null) return;
            int customId = receiver.AreaCookieAverageCustomId;
            bool retry = receiver.AreaCookieAverageReadbackDirty;
            receiver.AreaCookieAverageReadbackPending = false;
            receiver.AreaCookieAverageReadbackDirty = false;
            receiver.AreaCookieAverageCustomId = -1;

            if (success && customId >= 0 && !UploadAreaCookieAverageColor(customId, color)) RequestUpdateVolumes();
            if (retry && enabled && gameObject.activeInHierarchy) ReinitializeCustomTextures();
        }

        // Caches the readback color and patches the live shader buffer. Returns true when a live shader slot was found.
        private bool UploadAreaCookieAverageColor(int customId, Color color) {
            if (customId < 0) return false;

            float alpha = color.a;
            color.r *= alpha;
            color.g *= alpha;
            color.b *= alpha;
            color.a = 1f;

            PointLightVolumeInstance[] pointInstances = PointLightVolumeInstances;
            if (pointInstances == null) return false;
            int sourceCount = _pointLightCustomIDs.Length;
            if (_customSourceTypes.Length < sourceCount) sourceCount = _customSourceTypes.Length;
            if (_pointLightAreaCookieAverageColors.Length < sourceCount) sourceCount = _pointLightAreaCookieAverageColors.Length;
            if (pointInstances.Length < sourceCount) sourceCount = pointInstances.Length;
            for (int i = 0; i < sourceCount; i++) {
                if (_pointLightCustomIDs[i] != customId || _customSourceTypes[i] < 3) continue;
                PointLightVolumeInstance instance = pointInstances[i];
                if (instance == null || instance.LightType != 2 || instance.ProjectionMode != 2) continue;
                _pointLightAreaCookieAverageColors[i] = color;
                instance.AreaLightFallbackColor = color;
            }

            int pointLightCount = _pointLightCount;
            int pointInstanceCount = pointInstances.Length;
            bool foundLiveTarget = false;
            bool updatedColor = false;
            for (int shaderIndex = 0; shaderIndex < pointLightCount; shaderIndex++) {
                int sourceIndex = _enabledPointIDs[shaderIndex];
                if (sourceIndex < 0 || sourceIndex >= _pointLightCustomIDs.Length || _pointLightCustomIDs[sourceIndex] != customId) continue;
                if (sourceIndex >= _customSourceTypes.Length || _customSourceTypes[sourceIndex] < 3) continue; // 3/4: single texture/material cookie sources
                if (sourceIndex >= pointInstanceCount) continue;
                PointLightVolumeInstance sourceInstance = pointInstances[sourceIndex];
                if (sourceInstance == null || sourceInstance.LightType != 2 || sourceInstance.ProjectionMode != 2) continue; // 2: area light, 2: custom cookie
                foundLiveTarget = true;
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
            return foundLiveTarget;
        }

        // Rebuilds the runtime shadow texture array and assigns stable shader-side IDs to all shadowed point light instances
        public void ReinitializeShadowTextures() {
#if UNITY_EDITOR && !COMPILER_UDONSHARP
            if (!Application.isPlaying) CaptureEditorShadowSourceState();
#endif
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
                // Point and area emitters are omnidirectional shadow receivers in the shader ABI.
                // Canonicalize low-level/runtime data here so only spot lights may occupy a single slice.
                bool usesCubemapShadow = instance.LightType != 1 || instance.ShadowMapUsesCubemap;
                instance.ShadowMapUsesCubemap = usesCubemapShadow;

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
            bool autoGenerateMips = useMipMap;
#if !COMPILER_UDONSHARP && UNITY_EDITOR
            if (!Application.isPlaying) autoGenerateMips = false;
#endif
            bool recreate = ShouldRecreateRuntimeTextureArray(CustomTextures, width, height, depth, FixedCustomTexturesFormat, useMipMap, autoGenerateMips, FilterMode.Trilinear);
            if (!recreate) return CustomTextures != null;
            ReleaseRuntimeRenderTexture(CustomTextures);
            CustomTextures = CreateRuntimeTextureArray(width, height, depth, FixedCustomTexturesFormat, FilterMode.Trilinear, useMipMap, autoGenerateMips);
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
            bool recreate = ShouldRecreateRuntimeTextureArray(ShadowTextures, width, height, depth, renderTextureFormat, false, false, FilterMode.Bilinear);
            if (!recreate) return ShadowTextures != null;
            ReleaseRuntimeRenderTexture(ShadowTextures);
            ShadowTextures = CreateRuntimeTextureArray(width, height, depth, renderTextureFormat, FilterMode.Bilinear, false, false);
#if !COMPILER_UDONSHARP
            ShadowTextures.name = "ShadowTextures";
#endif
            _shadowTextureArrayDepth = depth;
            return true;
        }

        // Checks if a runtime texture array must be recreated for the requested layout
        private bool ShouldRecreateRuntimeTextureArray(RenderTexture texture, int width, int height, int depth, RenderTextureFormat format, bool useMipMap, bool autoGenerateMips, FilterMode filterMode) {
            return texture == null || texture.width != width || texture.height != height || texture.volumeDepth != depth || texture.useMipMap != useMipMap || texture.autoGenerateMips != autoGenerateMips || texture.filterMode != filterMode || texture.format != format;
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
        private RenderTexture CreateRuntimeTextureArray(int width, int height, int depth, RenderTextureFormat format, FilterMode filterMode, bool useMipMap, bool autoGenerateMips) {
            RenderTexture texture = new RenderTexture(width, height, 0, format, RenderTextureReadWrite.Linear);
            texture.dimension = TextureDimension.Tex2DArray;
            texture.volumeDepth = depth;
            texture.useMipMap = useMipMap;
            texture.autoGenerateMips = autoGenerateMips;
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
            BlitMaterialToSlice(null, CubemapFaceMaterial, destination, targetSlice);
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
            Texture blitSource = sourceMaterial.HasTexture(_cubemapMainTexID) ? sourceMaterial.GetTexture(_cubemapMainTexID) : null;
#else
            Texture blitSource = null;
#endif
            BlitMaterialToSlice(blitSource, sourceMaterial, destination, targetSlice);
        }

        // Renders one material pass into a destination texture-array slice using the active runtime API
        private void BlitMaterialToSlice(Texture sourceTexture, Material material, RenderTexture destination, int targetSlice) {
#if UDONSHARP
#if !COMPILER_UDONSHARP
            RenderTexture previousRenderTexture = RenderTexture.active;
#endif
            // Udon VRCGraphics needs a separate destination-binding blit before rendering the material into the selected slice
            if (_dummyRT == null) {
                _dummyRT = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                _dummyRT.dimension = TextureDimension.Tex2D;
                _dummyRT.useMipMap = false;
                _dummyRT.autoGenerateMips = false;
                _dummyRT.Create();
            }
            VRCGraphics.Blit(_dummyRT, destination, 0, targetSlice);
            VRCGraphics.Blit(sourceTexture, material, 0, targetSlice);
#if !COMPILER_UDONSHARP
            RenderTexture.active = previousRenderTexture == destination ? null : previousRenderTexture;
#endif
#else
            // Unity Graphics can bind the target slice directly, so the material pass can render in one blit
            RenderTexture previousRenderTexture = RenderTexture.active;
            VRCGraphics.SetRenderTarget(destination, 0, CubemapFace.Unknown, targetSlice);
            VRCGraphics.Blit(sourceTexture, material, 0);
            RenderTexture.active = previousRenderTexture == destination ? null : previousRenderTexture;
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

#if !COMPILER_UDONSHARP
        // Creates or reuses the one persistent hidden camera shared by all runtime shadow bakes.
        public void EnsureRuntimeShadowCamera() {
            if (RuntimeShadowCamera == null || RuntimeShadowCamera.transform.parent != transform) {
                RuntimeShadowCamera = null;
                Camera[] cameras = GetComponentsInChildren<Camera>(true);
                for (int i = 0; i < cameras.Length; i++) {
                    Camera camera = cameras[i];
                    if (camera == null || camera.transform.parent != transform) continue;
                    if (camera.gameObject.name != RuntimeShadowCameraName) continue;
                    if (camera.hideFlags != HideFlags.HideInInspector || camera.gameObject.hideFlags != HideFlags.HideInHierarchy) continue;
                    RuntimeShadowCamera = camera;
                    break;
                }
                if (RuntimeShadowCamera == null) {
                    GameObject cameraObject = new GameObject(RuntimeShadowCameraName);
                    cameraObject.transform.SetParent(transform, false);
                    RuntimeShadowCamera = cameraObject.AddComponent<Camera>();
                }
            }

            RuntimeShadowCamera.gameObject.name = RuntimeShadowCameraName;
            RuntimeShadowCamera.gameObject.hideFlags = HideFlags.HideInHierarchy;
            RuntimeShadowCamera.hideFlags = HideFlags.HideInInspector;
            RuntimeShadowCamera.enabled = false;
            RuntimeShadowCamera.clearFlags = CameraClearFlags.Depth;
            RuntimeShadowCamera.backgroundColor = Color.white;
            RuntimeShadowCamera.orthographic = false;
            RuntimeShadowCamera.fieldOfView = 90f;
            RuntimeShadowCamera.aspect = 1f;
            RuntimeShadowCamera.depthTextureMode = DepthTextureMode.None;
            RuntimeShadowCamera.renderingPath = RenderingPath.Forward;
            RuntimeShadowCamera.allowHDR = false;
            RuntimeShadowCamera.allowMSAA = false;
            RuntimeShadowCamera.useOcclusionCulling = false;
            RuntimeShadowCamera.stereoTargetEye = StereoTargetEyeMask.None;
            RuntimeShadowCamera.ResetReplacementShader();
        }

#endif

#if !COMPILER_UDONSHARP && (!UDONSHARP || UNITY_EDITOR)
        // Destroys the editor/runtime material instance used by non-Udon execution
        private void DestroyCubemapFaceRuntimeMaterial() {
            if (CubemapFaceMaterial == null) return;
            if (CubemapFaceMaterial.hideFlags != HideFlags.HideAndDontSave) return;
            if (Application.isPlaying) Destroy(CubemapFaceMaterial);
            else DestroyImmediate(CubemapFaceMaterial);
            CubemapFaceMaterial = null;
        }

        // Destroys the editor/standalone clustering material created outside the build preprocessor.
        private void DestroyClusteringMaterial() {
#if !COMPILER_UDONSHARP
            if (_generatedClusteringMaterial != null) {
                if (Application.isPlaying) Destroy(_generatedClusteringMaterial);
                else DestroyImmediate(_generatedClusteringMaterial);
                _generatedClusteringMaterial = null;
            }
#endif
            if (ClusteringMaterial != null && ClusteringMaterial.hideFlags == HideFlags.HideAndDontSave) {
                if (Application.isPlaying) Destroy(ClusteringMaterial);
                else DestroyImmediate(ClusteringMaterial);
                ClusteringMaterial = null;
            }
        }

#endif

#endregion

#region Froxel Clustering

        // Updates screen-camera froxel clustering in VRChat and safely disables it when camera data is unavailable.
        private void UpdateClustering() {
            if (!Clustering) {
                if (_clusteringActive) DisableClustering();
                return;
            }
            TryInitialize();
            int minLightCount = Mathf.Clamp(ClusteringMinLights, 1, MaxPointLightCount);
            if (_clusterGeometryUploadPending || _pointLightCount < minLightCount || _clusteringUnsupported) {
                DisableClustering();
                return;
            }

#if COMPILER_UDONSHARP
            VRCCameraSettings camera = VRCCameraSettings.ScreenCamera;
            if (camera == null || !camera.Active) {
                DisableClustering();
                return;
            }

            Vector3 position = camera.Position;
            Vector3 stereoLeftPosition = position;
            Vector3 stereoRightPosition = position;
            bool stereoEnabled = camera.StereoEnabled;
            if (stereoEnabled) {
                stereoLeftPosition = VRCCameraSettings.GetEyePosition(Camera.StereoscopicEye.Left);
                stereoRightPosition = VRCCameraSettings.GetEyePosition(Camera.StereoscopicEye.Right);
                position = (stereoLeftPosition + stereoRightPosition) * 0.5f;
            }

            float cameraFov = camera.FieldOfView;
            float cameraAspect = camera.Aspect;
            int pixelHeight = camera.PixelHeight;
            float verticalFov = cameraFov > 0.001f ? cameraFov : DefaultFroxelFov;
            float aspect = cameraAspect > 0.001f ? cameraAspect : (pixelHeight > 0 ? Mathf.Max((float)camera.PixelWidth / pixelHeight, 0.001f) : DefaultFroxelAspect);
            float rawFarClip = Mathf.Max(camera.FarClipPlane, 0.01f);

            Quaternion rotation = camera.Rotation;
            Vector3 right = rotation * Vector3.right;
            Vector3 up = rotation * Vector3.up;
            Vector3 forward = rotation * Vector3.forward;
            float horizontalPadding = 0f;
            float verticalPadding = 0f;
            float depthPadding = 0f;

            if (stereoEnabled) {
                Vector3 leftEyeOffset = stereoLeftPosition - position;
                Vector3 rightEyeOffset = stereoRightPosition - position;
                horizontalPadding = Mathf.Max(Mathf.Abs(Vector3.Dot(leftEyeOffset, right)), Mathf.Abs(Vector3.Dot(rightEyeOffset, right)));
                verticalPadding = Mathf.Max(Mathf.Abs(Vector3.Dot(leftEyeOffset, up)), Mathf.Abs(Vector3.Dot(rightEyeOffset, up)));
                depthPadding = Mathf.Max(Mathf.Abs(Vector3.Dot(leftEyeOffset, forward)), Mathf.Abs(Vector3.Dot(rightEyeOffset, forward)));
            }

            float nearClip = Mathf.Max(camera.NearClipPlane - depthPadding, 0.001f);
            float farClip = Mathf.Max(rawFarClip + depthPadding, nearClip + 0.001f);
            BuildClustering(position, right, up, forward, verticalFov, aspect, nearClip, farClip, horizontalPadding, verticalPadding, null);
#else
            Camera camera = Camera.main;
            if (camera == null) camera = Camera.current;
            UpdateClusteringFromCamera(camera);
#endif
        }

#if !COMPILER_UDONSHARP
        // Updates froxel clustering from an explicit Unity camera for standalone play mode and Scene View preview.
        public void UpdateClusteringFromCamera(Camera camera) {
            if (!Clustering) {
                if (_clusteringActive) DisableClustering();
                return;
            }
            TryInitialize();
            int minLightCount = Mathf.Clamp(ClusteringMinLights, 1, MaxPointLightCount);
            if (_clusterGeometryUploadPending || _pointLightCount < minLightCount || camera == null || camera.orthographic || !ClusteringSupported()) {
                DisableClustering();
                return;
            }

            Transform cameraTransform = camera.transform;
            Vector3 position = cameraTransform.position;
            Vector3 stereoLeftPosition = position;
            Vector3 stereoRightPosition = position;
            bool stereoEnabled = camera.stereoEnabled;
            if (stereoEnabled) {
                Matrix4x4 leftEyeMatrix = camera.GetStereoViewMatrix(Camera.StereoscopicEye.Left).inverse;
                Matrix4x4 rightEyeMatrix = camera.GetStereoViewMatrix(Camera.StereoscopicEye.Right).inverse;
                Vector4 leftEyeColumn = leftEyeMatrix.GetColumn(3);
                Vector4 rightEyeColumn = rightEyeMatrix.GetColumn(3);
                stereoLeftPosition = new Vector3(leftEyeColumn.x, leftEyeColumn.y, leftEyeColumn.z);
                stereoRightPosition = new Vector3(rightEyeColumn.x, rightEyeColumn.y, rightEyeColumn.z);
                position = (stereoLeftPosition + stereoRightPosition) * 0.5f;
            }

            float verticalFov = camera.fieldOfView > 0.001f ? camera.fieldOfView : DefaultFroxelFov;
            float aspect = camera.aspect > 0.001f ? camera.aspect : DefaultFroxelAspect;
            float rawFarClip = Mathf.Max(camera.farClipPlane, 0.01f);

            Quaternion rotation = cameraTransform.rotation;
            Vector3 right = rotation * Vector3.right;
            Vector3 up = rotation * Vector3.up;
            Vector3 forward = rotation * Vector3.forward;
            float horizontalPadding = 0f;
            float verticalPadding = 0f;
            float depthPadding = 0f;

            if (stereoEnabled) {
                Vector3 leftEyeOffset = stereoLeftPosition - position;
                Vector3 rightEyeOffset = stereoRightPosition - position;
                horizontalPadding = Mathf.Max(Mathf.Abs(Vector3.Dot(leftEyeOffset, right)), Mathf.Abs(Vector3.Dot(rightEyeOffset, right)));
                verticalPadding = Mathf.Max(Mathf.Abs(Vector3.Dot(leftEyeOffset, up)), Mathf.Abs(Vector3.Dot(rightEyeOffset, up)));
                depthPadding = Mathf.Max(Mathf.Abs(Vector3.Dot(leftEyeOffset, forward)), Mathf.Abs(Vector3.Dot(rightEyeOffset, forward)));
            }

            float nearClip = Mathf.Max(camera.nearClipPlane - depthPadding, 0.001f);
            float farClip = Mathf.Max(rawFarClip + depthPadding, nearClip + 0.001f);
            BuildClustering(position, right, up, forward, verticalFov, aspect, nearClip, farClip, horizontalPadding, verticalPadding, camera);
        }

        // Releases editor preview textures while leaving the shared material available for play-mode preparation.
        public void ReleaseClusteringPreview() {
            TryInitialize();
            DisableClustering();
#if UNITY_EDITOR
            // Shader globals outlive managed proxy state across play-mode transitions. Reset them
            // even when the restored edit-mode manager reports a stale _clusteringActive == false.
            VRCShader.SetGlobalFloat(_clusteringEnabledID, 0f);
            VRCShader.SetGlobalTexture(_clusterMaskID, null);
            VRCShader.SetGlobalTexture(_coarseClusterMaskID, null);
#endif
            _clusteringUnsupported = false;
            _clusteringAllocationFailed = false;
            _froxelLayoutValid = false;
            _froxelDepthValid = false;
            _froxelProjectionValid = false;
            _clusterMaskDirty = true;
            _clusterMaskValid = false;
            if (_clusterMask != null) {
                ReleaseRuntimeRenderTexture(_clusterMask);
                _clusterMask = null;
            }
            if (_coarseClusterMask != null) {
                ReleaseRuntimeRenderTexture(_coarseClusterMask);
                _coarseClusterMask = null;
            }
            if (_clusteringSource != null) {
                ReleaseRuntimeRenderTexture(_clusteringSource);
                _clusteringSource = null;
            }
        }

#if UNITY_EDITOR
        // Rebuilds all derived edit-mode data after script reloads and late UdonSharp asset imports.
        // Runtime flags can be restored independently from managed resources, so no cached gate is trusted.
        public void RebuildClusteringPreviewState() {
            if (Application.isPlaying) return;
            _isUpdatingVolumes = false;
            _volumeDataUpdateRequested = false;
#if UDONSHARP
            _isUpdateProcessRunning = false;
#endif
            _isInitialized = false;
            _isRangeDirty = true;
            _clusteringLightsDirty = true;
            _clusterGeometryUploadPending = false;
            ReleaseClusteringPreview();
            UpdateVolumes();
        }

        // Prevents the generated HideAndDontSave material from becoming unreachable with the
        // editor-state table during an assembly reload.
        public void ReleaseClusteringPreviewForAssemblyReload() {
            ReleaseClusteringPreview();
            DestroyClusteringMaterial();
        }
#endif
#endif

#if !COMPILER_UDONSHARP
        // Returns whether the active renderer can build and sample the packed integer mask atlas.
        private bool ClusteringSupported() {
            if (_clusteringUnsupported) return false;
            return SystemInfo.graphicsShaderLevel >= 35 && SystemInfo.SupportsRenderTextureFormat(ClusterMaskFormat);
        }
#endif

        // Resolves the camera grid, publishes its world-space transform, and builds both clustering masks.
        private void BuildClustering(Vector3 position, Vector3 right, Vector3 up, Vector3 forward, float verticalFov, float aspect, float nearClip, float farClip, float horizontalPadding, float verticalPadding, Camera renderCamera) {
            verticalFov = Mathf.Clamp(verticalFov, 1f, 179f);
            if (aspect < 0.001f) aspect = DefaultFroxelAspect;
            if (nearClip < 0.001f) nearClip = 0.001f;
            if (farClip < nearClip + 0.001f) farClip = nearClip + 0.001f;

            float density = Mathf.Clamp(FroxelDensity, 0.05f, 3f);
            int depthSlices = Mathf.Clamp(FroxelSlices, 1, MaxFroxelSize);
            int requestedCoarse = FroxelCoarse;
            int coarseFactor = requestedCoarse <= 2 ? 2 : (requestedCoarse <= 5 ? 4 : 8);

            bool layoutChanged = true;
            if (_froxelLayoutValid && _froxelLayoutFov == verticalFov && _froxelLayoutAspect == aspect && _froxelLayoutDensity == density
                && _froxelLayoutSlices == depthSlices && _froxelLayoutCoarse == coarseFactor) layoutChanged = false;
            if (layoutChanged) {
                _froxelLayoutValid = true;
                _froxelLayoutFov = verticalFov;
                _froxelLayoutAspect = aspect;
                _froxelLayoutDensity = density;
                _froxelLayoutSlices = depthSlices;
                _froxelLayoutCoarse = coarseFactor;
                _clusteringAllocationFailed = false;

                float halfVerticalRadians = verticalFov * (0.5f * Mathf.Deg2Rad);
                _froxelTanHalfVertical = Mathf.Tan(halfVerticalRadians);
                _froxelTanHalfHorizontal = _froxelTanHalfVertical * aspect;
                float horizontalFov = Mathf.Atan(_froxelTanHalfHorizontal) * (2f * Mathf.Rad2Deg);
                int columns = Mathf.Clamp(Mathf.CeilToInt(horizontalFov * density), 1, MaxFroxelSize);
                int rows = Mathf.Clamp(Mathf.CeilToInt(verticalFov * density), 1, MaxFroxelSize);

                // Tile rows only enough to fit the portable 4096 texture limit. Depth changes
                // storage packing, but never the camera's logical angular grid.
                int atlasTileShift = 0;
                int atlasTileColumns = 1;
                int atlasTileRows = rows;
                while (depthSlices * atlasTileRows > MaxFroxelAtlasSize && atlasTileShift < MaxFroxelTileShift) {
                    atlasTileShift++;
                    atlasTileColumns <<= 1;
                    atlasTileRows = (rows + atlasTileColumns - 1) >> atlasTileShift;
                }
                _fineAtlasWidth = columns * atlasTileColumns;
                _fineAtlasHeight = depthSlices * atlasTileRows;

                int coarseShift = coarseFactor == 2 ? 1 : (coarseFactor == 4 ? 2 : 3);
                int coarseColumns = (columns + coarseFactor - 1) >> coarseShift;
                int coarseRows = (rows + coarseFactor - 1) >> coarseShift;
                int coarseDepthSlices = (depthSlices + coarseFactor - 1) >> coarseShift;
                int coarseAtlasTileShift = 0;
                int coarseAtlasTileColumns = 1;
                int coarseAtlasTileRows = coarseRows;
                while (coarseDepthSlices * coarseAtlasTileRows > MaxFroxelAtlasSize && coarseAtlasTileShift < MaxFroxelTileShift) {
                    coarseAtlasTileShift++;
                    coarseAtlasTileColumns <<= 1;
                    coarseAtlasTileRows = (coarseRows + coarseAtlasTileColumns - 1) >> coarseAtlasTileShift;
                }
                _coarseAtlasWidth = coarseColumns * coarseAtlasTileColumns;
                _coarseAtlasHeight = coarseDepthSlices * coarseAtlasTileRows;

                _fineGridParams = new Vector4(columns, depthSlices, rows, atlasTileShift);
                _coarseGridParams = new Vector4(coarseColumns, coarseDepthSlices, coarseRows, coarseAtlasTileShift);
                _coarseReductionParams = new Vector4(coarseFactor, coarseShift, 1f / columns, 1f / rows);
                _froxelDepthValid = false;
                _froxelProjectionValid = false;
                _clusterMaskDirty = true;
                _clusterMaskValid = false;
            }

            if (_clusteringAllocationFailed) {
                DisableClustering();
                return;
            }

            Material clusteringMaterial = GetClusteringMaterial();
            bool materialMissing = clusteringMaterial == null;
            bool resourcesMissing = materialMissing || _clusterMask == null || _coarseClusterMask == null || _clusteringSource == null;
#if !COMPILER_UDONSHARP
            resourcesMissing |= (_clusterMask != null && !_clusterMask.IsCreated()) || (_coarseClusterMask != null && !_coarseClusterMask.IsCreated()) || (_clusteringSource != null && !_clusteringSource.IsCreated());
#endif
            if (layoutChanged || resourcesMissing) {
                if (!EnsureClusteringResources(_fineAtlasWidth, _fineAtlasHeight, _coarseAtlasWidth, _coarseAtlasHeight)) {
                    DisableClustering();
                    return;
                }
                clusteringMaterial = GetClusteringMaterial();

                VRCShader.SetGlobalVector(_froxelGridID, _fineGridParams);
                VRCShader.SetGlobalTexture(_clusterMaskID, _clusterMask);
                VRCShader.SetGlobalTexture(_coarseClusterMaskID, _coarseClusterMask);
                VRCShader.SetGlobalVector(_froxelCoarseGridID, _coarseGridParams);
                VRCShader.SetGlobalVector(_froxelCoarseID, _coarseReductionParams);
                clusteringMaterial.SetVector(_froxelFineGridID, _fineGridParams);
                clusteringMaterial.SetVector(_froxelCoarseGridID, _coarseGridParams);
                clusteringMaterial.SetVector(_froxelCoarseID, _coarseReductionParams);
                clusteringMaterial.SetTexture(_coarseClusterMaskID, _coarseClusterMask);
                if (materialMissing) _froxelDepthValid = false;
                _clusterMaskDirty = true;
            }

            bool depthChanged = true;
            if (_froxelDepthValid && _froxelNearClip == nearClip && _froxelFarClip == farClip) depthChanged = false;
            if (depthChanged) {
                _froxelDepthValid = true;
                _froxelNearClip = nearClip;
                _froxelFarClip = farClip;
                float logDepthRange = Mathf.Log(farClip / nearClip) * 1.4426950409f;
                if (logDepthRange < 0.000001f) logDepthRange = 0.000001f;
                float logDepthStep = logDepthRange / depthSlices;
#if !COMPILER_UDONSHARP
                _editorFroxelDepthParams = new Vector4(nearClip, farClip, 1f / nearClip, depthSlices / logDepthRange);
                VRCShader.SetGlobalVector(_froxelDepthID, _editorFroxelDepthParams);
#else
                VRCShader.SetGlobalVector(_froxelDepthID, new Vector4(nearClip, farClip, 1f / nearClip, depthSlices / logDepthRange));
#endif
                float fineDepthRatio = Mathf.Pow(2f, logDepthStep);
                float coarseDepthRatio = Mathf.Pow(2f, logDepthStep * coarseFactor);
                clusteringMaterial.SetVector(_froxelDepthStepID, new Vector4(logDepthStep, fineDepthRatio, coarseDepthRatio, 0f));
                _clusterMaskDirty = true;
            }

            bool projectionChanged = true;
            if (_froxelProjectionValid && _froxelHorizontalPadding == horizontalPadding && _froxelVerticalPadding == verticalPadding) projectionChanged = false;
            if (projectionChanged) {
                _froxelProjectionValid = true;
                _froxelHorizontalPadding = horizontalPadding;
                _froxelVerticalPadding = verticalPadding;
                VRCShader.SetGlobalVector(_froxelProjectionID, new Vector4(_froxelTanHalfHorizontal, _froxelTanHalfVertical, horizontalPadding, verticalPadding));
                _clusterMaskDirty = true;
            }

            bool cameraChanged = true;
            if (_clusterMaskValid && _froxelCameraPosition.Equals(position) && _froxelCameraRight.Equals(right) && _froxelCameraUp.Equals(up) && _froxelCameraForward.Equals(forward)) cameraChanged = false;
            if (cameraChanged) {
                _froxelCameraPosition = position;
                _froxelCameraRight = right;
                _froxelCameraUp = up;
                _froxelCameraForward = forward;
                VRCShader.SetGlobalVector(_froxelRightID, new Vector4(right.x, right.y, right.z, position.x));
                VRCShader.SetGlobalVector(_froxelUpID, new Vector4(up.x, up.y, up.z, position.y));
                VRCShader.SetGlobalVector(_froxelForwardID, new Vector4(forward.x, forward.y, forward.z, position.z));
            }

#if !COMPILER_UDONSHARP
            bool publishForEditorCamera = !Application.isPlaying;
            if (publishForEditorCamera) {
                // Shader globals are process-wide and may be reset or overwritten without invalidating this manager's caches.
                VRCShader.SetGlobalVector(_froxelGridID, _fineGridParams);
                VRCShader.SetGlobalVector(_froxelDepthID, _editorFroxelDepthParams);
                VRCShader.SetGlobalVector(_froxelCoarseGridID, _coarseGridParams);
                VRCShader.SetGlobalVector(_froxelCoarseID, _coarseReductionParams);
                VRCShader.SetGlobalVector(_froxelProjectionID, new Vector4(_froxelTanHalfHorizontal, _froxelTanHalfVertical, horizontalPadding, verticalPadding));
                VRCShader.SetGlobalVector(_froxelRightID, new Vector4(right.x, right.y, right.z, position.x));
                VRCShader.SetGlobalVector(_froxelUpID, new Vector4(up.x, up.y, up.z, position.y));
                VRCShader.SetGlobalVector(_froxelForwardID, new Vector4(forward.x, forward.y, forward.z, position.z));
                VRCShader.SetGlobalTexture(_clusterMaskID, _clusterMask);
                VRCShader.SetGlobalTexture(_coarseClusterMaskID, _coarseClusterMask);
                VRCShader.SetGlobalVectorArray(_clusteringLightsID, _clusteringLights);
            }
#endif

            bool maskNeedsBuild = _clusterMaskDirty || cameraChanged;
            if (_clusteringLightsDirty) {
                VRCShader.SetGlobalVectorArray(_clusteringLightsID, _clusteringLights);
                _clusteringLightsDirty = false;
                maskNeedsBuild = true;
            }
            if (maskNeedsBuild) {
                BuildClusterMasks(renderCamera, _fineGridParams, _coarseGridParams);
                _clusterMaskDirty = false;
                _clusterMaskValid = true;
            }

#if COMPILER_UDONSHARP
            bool publishForEditorCamera = false;
#endif
            if (!_clusteringActive || publishForEditorCamera) VRCShader.SetGlobalFloat(_clusteringEnabledID, 1f);
            _clusteringActive = true;
        }

        // Ensures the hidden build material, both packed integer targets and one-pixel blit source all exist.
        private bool EnsureClusteringResources(int atlasWidth, int atlasHeight, int coarseAtlasWidth, int coarseAtlasHeight) {
            if (_clusteringUnsupported) return false;
            bool ready = EnsureClusteringMaterial() && EnsureClusterMask(atlasWidth, atlasHeight) && EnsureCoarseClusterMask(coarseAtlasWidth, coarseAtlasHeight) && EnsureClusteringSource();
            if (ready) return true;

            // Do not retain the largest allocation after a later resource failed under memory pressure.
            ReleaseClusteringTextures();
            return false;
        }

        private void ReleaseClusteringTextures() {
            if (_clusterMask != null) ReleaseRuntimeRenderTexture(_clusterMask);
            if (_coarseClusterMask != null) ReleaseRuntimeRenderTexture(_coarseClusterMask);
            if (_clusteringSource != null) ReleaseRuntimeRenderTexture(_clusteringSource);
            _clusterMask = null;
            _coarseClusterMask = null;
            _clusteringSource = null;
            _clusterMaskDirty = true;
            _clusterMaskValid = false;
        }

        // Creates the build material outside Udon; runtime Udon receives the same dependency from the build preprocessor.
        private bool EnsureClusteringMaterial() {
            if (ClusteringMaterial != null) return true;
#if COMPILER_UDONSHARP
            _clusteringUnsupported = true;
            return false;
#else
            if (_generatedClusteringMaterial != null) return true;
            Shader shader = Shader.Find(ClusteringShaderName);
            if (shader == null || !shader.isSupported) {
                _clusteringUnsupported = true;
                return false;
            }
            _generatedClusteringMaterial = new Material(shader);
            _generatedClusteringMaterial.name = gameObject.name + "_ClusteringRuntime";
            _generatedClusteringMaterial.hideFlags = HideFlags.HideAndDontSave;
            return true;
#endif
        }

        // Runtime Udon receives a serialized build dependency; editor preview owns an unsaved material instead.
        private Material GetClusteringMaterial() {
#if COMPILER_UDONSHARP
            return ClusteringMaterial;
#else
            return ClusteringMaterial != null ? ClusteringMaterial : _generatedClusteringMaterial;
#endif
        }

        // Creates or recreates the point-filtered RGBA32I Texture2D atlas used as the 128-bit light mask.
        private bool EnsureClusterMask(int atlasWidth, int atlasHeight) {
            bool matches = _clusterMask != null && _clusterMask.width == atlasWidth && _clusterMask.height == atlasHeight && _clusterMask.dimension == TextureDimension.Tex2D && _clusterMask.format == ClusterMaskFormat && _clusterMask.filterMode == FilterMode.Point && !_clusterMask.useMipMap;
#if !COMPILER_UDONSHARP
            if (matches) matches = _clusterMask.IsCreated();
#endif
            if (matches) return true;

            ReleaseRuntimeRenderTexture(_clusterMask);
            _clusterMask = new RenderTexture(atlasWidth, atlasHeight, 0, ClusterMaskFormat, RenderTextureReadWrite.Linear);
            _clusterMask.dimension = TextureDimension.Tex2D;
            _clusterMask.useMipMap = false;
            _clusterMask.autoGenerateMips = false;
            _clusterMask.enableRandomWrite = false;
            _clusterMask.wrapMode = TextureWrapMode.Clamp;
            _clusterMask.filterMode = FilterMode.Point;
            _clusterMask.anisoLevel = 0;
#if !COMPILER_UDONSHARP
            _clusterMask.name = "Fine Froxel Cluster Mask";
            _clusterMask.hideFlags = HideFlags.HideAndDontSave;
#endif
            bool created = _clusterMask.Create();
            if (created) return true;
            ReleaseRuntimeRenderTexture(_clusterMask);
            _clusterMask = null;
            _clusteringAllocationFailed = true;
            _clusterMaskValid = false;
            return false;
        }

        // Creates or recreates the point-filtered Coarse RGBA32I atlas used only by the Fine builder.
        private bool EnsureCoarseClusterMask(int atlasWidth, int atlasHeight) {
            bool matches = _coarseClusterMask != null && _coarseClusterMask.width == atlasWidth && _coarseClusterMask.height == atlasHeight && _coarseClusterMask.dimension == TextureDimension.Tex2D
                && _coarseClusterMask.format == ClusterMaskFormat && _coarseClusterMask.filterMode == FilterMode.Point && !_coarseClusterMask.useMipMap;
#if !COMPILER_UDONSHARP
            if (matches) matches = _coarseClusterMask.IsCreated();
#endif
            if (matches) return true;

            ReleaseRuntimeRenderTexture(_coarseClusterMask);
            _coarseClusterMask = new RenderTexture(atlasWidth, atlasHeight, 0, ClusterMaskFormat, RenderTextureReadWrite.Linear);
            _coarseClusterMask.dimension = TextureDimension.Tex2D;
            _coarseClusterMask.useMipMap = false;
            _coarseClusterMask.autoGenerateMips = false;
            _coarseClusterMask.enableRandomWrite = false;
            _coarseClusterMask.wrapMode = TextureWrapMode.Clamp;
            _coarseClusterMask.filterMode = FilterMode.Point;
            _coarseClusterMask.anisoLevel = 0;
#if !COMPILER_UDONSHARP
            _coarseClusterMask.name = "Coarse Froxel Cluster Mask";
            _coarseClusterMask.hideFlags = HideFlags.HideAndDontSave;
#endif
            bool created = _coarseClusterMask.Create();
            if (created) return true;
            ReleaseRuntimeRenderTexture(_coarseClusterMask);
            _coarseClusterMask = null;
            _clusteringAllocationFailed = true;
            _clusterMaskValid = false;
            return false;
        }

        // Creates the one-pixel Texture2D source required by Graphics/VRCGraphics.Blit.
        private bool EnsureClusteringSource() {
            bool matches = _clusteringSource != null && _clusteringSource.dimension == TextureDimension.Tex2D;
#if !COMPILER_UDONSHARP
            if (matches) matches = _clusteringSource.IsCreated();
#endif
            if (matches) return true;

            ReleaseRuntimeRenderTexture(_clusteringSource);
            _clusteringSource = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            _clusteringSource.dimension = TextureDimension.Tex2D;
            _clusteringSource.useMipMap = false;
            _clusteringSource.autoGenerateMips = false;
            _clusteringSource.wrapMode = TextureWrapMode.Clamp;
            _clusteringSource.filterMode = FilterMode.Point;
#if !COMPILER_UDONSHARP
            _clusteringSource.name = "Froxel Clustering Source";
            _clusteringSource.hideFlags = HideFlags.HideAndDontSave;
#endif
            bool created = _clusteringSource.Create();
            if (created) return true;
            ReleaseRuntimeRenderTexture(_clusteringSource);
            _clusteringSource = null;
            _clusteringAllocationFailed = true;
            _clusterMaskValid = false;
            return false;
        }

        // Builds Coarse first, then filters it into Fine. Both draws are immediate and complete in the current frame.
        private void BuildClusterMasks(Camera renderCamera, Vector4 fineGridParams, Vector4 coarseGridParams) {
            Material clusteringMaterial = GetClusteringMaterial();
#if !COMPILER_UDONSHARP
            Camera previousCamera = Camera.current;
            RenderTexture previousRenderTexture = RenderTexture.active;
            if (renderCamera != null) Camera.SetupCurrent(renderCamera);
#endif
            // Never bind the Coarse destination as its own sampler: read/write feedback is undefined on GLES3.
            clusteringMaterial.SetTexture(_coarseClusterMaskID, _clusterMask);
#if COMPILER_UDONSHARP
            clusteringMaterial.SetFloat(_froxelPassID, 0f);
            clusteringMaterial.SetVector(_froxelGridID, coarseGridParams);
            VRCGraphics.Blit(_clusteringSource, _coarseClusterMask, clusteringMaterial);
            clusteringMaterial.SetTexture(_coarseClusterMaskID, _coarseClusterMask);
            clusteringMaterial.SetFloat(_froxelPassID, 1f);
            clusteringMaterial.SetVector(_froxelGridID, fineGridParams);
            VRCGraphics.Blit(_clusteringSource, _clusterMask, clusteringMaterial);
#else
            clusteringMaterial.SetFloat(_froxelPassID, 0f);
            clusteringMaterial.SetVector(_froxelGridID, coarseGridParams);
            Graphics.Blit(_clusteringSource, _coarseClusterMask, clusteringMaterial);
            clusteringMaterial.SetTexture(_coarseClusterMaskID, _coarseClusterMask);
            clusteringMaterial.SetFloat(_froxelPassID, 1f);
            clusteringMaterial.SetVector(_froxelGridID, fineGridParams);
            Graphics.Blit(_clusteringSource, _clusterMask, clusteringMaterial);
#endif
#if !COMPILER_UDONSHARP
            RenderTexture.active = previousRenderTexture;
            Camera.SetupCurrent(previousCamera);
#endif
        }

        // Publishes only the availability flag; all Point Light Volume globals remain untouched.
        private void DisableClustering() {
            if (_clusteringActive) {
                VRCShader.SetGlobalFloat(_clusteringEnabledID, 0f);
            }
            _clusteringActive = false;
        }

#endregion

#region Update Process

        // Requests to update volumes next frame
        public void RequestUpdateVolumes() {
#if !COMPILER_UDONSHARP && UDONSHARP && UNITY_EDITOR
            if (ShouldSkipEditorProxyRuntimeUpdate()) return;
            // Udon delayed events are not dispatched for the edit-mode C# proxy.
            if (!Application.isPlaying) {
                UpdateVolumes();
                return;
            }
#endif
            if (_isUpdatingVolumes) return;
            _volumeDataUpdateRequested = true;
            ScheduleUpdateProcess();
        }

        // Schedules the unified delayed update process when it is not already running
        private void ScheduleUpdateProcess() {
#if !COMPILER_UDONSHARP && UDONSHARP && UNITY_EDITOR
            if (ShouldSkipEditorProxyRuntimeUpdate()) return;
            if (!Application.isPlaying) {
                UpdateVolumes();
                return;
            }
#endif
#if UDONSHARP
            if (_isUpdateProcessRunning) return;
            _isUpdateProcessRunning = true;
            SendCustomEventDelayedFrames(nameof(UpdateProcess), 1);
#else
            if (_updateCoroutine != null || !isActiveAndEnabled) return;
            _updateCoroutine = StartCoroutine(UpdateCoroutine());
#endif
        }

        // Polls only cached Dynamic entries in the transform-safe frame phase shared with clustering.
        private void UpdateDynamicVolumeTransforms() {
            if (!AutoUpdateVolumes || _isUpdatingVolumes || _volumeDataUpdateRequested) return;
            if (_dynamicLightVolumeCount == 0 && _dynamicPointLightVolumeCount == 0) return;

            _updateLightVolumeBuffers = false;
            _updatePointLightBuffers = false;
            _updatePointLightPositionBuffer = false;
            _updateNeedsVolumeRebuild = false;
            UpdateAutoUpdatedVolumeChanges();
            if (_updateNeedsVolumeRebuild) {
                _updateLightVolumeBuffers = false;
                _updatePointLightBuffers = false;
                _updatePointLightPositionBuffer = false;
                RequestUpdateVolumes();
                return;
            }
            if (_updateLightVolumeBuffers || _updatePointLightBuffers || _updatePointLightPositionBuffer)
                UploadAutoUpdatedVolumeChanges();
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

                float packedShadowIdAbs = Mathf.Abs(_pointLightCustomId[shaderIndex].y);
                bool hasActiveShadow = packedShadowIdAbs >= 1f && packedShadowIdAbs < DisabledShadingShadowId;
                bool basisUnchanged = localToWorldMatrix.m00 == previousMatrix.m00 && localToWorldMatrix.m01 == previousMatrix.m01 && localToWorldMatrix.m02 == previousMatrix.m02
                    && localToWorldMatrix.m10 == previousMatrix.m10 && localToWorldMatrix.m11 == previousMatrix.m11 && localToWorldMatrix.m12 == previousMatrix.m12
                    && localToWorldMatrix.m20 == previousMatrix.m20 && localToWorldMatrix.m21 == previousMatrix.m21 && localToWorldMatrix.m22 == previousMatrix.m22;
                if (basisUnchanged && !hasActiveShadow) {
                    // Translation-only motion is the common case. Preserve all static light data and avoid
                    // repeated cross-Udon reads; active shadows still need their reprojection metadata rebuilt.
                    Vector3 position = localToWorldMatrix.GetPosition();
                    instance.Position = position;
                    Vector4 positionData = _pointLightPosition[shaderIndex];
                    if (positionData.x != position.x || positionData.y != position.y || positionData.z != position.z) {
                        positionData.x = position.x;
                        positionData.y = position.y;
                        positionData.z = position.z;
                        _pointLightPosition[shaderIndex] = positionData;
                        _clusterMaskDirty = true;
                        _clusterGeometryUploadPending = true;
                        _updatePointLightPositionBuffer = true;
                    }
                } else {
                    UpdatePointLightTransformData(instance, localToWorldMatrix, false);
                    WritePointLightShaderData(shaderIndex, _enabledPointIDs[shaderIndex], instance, false);
                    _updatePointLightBuffers = true;
                }
                _dynamicPointLightVolumeMatrices[i] = localToWorldMatrix;
            }
        }

        // Uploads only shader arrays affected by an incremental dynamic transform update
        private void UploadAutoUpdatedVolumeChanges() {
            if (_updateLightVolumeBuffers && _enabledCount != 0) {
                VRCShader.SetGlobalMatrixArray(_lightVolumeInvWorldMatrixID, _invWorldMatrix);
                VRCShader.SetGlobalVectorArray(_lightVolumeRotationID, _relativeRotation);
                VRCShader.SetGlobalVectorArray(_lightVolumeColorID, _colors);
            }
            if (_updatePointLightBuffers || _updatePointLightPositionBuffer) {
                if (_pointLightCount != 0) {
                    VRCShader.SetGlobalVectorArray(_pointLightPositionID, _pointLightPosition);
                    if (_updatePointLightBuffers) {
                        VRCShader.SetGlobalVectorArray(_pointLightColorID, _pointLightColor);
                        VRCShader.SetGlobalVectorArray(_pointLightExtraDataID, _pointLightExtraData);
                        VRCShader.SetGlobalVectorArray(_pointLightDirectionID, _pointLightDirection);
                        VRCShader.SetGlobalVectorArray(_pointLightCustomIdID, _pointLightCustomId);
                        if (_activeShadowCount > 0) {
                            VRCShader.SetGlobalVectorArray(_pointLightShadowReprojectionDataID, _pointLightShadowReprojectionData);
                            VRCShader.SetGlobalVectorArray(_pointLightShadowRotationDataID, _pointLightShadowRotationData);
                        }
                    }
                }
                if (_clusterGeometryUploadPending) _clusterMaskDirty = true;
                _clusterGeometryUploadPending = false;
            }
            _updateLightVolumeBuffers = false;
            _updatePointLightBuffers = false;
            _updatePointLightPositionBuffer = false;
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

            // Volume section: full rebuilds and direct dirty uploads. Dynamic transforms run in PostLateUpdate.
            bool updateVolumes = _volumeDataUpdateRequested;
            _volumeDataUpdateRequested = false;
            _updateLightVolumeBuffers = _lightVolumeArraysDirty;
            _updatePointLightBuffers = _pointLightArraysDirty;
            _updatePointLightPositionBuffer = false;
            _updateNeedsVolumeRebuild = false;
            _lightVolumeArraysDirty = false;
            _pointLightArraysDirty = false;

            if (updateVolumes) {
                UpdateVolumes();
            } else if (_updateLightVolumeBuffers || _updatePointLightBuffers || _updatePointLightPositionBuffer) {
                UploadAutoUpdatedVolumeChanges();
            }

            // Texture section: auto-updates only cached texture sources, without touching point light components
            if (AutoUpdateTextures) {
                if (!_customTexturesInitialized) ReinitializeCustomTextures();
                if (!_shadowTexturesInitialized) ReinitializeShadowTextures();
                if (HasAutoCustomTextureUpdates) UpdateAutoCustomTextures();
                if (HasAutoShadowTextureUpdates) UpdateAutoShadowTextures();
            }

            keepUpdating = AutoUpdateTextures && (HasAutoCustomTextureUpdates || HasAutoShadowTextureUpdates);

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
            float spotOuterTangent = 0f;
            float clusterOuterTangent = 0f;
            float spotOuterCosine = 1f;
            float spotCookieAspect = 1f;
            Vector3 clusterAxis = Vector3.forward;
            Vector4 directionData = Vector4.zero;
            if (isSpot) {
                spotOuterTangent = instance.OuterAngleTan;
                clusterOuterTangent = spotOuterTangent;
                if (isCustomCookie) {
                    spotCookieAspect = Mathf.Max(Mathf.Abs(instance.SpotCookieAspect), 0.001f);
                    // The cookie is a rectangular pyramid. Cluster against its circumscribed cone to avoid false negatives.
                    float inverseAspect = 1f / spotCookieAspect;
                    clusterOuterTangent *= Mathf.Sqrt(1f + inverseAspect * inverseAspect);
                    Quaternion rotation = instance.Rotation;
                    directionData = new Vector4(rotation.x, rotation.y, rotation.z, rotation.w);
                    clusterAxis = Quaternion.Inverse(rotation) * Vector3.forward;
                } else {
                    Vector3 direction = instance.Direction;
                    spotOuterCosine = instance.OuterAngleCos;
                    clusterAxis = direction;
                    directionData = new Vector4(direction.x, direction.y, direction.z, instance.ConeFalloff);
                }
            } else if (isArea || isCustomCookie) {
                Quaternion rotation = instance.Rotation;
                directionData = new Vector4(rotation.x, rotation.y, rotation.z, rotation.w);
                if (isArea) clusterAxis = rotation * Vector3.forward;
            }
            WriteClusteringLight(shaderIndex, squaredRange, lightType, clusterOuterTangent, clusterAxis);
            _pointLightDirection[shaderIndex] = directionData;
            int resolvedCustomId = sourceIndex < _pointLightCustomIDs.Length ? _pointLightCustomIDs[sourceIndex] : -1;
            bool hasAreaCookie = isArea && isCustomCookie && resolvedCustomId >= 0;

            float angleData;
            if (isArea) {
                float height = Mathf.Max(Mathf.Abs(instance.Height), 0.001f);
                pos.w = Mathf.Max(Mathf.Abs(instance.Width), 0.001f);
                angleData = 2f + height;
            } else {
                float typeSign = isSpot ? -1f : 1f;
                if (isLut) pos.w = typeSign * instance.InverseSquaredRange / Mathf.Max(squaredScale, 0.000001f);
                else {
                    float lightSourceSize = instance.LightSourceSize;
                    pos.w = typeSign * lightSourceSize * lightSourceSize * squaredScale;
                }
                if (isSpot && isCustomCookie) angleData = spotOuterTangent;
                else angleData = isSpot ? spotOuterCosine : instance.OuterAngleCos;
            }
            Vector4 previousPosition = _pointLightPosition[shaderIndex];
            if (previousPosition.x != pos.x || previousPosition.y != pos.y || previousPosition.z != pos.z) {
                _clusterMaskDirty = true;
                _clusterGeometryUploadPending = true;
            }
            _pointLightPosition[shaderIndex] = pos;

            Vector4 lightColor = instance.Color.linear * instance.Intensity;
            Vector4 extraData = lightColor;
            if (isSpot && isCustomCookie) extraData.x = spotCookieAspect;
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

            float shaderCustomId = 0;
            if (resolvedCustomId >= 0) {
                // Match the v2 shader ABI: point LUT uses the positive ID directly, while spot LUT subtracts one in shader.
                if (isLut) shaderCustomId = isSpot ? resolvedCustomId + 1 : resolvedCustomId;
                else if (isCustomCookie) shaderCustomId = -resolvedCustomId - 1;
            }
            int resolvedShadowId = sourceIndex < _pointLightShadowIDs.Length ? _pointLightShadowIDs[sourceIndex] : -1;
            float shadingStrength = Mathf.Clamp01(instance.ShadingStrength);
            bool hasShading = shadingStrength > 0f;
            bool hasShadow = hasShading && ShadowMapsCount > 0 && resolvedShadowId >= 0 && resolvedShadowId < ShadowMapsCount;
            if (countActiveShadow && hasShadow) _activeShadowCount++;
            float shadowNearClip = 0f;
            float shadowInvDepthRange = 0f;
            bool useLocalSpaceShadows = false;
            if (hasShadow) {
                shadowNearClip = Mathf.Max(instance.NearClip, 0.0001f);
                float requestedFarClip = instance.BakedFarClip > 0f ? instance.BakedFarClip : instance.FarClip;
                float resolvedFarClip = requestedFarClip > 0f ? Mathf.Max(requestedFarClip, shadowNearClip + 0.0001f) : Mathf.Sqrt(Mathf.Max(squaredRange, 0.000001f));
                if (shadowNearClip >= resolvedFarClip) resolvedFarClip = shadowNearClip + 0.0001f;
                // Far is needed by the bake/encoder, but the receiver only needs its precomputed reciprocal range.
                shadowInvDepthRange = 1f / Mathf.Max(resolvedFarClip - shadowNearClip, 0.0001f);
                useLocalSpaceShadows = !instance.WorldSpaceShadows;
            }
            extraData.w = shadowNearClip;
            float shadowMapID = DisabledShadingShadowId;
            if (hasShading) {
                shadowMapID = hasShadow ? (useLocalSpaceShadows ? -resolvedShadowId - 1f : resolvedShadowId + 1f) : 0f;
                float shadingFade = 1f - shadingStrength;
                if (shadingFade > 0f) shadowMapID += shadowMapID < 0f ? -shadingFade : shadingFade;
            }

            float customDataW = 0f;
            if (hasAreaCookie) {
                float areaCookieMirror = instance.AreaCookieMirror;
                customDataW = Mathf.Abs(areaCookieMirror) >= 0.5f ? areaCookieMirror : 1f;
            }
            if (hasShadow) {
                bool usesCubemapShadow = resolvedShadowId < ShadowCubemapsCount;
                Vector3 shadowBakePosition = instance.ShadowBakePosition;
                // A negative reciprocal range is a v3-only fast-path marker: the baked world-space
                // shadow origin exactly matches the current Point/Spot origin, so the receiver can
                // reuse its raw light vector and distance. Compare components directly; Unity's
                // Vector3 == is approximate and could incorrectly select this exact path.
                bool reuseWorldShadowOrigin = !isArea && !useLocalSpaceShadows && shadowInvDepthRange > 0f && shadowBakePosition.x == pos.x && shadowBakePosition.y == pos.y && shadowBakePosition.z == pos.z;
                // V2 declares CustomID as float3 and ignores W. Keep the full reciprocal range for
                // every v3 Point/Spot shadow; abs(W) is the value and sign(W) is the fast-path marker.
                if (!isArea) customDataW = reuseWorldShadowOrigin ? -shadowInvDepthRange : shadowInvDepthRange;

                float shadowTanAngle = spotOuterTangent;
                // Local single-slice Spot receivers fetch the tangent from otherwise unused ExtraData.Y.
                if (isSpot && !usesCubemapShadow) extraData.y = shadowTanAngle;
                float shadowReprojectionW = usesCubemapShadow ? -shadowInvDepthRange : shadowTanAngle;
                _pointLightShadowReprojectionData[shaderIndex] = new Vector4(shadowBakePosition.x, shadowBakePosition.y, shadowBakePosition.z, shadowReprojectionW);

                Quaternion shadowRotation = useLocalSpaceShadows ? Quaternion.Inverse(instance.transform.rotation) : Quaternion.Inverse(instance.ShadowBakeRotation);
                _pointLightShadowRotationData[shaderIndex] = new Vector4(shadowRotation.x, shadowRotation.y, shadowRotation.z, shadowRotation.w);
            }
            _pointLightCustomId[shaderIndex] = new Vector4(shaderCustomId, shadowMapID, squaredRange, customDataW);
            _pointLightExtraData[shaderIndex] = extraData;

        }

#if UNITY_EDITOR && !COMPILER_UDONSHARP
        // Exposes the exact runtime-packed light data to the editor probe baker without compiling
        // a second copy of the Point/Spot/Area packing math into Udon or the player build.
        public int GetEditorProbeBakePointLightData(Vector4[] positions, Vector4[] colors, Vector4[] extraData, Vector4[] directions, Vector4[] customIds, out int missingProjectionCount, out int overflowCount) {
            missingProjectionCount = 0;
            overflowCount = 0;
            if (Application.isPlaying || !enabled || !gameObject.activeInHierarchy || PointLightVolumeInstances == null || positions == null || colors == null || extraData == null
                || directions == null || customIds == null) return 0;

            int capacity = Mathf.Min(
                positions.Length,
                Mathf.Min(colors.Length, Mathf.Min(extraData.Length, Mathf.Min(directions.Length, customIds.Length))));

            UpdateVolumes();
            int count = 0;
            for (int shaderIndex = 0; shaderIndex < _pointLightCount; shaderIndex++) {
                int sourceIndex = _enabledPointIDs[shaderIndex];
                if (sourceIndex < 0 || sourceIndex >= PointLightVolumeInstances.Length) continue;
                PointLightVolumeInstance instance = PointLightVolumeInstances[sourceIndex];
                if (!IsEditorProbeBakePointLight(instance)) continue;

                int resolvedCustomId = sourceIndex < _pointLightCustomIDs.Length ? _pointLightCustomIDs[sourceIndex] : -1;
                if (instance.ProjectionMode != 0 && resolvedCustomId < 0) {
                    missingProjectionCount++;
                    continue;
                }
                if (count >= capacity) {
                    overflowCount++;
                    continue;
                }

                positions[count] = _pointLightPosition[shaderIndex];
                colors[count] = _pointLightColor[shaderIndex];
                Vector4 packedExtraData = _pointLightExtraData[shaderIndex];
                packedExtraData.w = 0f;
                extraData[count] = packedExtraData;
                directions[count] = _pointLightDirection[shaderIndex];
                Vector4 packedCustomId = _pointLightCustomId[shaderIndex];
                packedCustomId.y = DisabledShadingShadowId;
                if (instance.LightType != 2 || instance.ProjectionMode != 2) packedCustomId.w = 0f;
                customIds[count] = packedCustomId;
                count++;
            }

            // UpdateVolumes caps the compact shader list. Count otherwise eligible registry entries
            // past that limit so the bake reports the same global 128-light constraint explicitly.
            if (_pointLightCount >= MaxPointLightCount) {
                for (int i = 0; i < PointLightVolumeInstances.Length; i++) {
                    PointLightVolumeInstance instance = PointLightVolumeInstances[i];
                    if (!IsEditorProbeBakePointLight(instance)) continue;
                    bool packed = false;
                    for (int j = 0; j < _pointLightCount; j++) {
                        if (_enabledPointIDs[j] != i) continue;
                        packed = true;
                        break;
                    }
                    if (!packed) overflowCount++;
                }
            }
            return count;
        }

        private bool IsEditorProbeBakePointLight(PointLightVolumeInstance instance) {
            return instance != null && instance.LightVolumeManager == this && instance.BakeIntoProbes && instance.isActiveAndEnabled && !instance.CompareTag("EditorOnly")
                && instance.Intensity != 0f && instance.Color != Color.black;
        }
#endif

        // Recalculates all volume data immediately. Automatic runtime paths should call RequestUpdateVolumes instead
        public void UpdateVolumes() {

#if !COMPILER_UDONSHARP && UDONSHARP && UNITY_EDITOR
            if (ShouldSkipEditorProxyRuntimeUpdate()) return;
#endif
            if (_isUpdatingVolumes) return;
            _volumeDataUpdateRequested = false;
            _isUpdatingVolumes = true;
#if !COMPILER_UDONSHARP
            try {
#endif
            SanitizeRegistries();
            TryInitialize();

            if (!enabled || !gameObject.activeInHierarchy) {
                SetDisabledShaderState();
                _updateLightVolumeBuffers = false;
                _updatePointLightBuffers = false;
                _updatePointLightPositionBuffer = false;
                _updateNeedsVolumeRebuild = false;
                _isUpdatingVolumes = false;
                return;
            }

            bool isAtlas = LightVolumeAtlas != null;

#if UNITY_EDITOR && !COMPILER_UDONSHARP
            // Editor tests and inspector edits can change fields directly without going through instance notify methods.
            if (!Application.isPlaying) {
                int editorLightVolumeCount = LightVolumeInstances.Length;
                for (int i = 0; i < editorLightVolumeCount; i++) {
                    LightVolumeInstance instance = LightVolumeInstances[i];
                    if (instance == null) continue;
                    instance.IsActive = instance.isActiveAndEnabled && instance.Intensity != 0 && instance.Color != Color.black;
                }
                int editorPointLightCount = PointLightVolumeInstances.Length;
                for (int i = 0; i < editorPointLightCount; i++) {
                    PointLightVolumeInstance instance = PointLightVolumeInstances[i];
                    if (instance == null) continue;
                    instance.IsActive = instance.isActiveAndEnabled && instance.Intensity != 0 && instance.Color != Color.black;
                }
                if (CaptureEditorCustomSourceState()) _customTexturesInitialized = false;
                if (CaptureEditorShadowSourceState()) _shadowTexturesInitialized = false;
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
                int selectedLightVolumeCount = SelectLightVolumesByWeight();
                for (int additivePass = 0; additivePass < 2 && _enabledCount < selectedLightVolumeCount; additivePass++) {
                    bool isAdditivePass = additivePass == 0;
                    for (int i = 0; i < selectedLightVolumeCount; i++) {
                        int registryIndex = _selectedLightVolumeIDs[i];
                        LightVolumeInstance instance = LightVolumeInstances[registryIndex];
                        if (instance == null) continue;
                        if (!instance.IsActive || instance.IsAdditive != isAdditivePass) continue;
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
                        _enabledIDs[_enabledCount] = registryIndex;
                        if (isAdditivePass) _additiveCount++;
                        WriteLightVolumeShaderData(_enabledCount, instance);
                        _enabledCount++;
                    }
                }
            }
            _lightVolumeArraysDirty = false;

            // Rebuild Point Light Volume shader buffers and dynamic transform cache
            if (_prevLightsBrightnessCutoff != LightsBrightnessCutoff) {
                _prevLightsBrightnessCutoff = LightsBrightnessCutoff;
                _isRangeDirty = true;
            }
            int previousPointLightCount = _pointLightCount;
            _pointLightCount = 0;
            _activeShadowCount = 0;
            _dynamicPointLightVolumeCount = 0;
            int pointLightRegistryCount = PointLightVolumeInstances.Length;
            for (int registryIndex = 0; registryIndex < pointLightRegistryCount && _pointLightCount < MaxPointLightCount; registryIndex++) {
                PointLightVolumeInstance instance = PointLightVolumeInstances[registryIndex];
                if (instance == null) continue;
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
                _enabledPointIDs[_pointLightCount] = registryIndex;
                WritePointLightShaderData(_pointLightCount, registryIndex, instance, true);
                _pointLightCount++;
            }
            if (previousPointLightCount != _pointLightCount) _clusterMaskDirty = true;
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
                VRCShader.SetGlobalVector(_pointLightShadowReceiverParamsID, GetPointLightShadowReceiverParams());
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
            _updatePointLightPositionBuffer = false;
            _updateNeedsVolumeRebuild = false;
            _clusterGeometryUploadPending = false;
            if (AutoUpdateTextures && (HasAutoCustomTextureUpdates || HasAutoShadowTextureUpdates)) ScheduleUpdateProcess();
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
