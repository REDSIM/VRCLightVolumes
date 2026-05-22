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
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.Linq;
#endif

namespace VRCLightVolumes {
    [ExecuteAlways]
    public class LightVolumeSetup : MonoBehaviour {

        [SerializeField] public List<LightVolume> LightVolumes = new List<LightVolume>();
        [SerializeField] public List<float> LightVolumesWeights = new List<float>();

        [SerializeField] public List<PointLightVolume> PointLightVolumes = new List<PointLightVolume>();

        [Header("Point Light Volumes")]
        [Tooltip("Resolution used for point light cookie, LUT and cubemap projection textures.")]
        [FormerlySerializedAs("Resolution")]
        public TextureArrayResolution CookieResolution = TextureArrayResolution._128x128;
        [Tooltip("The minimum brightness at a point due to lighting from a Point Light Volume, before the light is culled. Larger values will result in better performance, but light attenuation will be less physically correct.")]
        [FormerlySerializedAs("LightsBrightnessCutoff")]
        [Range(0.05f, 1f)] public float BrightnessCutoff = 0.35f;
        [Tooltip("Resolution used for per-light shadow maps.")]
        public TextureArrayResolution ShadowResolution = TextureArrayResolution._128x128;

        [Header("Baking")]
        [Tooltip("Bakery usually gives better results and works faster.")]
#if BAKERY_INCLUDED
        public Baking BakingMode = Baking.Bakery;
#else
        public Baking BakingMode = Baking.Progressive;
#endif
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
        [Tooltip("Automatically fixes Bakery's \"burned\" light probes after a scene bake. But decreases their contrast slightly.")]
        public bool FixLightProbesL1 = true;
        [Tooltip("Downscales each light volume. Useful to make a lower atlas resolution for mobile platforms or to increase overall sharpness and decrease aliasing.")]
        public Downscale DownscaleVolumes = Downscale.None;
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
        [Min(1)]public int AdditiveMaxOverdraw = 4;
        [Tooltip("Disables min/max brightness limits for modern avatar shaders such as lilToon or Poiyomi. Check this only if you're sure your scene lighting is properly configured.")]
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

        public bool IsLegacyUVWConverted = false; // True once the legacy UVW fix has been applied

        private TextureArrayResolution _resolutionPrev = TextureArrayResolution._128x128;
        private TextureArrayResolution _shadowResolutionPrev = TextureArrayResolution._64x64;
#if UNITY_EDITOR
        private EditorCoroutine _generateAtlasCoroutine = null;
        private const string CubemapFaceMaterialPath = "Packages/red.sim.lightvolumes/Materials/LightVolumeCubemapFace.mat";
#endif
        public void RefreshVolumesList() {

            if(DontSync) return;

            // Searching for all light volumes in scene
            var volumes = FindObjectsOfType<LightVolume>(true);
            for (int i = 0; i < volumes.Length; i++) {
                if (volumes[i].CompareTag("EditorOnly")) continue;
                if (!LightVolumes.Contains(volumes[i])) {
                    LightVolumes.Add(volumes[i]);
                    LightVolumesWeights.Add(0.0f);
                }
            }
            // Removing volumes that no more exists
            for (int i = 0; i < LightVolumes.Count; i++) {
                if (LightVolumes[i] == null || LightVolumes[i].CompareTag("EditorOnly")) {
                    LightVolumes.RemoveAt(i);
                    LightVolumesWeights.RemoveAt(i);
                    i--;
                }
            }

            // Searching for all point light volumes in scene
            var pointVolumes = FindObjectsOfType<PointLightVolume>(true);
            for (int i = 0; i < pointVolumes.Length; i++) {
                if (pointVolumes[i].CompareTag("EditorOnly")) continue;
                if (!PointLightVolumes.Contains(pointVolumes[i])) {
                    PointLightVolumes.Add(pointVolumes[i]);
                }
            }
            // Removing point light volumes that no more exists
            for (int i = 0; i < PointLightVolumes.Count; i++) {
                if (PointLightVolumes[i] == null || PointLightVolumes[i].CompareTag("EditorOnly")) {
                    PointLightVolumes.RemoveAt(i);
                    i--;
                }
            }
            SyncUdonScript();
        }

#if UNITY_EDITOR

#if BAKERY_INCLUDED
        private bool _subscribedToBakery = false;
#endif
        private bool _subscribedToUnityLightmapper = false;

