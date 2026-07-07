using UnityEngine;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.Serialization;

#if UDONSHARP
using VRC.Udon;
#endif

#if UNITY_EDITOR
using Unity.EditorCoroutines.Editor;
using System.IO;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.Linq;
#endif

namespace VRCLightVolumes {
    [ExecuteAlways]
    public class LightVolumeSetup : MonoBehaviour {
        private const float ShadowMinVarianceValueMin = 0.0001f;
        private const float ShadowMinVarianceValueMax = 1.0f;

        [SerializeField] public List<LightVolume> LightVolumes = new List<LightVolume>();
        [SerializeField] public List<float> LightVolumesWeights = new List<float>();

        [SerializeField] public List<PointLightVolume> PointLightVolumes = new List<PointLightVolume>();

        [Header("Point Light Volumes")]
        [Tooltip("Resolution used for point light cookie, LUT and cubemap projection textures.")]
        [FormerlySerializedAs("Resolution")]
        public TextureArrayResolution CookieResolution = TextureArrayResolution._512x512;
        [Tooltip("The minimum brightness at a point due to lighting from a Point Light Volume, before the light is culled. Larger values will result in better performance, but light attenuation will be less physically correct.")]
        [FormerlySerializedAs("LightsBrightnessCutoff")]
        [Range(0.05f, 1f)] public float BrightnessCutoff = 0.35f;
        [Tooltip("Resolution used for per-light shadow maps. Resolution represents a single cubemap side, so it's actually x6 for each light with shadow.")]
        public TextureArrayResolution ShadowResolution = TextureArrayResolution._256x256;
        [HideInInspector]
        public ShadowTexturePrecision ShadowTextureFormat = ShadowTexturePrecision.Float;
        [Tooltip("Reduces EVSM light bleeding at the cost of shadow penumbra collapse. Increase per-light Blur to compensate if shadows become too thin.")]
        [Range(0f, 1f)] public float ShadowBleedReduction = 0.2f;
        [Tooltip("Logarithmic EVSM variance bias slider used for PC builds. The receiver shader scales this by warped depth, matching the EVSM derivative. Higher values reduce edge noise, but can detach contact shadows.")]
        [Range(0f, 1f)] public float ShadowMinVariance = 0f;
        [Tooltip("Logarithmic EVSM variance bias slider used for Android and iOS builds. Higher values reduce Half precision edge noise on Quest and Mobile, but can detach contact shadows.")]
        [Range(0f, 1f)] public float ShadowMinVarianceMobile = 1f;

        [Header("Baking")]
        [Tooltip("Bakery usually gives better results and works faster.")]
#if BAKERY_INCLUDED
        public Baking BakingMode = Baking.Bakery;
#else
        public Baking BakingMode = Baking.Progressive;
#endif
        [Tooltip("Light from Bakery light sources with this bitmask will affect Light Volumes.")]
        [FormerlySerializedAs("BakeryBitmask")]
        public int VolumeBitmask = 1;
        [Tooltip("Light from Bakery light sources with this bitmask will affect light probes.")]
        public int ProbeBitmask = 1;
        [Tooltip("Removes baked noise in Light Volumes but may slightly reduce sharpness. Recommended to keep it enabled.")]
        public bool Denoise = true;
        [Tooltip("Whether to dilate valid probe data into invalid probes, such as probes that are inside geometry. Helps mitigate light leaking.")]
        public bool DilateInvalidProbes = true;
        [Tooltip("How many iterations to run dilation for. Higher values will result in less leaking, but will also cause longer bakes.")]
        [Range(1, 8)]
        public int DilationIterations = 1;
        [Tooltip("The percentage of rays shot from a probe that should hit backfaces before the probe is considered invalid for the purpose of dilation. 0 means every probe is invalid, 1 means every probe is valid.")] 
        [Range(0, 1)]
        public float DilationBackfaceBias = 0.1f;
        [Tooltip("Probes deringing. Automatically fixes Bakery's \"burned\" light probes after a scene bake. But decreases their contrast slightly.")]
        public bool FixLightProbesL1 = true;
        [Tooltip("Downscales each light volume. Useful to make a lower atlas resolution for mobile platforms or to increase overall sharpness and decrease aliasing.")]
        public Downscale DownscaleVolumes = Downscale.None;
        [Header("Visuals")]
        [Tooltip("When enabled, areas outside Light Volumes fall back to light probes. Otherwise, the Light Volume with the smallest weight is used as fallback. It also improves performance.")]
        public bool LightProbesBlending = true;
        [Tooltip("Disables smooth blending with areas outside Light Volumes. Use it if your entire scene's play area is covered by Light Volumes. It also improves performance.")]
        public bool SharpBounds = true;
        [Tooltip("Automatically updates most volume properties at runtime. Enabling/disabling, Color and Intensity update automatically even without this option enabled. Position, Rotation and Scale get updated only for volumes that are marked dynamic. It's more performant to keep it off.")]
        public bool AutoUpdateVolumes = true;
        [Tooltip("Automatically updates dynamic point light cookie and shadow texture sources at runtime. It's more performant to keep it off.")]
        public bool AutoUpdateTextures = true;
        [Tooltip("Limits the maximum number of additive volumes and Point Light Volumes that can affect a single pixel. This also limits individual Point Light Volume speculars in modern compatible shaders. Lower values improve worst-case performance in overlap-heavy areas.")]
        [Min(1)]public int AdditiveMaxOverdraw = 4;
        [Tooltip("Disables min/max brightness limits for modern avatar shaders such as lilToon or Poiyomi. This feature prevents avatars from standing out from the scene due to their brightness. Check this only if you're sure your scene lighting is properly configured.")]
        public bool ForceSceneLighting = false;
        [Header("Debug")]
        [Tooltip("Removes all Light Volume scripts in play mode, except Udon components. Useful for testing in a clean setup, just like in VRChat. For example, Auto Update Volumes and Dynamic Light Volumes will work just like in VRChat.")]
        public bool DestroyInPlayMode = false;

        [SerializeField] public List<LightVolumeData> LightVolumeDataList = new List<LightVolumeData>();

        [Serializable]
        public struct PostProcessor {
            public RenderTexture RT;
            public Material Mat;
            public string TextureName;
            public Action Update;
            // Optional callback used by processors that need the previous texture without a material pass
            public Action<Texture> UpdateWithInput;
        }

        // Render textures applied top to bottom to the Light Volume Atlas at runtime
        // External scripts can register themselves here using `RegisterPostProcessorCRT` or `RegisterPostProcessor`
        // This field usually should not be edited manually
        public PostProcessor[] AtlasPostProcessors;

        public bool IsBakeryMode => BakingMode == Baking.Bakery; // Just a shortcut
        public LightVolumeManager LightVolumeManager;

        // Disables syncing with udon script to make it possible to destroy the manager and the other volumes and don't break the udon script
        private bool _dontSync = true;
        public bool DontSync {
            get { return Application.isPlaying ? _dontSync : false; }
            set { _dontSync = value; }
        }

#if UDONSHARP
        // UdonBehaviour is a real udon VM script. We need it to change public variables in play mode
        private UdonBehaviour _lightVolumeManagerBehaviour = null;
#endif

