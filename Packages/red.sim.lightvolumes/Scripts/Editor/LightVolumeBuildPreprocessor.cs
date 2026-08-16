using System;
using System.Collections.Generic;
#if UDONSHARP
using UdonSharp;
using UdonSharpEditor;
#endif
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UDONSHARP
using VRC.Udon;
using VRC.SDKBase.Editor.BuildPipeline;
#endif

namespace VRCLightVolumes {
    // Prepares Udon runtime resources and canonical data for the primary Manager, then strips build-only authoring references from Unity's temporary player scene.
    [InitializeOnLoad]
    internal static class LightVolumePreprocessor {

        private const string CubemapFaceShaderName = "Hidden/CubeFace";
        private const string ShadowDepthEncodeShaderName = "Hidden/VRCLV/PointLightShadowDepthEncode";
        private const string ShadowBlurShaderName = "Hidden/VRCLV/PointLightShadowRuntimeBlur";
        private const string ClusteringShaderName = "Hidden/VRCLV/FroxelClusteringBuild";

        private static readonly List<LightVolumeInstance> _lightVolumeBuffer = new List<LightVolumeInstance>();
        private static readonly List<PointLightVolumeInstance> _pointLightBuffer = new List<PointLightVolumeInstance>();
        private static readonly List<PointLightShadowRuntimeBaker> _shadowBakerBuffer = new List<PointLightShadowRuntimeBaker>();

        // Runtime materials cannot be constructed by Udon, so prepare temporary editor copies before play mode.
        static LightVolumePreprocessor() {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
            EditorApplication.quitting += Shutdown;
        }

        // Removes editor lifecycle callbacks before this editor domain ends.
        private static void Shutdown() {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= Shutdown;
            EditorApplication.quitting -= Shutdown;
            ClearBuffers();
        }

        [PostProcessScene]
        // Prepares runtime dependencies and strips heavy authoring references from Unity's temporary build scene.
        private static void OnPostProcessScene() {
            if (!BuildPipeline.isBuildingPlayer) return;
            GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects();
            PrepareRuntimeDependencies(roots, false, FindPrimaryManager(roots));
        }

        // Prepares the same single Manager before Play Mode (serialized Udon variables) and after entry
        // (the live Udon heap), then removes its temporary copies after exit.
        private static void OnPlayModeStateChanged(PlayModeStateChange state) {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.EnteredPlayMode)
                PreparePrimaryRuntimeDependencies();
            else if (state == PlayModeStateChange.EnteredEditMode)
                ClearPrimaryRuntimeDependencies();
        }

        // Prepares runtime-only materials and cameras for the primary Manager's scene.
        private static void PreparePrimaryRuntimeDependencies() {
            LightVolumeManager manager = LightVolumeManagerEditorBackend.GetPrimaryManager();
            if (manager != null) PrepareRuntimeDependencies(manager.gameObject.scene.GetRootGameObjects(), true, manager);
        }