        private void OnSelectionChanged() {
            if (Selection.activeObject == gameObject) {
                RefreshVolumesList();
            }
        }

        // Rebuilds manager-owned cookie source caches and the runtime RenderTextureArray
        public void ReinitializeCustomTextures() {
            ReinitializePointLightTextureArray(true);
        }

        // Loads the shared material used to unwrap cubemap faces at runtime
        private Material GetCubemapFaceMaterial() {
            return AssetDatabase.LoadAssetAtPath<Material>(CubemapFaceMaterialPath);
        }

        // Rebuilds manager-owned shadow source caches and the runtime RenderTextureArray
        public void ReinitializeShadowTextures() {
            ReinitializePointLightTextureArray(false);
        }

        // Rebuilds one of the manager-owned point light texture arrays and keeps Udon proxies in sync
        private void ReinitializePointLightTextureArray(bool customTextures) {
            SetupDependencies();

            if (LightVolumeManager == null || DontSync) return;

            if (customTextures) {
                LightVolumeManager.CustomTexturesWidth = (int)CookieResolution;
                LightVolumeManager.CustomTexturesHeight = (int)CookieResolution;
            } else {
                LightVolumeManager.ShadowTexturesWidth = (int)ShadowResolution;
                LightVolumeManager.ShadowTexturesHeight = (int)ShadowResolution;
            }
            LightVolumeManager.CubemapFaceMaterial = GetCubemapFaceMaterial();

            for (int i = 0; i < PointLightVolumes.Count; i++) {
                if (PointLightVolumes[i] != null) PointLightVolumes[i].SyncUdonScript();
            }

#if UDONSHARP
            if (customTextures) SyncCookieTextureMetadataToUdon();
            else SyncShadowTextureMetadataToUdon();
            if (Application.isPlaying && _lightVolumeManagerBehaviour != null) {
                var instances = GetPointLightVolumeInstances();
                UdonBehaviour[] pointLightVolumeInstances = new UdonBehaviour[instances.Length];
                for (int i = 0; i < instances.Length; i++) {
                    pointLightVolumeInstances[i] = instances[i].GetComponent<UdonBehaviour>();
                }
                _lightVolumeManagerBehaviour.SetProgramVariable("PointLightVolumeInstances", pointLightVolumeInstances);
                _lightVolumeManagerBehaviour.SendCustomEvent(customTextures ? "ReinitializeCustomTextures" : "ReinitializeShadowTextures");
                _lightVolumeManagerBehaviour.SendCustomEvent("UpdateVolumes");
                return;
            }
#endif
            LightVolumeManager.PointLightVolumeInstances = GetPointLightVolumeInstances();
            if (customTextures) LightVolumeManager.ReinitializeCustomTextures();
            else LightVolumeManager.ReinitializeShadowTextures();
        }

        // Subscribing to OnBaked events
        private void OnEnable() {
#if BAKERY_INCLUDED
            if (!Application.isPlaying && !_subscribedToBakery) {
                ftRenderLightmap.OnFinishedFullRender += OnBakeryFinishedRender;
                ftRenderLightmap.OnPreFullRender += OnBakeryStartedRender;
                _subscribedToBakery = true;
            }
#endif
            if (!Application.isPlaying && !_subscribedToUnityLightmapper) {
                UnityEditor.Experimental.Lightmapping.additionalBakedProbesCompleted += OnAdditionalProbesCompleted;
                Lightmapping.bakeStarted += OnUnityBakingStarted;
                _subscribedToUnityLightmapper = true;
            }

            Selection.selectionChanged += OnSelectionChanged;
            SyncUdonScript();
        }