        public Baking _bakingModePrev;

#if UNITY_EDITOR
        private TextureArrayResolution _resolutionPrev = TextureArrayResolution._128x128;
        private TextureArrayResolution _shadowResolutionPrev = TextureArrayResolution._64x64;
        private ShadowTexturePrecision _shadowTextureFormatPrev = ShadowTexturePrecision.Float;
        private EditorCoroutine _generateAtlasCoroutine = null;
        private static bool _postUndoGlobalSyncQueued = false;
        private bool _postUndoSyncQueued = false;
        private bool _postUndoReinitializePointLightTextures = false;
        private const string CubemapFaceShaderName = "Hidden/CubeFace";
#endif
        public void RefreshVolumesList() {

#if UNITY_EDITOR
            if (Undo.isProcessing) {
                QueuePostUndoSync(false);
                return;
            }
#endif

            if(DontSync) return;

            int setupCount = 0;
            var setups = FindObjectsOfType<LightVolumeSetup>(true);
            for (int i = 0; i < setups.Length; i++) {
                if (setups[i] == null || setups[i].CompareTag("EditorOnly")) continue;
                setupCount++;
            }
            bool canAdoptUnassignedVolumes = setupCount <= 1;

            // Searching for all light volumes in the scene
            var volumes = FindObjectsOfType<LightVolume>(true);
            for (int i = 0; i < volumes.Length; i++) {
                if (volumes[i].CompareTag("EditorOnly")) continue;
                if (volumes[i].LightVolumeSetup == null) {
                    if (!canAdoptUnassignedVolumes) continue;
                    volumes[i].LightVolumeSetup = this;
                }
                if (volumes[i].LightVolumeSetup != this) continue;
                if (!LightVolumes.Contains(volumes[i])) {
                    LightVolumes.Add(volumes[i]);
                    LightVolumesWeights.Add(0.0f);
                }
            }
            // Removing volumes that no more exists
            for (int i = 0; i < LightVolumes.Count; i++) {
                if (LightVolumes[i] == null || LightVolumes[i].CompareTag("EditorOnly") || LightVolumes[i].LightVolumeSetup != this) {
                    LightVolumes.RemoveAt(i);
                    LightVolumesWeights.RemoveAt(i);
                    i--;
                }
            }

            // Searching for all point light volumes in the scene
            var pointVolumes = FindObjectsOfType<PointLightVolume>(true);
            for (int i = 0; i < pointVolumes.Length; i++) {
                if (pointVolumes[i].CompareTag("EditorOnly")) continue;
                if (pointVolumes[i].LightVolumeSetup == null) {
                    if (!canAdoptUnassignedVolumes) continue;
                    pointVolumes[i].LightVolumeSetup = this;
                }
                if (pointVolumes[i].LightVolumeSetup != this) continue;
                if (!PointLightVolumes.Contains(pointVolumes[i])) {
                    PointLightVolumes.Add(pointVolumes[i]);
                }
            }
            // Removing point light volumes that no more exists
            for (int i = 0; i < PointLightVolumes.Count; i++) {
                if (PointLightVolumes[i] == null || PointLightVolumes[i].CompareTag("EditorOnly") || PointLightVolumes[i].LightVolumeSetup != this) {
                    PointLightVolumes.RemoveAt(i);
                    i--;
                }
            }
            SyncUdonScript();
        }

#if UNITY_EDITOR

#if BAKERY_INCLUDED
        private bool _subscribedToBakery = false;
        private bool _bakeryBitmaskOverridePrepared = false;
        private bool _bakeryBitmaskOverridePending = false;
        private static readonly System.Reflection.FieldInfo _bakeryLightProbeGroupField = typeof(ftBuildGraphics).GetField("lightProbeLMGroup", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        private static readonly System.Reflection.FieldInfo _bakeryVolumeGroupField = typeof(ftBuildGraphics).GetField("volumeLMGroup", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
#endif
        private bool _subscribedToUnityLightmapper = false;
        private bool _unityLightProbePostProcessApplied = false;

        private void OnSelectionChanged() {
            if (Selection.activeObject == gameObject) {
                RefreshVolumesList();
            }
        }

        // Registers global Undo/Redo sync because restored objects may not have valid setup references during lifecycle callbacks.
        [InitializeOnLoadMethod]
        private static void InitializeUndoRedoSync() {
            Undo.undoRedoPerformed -= QueueGlobalPostUndoSync;
            Undo.undoRedoPerformed += QueueGlobalPostUndoSync;
        }

        // Queues all setup synchronization until Unity has fully restored destroyed scene objects.
        private static void QueueGlobalPostUndoSync() {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (_postUndoGlobalSyncQueued) return;
            _postUndoGlobalSyncQueued = true;
            EditorApplication.delayCall += SyncSceneSetupsAfterUndo;
        }

        // Rebuilds all scene setup registries from restored authoring components.
        private static void SyncSceneSetupsAfterUndo() {
            if (EditorApplication.isPlayingOrWillChangePlaymode) {
                _postUndoGlobalSyncQueued = false;
                return;
            }
            if (Undo.isProcessing) {
                EditorApplication.delayCall += SyncSceneSetupsAfterUndo;
                return;
            }
            _postUndoGlobalSyncQueued = false;

            LightVolumeSetup[] setups = Resources.FindObjectsOfTypeAll<LightVolumeSetup>();
            for (int i = 0; i < setups.Length; i++) {
                LightVolumeSetup setup = setups[i];
                if (setup == null || setup.gameObject == null) continue;
                if (!setup.gameObject.scene.IsValid() || !setup.gameObject.scene.isLoaded) continue;
                if (setup.CompareTag("EditorOnly")) continue;
                setup.RefreshVolumesList();
            }

            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }

        // Queues setup synchronization until Unity finishes Undo and Transform access is valid again.
        public void QueuePostUndoSync(bool reinitializePointLightTextures) {
            _postUndoReinitializePointLightTextures |= reinitializePointLightTextures;
            if (_postUndoSyncQueued) return;
            _postUndoSyncQueued = true;
            EditorApplication.delayCall += ApplyPostUndoSync;
        }

        // Applies deferred setup synchronization requested by component lifecycle callbacks during Undo.
        private void ApplyPostUndoSync() {
            if (this == null) return;
            if (Undo.isProcessing) {
                EditorApplication.delayCall += ApplyPostUndoSync;
                return;
            }

            bool reinitializePointLightTextures = _postUndoReinitializePointLightTextures;
            _postUndoSyncQueued = false;
            _postUndoReinitializePointLightTextures = false;

            RefreshVolumesList();
            if (reinitializePointLightTextures) {
                ReinitializeCustomTextures();
                ReinitializeShadowTextures();
            }
        }

        // Returns the shadow texture precision required by the active Unity build target.
        private static ShadowTexturePrecision GetAutomaticShadowTextureFormat() {
            return IsMobileBuildTarget() ? ShadowTexturePrecision.Half : ShadowTexturePrecision.Float;
        }

        // Updates the hidden serialized shadow texture format from the active build target.
        private bool ApplyAutomaticShadowTextureFormat() {
            ShadowTexturePrecision textureFormat = GetAutomaticShadowTextureFormat();
            if (ShadowTextureFormat == textureFormat) return false;
            ShadowTextureFormat = textureFormat;
            EditorUtility.SetDirty(this);
            return true;
        }

        // Rebuilds shadow data after the build target changes the required texture format.
        private void RebuildShadowsAfterTextureFormatChange() {
            _shadowTextureFormatPrev = ShadowTextureFormat;
            if (!Application.isPlaying) {
                bool rebaked = BakeShadowMaps(true);
                if (!rebaked) ReinitializeShadowTextures();
            } else {
                ReinitializeShadowTextures();
            }
        }

        // Applies automatic shadow precision immediately when Unity or the VRC SDK switches build target.
        private void OnActiveBuildTargetChanged() {
            if (!ApplyAutomaticShadowTextureFormat()) return;
            RebuildShadowsAfterTextureFormatChange();
            if (CanSyncFromLifecycle()) SyncUdonScript();
        }

        // Applies build-target dependent settings to every active setup in loaded scenes.
        internal static void HandleActiveBuildTargetChanged() {
            LightVolumeSetup[] setups = UnityEngine.Object.FindObjectsByType<LightVolumeSetup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < setups.Length; i++) {
                LightVolumeSetup setup = setups[i];
                if (setup == null || !setup.isActiveAndEnabled) continue;
                setup.OnActiveBuildTargetChanged();
            }
        }

        // Rebuilds manager-owned cookie source caches and the runtime RenderTexture texture array
        public void ReinitializeCustomTextures() {
            ReinitializePointLightTextureArray(true);
        }

        // Creates or returns the manager-owned material used to unwrap cubemap faces at runtime
        private Material GetCubemapFaceMaterial() {
            if (LightVolumeManager == null) return null;
            Material material = LightVolumeManager.CubemapFaceMaterial;
            if (material != null && !AssetDatabase.Contains(material) && material.shader != null && material.shader.name == CubemapFaceShaderName) return material;

            Shader shader = Shader.Find(CubemapFaceShaderName);
            if (shader == null) return null;

            material = new Material(shader) {
                name = LightVolumeManager.name + "_CubemapFaceRuntime",
                hideFlags = HideFlags.HideAndDontSave
            };
            LightVolumeManager.CubemapFaceMaterial = material;
            return material;
        }

        // Rebuilds manager-owned shadow source caches and the runtime RenderTexture texture array
        public void ReinitializeShadowTextures() {
            ReinitializePointLightTextureArray(false);
        }

        // Rebuilds one of the manager-owned point light texture arrays and keeps Udon proxies in sync
        private void ReinitializePointLightTextureArray(bool customTextures) {
#if UNITY_EDITOR
            if (Undo.isProcessing) {
                QueuePostUndoSync(true);
                return;
            }
#endif
            SetupDependencies();

            if (LightVolumeManager == null || DontSync) return;

#if UNITY_EDITOR
            if (customTextures) PrepareCustomProjectionTextureImports();
#endif

            if (customTextures) {
                LightVolumeManager.CustomTexturesWidth = (int)CookieResolution;
                LightVolumeManager.CustomTexturesHeight = (int)CookieResolution;
            } else {
                LightVolumeManager.ShadowTexturesWidth = (int)ShadowResolution;
                LightVolumeManager.ShadowTexturesHeight = (int)ShadowResolution;
                LightVolumeManager.ShadowTextureFormat = GetShadowTextureFormatValue();
                LightVolumeManager.ShadowBleedReduction = ShadowBleedReduction;
                LightVolumeManager.ShadowMinVariance = GetShadowMinVarianceValue();
            }
            LightVolumeManager.CubemapFaceMaterial = GetCubemapFaceMaterial();

            bool notifyPointLightManager = !Application.isPlaying;
            for (int i = 0; i < PointLightVolumes.Count; i++) {
                if (PointLightVolumes[i] != null) PointLightVolumes[i].SyncUdonScript(true, notifyPointLightManager);
            }

#if UDONSHARP
            if (customTextures) SyncCookieTextureMetadataToUdon();
            else SyncShadowTextureMetadataToUdon();
            if (Application.isPlaying && _lightVolumeManagerBehaviour != null) {
                var instances = GetPointLightVolumeInstances();
#if UNITY_EDITOR
                LightVolumeManager.PointLightVolumeInstances = instances;
#endif
                UdonBehaviour[] pointLightVolumeInstances = new UdonBehaviour[instances.Length];
                for (int i = 0; i < instances.Length; i++) {
                    pointLightVolumeInstances[i] = instances[i].GetComponent<UdonBehaviour>();
                }
                _lightVolumeManagerBehaviour.SetProgramVariable("PointLightVolumeInstances", pointLightVolumeInstances);
                _lightVolumeManagerBehaviour.SendCustomEvent(customTextures ? "ReinitializeCustomTextures" : "ReinitializeShadowTextures");
                _lightVolumeManagerBehaviour.SendCustomEvent("UpdateVolumes");
#if UNITY_EDITOR
                if (customTextures) LightVolumeManager.ReinitializeCustomTextures();
                else LightVolumeManager.ReinitializeShadowTextures();
                LightVolumeManager.UpdateVolumes();
#endif
                return;
            }
#endif
            LightVolumeManager.PointLightVolumeInstances = GetPointLightVolumeInstances();
            if (customTextures) LightVolumeManager.ReinitializeCustomTextures();
            else LightVolumeManager.ReinitializeShadowTextures();
        }

#if UNITY_EDITOR
        // Fixes EXR projection import settings before Android target data is copied into runtime texture arrays.
        public void PrepareCustomProjectionTextureImports() {
            if (Application.isPlaying) return;
            if (PointLightVolumes == null) return;
            for (int i = 0; i < PointLightVolumes.Count; i++) {
                PointLightVolume pointLightVolume = PointLightVolumes[i];
                if (pointLightVolume == null) continue;
                LVUtils.TextureSetLinearHDRAndroidImport(pointLightVolume.GetCustomTexture());
            }
        }
#endif

        // Subscribing to OnBaked events
        private void OnEnable() {
#if BAKERY_INCLUDED
            if (!Application.isPlaying && !_subscribedToBakery) {
                ftRenderLightmap.OnFinishedFullRender += OnBakeryFinishedRender;
                EditorApplication.update += WatchBakeryBitmaskOverride;
                _subscribedToBakery = true;
            }
#endif
            if (!Application.isPlaying && !_subscribedToUnityLightmapper) {
                UnityEditor.Experimental.Lightmapping.additionalBakedProbesCompleted += OnAdditionalProbesCompleted;
                Lightmapping.bakeStarted += OnUnityBakingStarted;
                Lightmapping.bakeCompleted += OnUnityBakingCompleted;
                _subscribedToUnityLightmapper = true;
            }

            Selection.selectionChanged += OnSelectionChanged;
#if UNITY_EDITOR
            bool shadowTextureFormatChanged = ApplyAutomaticShadowTextureFormat();
            _resolutionPrev = CookieResolution;
            _shadowResolutionPrev = ShadowResolution;
            _shadowTextureFormatPrev = ShadowTextureFormat;
            if (shadowTextureFormatChanged) RebuildShadowsAfterTextureFormatChange();
#endif
            SyncUdonScript();
        }

        // Unsubscribing from OnBaked events
        private void OnDisable() {
#if BAKERY_INCLUDED
            if (!Application.isPlaying && _subscribedToBakery) {
                ftRenderLightmap.OnFinishedFullRender -= OnBakeryFinishedRender;
                EditorApplication.update -= WatchBakeryBitmaskOverride;
                _subscribedToBakery = false;
            }
#endif
            if (!Application.isPlaying && _subscribedToUnityLightmapper) {
                UnityEditor.Experimental.Lightmapping.additionalBakedProbesCompleted -= OnAdditionalProbesCompleted;
                Lightmapping.bakeStarted -= OnUnityBakingStarted;
                Lightmapping.bakeCompleted -= OnUnityBakingCompleted;
                _subscribedToUnityLightmapper = false;

            }

            Selection.selectionChanged -= OnSelectionChanged;
            if (CanSyncFromLifecycle()) SyncUdonScript();
        }

        private void Awake() {
            if (CanSyncFromLifecycle()) SyncUdonScript();
        }

        private void OnValidate() {
#if UNITY_EDITOR
            ApplyAutomaticShadowTextureFormat();
#endif
            if (CanSyncFromLifecycle()) SyncUdonScript();
        }

        // Avoids adding manager components from Unity lifecycle validation callbacks.
        private bool CanSyncFromLifecycle() {
#if UNITY_EDITOR
            if (!Application.isPlaying && LightVolumeManager == null && !TryGetComponent(out LightVolumeManager)) return false;
#endif
            return true;
        }

#if BAKERY_INCLUDED

        // Prepares Bakery probe groups and marks the bitmask override as needed for the current bake.
        private void PrepareBakeryBitmaskOverride() {
            if (_bakeryBitmaskOverridePrepared) return;

            _bakeryBitmaskOverridePrepared = true;
            _bakeryBitmaskOverridePending = false;

            if (!IsBakeryMode) return;

            var volumes = FindObjectsOfType<LightVolume>(true);
            for (int i = 0; i < volumes.Length; i++) {
                volumes[i].SetupDependencies();
                if (volumes[i].LightVolumeSetup != this) continue;

                // Attempt to fix a bakery bug
                volumes[i].SetupBakeryDependencies();
            }

            ApplyBakeryBitmaskToStoredGroups();
            _bakeryLightProbeGroupField?.SetValue(null, null);
            _bakeryVolumeGroupField?.SetValue(null, null);
            _bakeryBitmaskOverridePending = true;
        }

        // On Bakery Finished baking
        private void OnBakeryFinishedRender(object sender, EventArgs e) {
            _bakeryBitmaskOverridePrepared = false;
            _bakeryBitmaskOverridePending = false;

            LightVolume[] volumes = FindObjectsByType<LightVolume>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < volumes.Length; i++) {
                if (volumes[i].Bake && volumes[i].LightVolumeInstance != null) {
                    volumes[i].RecalculateProbesPositions();
                    volumes[i].LightVolumeInstance.InvBakedRotation = Quaternion.Inverse(volumes[i].GetRotation());
                    if (IsBakeryMode && volumes[i].BakeryVolume != null) {
                        volumes[i].Texture0 = volumes[i].BakeryVolume.bakedTexture0;
                        volumes[i].Texture1 = volumes[i].BakeryVolume.bakedTexture1;
                        volumes[i].Texture2 = volumes[i].BakeryVolume.bakedTexture2;
                    }
                }
            }
            PostProcessLightProbes(FixLightProbesL1);
            BakeShadowMaps();
            GenerateAtlas();
            Debug.Log($"[LightVolumeSetup] Generating 3D Atlas finished!");
        }

