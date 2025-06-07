using UnityEngine;
using System;
using System.Collections.Generic;
using UnityEditor;

#if UNITY_EDITOR
using Unity.EditorCoroutines.Editor;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
#endif

namespace VRCLightVolumes {
    [ExecuteAlways]
    public class LightVolumeSetup : MonoBehaviour {

        [SerializeField] public List<LightVolume> LightVolumes = new List<LightVolume>();
        [SerializeField] public List<float> LightVolumesWeights = new List<float>();

        [SerializeField] public List<PointLightVolume> PointLightVolumes = new List<PointLightVolume>();

        [Header("Point Light Volumes")]
        public TextureArrayResolution Resolution = TextureArrayResolution._128x128;
        public TextureArrayFormat Format = TextureArrayFormat.RGBAHalf;

        [Header("Baking")]
        [Tooltip("Bakery usually gives better results and works faster.")]
#if BAKERY_INCLUDED
        public Baking BakingMode = Baking.Bakery;
#else
        public Baking BakingMode = Baking.Progressive;
#endif
        [Tooltip("Removes baked noise in Light Volumes but may slightly reduce sharpness. Recommended to keep it enabled.")]
        public bool Denoise = true;
        [Tooltip("Automatically fixes Bakery's \"burned\" light probes after a scene bake. But decreases their contrast slightly.")]
        public bool FixLightProbesL1 = true;
        [Header("Visuals")]
        [Tooltip("When enabled, areas outside Light Volumes fall back to light probes. Otherwise, the Light Volume with the smallest weight is used as fallback. It also improves performance.")]
        public bool LightProbesBlending = true;
        [Tooltip("Disables smooth blending with areas outside Light Volumes. Use it if your entire scene's play area is covered by Light Volumes. It also improves performance.")]
        public bool SharpBounds = true;
        [Tooltip("Automatically updates any volumes data in runtime: Enabling/Disabling, Color, Edge Smoothing, all the global settings and more. Position, Rotation and Scale gets updated only for volumes that are marked dynamic.")]
        public bool AutoUpdateVolumes = false;
        [Tooltip("Limits the maximum number of additive volumes that can affect a single pixel. If you have many dynamic additive volumes that may overlap, it's good practice to limit overdraw to maintain performance.")]
        [Min(1)]public int AdditiveMaxOverdraw = 4;
        [Tooltip("The minimum brightness at a point due to lighting from an area light, before the area light is culled. Larger values will result in better performance, but may cause artifacts. Setting this to 0 disables distance-based culling for area lights.")]
        [Range(0.0f, 0.1f)] public float AreaLightBrightnessCutoff = 0.005f;
        
        [SerializeField] public List<LightVolumeData> LightVolumeDataList = new List<LightVolumeData>();

        public bool IsBakeryMode => BakingMode == Baking.Bakery; // Just a shortcut
        public LightVolumeManager LightVolumeManager;

        public Baking _bakingModePrev;

        public bool IsLegacyUVWConverted = false; // Is legacy UVW fix applied. Only need to do it once, so it's a flag for that

        private TextureArrayResolution _resolutionPrev = TextureArrayResolution._128x128;
        private TextureArrayFormat _formatPrev = TextureArrayFormat.RGBAHalf;