        // Unsubscribing from OnBaked events
        private void OnDisable() {
#if BAKERY_INCLUDED
            if (!Application.isPlaying && _subscribedToBakery) {
                ftRenderLightmap.OnFinishedFullRender -= OnBakeryFinishedRender;
                ftRenderLightmap.OnPreFullRender -= OnBakeryStartedRender;
                _subscribedToBakery = false;
            }
#endif
            if (!Application.isPlaying && _subscribedToUnityLightmapper) {
                UnityEditor.Experimental.Lightmapping.additionalBakedProbesCompleted -= OnAdditionalProbesCompleted;
                Lightmapping.bakeStarted -= OnUnityBakingStarted;
                _subscribedToUnityLightmapper = false;

            }

            Selection.selectionChanged -= OnSelectionChanged;
            SyncUdonScript();
        }

        private void Awake() {
            SyncUdonScript();
        }

        private void OnValidate() {
            SyncUdonScript();
        }

#if BAKERY_INCLUDED

        // On Bakery Started baking
        private void OnBakeryStartedRender(object sender, EventArgs e) {
            // Attempt to fix a bakery bug
            var volumes = FindObjectsOfType<LightVolume>(true);
            for (int i = 0; i < volumes.Length; i++) {
                volumes[i].SetupBakeryDependencies();
            }
        }

        // On Bakery Finished baking
        private void OnBakeryFinishedRender(object sender, EventArgs e) {
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
            if (FixLightProbesL1) FixLightProbes();
            GenerateAtlas();
            Debug.Log($"[LightVolumeSetup] Generating 3D Atlas finished!");
        }

#endif

        // On Unity Lightmapper started baking
        private void OnUnityBakingStarted() {
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
            GenerateAtlas();
            Debug.Log($"[LightVolumeSetup] Generating 3D Atlas finished!");

        }

        private void Update() {
            if (DontSync) return;
            SetupDependencies();
            ConvertLegacyUVW();
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
            if (_shadowResolutionPrev != ShadowResolution) {
                _shadowResolutionPrev = ShadowResolution;
                ReinitializeShadowTextures();
            }
            if (!Application.isPlaying && LightVolumeManager != null) {
                LightVolumeManager.UpdateVolumes();
                if (LightVolumeManager.HasAutoTextureUpdates()) {
                    LightVolumeManager.UpdateAutoCustomTextures();
                    LightVolumeManager.UpdateAutoShadowTextures();
                    EditorApplication.QueuePlayerLoopUpdate();
                    SceneView.RepaintAll();
                }
            }
        }