        // Watches Bakery bakes started from paths that don't invoke OnPreFullRender.
        private void WatchBakeryBitmaskOverride() {
            if (!ftRenderLightmap.bakeInProgress) {
                _bakeryBitmaskOverridePrepared = false;
                _bakeryBitmaskOverridePending = false;
                return;
            }

            PrepareBakeryBitmaskOverride();
            if (!_bakeryBitmaskOverridePending) return;

            var lightProbeGroup = _bakeryLightProbeGroupField?.GetValue(null) as BakeryLightmapGroup;
            var volumeGroup = _bakeryVolumeGroupField?.GetValue(null) as BakeryLightmapGroup;
            if (lightProbeGroup == null && volumeGroup == null) return;

            if (lightProbeGroup != null) lightProbeGroup.bitmask = ProbeBitmask;
            if (volumeGroup != null) volumeGroup.bitmask = VolumeBitmask;
            ApplyBakeryBitmaskToStoredGroups();
            _bakeryBitmaskOverridePending = false;
        }

        // Applies the configured bitmask to Bakery's stored implicit probe groups.
        private void ApplyBakeryBitmaskToStoredGroups() {
            for (int i = 0; i < SceneManager.sceneCount; i++) {
                Scene scene = EditorSceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                GameObject storageObject = ftLightmaps.FindInScene("!ftraceLightmaps", scene);
                if (storageObject == null) continue;

                ftLightmapsStorage storage = storageObject.GetComponent<ftLightmapsStorage>();
                if (storage == null || storage.implicitGroups == null) continue;

                for (int j = 0; j < storage.implicitGroups.Count; j++) {
                    BakeryLightmapGroup group = storage.implicitGroups[j] as BakeryLightmapGroup;
                    if (group != null && group.isImplicit && group.probes) group.bitmask = group.name == "volumes" ? VolumeBitmask : ProbeBitmask;
                }
            }
        }

#endif

        // On Unity Lightmapper started baking
        private void OnUnityBakingStarted() {
            _unityLightProbePostProcessApplied = false;
            if (BakingMode == Baking.Bakery) {
                return;
            }
            LightVolume[] volumes = FindObjectsByType<LightVolume>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < volumes.Length; i++) {
                if (volumes[i].Bake) {
                    Debug.Log($"[LightVolumeSetup] Adding additional probes to bake with Light Volume \"{volumes[i].gameObject.name}\" using Unity Lightmapper. Group {i}");
                    volumes[i].SetAdditionalProbes(i);
                }
            }
        }

        // On Unity Lightmapper finished baking
        private void OnUnityBakingCompleted() {
            if (BakingMode == Baking.Bakery || _unityLightProbePostProcessApplied) return;
            _unityLightProbePostProcessApplied = PostProcessLightProbes(false);
        }