        public void RefreshVolumesList() {
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

        // Generates LUT array based on all the LUT Textures2D provided in PointLightVolumes
        List<PointLightVolume> _customTexPointVolumes = new List<PointLightVolume>();
        public void GenerateCustomTexturesArray() {

            // Cubemap Textures - store first
            List<Texture> cubeTextures = new List<Texture>(); 
            List<PointLightVolume> cubePLVs = new List<PointLightVolume>();

            // Other texture goes next
            List<Texture> singleTextures = new List<Texture>();
            List<PointLightVolume> singlePLVs = new List<PointLightVolume>();

            int count = PointLightVolumes.Count;
            for (int i = 0; i < count; i++) {
                Texture tex = PointLightVolumes[i].GetCustomTexture();
                if (tex == null) continue;
                if(tex.GetType() == typeof(Cubemap)) {
                    cubeTextures.Add(PointLightVolumes[i].Cubemap);
                    cubePLVs.Add(PointLightVolumes[i]);
                } else if(tex.GetType() == typeof(Texture2D)) {
                    singleTextures.Add(PointLightVolumes[i].FalloffLUT);
                    singlePLVs.Add(PointLightVolumes[i]);
                }
            }

            // Merging lists
            List<Texture> textures = new List<Texture>();
            textures.AddRange(cubeTextures);
            textures.AddRange(singleTextures);
            _customTexPointVolumes.Clear();
            _customTexPointVolumes.AddRange(cubePLVs);
            _customTexPointVolumes.AddRange(singlePLVs);

            EditorCoroutineUtility.StartCoroutine(TextureArrayGenerator.CreateTexture2DArrayAsync(textures, (int)Resolution, (TextureFormat)Format, (texArray, ids) => {

                if (texArray != null) {
                    for (int i = 0; i < ids.Length; i++) {
                        _customTexPointVolumes[i].CustomID = ids[i];
                        _customTexPointVolumes[i].SyncUdonScript();
                    }
                }
                LightVolumeManager.CustomTextures = texArray;
                LightVolumeManager.CubemapsCount = cubeTextures.Count;
                if (texArray != null) LVUtils.SaveAsAsset(texArray, $"{Path.GetDirectoryName(SceneManager.GetActiveScene().path)}/{SceneManager.GetActiveScene().name}/PointLightVolumeArray.asset");

            }), this);

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

        }


#if BAKERY_INCLUDED

        // On Bakery Started baking
        private void OnBakeryStartedRender(object sender, EventArgs e) {
            if (BakingMode != Baking.Bakery) {
                BakingMode = Baking.Bakery;
            }

            // Attempt to fix a bakery bug
            var volumes = FindObjectsOfType<LightVolume>(true);
            for (int i = 0; i < volumes.Length; i++) {
                volumes[i].SetupBakeryDependencies();
            }

        }

        // On Bakery Finished baking
        private void OnBakeryFinishedRender(object sender, EventArgs e) {
            if (BakingMode != Baking.Bakery) {
                BakingMode = Baking.Bakery;
            }
            LightVolume[] volumes = FindObjectsByType<LightVolume>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < volumes.Length; i++) {
                if (volumes[i].Bake && volumes[i].LightVolumeInstance != null) {
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
            if (BakingMode != Baking.Progressive) {
                BakingMode = Baking.Progressive;
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

            if (BakingMode != Baking.Progressive) {
                BakingMode = Baking.Progressive;
            }
            LightVolume[] volumes = FindObjectsByType<LightVolume>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < volumes.Length; i++) {
                if (volumes[i].Bake) {
                    volumes[i].Save3DTextures(i);
                    volumes[i].RemoveAdditionalProbes(i);
                    if (volumes[i].LightVolumeInstance != null) volumes[i].LightVolumeInstance.InvBakedRotation = Quaternion.Inverse(volumes[i].GetRotation());
                }
            }
            Debug.Log($"[LightVolumeSetup] Additional probes baking finished! Generating 3D Atlas...");
            GenerateAtlas();
            Debug.Log($"[LightVolumeSetup] Generating 3D Atlas finished!");

        }

        private void Update() {
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
            if (_resolutionPrev != Resolution || _formatPrev != Format) {
                _resolutionPrev = Resolution;
                _formatPrev = Format;
                GenerateCustomTexturesArray();
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

            if (LVUtils.IsInPrefabAsset(this) || LightVolumes.Count == 0) return;

            SetupDependencies();

            var atlas = Texture3DAtlasGenerator.CreateAtlas(LightVolumes.ToArray());
            if (atlas.Texture == null) return; // Return if atlas packing failed

            LightVolumeManager.LightVolumeAtlas = atlas.Texture;

            LightVolumeDataList.Clear();

            for (int i = 0; i < LightVolumes.Count; i++) {

                if (LightVolumes[i] == null) continue;
                var lightVolumeInstance = LightVolumes[i].LightVolumeInstance;

                if (lightVolumeInstance == null) continue;

                Vector3 scale = atlas.BoundsUvwMax[i * 3] - atlas.BoundsUvwMin[i * 3];
                Vector3 uvwMin0 = atlas.BoundsUvwMin[i * 3];
                Vector3 uvwMin1 = atlas.BoundsUvwMin[i * 3 + 1];
                Vector3 uvwMin2 = atlas.BoundsUvwMin[i * 3 + 2];

                lightVolumeInstance.BoundsUvwMin0 = new Vector4(uvwMin0.x, uvwMin0.y, uvwMin0.z, scale.x);
                lightVolumeInstance.BoundsUvwMin1 = new Vector4(uvwMin1.x, uvwMin1.y, uvwMin1.z, scale.y);
                lightVolumeInstance.BoundsUvwMin2 = new Vector4(uvwMin2.x, uvwMin2.y, uvwMin2.z, scale.z);

                // Legacy
                lightVolumeInstance.BoundsUvwMax0 = atlas.BoundsUvwMax[i * 3];
                lightVolumeInstance.BoundsUvwMax1 = atlas.BoundsUvwMax[i * 3 + 1];
                lightVolumeInstance.BoundsUvwMax2 = atlas.BoundsUvwMax[i * 3 + 2];

                LightVolumeDataList.Add(new LightVolumeData(i < LightVolumesWeights.Count ? LightVolumesWeights[i] : 0, lightVolumeInstance));

                LVUtils.MarkDirty(lightVolumeInstance);
            }

            LVUtils.SaveAsAsset(atlas.Texture, $"{Path.GetDirectoryName(SceneManager.GetActiveScene().path)}/{SceneManager.GetActiveScene().name}/LightVolumeAtlas.asset");

            SyncUdonScript();

        }

        // Looks for LightVolumeManager udon script and setups it if needed
        public void SetupDependencies() {
            if (LightVolumeManager == null && !TryGetComponent(out LightVolumeManager)) {
                LightVolumeManager = gameObject.AddComponent<LightVolumeManager>();
            }
        }

        // Fixes light probes baked with Bakery L1
        private static void FixLightProbes() {

            var probes = LightmapSettings.lightProbes;
            if (probes == null || probes.count == 0) {
                Debug.LogWarning("[LightVolumeSetup] No Light Probes found to fix.");
                return;
            }

            var shs = probes.bakedProbes;
            for (int i = 0; i < shs.Length; ++i) {
                shs[i] = LVUtils.LinearizeSH(shs[i]);
            }

            probes.bakedProbes = shs;
            EditorUtility.SetDirty(probes);
            EditorSceneManager.MarkAllScenesDirty();

            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveOpenScenes();

            Debug.Log($"[LightVolumeSetup] {shs.Length} Light Probes fixed!");

        }

#endif

        // Syncs udon LightVolumeManager script with this script
        public void SyncUdonScript() {

            if (LightVolumeManager == null) return;

            LightVolumeManager.AutoUpdateVolumes = AutoUpdateVolumes;
            LightVolumeManager.LightProbesBlending = LightProbesBlending;
            LightVolumeManager.SharpBounds = SharpBounds;
            LightVolumeManager.AdditiveMaxOverdraw = AdditiveMaxOverdraw;
            LightVolumeManager.AreaLightBrightnessCutoff = AreaLightBrightnessCutoff;

            if (LightVolumes.Count != 0) {
                LightVolumeManager.LightVolumeInstances = LightVolumeDataSorter.GetData(LightVolumeDataSorter.SortData(LightVolumeDataList));
            }

            if (PointLightVolumes.Count != 0) {
                LightVolumeManager.PointLightVolumeInstances = GetPointLightVolumeInstances();
            }

            LightVolumeManager.UpdateVolumes();

        }

        private PointLightVolumeInstance[] GetPointLightVolumeInstances() {
            List<PointLightVolumeInstance> list = new List<PointLightVolumeInstance>();
            int count = PointLightVolumes.Count;
            for (int i = 0; i < count; i++) {
                if(PointLightVolumes[i].PointLightVolumeInstance == null) continue;
                list.Add(PointLightVolumes[i].PointLightVolumeInstance);
            }
            return list.ToArray();
        }

        // Delete self in play mode
        private void Start() {
            if (Application.isPlaying) {
                Destroy(this);
            }
        }

        public enum Baking {
            Progressive,
            Bakery
        }

        public enum TextureArrayFormat {
            RGBA32 = 4,
            RGBAHalf = 17,
            RGBAFloat = 20
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

    }
}