        // Try to convert Legacy UVW data into a new compact data format
        private void ConvertLegacyUVW() {

            if (IsLegacyUVWConverted || LVUtils.IsInPrefabAsset(this) || LightVolumes.Count == 0) return;

            for (int i = 0; i < LightVolumes.Count; i++) {

                if (LightVolumes[i] == null) continue;
                var lightVolumeInstance = LightVolumes[i].LightVolumeInstance;
                if (lightVolumeInstance == null) continue;
                if(lightVolumeInstance.BoundsUvwMin0.w != 0 && lightVolumeInstance.BoundsUvwMin1.w != 0 && lightVolumeInstance.BoundsUvwMin2.w != 0) {
                    continue; // This is already NOT Legacy UVW, skip
                }

                Vector3 scale = lightVolumeInstance.BoundsUvwMax0 - lightVolumeInstance.BoundsUvwMin0;
                Vector3 uvwMin0 = lightVolumeInstance.BoundsUvwMin0;
                Vector3 uvwMin1 = lightVolumeInstance.BoundsUvwMin1;
                Vector3 uvwMin2 = lightVolumeInstance.BoundsUvwMin2;

                lightVolumeInstance.BoundsUvwMin0 = new Vector4(uvwMin0.x, uvwMin0.y, uvwMin0.z, scale.x);
                lightVolumeInstance.BoundsUvwMin1 = new Vector4(uvwMin1.x, uvwMin1.y, uvwMin1.z, scale.y);
                lightVolumeInstance.BoundsUvwMin2 = new Vector4(uvwMin2.x, uvwMin2.y, uvwMin2.z, scale.z);

                LVUtils.MarkDirty(lightVolumeInstance);
            }

            IsLegacyUVWConverted = true;

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
            var packingStrategy = AtlasPostProcessors != null && AtlasPostProcessors.Length > 0 && AtlasPostProcessors.Any(pp => pp.RT is CustomRenderTexture)
                ? TexturePackingStrategy.MinimumDepth : TexturePackingStrategy.MinimumVRAM;

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

                        lightVolumeBehaviour.SetProgramVariable("BoundsUvwMax0", (Vector4) atlas.BoundsUvwMax[atlasIndex]);
                        lightVolumeBehaviour.SetProgramVariable("BoundsUvwMax1", (Vector4) atlas.BoundsUvwMax[atlasIndex + 1]);
                        lightVolumeBehaviour.SetProgramVariable("BoundsUvwMax2", (Vector4) atlas.BoundsUvwMax[atlasIndex + 2]);

                    } else {
#endif
                        lightVolumeInstance.BoundsUvwMin0 = new Vector4(uvwMin0.x, uvwMin0.y, uvwMin0.z, scale.x);
                        lightVolumeInstance.BoundsUvwMin1 = new Vector4(uvwMin1.x, uvwMin1.y, uvwMin1.z, scale.y);
                        lightVolumeInstance.BoundsUvwMin2 = new Vector4(uvwMin2.x, uvwMin2.y, uvwMin2.z, scale.z);

                        // Legacy
                        lightVolumeInstance.BoundsUvwMax0 = (Vector4) atlas.BoundsUvwMax[atlasIndex];
                        lightVolumeInstance.BoundsUvwMax1 = (Vector4) atlas.BoundsUvwMax[atlasIndex + 1];
                        lightVolumeInstance.BoundsUvwMax2 = (Vector4) atlas.BoundsUvwMax[atlasIndex + 2];
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
#if UDONSHARP
            if (_lightVolumeManagerBehaviour == null) {
                TryGetComponent(out _lightVolumeManagerBehaviour);
            }
#endif
        }

        // Fixes light probes baked with Bakery L1
        private static void FixLightProbes() {

            var probes = LightmapSettings.lightProbes;
            if (probes == null || probes.count == 0) {
                Debug.LogWarning("[LightVolumeSetup] No Light Probes found to fix.");
                return;
            } else if (LVUtils.CheckSHL2(probes.bakedProbes[0])) {
                Debug.Log("[LightVolumeSetup] L2 Light Probes detected - no need to apply L1 Bakery fix.");
                return;
            }

            var shs = probes.bakedProbes;
            for (int i = 0; i < shs.Length; ++i) {
                shs[i] = LVUtils.DeringSH(shs[i]);
            }

            probes.bakedProbes = shs;
            EditorUtility.SetDirty(probes);
            EditorSceneManager.MarkAllScenesDirty();

            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveOpenScenes();

            Debug.Log($"[LightVolumeSetup] {shs.Length} Light Probes fixed!");

        }

#endif

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
            _lightVolumeManagerBehaviour.SendCustomEvent("UpdateVolumes");
            return true;
        }
#endif