        // On Unity Lightmapper baked additional probes
        private void OnAdditionalProbesCompleted() {

            if (BakingMode == Baking.Bakery) {
                return;
            }
            LightVolume[] volumes = FindObjectsByType<LightVolume>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < volumes.Length; i++) {
                if (volumes[i].Bake) {
                    volumes[i].Save3DTexturesProgressive(i);
                    volumes[i].RemoveAdditionalProbes(i);
                    if (volumes[i].LightVolumeInstance != null) volumes[i].LightVolumeInstance.InvBakedRotation = Quaternion.Inverse(volumes[i].GetRotation());
                }
            }
            Debug.Log($"[LightVolumeSetup] Additional probes baking finished! Generating 3D Atlas...");
            BakeShadowMaps();
            GenerateAtlas();
            Debug.Log($"[LightVolumeSetup] Generating 3D Atlas finished!");

        }

        private void Update() {
            if (DontSync) return;
            SetupDependencies();
#if UNITY_EDITOR
            ApplyAutomaticShadowTextureFormat();
#endif
            // Resetup required game objects and components for light volumes in new baking mode
            if (_bakingModePrev != BakingMode) {
                _bakingModePrev = BakingMode;
                var volumes = FindObjectsOfType<LightVolume>();
                for (int i = 0; i < volumes.Length; i++) {
                    volumes[i].SetupBakeryDependencies();
                }
                SyncUdonScript();
            }
            if (_resolutionPrev != CookieResolution) {
                _resolutionPrev = CookieResolution;
                ReinitializeCustomTextures();
            }
            bool shadowResolutionChanged = _shadowResolutionPrev != ShadowResolution;
            bool shadowTextureFormatChanged = _shadowTextureFormatPrev != ShadowTextureFormat;
            if (shadowResolutionChanged || shadowTextureFormatChanged) {
                _shadowResolutionPrev = ShadowResolution;
                _shadowTextureFormatPrev = ShadowTextureFormat;
                if (shadowTextureFormatChanged && !Application.isPlaying) {
                    bool rebaked = BakeShadowMaps(true);
                    if (!rebaked) ReinitializeShadowTextures();
                } else {
                    ReinitializeShadowTextures();
                }
            }
            if (!Application.isPlaying && LightVolumeManager != null) {
                LightVolumeManager.UpdateVolumes();
                if (LightVolumeManager.AutoUpdateTextures && (LightVolumeManager.HasAutoCustomTextureUpdates || LightVolumeManager.HasAutoShadowTextureUpdates)) {
                    LightVolumeManager.UpdateAutoCustomTextures();
                    LightVolumeManager.UpdateAutoShadowTextures();
                    EditorApplication.QueuePlayerLoopUpdate();
                    SceneView.RepaintAll();
                }
            }
        }

        // Generates atlas and setups udon script
        public void GenerateAtlas() {

            if (LVUtils.IsInPrefabAsset(this) || LightVolumes.Count == 0 || DontSync) return;

            SetupDependencies();

            if(_generateAtlasCoroutine != null) { // Stop old coroutine in case one is in process already
                EditorCoroutineUtility.StopCoroutine(_generateAtlasCoroutine);
                _generateAtlasCoroutine = null;
            }

            // @pimaker: If there are post processors, the 3D texture will run through a Custom Render Texture every frame
            // Unity dispatches CRT renders on 3D textures in slices by depth, so we want to reduce the z axis of the atlas
            // as much as possible to reduce per-frame drawcalls - even at the cost of slightly higher VRAM efficiency
            var packingStrategy = AtlasPostProcessors != null && AtlasPostProcessors.Length > 0 ? TexturePackingStrategy.MinimumDepth : TexturePackingStrategy.MinimumVRAM;

            _generateAtlasCoroutine = EditorCoroutineUtility.StartCoroutine(Texture3DAtlasGenerator.CreateAtlas(LightVolumes.ToArray(), (Atlas3D atlas) => {

                if (atlas.Texture == null || DontSync) return; // Return if atlas packing failed

                LightVolumeManager.LightVolumeAtlasBase = atlas.Texture;
                UpdateAtlasPostProcessors();

                LightVolumeDataList.Clear();

                int lvCount = (int)Mathf.Min(LightVolumes.Count, Mathf.Min(Mathf.Floor(atlas.BoundsUvwMax.Length / 3), Mathf.Floor(atlas.BoundsUvwMin.Length / 3)));
                for (int i = 0; i < lvCount; i++) {

                    if (LightVolumes[i] == null) continue;
                    var lightVolumeInstance = LightVolumes[i].LightVolumeInstance;

                    if (lightVolumeInstance == null) continue;
                    if (!LightVolumes[i].Bake && LightVolumes[i].ReserveUVSpace) lightVolumeInstance.InvBakedRotation = Quaternion.Inverse(LightVolumes[i].GetRotation());

                    int atlasIndex = i * 3;
                    Vector3 scale = atlas.BoundsUvwMax[atlasIndex] - atlas.BoundsUvwMin[atlasIndex];
                    Vector3 uvwMin0 = atlas.BoundsUvwMin[atlasIndex];
                    Vector3 uvwMin1 = atlas.BoundsUvwMin[atlasIndex + 1];
                    Vector3 uvwMin2 = atlas.BoundsUvwMin[atlasIndex + 2];
#if UDONSHARP
                    if (Application.isPlaying) {

                        UdonBehaviour lightVolumeBehaviour = lightVolumeInstance.GetComponent<UdonBehaviour>();

                        lightVolumeBehaviour.SetProgramVariable("BoundsUvwMin0", new Vector4(uvwMin0.x, uvwMin0.y, uvwMin0.z, scale.x));
                        lightVolumeBehaviour.SetProgramVariable("BoundsUvwMin1", new Vector4(uvwMin1.x, uvwMin1.y, uvwMin1.z, scale.y));
                        lightVolumeBehaviour.SetProgramVariable("BoundsUvwMin2", new Vector4(uvwMin2.x, uvwMin2.y, uvwMin2.z, scale.z));

                    } else {
#endif
                        lightVolumeInstance.BoundsUvwMin0 = new Vector4(uvwMin0.x, uvwMin0.y, uvwMin0.z, scale.x);
                        lightVolumeInstance.BoundsUvwMin1 = new Vector4(uvwMin1.x, uvwMin1.y, uvwMin1.z, scale.y);
                        lightVolumeInstance.BoundsUvwMin2 = new Vector4(uvwMin2.x, uvwMin2.y, uvwMin2.z, scale.z);
#if UDONSHARP
                    }
#endif
                    LightVolumeDataList.Add(new LightVolumeData(i < LightVolumesWeights.Count ? LightVolumesWeights[i] : 0, lightVolumeInstance));

                    LVUtils.MarkDirty(lightVolumeInstance);
                }

                LVUtils.SaveAsAssetDelayed(atlas.Texture, $"{Path.GetDirectoryName(SceneManager.GetActiveScene().path)}/{SceneManager.GetActiveScene().name}/VRCLightVolumes/LightVolumeAtlas.asset");

                SyncUdonScript();

                _generateAtlasCoroutine = null;

            }, (int)DownscaleVolumes, packingStrategy), this);

        }

        // Looks for LightVolumeManager udon script and setups it if needed
        public void SetupDependencies() {
            if (this == null || gameObject == null || DontSync) return;
            if (LightVolumeManager == null && !TryGetComponent(out LightVolumeManager)) {
                LightVolumeManager = gameObject.AddComponent<LightVolumeManager>();
            }
#if !COMPILER_UDONSHARP
            if (LightVolumeManager != null) LightVolumeManager.EnsureRuntimeShadowCamera();
#endif
#if UDONSHARP
            if (_lightVolumeManagerBehaviour == null) {
                TryGetComponent(out _lightVolumeManagerBehaviour);
            }
#endif
        }

        private const int MaxProbeBakedPointLightCount = 128;
        private const int ProbeBakeThreadGroupSize = 64;
        private const float DisabledProbeBakeShadowId = 10000f;
        private const string ProbeBakeComputePath = "Packages/red.sim.lightvolumes/Scripts/Editor/PointLightProbeBake.compute";
        private const string ProbeBakeKernelName = "BakePointLightVolumesIntoProbes";
        private static Texture3D _probeBakeDummyVolumeTexture = null;
        private static Texture2DArray _probeBakeDummyTextureArray = null;

        // Applies all Light Volume light probe post processing steps
        private bool PostProcessLightProbes(bool dering) {
            bool bakePointLights = HasProbeBakedPointLightVolumes();
            if (!dering && !bakePointLights) return false;

            var probes = LightmapSettings.lightProbes;
            if (probes == null || probes.count == 0) {
                Debug.LogWarning("[LightVolumeSetup] No Light Probes found to postprocess.");
                return false;
            }

            var shs = probes.bakedProbes;
            if (shs == null || shs.Length == 0) return false;
            bool didDering = dering && !LVUtils.CheckSHL2(shs[0]);
            if (dering && !didDering) {
                Debug.Log("[LightVolumeSetup] L2 Light Probes detected - no need to apply L1 Bakery fix.");
            }

            if (didDering)
                for (int i = 0; i < shs.Length; ++i)
                    shs[i] = LVUtils.DeringSH(shs[i]);

            int bakedPointLightCount = bakePointLights ? BakePointLightVolumesIntoProbesGPU(shs, probes.positions) : 0;
            if (!didDering && bakedPointLightCount == 0) return false;

            probes.bakedProbes = shs;
            EditorUtility.SetDirty(probes);
            EditorSceneManager.MarkAllScenesDirty();

            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveOpenScenes();

            string fixLog = didDering ? $"{shs.Length} Light Probes fixed" : "";
            string pointLightLog = bakedPointLightCount > 0 ? $"{bakedPointLightCount} Point Light Volumes baked into Light Probes" : "";
            Debug.Log($"[LightVolumeSetup] {fixLog}{(didDering && bakedPointLightCount > 0 ? ", " : "")}{pointLightLog}!");
            return true;

        }

        // Checks if any Point Light Volume should be added to baked light probes
        private bool HasProbeBakedPointLightVolumes() {
            for (int i = 0; i < PointLightVolumes.Count; i++)
                if (IsProbeBakedPointLightVolume(PointLightVolumes[i]))
                    return true;
            return false;
        }

        // Checks if one Point Light Volume is valid for probe baking
        private static bool IsProbeBakedPointLightVolume(PointLightVolume pointLightVolume) {
            return pointLightVolume != null && pointLightVolume.BakeIntoProbes && pointLightVolume.isActiveAndEnabled && !pointLightVolume.CompareTag("EditorOnly") && pointLightVolume.Intensity != 0 && pointLightVolume.Color != Color.black;
        }

        // Dispatches the probe bake compute shader and writes the result back into baked probes
        private int BakePointLightVolumesIntoProbesGPU(UnityEngine.Rendering.SphericalHarmonicsL2[] shs, Vector3[] probePositions) {
            if (shs == null || probePositions == null || shs.Length == 0 || probePositions.Length == 0) return 0;
            if (!SystemInfo.supportsComputeShaders) {
                Debug.LogError("[LightVolumeSetup] Compute shaders are not supported on this editor graphics device. Point Light Volumes were not baked into Light Probes.");
                return 0;
            }

            Vector4[] pointPositions = new Vector4[MaxProbeBakedPointLightCount];
            Vector4[] pointColors = new Vector4[MaxProbeBakedPointLightCount];
            Vector4[] pointExtraData = new Vector4[MaxProbeBakedPointLightCount];
            Vector4[] pointDirections = new Vector4[MaxProbeBakedPointLightCount];
            Vector4[] pointCustomIds = new Vector4[MaxProbeBakedPointLightCount];
            Texture customTextureArray = GetProbeBakeCustomTextureArray();
            int pointLightCount = BuildProbeBakePointLightData(pointPositions, pointColors, pointExtraData, pointDirections, pointCustomIds, customTextureArray != null);
            if (pointLightCount == 0) return 0;

            ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ProbeBakeComputePath);
            if (compute == null) {
                Debug.LogError($"[LightVolumeSetup] Missing probe bake compute shader at {ProbeBakeComputePath}.");
                return 0;
            }
            if (!compute.HasKernel(ProbeBakeKernelName)) {
                Debug.LogError($"[LightVolumeSetup] Probe bake compute shader has no valid '{ProbeBakeKernelName}' kernel. Reimport {ProbeBakeComputePath} and check shader compiler errors.");
                return 0;
            }

            int probeCount = Mathf.Min(shs.Length, probePositions.Length);
            Vector4[] probeSH = new Vector4[probeCount * 3];
            PackProbeSH(shs, probeSH, probeCount);

            ComputeBuffer probePositionsBuffer = null;
            ComputeBuffer probeSHBuffer = null;
            try {
                probePositionsBuffer = new ComputeBuffer(probeCount, 12);
                probeSHBuffer = new ComputeBuffer(probeSH.Length, 16);
                probePositionsBuffer.SetData(probePositions, 0, 0, probeCount);
                probeSHBuffer.SetData(probeSH);

                int kernel = compute.FindKernel(ProbeBakeKernelName);
                compute.SetInt("_ProbeCount", probeCount);
                compute.SetFloat("_UdonLightVolumeVersion", 3f);
                compute.SetFloat("_UdonPointLightVolumeCount", pointLightCount);
                compute.SetFloat("_UdonPointLightVolumeCubeCount", customTextureArray != null && LightVolumeManager != null ? LightVolumeManager.CubemapsCount : 0f);
                compute.SetFloat("_UdonPointLightVolumeShadowCubeCount", 0f);
                compute.SetFloat("_UdonPointLightVolumeShadowCount", 0f);
                compute.SetFloat("_UdonPointLightVolumeShadowBleedReduction", ShadowBleedReduction);
                compute.SetFloat("_UdonPointLightVolumeShadowMinVariance", GetShadowMinVarianceValue());
                compute.SetFloat("_UdonLightVolumeOcclusionCount", 0f);
                compute.SetTexture(kernel, "_UdonLightVolume", GetProbeBakeDummyVolumeTexture());
                compute.SetTexture(kernel, "_UdonPointLightVolumeTexture", customTextureArray != null ? customTextureArray : GetProbeBakeDummyTextureArray());
                compute.SetTexture(kernel, "_UdonPointLightVolumeShadowTexture", GetProbeBakeDummyTextureArray());
                compute.SetVectorArray("_UdonPointLightVolumePosition", pointPositions);
                compute.SetVectorArray("_UdonPointLightVolumeColor", pointColors);
                compute.SetVectorArray("_UdonPointLightVolumeExtraData", pointExtraData);
                compute.SetVectorArray("_UdonPointLightVolumeDirection", pointDirections);
                compute.SetVectorArray("_UdonPointLightVolumeCustomID", pointCustomIds);
                compute.SetBuffer(kernel, "_ProbePositions", probePositionsBuffer);
                compute.SetBuffer(kernel, "_ProbeSH", probeSHBuffer);
                compute.Dispatch(kernel, Mathf.CeilToInt(probeCount / (float)ProbeBakeThreadGroupSize), 1, 1);
                probeSHBuffer.GetData(probeSH);
            } finally {
                if (probePositionsBuffer != null) probePositionsBuffer.Release();
                if (probeSHBuffer != null) probeSHBuffer.Release();
            }

            UnpackProbeSH(probeSH, shs, probeCount);
            return pointLightCount;
        }

        // Returns a dummy 3D texture for probe bake compute resource bindings
        private static Texture3D GetProbeBakeDummyVolumeTexture() {
            if (_probeBakeDummyVolumeTexture != null) return _probeBakeDummyVolumeTexture;
            _probeBakeDummyVolumeTexture = new Texture3D(1, 1, 1, TextureFormat.RGBA32, false);
            _probeBakeDummyVolumeTexture.hideFlags = HideFlags.HideAndDontSave;
            _probeBakeDummyVolumeTexture.Apply(false, true);
            return _probeBakeDummyVolumeTexture;
        }

        // Returns a dummy 2D array texture for probe bake compute resource bindings
        private static Texture2DArray GetProbeBakeDummyTextureArray() {
            if (_probeBakeDummyTextureArray != null) return _probeBakeDummyTextureArray;
            _probeBakeDummyTextureArray = new Texture2DArray(1, 1, 1, TextureFormat.RGBA32, false);
            _probeBakeDummyTextureArray.hideFlags = HideFlags.HideAndDontSave;
            _probeBakeDummyTextureArray.Apply(false, true);
            return _probeBakeDummyTextureArray;
        }

        // Returns already built point light projection texture array without refreshing it
        private Texture GetProbeBakeCustomTextureArray() {
            RenderTexture customTextures = GetSerializedCustomTextures();
            return customTextures != null && customTextures.volumeDepth > 0 && customTextures.IsCreated() ? customTextures : null;
        }

        // Reads manager render texture references through serialization so stale migrated Udon proxy fields do not break editor bake paths.
        private RenderTexture GetSerializedCustomTextures() {
            if (LightVolumeManager == null) return null;
            SerializedObject serializedManager = new SerializedObject(LightVolumeManager);
            SerializedProperty property = serializedManager.FindProperty("CustomTextures");
            if (property == null) return null;

            try {
                return property.objectReferenceValue as RenderTexture;
            } catch (MissingReferenceException) {
                return null;
            }
        }

        // Builds compute shader uniforms for Point Light Volumes marked for probe baking
        private int BuildProbeBakePointLightData(Vector4[] pointPositions, Vector4[] pointColors, Vector4[] pointExtraData, Vector4[] pointDirections, Vector4[] pointCustomIds, bool hasCustomTextureArray) {
            int pointLightCount = 0;
            int missingTextureCount = 0;
            int overflowCount = 0;
            for (int i = 0; i < PointLightVolumes.Count; i++) {
                PointLightVolume pointLightVolume = PointLightVolumes[i];
                if (!IsProbeBakedPointLightVolume(pointLightVolume)) continue;

                if (pointLightCount >= MaxProbeBakedPointLightCount) {
                    overflowCount++;
                    continue;
                }

                if (!TryGetProbeBakeCustomId(pointLightVolume, hasCustomTextureArray, out float customId)) {
                    missingTextureCount++;
                    continue;
                }

                if (TryWriteProbeBakePointLightData(pointLightVolume, pointLightCount, customId, pointPositions, pointColors, pointExtraData, pointDirections, pointCustomIds)) pointLightCount++;
            }

            if (missingTextureCount > 0) Debug.LogWarning($"[LightVolumeSetup] Skipped {missingTextureCount} Point Light Volumes while baking into Light Probes because their projection texture data is not available in the current Point Light Volume texture array.");
            if (overflowCount > 0) Debug.LogWarning($"[LightVolumeSetup] Skipped {overflowCount} Point Light Volumes while baking into Light Probes. Maximum supported count is {MaxProbeBakedPointLightCount}.");
            return pointLightCount;
        }

        // Resolves the shader custom ID for a Point Light Volume projection using existing manager texture array data
        private bool TryGetProbeBakeCustomId(PointLightVolume pointLightVolume, bool hasCustomTextureArray, out float customId) {
            customId = 0;
            bool hasProjectionSource = pointLightVolume.HasProjectionSource();
            if (!hasProjectionSource || (pointLightVolume.Type != PointLightVolume.LightType.AreaLight && pointLightVolume.Projection == PointLightVolume.LightProjection.Parametric)) return true;
            if (!hasCustomTextureArray || LightVolumeManager == null) return false;

            int resolvedCustomId = LightVolumeManager.GetPointLightCustomID(pointLightVolume.PointLightVolumeInstance);
            if (resolvedCustomId < 0) return false;
            if (pointLightVolume.Projection == PointLightVolume.LightProjection.LUT) customId = pointLightVolume.Type == PointLightVolume.LightType.PointLight ? resolvedCustomId : resolvedCustomId + 1f;
            else customId = -resolvedCustomId - 1f;
            return true;
        }

        // Writes one Point Light Volume into compute shader uniform arrays
        private bool TryWriteProbeBakePointLightData(PointLightVolume pointLightVolume, int index, float customId, Vector4[] pointPositions, Vector4[] pointColors, Vector4[] pointExtraData, Vector4[] pointDirections, Vector4[] pointCustomIds) {
            Transform lightTransform = pointLightVolume.transform;
            Color linearColor = pointLightVolume.Color.linear;
            Vector4 color = new Vector4(linearColor.r, linearColor.g, linearColor.b, 1f) * pointLightVolume.Intensity;
            if (color.x == 0 && color.y == 0 && color.z == 0) return false;

            Vector4 position = lightTransform.position;
            Vector4 direction;
            bool isArea = pointLightVolume.Type == PointLightVolume.LightType.AreaLight;
            bool isSpot = pointLightVolume.Type == PointLightVolume.LightType.SpotLight;
            bool isLut = customId > 0;
            bool isCustomProjection = customId < 0;
            float farClip = pointLightVolume.GetShadowFarClip();
            float squaredRange = farClip * farClip;
            if (squaredRange <= 0) return false;

            if (isArea) {
                Vector3 scale = lightTransform.lossyScale;
                float width = Mathf.Max(Mathf.Abs(scale.x), 0.001f);
                float height = Mathf.Max(Mathf.Abs(scale.y), 0.001f);
                Quaternion rotation = lightTransform.rotation;
                position.w = width;
                color.w = 2f + height;
                direction = new Vector4(rotation.x, rotation.y, rotation.z, rotation.w);
            } else {
                Vector3 scale = lightTransform.lossyScale;
                float averageScale = (Mathf.Abs(scale.x) + Mathf.Abs(scale.y) + Mathf.Abs(scale.z)) * 0.3333333333f;
                float typeSign = isSpot ? -1f : 1f;
                if (isLut) {
                    float range = Mathf.Max(Mathf.Abs(pointLightVolume.Range * averageScale), 0.0001f);
                    position.w = typeSign / (range * range);
                } else {
                    float lightSourceSize = Mathf.Max(Mathf.Abs(pointLightVolume.LightSourceSize * averageScale), 0.0001f);
                    float squaredSize = lightSourceSize * lightSourceSize;
                    position.w = typeSign * squaredSize;
                }

                if (isSpot) {
                    float angle = Mathf.Clamp(pointLightVolume.Angle, 0.1f, 360f) * Mathf.Deg2Rad * 0.5f;
                    if (isCustomProjection) {
                        color.w = Mathf.Tan(angle);
                        Quaternion rotation = Quaternion.Inverse(lightTransform.rotation);
                        direction = new Vector4(rotation.x, rotation.y, rotation.z, rotation.w);
                    } else {
                        float outerAngleCos = Mathf.Cos(angle);
                        float coneFalloff = 1f / Mathf.Max(Mathf.Cos(angle * (1f - Mathf.Clamp01(pointLightVolume.Falloff))) - outerAngleCos, 0.000001f);
                        Vector3 forward = lightTransform.forward;
                        color.w = outerAngleCos;
                        direction = new Vector4(forward.x, forward.y, forward.z, coneFalloff);
                    }
                } else {
                    color.w = 1f;
                    if (isCustomProjection) {
                        Quaternion rotation = Quaternion.Inverse(lightTransform.rotation);
                        direction = new Vector4(rotation.x, rotation.y, rotation.z, rotation.w);
                    } else {
                        direction = new Vector4(0, 0, 0, 1);
                    }
                }
            }

            pointPositions[index] = position;
            pointColors[index] = color;
            Vector4 extraData = new Vector4(linearColor.r * pointLightVolume.Intensity, linearColor.g * pointLightVolume.Intensity, linearColor.b * pointLightVolume.Intensity, 0f);
            if (isSpot && isCustomProjection) extraData.x = Mathf.Max(Mathf.Abs(pointLightVolume.SpotCookieAspect), 0.001f);
            pointExtraData[index] = extraData;
            pointDirections[index] = direction;
            pointCustomIds[index] = new Vector4(customId, DisabledProbeBakeShadowId, squaredRange, 0);
            return true;
        }

        // Packs Unity SphericalHarmonicsL2 data into the same L0/L1 layout used by LightVolumes.cginc
        private static void PackProbeSH(UnityEngine.Rendering.SphericalHarmonicsL2[] shs, Vector4[] probeSH, int probeCount) {
            const int r = 0;
            const int g = 1;
            const int b = 2;
            const int a = 0;
            const int x = 3;
            const int y = 1;
            const int z = 2;

            for (int i = 0; i < probeCount; i++) {
                int index = i * 3;
                probeSH[index] = new Vector4(shs[i][r, x], shs[i][r, y], shs[i][r, z], shs[i][r, a]);
                probeSH[index + 1] = new Vector4(shs[i][g, x], shs[i][g, y], shs[i][g, z], shs[i][g, a]);
                probeSH[index + 2] = new Vector4(shs[i][b, x], shs[i][b, y], shs[i][b, z], shs[i][b, a]);
            }
        }

        // Unpacks compute shader L0/L1 data back into Unity SphericalHarmonicsL2 coefficients
        private static void UnpackProbeSH(Vector4[] probeSH, UnityEngine.Rendering.SphericalHarmonicsL2[] shs, int probeCount) {
            const int r = 0;
            const int g = 1;
            const int b = 2;
            const int a = 0;
            const int x = 3;
            const int y = 1;
            const int z = 2;

            for (int i = 0; i < probeCount; i++) {
                int index = i * 3;
                shs[i][r, x] = probeSH[index].x;
                shs[i][r, y] = probeSH[index].y;
                shs[i][r, z] = probeSH[index].z;
                shs[i][r, a] = probeSH[index].w;
                shs[i][g, x] = probeSH[index + 1].x;
                shs[i][g, y] = probeSH[index + 1].y;
                shs[i][g, z] = probeSH[index + 1].z;
                shs[i][g, a] = probeSH[index + 1].w;
                shs[i][b, x] = probeSH[index + 2].x;
                shs[i][b, y] = probeSH[index + 2].y;
                shs[i][b, z] = probeSH[index + 2].z;
                shs[i][b, a] = probeSH[index + 2].w;
            }
        }