        // Applies Android-safe settings to active EXR point-light projection textures.
        internal static void PrepareProjectionTextureImportsForOpenScenes() {
            for (int i = 0; i < SceneManager.sceneCount; i++) {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded) PrepareProjectionTextureImports(scene.GetRootGameObjects());
            }
        }

        // Removes temporary play-mode dependencies from the primary Manager's scene.
        private static void ClearPrimaryRuntimeDependencies() {
            LightVolumeManager manager = LightVolumeManagerEditorBackend.GetPrimaryManager();
            if (manager != null) ClearRuntimeDependencies(manager.gameObject.scene.GetRootGameObjects(), manager);
        }

        // Canonicalizes build data and prepares Manager, projection and shadow runtime dependencies.
        private static void PrepareRuntimeDependencies(GameObject[] roots, bool editorTemporary, LightVolumeManager manager) {
            if (manager == null) return;
            if (editorTemporary) PrepareProjectionTextureImports(roots);
            else CanonicalizeBuildScene(roots, manager);

            Shader cubemapFaceShader = Shader.Find(CubemapFaceShaderName);
            Shader shadowDepthEncodeShader = Shader.Find(ShadowDepthEncodeShaderName);
            Shader shadowBlurShader = Shader.Find(ShadowBlurShaderName);
            Shader clusteringShader = Shader.Find(ClusteringShaderName);
            PrepareManagerRuntimeDependencies(manager, cubemapFaceShader, shadowDepthEncodeShader, shadowBlurShader, clusteringShader, editorTemporary);

            for (int i = 0; i < roots.Length; i++) {
                GameObject root = roots[i];
                if (root == null) continue;

                _pointLightBuffer.Clear();
                root.GetComponentsInChildren(true, _pointLightBuffer);
                for (int j = 0; j < _pointLightBuffer.Count; j++) {
                    PreparePointLightForRuntime(_pointLightBuffer[j], manager, editorTemporary);
                }

                // External runtime bakers need the same local point-light dependencies even without Bake In Game.
                _shadowBakerBuffer.Clear();
                root.GetComponentsInChildren(true, _shadowBakerBuffer);
                for (int j = 0; j < _shadowBakerBuffer.Count; j++) {
                    PointLightShadowRuntimeBaker baker = _shadowBakerBuffer[j];
                    if (baker != null) PreparePointLightRuntimeShadowDependencies(baker.TargetPointLightVolume, manager);
                }
            }

            // Manager resources are shared by every runtime shadow light. Publish them once after
            // all dependencies are prepared instead of repeating the same Udon writes per light.
            ApplyManagerRuntimeDependencies(manager);

            if (!editorTemporary) {
                ClearManagerBuildOnlySerializedReferences(manager);
                for (int i = 0; i < roots.Length; i++) ClearBuildOnlySerializedReferences(roots[i]);
            }

            ClearBuffers();
        }

        // Rebuild every runtime field once on Unity's temporary build-scene copy. Editor and play-mode scenes keep the event-driven authoring path and are never changed here.
        private static void CanonicalizeBuildScene(GameObject[] roots, LightVolumeManager manager) {
            // Apply target-dependent manager settings before child data uses those settings.
            LightVolumeManagerEditorBackend.ApplySettings(manager, false, updateVolumes: false);

            // Canonicalize every child without rebuilding its manager per point light.
            for (int i = 0; i < roots.Length; i++) {
                GameObject root = roots[i];
                if (root == null) continue;

                _lightVolumeBuffer.Clear();
                root.GetComponentsInChildren(true, _lightVolumeBuffer);
                for (int j = 0; j < _lightVolumeBuffer.Count; j++) {
                    LightVolumeInstance volume = _lightVolumeBuffer[j];
                    LightVolumeTools.ApplyRuntimeState(volume, false);
                    LightVolumeManagerEditorBackend.CopyProxyToUdon(volume);
                }

                _pointLightBuffer.Clear();
                root.GetComponentsInChildren(true, _pointLightBuffer);
                for (int j = 0; j < _pointLightBuffer.Count; j++) {
                    PointLightVolumeInstance pointLight = _pointLightBuffer[j];
                    if (pointLight == null) continue;
                    bool customTexturesChanged = pointLight.HasEditorCustomTextureChanges();
                    bool shadowTexturesChanged = pointLight.HasEditorShadowTextureChanges();
                    pointLight.EditorApplyAuthoringData(customTexturesChanged, shadowTexturesChanged, false);
                    pointLight.CacheEditorObservedValues();
                    LightVolumeManagerEditorBackend.CopyProxyToUdon(pointLight);
                }
            }

            // Publish the completed registries once after every child has been canonicalized.
            manager.UpdateVolumes();
            LightVolumeManagerEditorBackend.CopyProxyToUdon(manager);

            ClearBuffers();
        }

        // Applies mobile-safe HDR import settings to active projection texture assets.
        private static void PrepareProjectionTextureImports(GameObject[] roots) {
            for (int i = 0; i < roots.Length; i++) {
                GameObject root = roots[i];
                if (root == null) continue;
                _pointLightBuffer.Clear();
                root.GetComponentsInChildren(true, _pointLightBuffer);
                for (int j = 0; j < _pointLightBuffer.Count; j++) {
                    PointLightVolumeInstance pointLight = _pointLightBuffer[j];
                    if (pointLight != null) LVUtils.TextureSetLinearHDRAndroidImport(pointLight.GetCustomTexture());
                }
            }
            _pointLightBuffer.Clear();
        }

        // Destroys temporary materials and clears runtime-shadow references below the supplied roots.
        private static void ClearRuntimeDependencies(GameObject[] roots, LightVolumeManager manager) {
            if (manager != null) ClearManagerMaterials(manager);
            for (int i = 0; i < roots.Length; i++) {
                GameObject root = roots[i];
                if (root == null) continue;
                _pointLightBuffer.Clear();
                root.GetComponentsInChildren(true, _pointLightBuffer);
                for (int j = 0; j < _pointLightBuffer.Count; j++) ClearPointLightRuntimeShadowDependencies(_pointLightBuffer[j]);
            }
            _pointLightBuffer.Clear();
        }

        // Creates all runtime materials and shadow-camera dependencies owned by one Manager.
        private static void PrepareManagerRuntimeDependencies(LightVolumeManager manager, Shader cubemapFaceShader, Shader depthEncodeShader, Shader blurShader, Shader clusteringShader, bool editorTemporary) {
            if (manager == null) return;
            manager.EnsureRuntimeShadowCamera();
            manager.RuntimeShadowDepthEncodeMaterial = CreateRuntimeMaterialInstance(depthEncodeShader, manager.RuntimeShadowDepthEncodeMaterial, manager.name + "_ShadowDepthEncodeRuntime", editorTemporary);
            manager.RuntimeShadowBlurMaterial = CreateRuntimeMaterialInstance(blurShader, manager.RuntimeShadowBlurMaterial, manager.name + "_ShadowBlurRuntime", editorTemporary);
            ResetManagerRuntimeShadowBlurState(manager);
            manager.CubemapFaceMaterial = CreateRuntimeMaterialInstance(cubemapFaceShader, manager.CubemapFaceMaterial, manager.name + "_CubemapFaceRuntime", editorTemporary);
            manager.ClusteringMaterial = CreateRuntimeMaterialInstance(clusteringShader, manager.ClusteringMaterial, manager.name + "_ClusteringRuntime", editorTemporary);
        }

        // Resolves build-safe projection and optional Bake In Game state for one Point Light Volume.
        private static void PreparePointLightForRuntime(PointLightVolumeInstance pointLight, LightVolumeManager manager, bool editorTemporary) {
            if (pointLight == null || pointLight.LightVolumeManager != manager) return;
            bool bakeInGame = pointLight.Shadows && pointLight.BakeInGame;
            if (!editorTemporary && pointLight.BakeInGame != bakeInGame) pointLight.BakeInGame = bakeInGame;
            pointLight.RuntimeShadowResolution = PointLightShadowBaker.ResolveShadowBakeResolution(pointLight, manager);
            pointLight.RuntimeShadowBlurSamplePreset = Mathf.Clamp(pointLight.RuntimeShadowBlurSamplePreset, 0, 2);
            if (bakeInGame) {
                pointLight.RuntimeShadowDirectOutput = false;
                PreparePointLightRuntimeShadowDependencies(pointLight, manager);
            } else {
#if UDONSHARP
                UdonBehaviour udonBehaviour = GetBackingUdonBehaviour(pointLight);
                ApplyPointLightRuntimeShadowBakeSettings(pointLight, udonBehaviour);
                ApplyPointLightRuntimeShadowSource(pointLight, udonBehaviour);
#endif
            }
            ApplyPointLightRuntimeCustomSource(pointLight);
        }

        // Assigns shared Manager shadow resources to a light that can bake shadows at runtime.
        private static void PreparePointLightRuntimeShadowDependencies(PointLightVolumeInstance pointLight, LightVolumeManager manager) {
            if (pointLight == null || manager == null || pointLight.LightVolumeManager != manager) return;
            if (pointLight.Shadows && pointLight.BakeInGame) ClearPointLightRuntimeShadowSource(pointLight);
            pointLight.RuntimeShadowCamera = manager.RuntimeShadowCamera;
            pointLight.RuntimeShadowDepthEncodeMaterial = manager.RuntimeShadowDepthEncodeMaterial;
            pointLight.RuntimeShadowBlurMaterial = manager.RuntimeShadowBlurMaterial;
            ApplyPointLightRuntimeShadowDependencies(pointLight);
        }

        // Publishes prepared Manager materials and clustering settings to its backing Udon behaviour.
        private static void ApplyManagerRuntimeDependencies(LightVolumeManager manager) {
            if (manager == null) return;
#if UDONSHARP
            UdonBehaviour udonBehaviour = GetBackingUdonBehaviour(manager);
            if (udonBehaviour == null) return;
            SetUdonProgramVariable(udonBehaviour, "CubemapFaceMaterial", manager.CubemapFaceMaterial);
            SetUdonProgramVariable(udonBehaviour, "RuntimeShadowCamera", manager.RuntimeShadowCamera);
            SetUdonProgramVariable(udonBehaviour, "RuntimeShadowDepthEncodeMaterial", manager.RuntimeShadowDepthEncodeMaterial);
            SetUdonProgramVariable(udonBehaviour, "RuntimeShadowBlurMaterial", manager.RuntimeShadowBlurMaterial);
            SetUdonProgramVariable(udonBehaviour, "RuntimeShadowBlurQualityPreset", manager.RuntimeShadowBlurQualityPreset);
            SetUdonProgramVariable(udonBehaviour, "RuntimeShadowBlurUniformKeyword", manager.RuntimeShadowBlurUniformKeyword);
            SetUdonProgramVariable(udonBehaviour, "RuntimeShadowBlurDirectKeyword", manager.RuntimeShadowBlurDirectKeyword);
            SetUdonProgramVariable(udonBehaviour, "RuntimeShadowBlurSphericalKeyword", manager.RuntimeShadowBlurSphericalKeyword);
            SetUdonProgramVariable(udonBehaviour, "Clustering", manager.Clustering);
            SetUdonProgramVariable(udonBehaviour, "FroxelDensity", manager.FroxelDensity);
            SetUdonProgramVariable(udonBehaviour, "FroxelSlices", manager.FroxelSlices);
            SetUdonProgramVariable(udonBehaviour, "FroxelCoarse", manager.FroxelCoarse);
            SetUdonProgramVariable(udonBehaviour, "ClusteringMinLights", manager.ClusteringMinLights);
            SetUdonProgramVariable(udonBehaviour, "ClusteringMaterial", manager.ClusteringMaterial);
#endif
        }

        // Publishes a Point Light Volume's runtime shadow dependencies to its backing Udon behaviour.
        private static void ApplyPointLightRuntimeShadowDependencies(PointLightVolumeInstance pointLight) {
            if (pointLight == null) return;
#if UDONSHARP
            UdonBehaviour udonBehaviour = GetBackingUdonBehaviour(pointLight);
            if (udonBehaviour == null) return;
            // Udon stores U# references as backing UdonBehaviours in both serialized data and the live heap.
            SetUdonProgramVariable(udonBehaviour, "LightVolumeManager", GetBackingUdonBehaviour(pointLight.LightVolumeManager));
            SetUdonProgramVariable(udonBehaviour, "RuntimeShadowCamera", pointLight.RuntimeShadowCamera);
            SetUdonProgramVariable(udonBehaviour, "RuntimeShadowDepthEncodeMaterial", pointLight.RuntimeShadowDepthEncodeMaterial);
            SetUdonProgramVariable(udonBehaviour, "RuntimeShadowBlurMaterial", pointLight.RuntimeShadowBlurMaterial);
            ApplyPointLightRuntimeShadowBakeSettings(pointLight, udonBehaviour);
            if (pointLight.Shadows && pointLight.BakeInGame) ApplyPointLightRuntimeShadowSource(pointLight, udonBehaviour);
#endif
        }

        // Destroys temporary Manager materials and clears their proxy and Udon references.
        private static void ClearManagerMaterials(LightVolumeManager manager) {
            if (manager == null) return;
            DestroyRuntimeMaterialInstance(manager.CubemapFaceMaterial);
            DestroyRuntimeMaterialInstance(manager.RuntimeShadowDepthEncodeMaterial);
            DestroyRuntimeMaterialInstance(manager.RuntimeShadowBlurMaterial);
            DestroyRuntimeMaterialInstance(manager.ClusteringMaterial);
            manager.CubemapFaceMaterial = null;
            manager.RuntimeShadowDepthEncodeMaterial = null;
            manager.RuntimeShadowBlurMaterial = null;
            manager.ClusteringMaterial = null;
            ResetManagerRuntimeShadowBlurState(manager);
#if UDONSHARP
            UdonBehaviour udonBehaviour = GetBackingUdonBehaviour(manager);
            if (udonBehaviour == null) return;
            SetUdonProgramVariable(udonBehaviour, "CubemapFaceMaterial", null);
            SetUdonProgramVariable(udonBehaviour, "RuntimeShadowDepthEncodeMaterial", null);
            SetUdonProgramVariable(udonBehaviour, "RuntimeShadowBlurMaterial", null);
            SetUdonProgramVariable(udonBehaviour, "ClusteringMaterial", null);
            SetUdonProgramVariable(udonBehaviour, "RuntimeShadowBlurQualityPreset", -1);
            SetUdonProgramVariable(udonBehaviour, "RuntimeShadowBlurUniformKeyword", -1);
            SetUdonProgramVariable(udonBehaviour, "RuntimeShadowBlurDirectKeyword", -1);
            SetUdonProgramVariable(udonBehaviour, "RuntimeShadowBlurSphericalKeyword", -1);
#endif
        }

        // Clears play-mode-only dependencies from both the proxy and its live Udon heap.
        private static void ClearPointLightRuntimeShadowDependencies(PointLightVolumeInstance pointLight) {
            if (pointLight == null) return;
            pointLight.RuntimeShadowCamera = null;
            pointLight.RuntimeShadowDepthEncodeMaterial = null;
            pointLight.RuntimeShadowBlurMaterial = null;
#if UDONSHARP
            UdonBehaviour udonBehaviour = GetBackingUdonBehaviour(pointLight);
            if (udonBehaviour == null) return;
            SetUdonProgramVariable(udonBehaviour, "RuntimeShadowCamera", null);
            SetUdonProgramVariable(udonBehaviour, "RuntimeShadowDepthEncodeMaterial", null);
            SetUdonProgramVariable(udonBehaviour, "RuntimeShadowBlurMaterial", null);
#endif
        }

        // Finds the first eligible Manager below build-scene roots in Unity hierarchy order.
        private static LightVolumeManager FindPrimaryManager(GameObject[] roots) {
            for (int i = 0; i < roots.Length; i++) {
                GameObject root = roots[i];
                if (root == null) continue;
                LightVolumeManager[] managers = root.GetComponentsInChildren<LightVolumeManager>(true);
                for (int j = 0; j < managers.Length; j++) {
                    LightVolumeManager manager = managers[j];
                    if (manager == null || manager.CompareTag("EditorOnly")) continue;
                    return manager;
                }
            }
            return null;
        }

        // Invalidates cached runtime shadow blur keyword state after material replacement.
        private static void ResetManagerRuntimeShadowBlurState(LightVolumeManager manager) {
            if (manager == null) return;
            manager.RuntimeShadowBlurQualityPreset = -1;
            manager.RuntimeShadowBlurUniformKeyword = -1;
            manager.RuntimeShadowBlurDirectKeyword = -1;
            manager.RuntimeShadowBlurSphericalKeyword = -1;
        }

        // Clears editor-baked shadow state before a Bake In Game source is generated.
        private static void ClearPointLightRuntimeShadowSource(PointLightVolumeInstance pointLight) {
            if (pointLight == null) return;
            pointLight.ShadowMapTexture = null;
            pointLight.ShadowMapMaterial = null;
            pointLight.AutoUpdateShadowMap = false;
            pointLight.ShadowMapID = -1f;
            pointLight.ShadowMapTextureIsCubemap = false;
            pointLight.ShadowMapTextureHasDepthSlices = false;
        }