        // Syncs udon LightVolumeManager script with this script
        public void SyncUdonScript() {
#if UNITY_EDITOR
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
                _lightVolumeManagerBehaviour.SetProgramVariable("ForceSceneLighting", ForceSceneLighting);
#if UNITY_EDITOR
                LightVolumeManager.CustomTexturesWidth = (int)CookieResolution;
                LightVolumeManager.CustomTexturesHeight = (int)CookieResolution;
                LightVolumeManager.ShadowTexturesWidth = (int)ShadowResolution;
                LightVolumeManager.ShadowTexturesHeight = (int)ShadowResolution;
                LightVolumeManager.CubemapFaceMaterial = GetCubemapFaceMaterial();
#endif
                SyncBaseTextureMetadataToUdon();

                if (LightVolumes.Count != 0) {
                    var instances = GetLightVolumeInstances();
                    UdonBehaviour[] lightVolumeInstances = new UdonBehaviour[instances.Length];
                    for (int i = 0; i < instances.Length; i++) {
                        lightVolumeInstances[i] = instances[i].GetComponent<UdonBehaviour>();
                    }
                    _lightVolumeManagerBehaviour.SetProgramVariable("LightVolumeInstances", lightVolumeInstances);
                }

                if (PointLightVolumes.Count != 0) {
                    var instances = GetPointLightVolumeInstances();
                    UdonBehaviour[] pointLightVolumeInstances = new UdonBehaviour[instances.Length];
                    for (int i = 0; i < instances.Length; i++) {
                        pointLightVolumeInstances[i] = instances[i].GetComponent<UdonBehaviour>();
                    }
                    _lightVolumeManagerBehaviour.SetProgramVariable("PointLightVolumeInstances", pointLightVolumeInstances);
                }
                // General setup changes must not rebuild point light texture arrays; explicit Reinitialize* paths sync point light texture sources first
                _lightVolumeManagerBehaviour.SendCustomEvent(AutoUpdateVolumes || AutoUpdateTextures ? "RequestUpdateVolumes" : "UpdateVolumes");

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
                LightVolumeManager.ForceSceneLighting = ForceSceneLighting;
#if UNITY_EDITOR
                LightVolumeManager.CustomTexturesWidth = (int)CookieResolution;
                LightVolumeManager.CustomTexturesHeight = (int)CookieResolution;
                LightVolumeManager.CubemapFaceMaterial = GetCubemapFaceMaterial();
                RefreshAtlasOutput();
#endif

                if (LightVolumes.Count != 0) LightVolumeManager.LightVolumeInstances = GetLightVolumeInstances();

                if (PointLightVolumes.Count != 0) {
                    LightVolumeManager.PointLightVolumeInstances = GetPointLightVolumeInstances();
                }

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

            LightVolumeInstance[] volumes = new LightVolumeInstance[sortedCount];
            for (int i = 0; i < sortedCount; i++) volumes[i] = sortedData[i].LightVolumeInstance;
            return volumes;
        }

        // Builds Point Light Volume runtime instances from the current authoring list.
        private PointLightVolumeInstance[] GetPointLightVolumeInstances() {
            List<PointLightVolumeInstance> list = new List<PointLightVolumeInstance>();
            int count = PointLightVolumes.Count;
            for (int i = 0; i < count; i++) {
                if(PointLightVolumes[i] == null || PointLightVolumes[i].PointLightVolumeInstance == null) continue;
                list.Add(PointLightVolumes[i].PointLightVolumeInstance);
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

        // Returns the fixed texture format used for baked shadow map cubemaps
        public TextureFormat GetShadowMapBakeFormat() {
            return TextureFormat.RHalf;
        }

        // Bakes all requested per-light shadow maps
        public void BakeShadowMaps() {
#if UNITY_EDITOR
            bool isRebaked = false;
            for (int i = 0; i < PointLightVolumes.Count; i++) {
                PointLightVolume pointLightVolume = PointLightVolumes[i];
                if (pointLightVolume == null || !pointLightVolume.Shadows || !pointLightVolume.RebakeShadows) continue;
                bool isBaked = pointLightVolume.BakeShadowMap($"| {pointLightVolume.gameObject.name} ({i}/{PointLightVolumes.Count})", false);
                isRebaked = isRebaked || isBaked;
            }
            if (isRebaked) ReinitializeShadowTextures();
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

        public enum Downscale {
            None = 0,
            x2 = 1,
            x4 = 2,
            x8 = 3
        }

    }
}