#endif

        // Converts the active build target's normalized inspector slider into the raw EVSM variance value used by shaders.
        public float GetShadowMinVarianceValue() {
            return GetShadowMinVarianceValue(IsMobileBuildTarget() ? ShadowMinVarianceMobile : ShadowMinVariance);
        }

        // Converts a normalized inspector slider into the raw EVSM variance value used by shaders.
        private static float GetShadowMinVarianceValue(float shadowMinVariance) {
            return ShadowMinVarianceValueMin * Mathf.Pow(ShadowMinVarianceValueMax / ShadowMinVarianceValueMin, Mathf.Clamp01(shadowMinVariance));
        }

        // Returns true when the active runtime or editor build target should use mobile shadow settings.
        public static bool IsMobileBuildTarget() {
#if UNITY_EDITOR
            BuildTarget activeBuildTarget = EditorUserBuildSettings.activeBuildTarget;
            return activeBuildTarget == BuildTarget.Android || activeBuildTarget == BuildTarget.iOS;
#else
            return Application.platform == RuntimePlatform.Android || Application.platform == RuntimePlatform.IPhonePlayer;
#endif
        }

        // Returns the effective shadow texture precision used by the current build target.
        public ShadowTexturePrecision GetResolvedShadowTextureFormat() {
#if UNITY_EDITOR
            return GetAutomaticShadowTextureFormat();
#else
            return ShadowTextureFormat;
#endif
        }

        // Returns the manager integer value for the effective shadow texture precision.
        public int GetShadowTextureFormatValue() {
            return GetResolvedShadowTextureFormat() == ShadowTexturePrecision.Half ? 0 : 1;
        }