#if UDONSHARP
        // Publishes resolved shadow source metadata to a Point Light Volume's Udon heap.
        private static void ApplyPointLightRuntimeShadowSource(PointLightVolumeInstance pointLight, UdonBehaviour udonBehaviour) {
            if (pointLight == null || udonBehaviour == null) return;
            SetUdonProgramVariable(udonBehaviour, "ShadowMapTexture", pointLight.ShadowMapTexture);
            SetUdonProgramVariable(udonBehaviour, "ShadowMapMaterial", pointLight.ShadowMapMaterial);
            SetUdonProgramVariable(udonBehaviour, "AutoUpdateShadowMap", pointLight.AutoUpdateShadowMap);
            SetUdonProgramVariable(udonBehaviour, "ShadowMapID", pointLight.ShadowMapID);
            SetUdonProgramVariable(udonBehaviour, "ShadowMapTextureIsCubemap", pointLight.ShadowMapTextureIsCubemap);
            SetUdonProgramVariable(udonBehaviour, "ShadowMapTextureHasDepthSlices", pointLight.ShadowMapTextureHasDepthSlices);
            SetUdonProgramVariable(udonBehaviour, "ShadowMapUsesCubemap", pointLight.ShadowMapUsesCubemap);
        }

        // Publishes runtime shadow bake settings and excluded renderers to a Point Light Volume's Udon heap.
        private static void ApplyPointLightRuntimeShadowBakeSettings(PointLightVolumeInstance pointLight, UdonBehaviour udonBehaviour) {
            if (pointLight == null || udonBehaviour == null) return;
            SetUdonProgramVariable(udonBehaviour, "BakeInGame", pointLight.BakeInGame);
            SetUdonProgramVariable(udonBehaviour, "ExclusionMask", pointLight.ExclusionMask ?? Array.Empty<Renderer>());
            SetUdonProgramVariable(udonBehaviour, "RuntimeShadowResolution", pointLight.RuntimeShadowResolution);
            SetUdonProgramVariable(udonBehaviour, "RuntimeShadowBlurSamplePreset", pointLight.RuntimeShadowBlurSamplePreset);
            SetUdonProgramVariable(udonBehaviour, "RuntimeShadowSphericalBlur", pointLight.RuntimeShadowSphericalBlur);
            SetUdonProgramVariable(udonBehaviour, "RuntimeShadowDirectOutput", false);
            SetUdonProgramVariable(udonBehaviour, "ShadowBakeResolution", pointLight.ShadowBakeResolution);
        }