#if UDONSHARP
        // Syncs atlas, cookie and shadow metadata to the Udon manager without rebuilding runtime texture arrays
        private void SyncBaseTextureMetadataToUdon() {
            SyncManagerProgramVariable("LightVolumeAtlasBase", LightVolumeManager.LightVolumeAtlasBase);
            SyncCookieTextureMetadataToUdon();
            SyncShadowTextureMetadataToUdon();
        }

        // Syncs cookie runtime texture metadata to the Udon manager
        private void SyncCookieTextureMetadataToUdon() {
            SyncManagerProgramVariable("CustomTexturesWidth", LightVolumeManager.CustomTexturesWidth);
            SyncManagerProgramVariable("CustomTexturesHeight", LightVolumeManager.CustomTexturesHeight);
            SyncManagerProgramVariable("CubemapFaceMaterial", LightVolumeManager.CubemapFaceMaterial);
        }

        // Syncs shadow runtime texture metadata to the Udon manager
        private void SyncShadowTextureMetadataToUdon() {
            SyncManagerProgramVariable("ShadowTexturesWidth", LightVolumeManager.ShadowTexturesWidth);
            SyncManagerProgramVariable("ShadowTexturesHeight", LightVolumeManager.ShadowTexturesHeight);
            SyncManagerProgramVariable("ShadowTextureFormat", LightVolumeManager.ShadowTextureFormat);
            SyncManagerProgramVariable("ShadowBleedReduction", LightVolumeManager.ShadowBleedReduction);
            SyncManagerProgramVariable("ShadowMinVariance", LightVolumeManager.ShadowMinVariance);
            SyncManagerProgramVariable("CubemapFaceMaterial", LightVolumeManager.CubemapFaceMaterial);
        }

        // Sets a manager Udon program variable when running in play mode
        private void SyncManagerProgramVariable(string variableName, object value) {
            if (!Application.isPlaying) return;
#if UNITY_EDITOR
            if (_lightVolumeManagerBehaviour == null) SetupDependencies();
#endif
            if (_lightVolumeManagerBehaviour == null) return;
            _lightVolumeManagerBehaviour.SetProgramVariable(variableName, value);
        }

        // Requests a shader globals refresh on the Udon manager in play mode
        private bool UpdateUdonManagerVolumes() {
            if (!Application.isPlaying) return false;
#if UNITY_EDITOR
            if (_lightVolumeManagerBehaviour == null) SetupDependencies();
#endif
            if (_lightVolumeManagerBehaviour == null) return false;
            _lightVolumeManagerBehaviour.SendCustomEvent("RequestUpdateVolumes");
            return true;
        }
#endif

        // Syncs udon LightVolumeManager script with this script
        public void SyncUdonScript() {
#if UNITY_EDITOR
            if (Undo.isProcessing) {
                QueuePostUndoSync(false);
                return;
            }
            SetupDependencies();
#endif
            if (LightVolumeManager == null || DontSync) return;
#if UDONSHARP
            if (Application.isPlaying) {

                // To sync variables in play-mode, we need to do it directly to the UdonBehaviour
                _lightVolumeManagerBehaviour.SetProgramVariable("AutoUpdateVolumes", AutoUpdateVolumes);
                _lightVolumeManagerBehaviour.SetProgramVariable("AutoUpdateTextures", AutoUpdateTextures);
                _lightVolumeManagerBehaviour.SetProgramVariable("LightProbesBlending", LightProbesBlending);
                _lightVolumeManagerBehaviour.SetProgramVariable("SharpBounds", SharpBounds);
                _lightVolumeManagerBehaviour.SetProgramVariable("AdditiveMaxOverdraw", AdditiveMaxOverdraw);
                _lightVolumeManagerBehaviour.SetProgramVariable("LightsBrightnessCutoff", BrightnessCutoff);
                _lightVolumeManagerBehaviour.SetProgramVariable("ShadowTexturesWidth", (int)ShadowResolution);
                _lightVolumeManagerBehaviour.SetProgramVariable("ShadowTexturesHeight", (int)ShadowResolution);
                _lightVolumeManagerBehaviour.SetProgramVariable("ShadowTextureFormat", GetShadowTextureFormatValue());
                _lightVolumeManagerBehaviour.SetProgramVariable("ShadowBleedReduction", ShadowBleedReduction);
                _lightVolumeManagerBehaviour.SetProgramVariable("ShadowMinVariance", GetShadowMinVarianceValue());
                _lightVolumeManagerBehaviour.SetProgramVariable("ForceSceneLighting", ForceSceneLighting);
#if UNITY_EDITOR
                LightVolumeManager.CustomTexturesWidth = (int)CookieResolution;
                LightVolumeManager.CustomTexturesHeight = (int)CookieResolution;
                LightVolumeManager.ShadowTexturesWidth = (int)ShadowResolution;
                LightVolumeManager.ShadowTexturesHeight = (int)ShadowResolution;
                LightVolumeManager.ShadowTextureFormat = GetShadowTextureFormatValue();
                LightVolumeManager.ShadowBleedReduction = ShadowBleedReduction;
                LightVolumeManager.ShadowMinVariance = GetShadowMinVarianceValue();
                LightVolumeManager.CubemapFaceMaterial = GetCubemapFaceMaterial();
#endif
                SyncBaseTextureMetadataToUdon();

                SyncLightVolumeRuntimeInstances();
                var instances = GetLightVolumeInstances();
#if UNITY_EDITOR
                LightVolumeManager.LightVolumeInstances = instances;
#endif
                UdonBehaviour[] lightVolumeInstances = new UdonBehaviour[instances.Length];
                for (int i = 0; i < instances.Length; i++) {
                    lightVolumeInstances[i] = instances[i].GetComponent<UdonBehaviour>();
                }
                _lightVolumeManagerBehaviour.SetProgramVariable("LightVolumeInstances", lightVolumeInstances);

                SyncPointLightVolumeRuntimeInstances();
                var pointInstances = GetPointLightVolumeInstances();
#if UNITY_EDITOR
                LightVolumeManager.PointLightVolumeInstances = pointInstances;
#endif
                UdonBehaviour[] pointLightVolumeInstances = new UdonBehaviour[pointInstances.Length];
                for (int i = 0; i < pointInstances.Length; i++) {
                    pointLightVolumeInstances[i] = pointInstances[i].GetComponent<UdonBehaviour>();
                }
                _lightVolumeManagerBehaviour.SetProgramVariable("PointLightVolumeInstances", pointLightVolumeInstances);
#if UNITY_EDITOR
                _lightVolumeManagerBehaviour.SendCustomEvent("ReinitializeCustomTextures");
                _lightVolumeManagerBehaviour.SendCustomEvent("ReinitializeShadowTextures");
                _lightVolumeManagerBehaviour.SendCustomEvent("UpdateVolumes");
                LightVolumeManager.ReinitializeCustomTextures();
                LightVolumeManager.ReinitializeShadowTextures();
                LightVolumeManager.UpdateVolumes();
#else
                _lightVolumeManagerBehaviour.SendCustomEvent("ReinitializeCustomTextures");
                _lightVolumeManagerBehaviour.SendCustomEvent("ReinitializeShadowTextures");
                // General setup changes are applied by the manager on the next scheduled Udon update frame
                _lightVolumeManagerBehaviour.SendCustomEvent("RequestUpdateVolumes");
#endif

            } else {
#endif
                LightVolumeManager.AutoUpdateVolumes = AutoUpdateVolumes;
                LightVolumeManager.AutoUpdateTextures = AutoUpdateTextures;
                LightVolumeManager.LightProbesBlending = LightProbesBlending;
                LightVolumeManager.SharpBounds = SharpBounds;
                LightVolumeManager.AdditiveMaxOverdraw = AdditiveMaxOverdraw;
                LightVolumeManager.LightsBrightnessCutoff = BrightnessCutoff;
                LightVolumeManager.ShadowTexturesWidth = (int)ShadowResolution;
                LightVolumeManager.ShadowTexturesHeight = (int)ShadowResolution;
                LightVolumeManager.ShadowTextureFormat = GetShadowTextureFormatValue();
                LightVolumeManager.ShadowBleedReduction = ShadowBleedReduction;
                LightVolumeManager.ShadowMinVariance = GetShadowMinVarianceValue();
                LightVolumeManager.ForceSceneLighting = ForceSceneLighting;
#if UNITY_EDITOR
                LightVolumeManager.CustomTexturesWidth = (int)CookieResolution;
                LightVolumeManager.CustomTexturesHeight = (int)CookieResolution;
                LightVolumeManager.CubemapFaceMaterial = GetCubemapFaceMaterial();
                RefreshAtlasOutput();
#endif

                SyncLightVolumeRuntimeInstances();
                LightVolumeManager.LightVolumeInstances = GetLightVolumeInstances();

                SyncPointLightVolumeRuntimeInstances();
                LightVolumeManager.PointLightVolumeInstances = GetPointLightVolumeInstances();

                LightVolumeManager.ReinitializeCustomTextures();
                LightVolumeManager.ReinitializeShadowTextures();
                LightVolumeManager.UpdateVolumes();
#if UDONSHARP
            }
#endif
        }

        // All Non-udon mono behaviours should be destroyed in playmode
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CommitSudoku() {
            if (Application.isPlaying) {

                bool isDestroy = false;
                var s = FindObjectsByType<LightVolumeSetup>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                for (int i = 0; i < s.Length; i++) {
                    if (!s[i].DestroyInPlayMode) {
                        s[i].DontSync = false;
                    } else {
                        isDestroy = true;
                    }
                }
                if(!isDestroy) return;

                // Killing Light Volumes
                var lvs = FindObjectsByType<LightVolume>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                for (int i = 0; i < lvs.Length; i++) {
#if BAKERY_INCLUDED
                    if (lvs[i].BakeryVolume != null) Destroy(lvs[i].BakeryVolume.gameObject);
#endif
                    Destroy(lvs[i]);
                }

                // Killing Point Light Volumes
                var plvs = FindObjectsByType<PointLightVolume>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                for (int i = 0; i < plvs.Length; i++) {
                    Destroy(plvs[i]);
                }

                // Sudoku
                for (int i = 0; i < s.Length; i++) {
                    Destroy(s[i]);
                }

            }
        }

        // Synchronizes authoring Light Volume fields before setup sorts and uploads runtime instances.
        private void SyncLightVolumeRuntimeInstances() {
            int count = LightVolumes.Count;
            for (int i = 0; i < count; i++) {
                LightVolume lightVolume = LightVolumes[i];
                if (lightVolume == null) continue;
                lightVolume.SyncUdonScript();
                if (lightVolume.LightVolumeInstance != null) lightVolume.LightVolumeInstance.IsAdditive = lightVolume.Additive;
            }
        }

        // Synchronizes authoring Point Light Volume fields before setup uploads runtime instances.
        private void SyncPointLightVolumeRuntimeInstances() {
            bool notifyManager = !Application.isPlaying;
            int count = PointLightVolumes.Count;
            for (int i = 0; i < count; i++) {
                PointLightVolume pointLightVolume = PointLightVolumes[i];
                if (pointLightVolume == null) continue;
                pointLightVolume.SyncUdonScript(true, notifyManager);
            }
        }

        // Builds sorted Light Volume runtime instances from the current authoring list.
        private LightVolumeInstance[] GetLightVolumeInstances() {
            int count = LightVolumes.Count;
            LightVolumeData[] sortedData = new LightVolumeData[count];
            int sortedCount = 0;
            for (int i = 0; i < count; i++) {
                LightVolume lightVolume = LightVolumes[i];
                if (lightVolume == null || lightVolume.LightVolumeInstance == null) continue;

                LightVolumeData data = new LightVolumeData(i < LightVolumesWeights.Count ? LightVolumesWeights[i] : 0, lightVolume.LightVolumeInstance);
                int insertIndex = sortedCount;
                while (insertIndex > 0) {
                    LightVolumeData previous = sortedData[insertIndex - 1];
                    if (previous.LightVolumeInstance.IsAdditive && !data.LightVolumeInstance.IsAdditive) break;
                    if (previous.LightVolumeInstance.IsAdditive == data.LightVolumeInstance.IsAdditive && previous.Weight >= data.Weight) break;
                    sortedData[insertIndex] = previous;
                    insertIndex--;
                }
                sortedData[insertIndex] = data;
                sortedCount++;
            }

            LightVolumeInstance[] sortedVolumes = new LightVolumeInstance[sortedCount];
            int activeCount = 0;
            for (int i = 0; i < sortedCount; i++) {
                LightVolumeInstance instance = sortedData[i].LightVolumeInstance;
                float weight = sortedData[i].Weight;
                if (instance.RegistryOrder != i || instance.RegistryWeight != weight) {
                    instance.RegistryOrder = i;
                    instance.RegistryWeight = weight;
#if UDONSHARP
                    if (Application.isPlaying) {
                        UdonBehaviour behaviour = instance.GetComponent<UdonBehaviour>();
                        if (behaviour != null) {
                            behaviour.SetProgramVariable("RegistryOrder", i);
                            behaviour.SetProgramVariable("RegistryWeight", weight);
                        }
                    }
#endif
#if UNITY_EDITOR
                    LVUtils.MarkDirty(instance);
#endif
                }
                if (!instance.gameObject.activeInHierarchy) continue;
                sortedVolumes[activeCount] = instance;
                activeCount++;
            }

            LightVolumeInstance[] volumes = new LightVolumeInstance[activeCount];
            for (int i = 0; i < activeCount; i++) volumes[i] = sortedVolumes[i];
            return volumes;
        }

        // Builds Point Light Volume runtime instances from the current authoring list.
        private PointLightVolumeInstance[] GetPointLightVolumeInstances() {
            List<PointLightVolumeInstance> list = new List<PointLightVolumeInstance>();
            int count = PointLightVolumes.Count;
            for (int i = 0; i < count; i++) {
                if (PointLightVolumes[i] == null || PointLightVolumes[i].PointLightVolumeInstance == null) continue;
                PointLightVolumeInstance instance = PointLightVolumes[i].PointLightVolumeInstance;
                if (instance.RegistryOrder != i || instance.RegistryWeight != 0f) {
                    instance.RegistryOrder = i;
                    instance.RegistryWeight = 0f;
#if UDONSHARP
                    if (Application.isPlaying) {
                        UdonBehaviour behaviour = instance.GetComponent<UdonBehaviour>();
                        if (behaviour != null) {
                            behaviour.SetProgramVariable("RegistryOrder", i);
                            behaviour.SetProgramVariable("RegistryWeight", 0f);
                        }
                    }
#endif
#if UNITY_EDITOR
                    LVUtils.MarkDirty(instance);
#endif
                }
                if (!PointLightVolumes[i].gameObject.activeInHierarchy) continue;
                list.Add(instance);
            }
            return list.ToArray();
        }