#endif

        // Publishes the resolved projection source and layout to a Point Light Volume's Udon heap.
        private static void ApplyPointLightRuntimeCustomSource(PointLightVolumeInstance pointLight) {
            if (pointLight == null) return;
#if UDONSHARP
            UdonBehaviour udonBehaviour = GetBackingUdonBehaviour(pointLight);
            if (udonBehaviour == null) return;
            SetUdonProgramVariable(udonBehaviour, "CustomTexture", pointLight.CustomTexture);
            SetUdonProgramVariable(udonBehaviour, "CustomTextureMaterial", pointLight.CustomTextureMaterial);
            SetUdonProgramVariable(udonBehaviour, "ProjectionType", pointLight.ProjectionType);
            SetUdonProgramVariable(udonBehaviour, "ProjectionMode", pointLight.ProjectionMode);
            SetUdonProgramVariable(udonBehaviour, "AutoUpdateCustomTexture", pointLight.AutoUpdateCustomTexture);
            SetUdonProgramVariable(udonBehaviour, "CustomTextureIsCubemap", pointLight.CustomTextureIsCubemap);
            SetUdonProgramVariable(udonBehaviour, "CustomTextureHasDepthSlices", pointLight.CustomTextureHasDepthSlices);
#endif
        }

        // Runtime fields are published first; only per-volume authoring references are stripped afterward.
        private static void ClearBuildOnlySerializedReferences(GameObject root) {
            if (root == null) return;

            _lightVolumeBuffer.Clear();
            root.GetComponentsInChildren(true, _lightVolumeBuffer);
            for (int i = 0; i < _lightVolumeBuffer.Count; i++) ClearLightVolumeBuildOnlySerializedReferences(_lightVolumeBuffer[i]);

            _pointLightBuffer.Clear();
            root.GetComponentsInChildren(true, _pointLightBuffer);
            for (int i = 0; i < _pointLightBuffer.Count; i++) ClearPointLightBuildOnlySerializedReferences(_pointLightBuffer[i]);
        }

        // Strips base-atlas, generated arrays and post-processor authoring references from a build copy.
        private static void ClearManagerBuildOnlySerializedReferences(LightVolumeManager manager) {
            if (manager == null) return;
            Texture finalAtlas = manager.LightVolumeAtlas != null ? manager.LightVolumeAtlas : manager.LightVolumeAtlasBase;
            RenderTexture[] noPostProcessorTargets = Array.Empty<RenderTexture>();
            Material[] noPostProcessorMaterials = Array.Empty<Material>();
            string[] noPostProcessorTextureNames = Array.Empty<string>();
            manager.LightVolumeAtlas = finalAtlas;
            manager.LightVolumeAtlasBase = null;
            manager.CustomTextures = null;
            manager.ShadowTextures = null;
            manager.AtlasPostProcessorTargets = noPostProcessorTargets;
            manager.AtlasPostProcessorMaterials = noPostProcessorMaterials;
            manager.AtlasPostProcessorTextureNames = noPostProcessorTextureNames;
#if UDONSHARP
            UdonBehaviour udonBehaviour = GetBackingUdonBehaviour(manager);
            if (udonBehaviour == null) return;
            SetUdonProgramVariable(udonBehaviour, "LightVolumeAtlas", finalAtlas);
            SetUdonProgramVariable(udonBehaviour, "LightVolumeAtlasBase", null);
            SetUdonProgramVariable(udonBehaviour, "CustomTextures", null);
            SetUdonProgramVariable(udonBehaviour, "ShadowTextures", null);
            SetUdonProgramVariable(udonBehaviour, "AtlasPostProcessorTargets", noPostProcessorTargets);
            SetUdonProgramVariable(udonBehaviour, "AtlasPostProcessorMaterials", noPostProcessorMaterials);
            SetUdonProgramVariable(udonBehaviour, "AtlasPostProcessorTextureNames", noPostProcessorTextureNames);
#endif
        }

        // Strips baked source textures and Bakery helpers after their data has entered the atlas.
        private static void ClearLightVolumeBuildOnlySerializedReferences(LightVolumeInstance volume) {
            if (volume == null) return;
            volume.Texture0 = null;
            volume.Texture1 = null;
            volume.Texture2 = null;
            volume.BakeryVolume = null;
#if UDONSHARP
            UdonBehaviour udonBehaviour = GetBackingUdonBehaviour(volume);
            if (udonBehaviour == null) return;
            SetUdonProgramVariable(udonBehaviour, "Texture0", null);
            SetUdonProgramVariable(udonBehaviour, "Texture1", null);
            SetUdonProgramVariable(udonBehaviour, "Texture2", null);
            SetUdonProgramVariable(udonBehaviour, "BakeryVolume", null);
#endif
        }

        // Strips duplicate authoring projection and shadow references after runtime fields are published.
        private static void ClearPointLightBuildOnlySerializedReferences(PointLightVolumeInstance pointLight) {
            if (pointLight == null) return;

            // Active runtime sources already point at the selected authoring source.
            ApplyPointLightRuntimeCustomSource(pointLight);
#if UDONSHARP
            ApplyPointLightRuntimeShadowSource(pointLight, GetBackingUdonBehaviour(pointLight));
#endif
            pointLight.FalloffLUT = null;
            pointLight.Cookie = null;
            pointLight.Cubemap = null;
            pointLight.ShadowMap = null;

#if UDONSHARP
            UdonBehaviour udonBehaviour = GetBackingUdonBehaviour(pointLight);
            if (udonBehaviour == null) return;
            SetUdonProgramVariable(udonBehaviour, "FalloffLUT", null);
            SetUdonProgramVariable(udonBehaviour, "Cookie", null);
            SetUdonProgramVariable(udonBehaviour, "Cubemap", null);
            SetUdonProgramVariable(udonBehaviour, "ShadowMap", null);
#endif
        }

        // Reuses or creates a non-asset material for one required runtime shader.
        private static Material CreateRuntimeMaterialInstance(Shader shader, Material existing, string name, bool editorTemporary) {
            if (shader == null) {
                DestroyRuntimeMaterialInstance(existing);
                return null;
            }
            bool canReuse = existing != null && !AssetDatabase.Contains(existing) && existing.shader == shader;
            Material material = canReuse ? existing : new Material(shader);
            if (!canReuse) DestroyRuntimeMaterialInstance(existing);
            material.name = name;
            material.hideFlags = editorTemporary ? HideFlags.HideAndDontSave : HideFlags.None;
            return material;
        }

        // Destroys a generated runtime material while preserving imported material assets.
        private static void DestroyRuntimeMaterialInstance(Material material) {
            if (material == null || AssetDatabase.Contains(material)) return;
            UnityEngine.Object.DestroyImmediate(material);
        }

        // Clears reusable component buffers after hierarchy traversal.
        private static void ClearBuffers() {
            _lightVolumeBuffer.Clear();
            _pointLightBuffer.Clear();
            _shadowBakerBuffer.Clear();
        }

#if UDONSHARP
        // Resolves a UdonSharp proxy to its backing UdonBehaviour.
        private static UdonBehaviour GetBackingUdonBehaviour(Component proxy) {
            UdonSharpBehaviour behaviour = proxy as UdonSharpBehaviour;
            return behaviour != null ? UdonSharpEditorUtility.GetBackingUdonBehaviour(behaviour) : null;
        }

        // Writes to the live Udon heap in play mode or serialized public variables in edit mode.
        private static void SetUdonProgramVariable(UdonBehaviour udonBehaviour, string variableName, object value) {
            if (udonBehaviour == null) return;
            // Edit/build instances have no live program; Play Mode writes must reach the initialized heap.
            if (Application.isPlaying) udonBehaviour.SetProgramVariable(variableName, value);
            else udonBehaviour.publicVariables.TrySetVariableValue(variableName, value);
        }
#endif
    }

    internal sealed class LightVolumeTextureImportBuildPreprocessor : IPreprocessBuildWithReport {
        public int callbackOrder => -1000;
        // Applies mobile-safe projection texture import settings before a player build begins.
        public void OnPreprocessBuild(BuildReport report) {
            LightVolumePreprocessor.PrepareProjectionTextureImportsForOpenScenes();
        }
    }

#if UDONSHARP
    // Runs immediately before UdonSharp and performs a read-only proxy/backing integrity check.
    internal sealed class LightVolumeSdkBuildIntegrityPreflight : IVRCSDKBuildRequestedCallback {
        public int callbackOrder => 90;

        // Blocks scene builds whose unified proxies and backing Udon behaviours are inconsistent.
        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType) {
            if (requestedBuildType != VRCSDKRequestedBuildType.Scene) return true;
            if (LightVolumeMigration.ValidateLoadedSceneUdonPairs(out int issueCount, out string issueSummary)) return true;
            Debug.LogError("[LightVolumes] Build blocked: " + issueCount + " Light Volume setup issue(s) found. " + issueSummary + ". Fix the reported setup, save the affected scene(s), and try building again.");
            return false;
        }
    }
#endif
}