#if UNITY_EDITOR
        // Registers a Custom Render Texture post processor for the Light Volume 3D atlas
        public void RegisterPostProcessorCRT(CustomRenderTexture crt) {
            RegisterPostProcessorCRT(ref AtlasPostProcessors, crt, "", UpdateAtlasPostProcessors);
        }

        // Unregisters a Custom Render Texture post processor from the Light Volume 3D atlas
        public void UnregisterPostProcessorCRT(CustomRenderTexture crt) => UnregisterPostProcessor(crt); // API backwards compat

        // Unregisters a post processor from the Light Volume 3D atlas
        public void UnregisterPostProcessor(RenderTexture crt) {
            UnregisterPostProcessor(ref AtlasPostProcessors, crt, "", UpdateAtlasPostProcessors);
        }

        // Unregisters a post processor from the Light Volume 3D atlas
        public void UnregisterPostProcessor(PostProcessor pp) {
            UnregisterPostProcessor(ref AtlasPostProcessors, pp, "", UpdateAtlasPostProcessors);
        }

        // Registers a Render Texture post processor for the Light Volume 3D atlas
        public void RegisterPostProcessor(PostProcessor pp) {
            RegisterPostProcessor(ref AtlasPostProcessors, pp, "", UpdateAtlasPostProcessors);
        }

        // Registers a Custom Render Texture post processor in a shared post processor list
        private void RegisterPostProcessorCRT(ref PostProcessor[] postProcessors, CustomRenderTexture crt, string targetName, Action updatePostProcessors) {
            if (crt == null) return;
            RegisterPostProcessor(ref postProcessors, new PostProcessor { RT = crt, Mat = crt.material, TextureName = "_MainTex", Update = crt.Update }, targetName, updatePostProcessors);
        }

        // Unregisters a post processor from a shared post processor list
        private void UnregisterPostProcessor(ref PostProcessor[] postProcessors, RenderTexture rt, string targetName, Action updatePostProcessors) {
            if (rt == null) return;
            UnregisterPostProcessor(ref postProcessors, new PostProcessor { RT = rt }, targetName, updatePostProcessors);
        }

        // Unregisters a post processor from a shared post processor list
        private void UnregisterPostProcessor(ref PostProcessor[] postProcessors, PostProcessor pp, string targetName, Action updatePostProcessors) {
            if (postProcessors == null) return;
            int removeCount = 0;
            RenderTexture removedRt = pp.RT;
            for (int i = 0; i < postProcessors.Length; i++) {
                if (!IsSamePostProcessor(postProcessors[i], pp)) continue;
                if (removedRt == null) removedRt = postProcessors[i].RT;
                removeCount++;
            }
            if (removeCount == 0) return;

            PostProcessor[] newArray = new PostProcessor[postProcessors.Length - removeCount];
            for (int i = 0, j = 0; i < postProcessors.Length; i++) {
                if (IsSamePostProcessor(postProcessors[i], pp)) continue;
                newArray[j] = postProcessors[i];
                j++;
            }
            postProcessors = newArray;
            Debug.Log($"[LightVolumeSetup] Unregistered {GetPostProcessorLogName(targetName)}: {(removedRt != null ? removedRt.name : "")}");
            updatePostProcessors?.Invoke();
        }

        // Registers a Render Texture post processor in a shared post processor list
        private void RegisterPostProcessor(ref PostProcessor[] postProcessors, PostProcessor pp, string targetName, Action updatePostProcessors) {
            if (pp.RT == null || (pp.Mat == null && pp.Update == null && pp.UpdateWithInput == null)) return;
            if (postProcessors == null) postProcessors = new PostProcessor[0];
            if (string.IsNullOrEmpty(pp.TextureName)) pp.TextureName = "_MainTex";
            int index = FindPostProcessorIndex(postProcessors, pp);
            if (index >= 0) {
                bool changed = postProcessors[index].RT != pp.RT || postProcessors[index].Mat != pp.Mat || postProcessors[index].TextureName != pp.TextureName || postProcessors[index].Update != pp.Update || postProcessors[index].UpdateWithInput != pp.UpdateWithInput;
                postProcessors[index] = pp;
                bool removedDuplicates = RemoveDuplicatePostProcessors(ref postProcessors, pp, index);
                if (!changed && !removedDuplicates) return;
                Debug.Log($"[LightVolumeSetup] Updated {GetPostProcessorLogName(targetName)}: {pp.RT.name}");
                updatePostProcessors?.Invoke();
                return;
            }
            Array.Resize(ref postProcessors, postProcessors.Length + 1);
            postProcessors[postProcessors.Length - 1] = pp;
            Debug.Log($"[LightVolumeSetup] Registered {GetPostProcessorLogName(targetName)}: {pp.RT.name}");
            updatePostProcessors?.Invoke();
        }

        // Finds a post processor by render target or callback identity
        private static int FindPostProcessorIndex(PostProcessor[] postProcessors, PostProcessor pp) {
            for (int i = 0; i < postProcessors.Length; i++) {
                if (IsSamePostProcessor(postProcessors[i], pp)) return i;
            }
            return -1;
        }

        // Removes duplicate registrations that point to the same render target or callback
        private static bool RemoveDuplicatePostProcessors(ref PostProcessor[] postProcessors, PostProcessor pp, int keepIndex) {
            int duplicateCount = 0;
            for (int i = 0; i < postProcessors.Length; i++) {
                if (i != keepIndex && IsSamePostProcessor(postProcessors[i], pp)) duplicateCount++;
            }
            if (duplicateCount == 0) return false;

            PostProcessor[] newArray = new PostProcessor[postProcessors.Length - duplicateCount];
            for (int i = 0, j = 0; i < postProcessors.Length; i++) {
                if (i != keepIndex && IsSamePostProcessor(postProcessors[i], pp)) continue;
                newArray[j] = postProcessors[i];
                j++;
            }
            postProcessors = newArray;
            return true;
        }

        // Checks if an existing post processor matches a requested registration
        private static bool IsSamePostProcessor(PostProcessor existing, PostProcessor requested) {
            if (requested.RT != null && existing.RT == requested.RT) return true;
            if (requested.Update != null && existing.Update == requested.Update) return true;
            if (requested.UpdateWithInput != null && existing.UpdateWithInput == requested.UpdateWithInput) return true;
            return false;
        }

        // Builds the display name used by post processor log messages
        private static string GetPostProcessorLogName(string targetName) {
            return string.IsNullOrEmpty(targetName) ? "post processor" : $"{targetName} post processor";
        }

        // Updates the active Light Volume 3D atlas output without pushing shader globals
        private void RefreshAtlasOutput() {
            if (LightVolumeManager == null) return;
            LightVolumeManager.LightVolumeAtlas = UpdatePostProcessorChain(
                AtlasPostProcessors,
                LightVolumeManager.LightVolumeAtlasBase,
                UnityEngine.Rendering.TextureDimension.Tex3D,
                UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat,
                FilterMode.Trilinear);
        }

        // Updates the Light Volume 3D atlas post processor chain and stores its active output
        private void UpdateAtlasPostProcessors() {
            RefreshAtlasOutput();
#if UDONSHARP
            SyncManagerProgramVariable("LightVolumeAtlas", LightVolumeManager.LightVolumeAtlas);
            if (UpdateUdonManagerVolumes()) return;
#endif
            LightVolumeManager.UpdateVolumes();
        }

        // Applies a post processor chain to a base texture and returns the last valid output
        private Texture UpdatePostProcessorChain(PostProcessor[] postProcessors, Texture baseTexture, UnityEngine.Rendering.TextureDimension dimension, UnityEngine.Experimental.Rendering.GraphicsFormat graphicsFormat, FilterMode filterMode, int targetWidth = 0, int targetHeight = 0, int targetDepth = 0) {
            if (baseTexture == null || postProcessors == null || postProcessors.Length == 0) return baseTexture;

            Texture prevTexture = baseTexture;
            bool hasValidProcessor = false;
            for (int i = 0; i < postProcessors.Length; i++) {
                PostProcessor pp = postProcessors[i];
                RenderTexture rt = pp.RT;
                Material mat = pp.Mat;
                if (rt == null || (mat == null && pp.Update == null && pp.UpdateWithInput == null)) continue;

                SetupPostProcessorRenderTexture(rt, baseTexture, dimension, graphicsFormat, filterMode, targetWidth, targetHeight, targetDepth);

                Texture inputTexture = prevTexture;
                string textureName = string.IsNullOrEmpty(pp.TextureName) ? "_MainTex" : pp.TextureName;
                if (mat != null) mat.SetTexture(textureName, inputTexture);
                prevTexture = rt;
                hasValidProcessor = true;

                if (pp.UpdateWithInput != null) pp.UpdateWithInput(inputTexture);
                else pp.Update?.Invoke();
            }

            return hasValidProcessor ? prevTexture : baseTexture;
        }

        // Enforces dimensions and format on a post processor render target before running its update
        private static void SetupPostProcessorRenderTexture(RenderTexture rt, Texture baseTexture, UnityEngine.Rendering.TextureDimension dimension, UnityEngine.Experimental.Rendering.GraphicsFormat graphicsFormat, FilterMode filterMode, int targetWidth = 0, int targetHeight = 0, int targetDepth = 0) {
            RenderTexture.active = null;
            rt.Release();
            rt.dimension = dimension;
            rt.graphicsFormat = graphicsFormat;
            rt.enableRandomWrite = false;
            rt.wrapMode = TextureWrapMode.Clamp;
            rt.filterMode = filterMode;
            rt.anisoLevel = 0;
            rt.width = targetWidth > 0 ? targetWidth : Mathf.Max(baseTexture.width, 1);
            rt.height = targetHeight > 0 ? targetHeight : Mathf.Max(baseTexture.height, 1);
            rt.volumeDepth = targetDepth > 0 ? targetDepth : Mathf.Max(GetTextureDepth(baseTexture), 1);
            if (rt is CustomRenderTexture crt) {
                crt.updateMode = CustomRenderTextureUpdateMode.Realtime;
            }
            rt.Create();
        }

        // Returns the depth or array-slice count for any texture type used by post processor chains
        private static int GetTextureDepth(Texture texture) {
            if (texture is Texture3D texture3D) return texture3D.depth;
            if (texture is Texture2DArray textureArray) return textureArray.depth;
            if (texture is RenderTexture renderTexture) return renderTexture.volumeDepth;
            if (texture is Cubemap) return 6;
            return 1;
        }
#endif

        // Returns the fixed texture format used for baked shadow map moments
        public TextureFormat GetShadowMapBakeFormat() {
            return GetResolvedShadowTextureFormat() == ShadowTexturePrecision.Half ? TextureFormat.RGBAHalf : TextureFormat.RGBAFloat;
        }

        // Bakes all requested per-light shadow maps
        public void BakeShadowMaps() {
            BakeShadowMaps(false);
        }

        // Bakes point light shadow maps, optionally ignoring per-light rebake flags
        private bool BakeShadowMaps(bool forceAll) {
#if UNITY_EDITOR
            bool isRebaked = false;
            for (int i = 0; i < PointLightVolumes.Count; i++) {
                PointLightVolume pointLightVolume = PointLightVolumes[i];
                if (pointLightVolume == null || !pointLightVolume.Shadows || (!forceAll && !pointLightVolume.RebakeShadows)) continue;
                bool isBaked = pointLightVolume.BakeShadowMap($"| {pointLightVolume.gameObject.name} ({i}/{PointLightVolumes.Count})", false);
                isRebaked = isRebaked || isBaked;
            }
            if (isRebaked) ReinitializeShadowTextures();
            return isRebaked;
#else
            return false;
#endif
        }

        public enum Baking {
            Progressive,
            Bakery
        }

        public enum TextureArrayResolution {
            _16x16 = 16,
            _32x32 = 32,
            _64x64 = 64,
            _128x128 = 128,
            _256x256 = 256,
            _512x512 = 512,
            _1024x1024 = 1024,
            _2048x2048 = 2048
        }

        public enum ShadowTexturePrecision {
            Half = 0,
            Float = 1
        }

        public enum Downscale {
            None = 0,
            x2 = 1,
            x4 = 2,
            x8 = 3
        }

    }

#if UNITY_EDITOR
    internal class LightVolumeSetupBuildTargetChanged : IActiveBuildTargetChanged {
        public int callbackOrder => 0;

        // Receives Unity build target changes without using the obsolete EditorUserBuildSettings event.
        public void OnActiveBuildTargetChanged(BuildTarget previousTarget, BuildTarget newTarget) {
            LightVolumeSetup.HandleActiveBuildTargetChanged();
        }
    }
#endif
}
