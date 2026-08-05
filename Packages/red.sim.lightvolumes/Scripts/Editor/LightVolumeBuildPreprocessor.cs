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
    [InitializeOnLoad]
    internal static class LightVolumePreprocessor {
        private const string CubemapFaceShaderName = "Hidden/CubeFace";
        private const string ShadowDepthEncodeShaderName = "Hidden/VRCLV/PointLightShadowDepthEncode";
        private const string ShadowBlurShaderName = "Hidden/VRCLV/PointLightShadowRuntimeBlur";
        private const string ClusteringShaderName = "Hidden/VRCLV/FroxelClusteringBuild";
        private static readonly List<LightVolumeManager> _managerBuffer = new List<LightVolumeManager>();
        private static readonly List<LightVolumeInstance> _lightVolumeBuffer = new List<LightVolumeInstance>();
        private static readonly List<PointLightVolumeInstance> _pointLightBuffer = new List<PointLightVolumeInstance>();
        private static readonly List<PointLightShadowRuntimeBaker> _shadowBakerBuffer = new List<PointLightShadowRuntimeBaker>();
        // Runtime materials cannot be constructed by Udon, so prepare temporary editor copies before play mode.
        static LightVolumePreprocessor() {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        [PostProcessScene]
        // Prepares runtime dependencies and strips heavy authoring references from Unity's temporary build scene.
        private static void OnPostProcessScene() {
            if (!BuildPipeline.isBuildingPlayer) return;
            PrepareRuntimeDependencies(SceneManager.GetActiveScene().GetRootGameObjects(), false);
        }

        // Creates play-mode dependencies before entry and removes temporary copies after exit.
        private static void OnPlayModeStateChanged(PlayModeStateChange state) {
            if (state == PlayModeStateChange.ExitingEditMode) {
                PrepareRuntimeDependenciesForOpenScenes();
            } else if (state == PlayModeStateChange.EnteredPlayMode) {
                PrepareRuntimeDependenciesForOpenScenes();
                ApplyRuntimeDependenciesForOpenScenes();
            } else if (state == PlayModeStateChange.EnteredEditMode) {
                ClearRuntimeDependenciesForOpenScenes();
            }
        }

        // Prepares runtime-only materials and cameras for every loaded scene.
        private static void PrepareRuntimeDependenciesForOpenScenes() {
            for (int i = 0; i < SceneManager.sceneCount; i++) {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded) PrepareRuntimeDependencies(scene.GetRootGameObjects(), true);
            }
        }

        // Applies Android-safe settings to active EXR point-light projection textures.
        internal static void PrepareProjectionTextureImportsForOpenScenes() {
            for (int i = 0; i < SceneManager.sceneCount; i++) {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded) PrepareProjectionTextureImports(scene.GetRootGameObjects());
            }
        }

        // Publishes prepared dependencies to the live Udon heaps of all open scenes.
        private static void ApplyRuntimeDependenciesForOpenScenes() {
            for (int i = 0; i < SceneManager.sceneCount; i++) {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded) ApplyRuntimeDependencies(scene.GetRootGameObjects());
            }
        }

        // Removes temporary play-mode dependencies from all open scenes.
        private static void ClearRuntimeDependenciesForOpenScenes() {
            for (int i = 0; i < SceneManager.sceneCount; i++) {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded) ClearRuntimeDependencies(scene.GetRootGameObjects());
            }
        }

        // Canonicalizes build data and prepares Manager, projection and shadow runtime dependencies.
        private static void PrepareRuntimeDependencies(GameObject[] roots, bool editorTemporary) {
            if (editorTemporary) PrepareProjectionTextureImports(roots);
            else CanonicalizeBuildScene(roots);

            Shader cubemapFaceShader = Shader.Find(CubemapFaceShaderName);
            Shader shadowDepthEncodeShader = Shader.Find(ShadowDepthEncodeShaderName);
            Shader shadowBlurShader = Shader.Find(ShadowBlurShaderName);
            Shader clusteringShader = Shader.Find(ClusteringShaderName);

            for (int i = 0; i < roots.Length; i++) {
                GameObject root = roots[i];
                if (root == null) continue;

                _managerBuffer.Clear();
                root.GetComponentsInChildren(true, _managerBuffer);
                for (int j = 0; j < _managerBuffer.Count; j++) {
                    PrepareManagerRuntimeDependencies(_managerBuffer[j], cubemapFaceShader, shadowDepthEncodeShader,
                        shadowBlurShader, clusteringShader, editorTemporary);
                }

                _pointLightBuffer.Clear();
                root.GetComponentsInChildren(true, _pointLightBuffer);
                for (int j = 0; j < _pointLightBuffer.Count; j++) {
                    PreparePointLightForRuntime(_pointLightBuffer[j], shadowDepthEncodeShader, shadowBlurShader, editorTemporary);
                }

                // External runtime bakers need the same local point-light dependencies even without Bake In Game.
                _shadowBakerBuffer.Clear();
                root.GetComponentsInChildren(true, _shadowBakerBuffer);
                for (int j = 0; j < _shadowBakerBuffer.Count; j++) {
                    PointLightShadowRuntimeBaker baker = _shadowBakerBuffer[j];
                    if (baker != null) PreparePointLightRuntimeShadowDependencies(baker.TargetPointLightVolume, shadowDepthEncodeShader, shadowBlurShader, editorTemporary);
                }

                if (!editorTemporary) ClearBuildOnlySerializedReferences(root);
            }

            ClearBuffers();
        }

        // Rebuild every runtime field once on Unity's temporary build-scene copy. Editor and
        // play-mode scenes keep the event-driven authoring path and are never changed here.
        private static void CanonicalizeBuildScene(GameObject[] roots) {
            // Apply target-dependent manager settings before child data uses those settings.
            for (int i = 0; i < roots.Length; i++) {
                GameObject root = roots[i];
                if (root == null) continue;

                _managerBuffer.Clear();
                root.GetComponentsInChildren(true, _managerBuffer);
                for (int j = 0; j < _managerBuffer.Count; j++) {
                    LightVolumeManagerTools.ApplySettings(_managerBuffer[j], false, updateVolumes: false);
                }
            }

            // Canonicalize every child without rebuilding its manager per point light.
            for (int i = 0; i < roots.Length; i++) {
                GameObject root = roots[i];
                if (root == null) continue;

                _lightVolumeBuffer.Clear();
                root.GetComponentsInChildren(true, _lightVolumeBuffer);
                for (int j = 0; j < _lightVolumeBuffer.Count; j++) {
                    LightVolumeInstance volume = _lightVolumeBuffer[j];
                    LightVolumeTools.ApplyRuntimeState(volume, false);
                    LightVolumeManagerTools.CopyProxyToUdon(volume);
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
                    LightVolumeManagerTools.CopyProxyToUdon(pointLight);
                }
            }

            // Publish the completed registries once after every child has been canonicalized.
            for (int i = 0; i < roots.Length; i++) {
                GameObject root = roots[i];
                if (root == null) continue;

                _managerBuffer.Clear();
                root.GetComponentsInChildren(true, _managerBuffer);
                for (int j = 0; j < _managerBuffer.Count; j++) {
                    LightVolumeManager manager = _managerBuffer[j];
                    if (manager == null) continue;
                    manager.UpdateVolumes();
                    LightVolumeManagerTools.CopyProxyToUdon(manager);
                }
            }

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

        // Copies prepared Manager and runtime-shadow dependencies into live Udon behaviours.
        private static void ApplyRuntimeDependencies(GameObject[] roots) {
            for (int i = 0; i < roots.Length; i++) {
                GameObject root = roots[i];
                if (root == null) continue;

                _managerBuffer.Clear();
                root.GetComponentsInChildren(true, _managerBuffer);
                for (int j = 0; j < _managerBuffer.Count; j++) ApplyManagerRuntimeDependencies(_managerBuffer[j]);

                _pointLightBuffer.Clear();
                root.GetComponentsInChildren(true, _pointLightBuffer);
                for (int j = 0; j < _pointLightBuffer.Count; j++) {
                    PointLightVolumeInstance pointLight = _pointLightBuffer[j];
                    if (HasPointLightRuntimeShadowDependencies(pointLight)) ApplyPointLightRuntimeShadowDependencies(pointLight);
                }
            }

            _managerBuffer.Clear();
            _pointLightBuffer.Clear();
        }

        // Destroys temporary materials and clears runtime-shadow references below the supplied roots.
        private static void ClearRuntimeDependencies(GameObject[] roots) {
            for (int i = 0; i < roots.Length; i++) {
                GameObject root = roots[i];
                if (root == null) continue;

                _managerBuffer.Clear();
                root.GetComponentsInChildren(true, _managerBuffer);
                for (int j = 0; j < _managerBuffer.Count; j++) ClearManagerMaterials(_managerBuffer[j]);

                _pointLightBuffer.Clear();
                root.GetComponentsInChildren(true, _pointLightBuffer);
                for (int j = 0; j < _pointLightBuffer.Count; j++) ClearPointLightRuntimeShadowDependencies(_pointLightBuffer[j]);
            }

            _managerBuffer.Clear();
            _pointLightBuffer.Clear();
        }

        // Creates all runtime materials and shadow-camera dependencies owned by one Manager.
        private static void PrepareManagerRuntimeDependencies(LightVolumeManager manager, Shader cubemapFaceShader,
            Shader depthEncodeShader, Shader blurShader, Shader clusteringShader, bool editorTemporary) {
            if (manager == null) return;
            PrepareManagerRuntimeShadowDependencies(manager, depthEncodeShader, blurShader, editorTemporary, false);
            manager.CubemapFaceMaterial = CreateRuntimeMaterialInstance(cubemapFaceShader, manager.CubemapFaceMaterial, manager.name + "_CubemapFaceRuntime", editorTemporary);
            manager.ClusteringMaterial = CreateRuntimeMaterialInstance(clusteringShader, manager.ClusteringMaterial, manager.name + "_ClusteringRuntime", editorTemporary);
            ApplyManagerRuntimeDependencies(manager);
        }

        // Ensures one Manager owns a shadow camera and compatible encode and blur materials.
        private static void PrepareManagerRuntimeShadowDependencies(LightVolumeManager manager, Shader depthEncodeShader, Shader blurShader, bool editorTemporary, bool apply) {
            if (manager == null) return;
            manager.EnsureRuntimeShadowCamera();
            manager.RuntimeShadowDepthEncodeMaterial = CreateRuntimeMaterialInstance(depthEncodeShader, manager.RuntimeShadowDepthEncodeMaterial, manager.name + "_ShadowDepthEncodeRuntime", editorTemporary);
            manager.RuntimeShadowBlurMaterial = CreateRuntimeMaterialInstance(blurShader, manager.RuntimeShadowBlurMaterial, manager.name + "_ShadowBlurRuntime", editorTemporary);
            ResetManagerRuntimeShadowBlurState(manager);
            if (apply) ApplyManagerRuntimeDependencies(manager);
        }

        // Resolves build-safe projection and optional Bake In Game state for one Point Light Volume.
        private static void PreparePointLightForRuntime(PointLightVolumeInstance pointLight, Shader depthEncodeShader, Shader blurShader, bool editorTemporary) {
            if (pointLight == null) return;

            bool bakeInGame = pointLight.Shadows && pointLight.BakeInGame;
            if (!editorTemporary && pointLight.BakeInGame != bakeInGame) pointLight.BakeInGame = bakeInGame;
            if (bakeInGame) {
                LightVolumeManager manager = ResolvePointLightManager(pointLight);
                if (manager != null) pointLight.RuntimeShadowResolution = Mathf.Max(manager.ShadowTexturesWidth, 16);
                pointLight.RuntimeShadowBlurSamplePreset = 2;
                pointLight.RuntimeShadowSphericalBlur = true;
                pointLight.RuntimeShadowFacesPerFrame = 6;
                pointLight.RuntimeShadowDirectOutput = false;
                PreparePointLightRuntimeShadowDependencies(pointLight, depthEncodeShader, blurShader, editorTemporary);
            } else {
#if UDONSHARP
                ApplyPointLightRuntimeShadowBakeSettings(pointLight, GetBackingUdonBehaviour(pointLight));
#endif
            }

            ApplyPointLightRuntimeCustomSource(pointLight);
#if UDONSHARP
            ApplyPointLightRuntimeShadowSource(pointLight, GetBackingUdonBehaviour(pointLight));
#endif
        }

        // Assigns shared Manager shadow resources to a light that can bake shadows at runtime.
        private static void PreparePointLightRuntimeShadowDependencies(PointLightVolumeInstance pointLight, Shader depthEncodeShader, Shader blurShader, bool editorTemporary) {
            if (pointLight == null) return;
            LightVolumeManager manager = ResolvePointLightManager(pointLight);
            if (manager != null) {
                manager.EnsureRuntimeShadowCamera();
                if (manager.RuntimeShadowCamera == null || !IsRuntimeMaterialReady(manager.RuntimeShadowDepthEncodeMaterial, depthEncodeShader) || !IsRuntimeMaterialReady(manager.RuntimeShadowBlurMaterial, blurShader)) {
                    PrepareManagerRuntimeShadowDependencies(manager, depthEncodeShader, blurShader, editorTemporary, true);
                } else {
                    ApplyManagerRuntimeDependencies(manager);
                }
                pointLight.LightVolumeManager = manager;
            }

            if (pointLight.Shadows && pointLight.BakeInGame) ClearPointLightRuntimeShadowSource(pointLight);
            pointLight.RuntimeShadowCamera = manager != null ? manager.RuntimeShadowCamera : null;
            pointLight.RuntimeShadowDepthEncodeMaterial = manager != null ? manager.RuntimeShadowDepthEncodeMaterial : null;
            pointLight.RuntimeShadowBlurMaterial = manager != null ? manager.RuntimeShadowBlurMaterial : null;
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

        // Checks whether a light needs prepared dependencies for Bake In Game.
        private static bool HasPointLightRuntimeShadowDependencies(PointLightVolumeInstance pointLight) {
            return pointLight != null && pointLight.Shadows && pointLight.BakeInGame;
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

        // Returns the Manager explicitly assigned to a Point Light Volume.
        private static LightVolumeManager ResolvePointLightManager(PointLightVolumeInstance pointLight) {
            return pointLight != null ? pointLight.LightVolumeManager : null;
        }

        // Checks whether an existing runtime material uses the required shader.
        private static bool IsRuntimeMaterialReady(Material material, Shader shader) {
            return material != null && shader != null && material.shader == shader;
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

        // Publishes runtime shadow bake settings and exclusion roots to a Point Light Volume's Udon heap.
        private static void ApplyPointLightRuntimeShadowBakeSettings(PointLightVolumeInstance pointLight, UdonBehaviour udonBehaviour) {
            if (pointLight == null || udonBehaviour == null) return;
            SetUdonProgramVariable(udonBehaviour, "BakeInGame", pointLight.BakeInGame);
            SetUdonProgramVariable(udonBehaviour, "ExclusionMask", pointLight.ExclusionMask ?? Array.Empty<GameObject>());
            SetUdonProgramVariable(udonBehaviour, "RuntimeShadowResolution", pointLight.RuntimeShadowResolution);
            SetUdonProgramVariable(udonBehaviour, "RuntimeShadowBlurSamplePreset", pointLight.RuntimeShadowBlurSamplePreset);
            SetUdonProgramVariable(udonBehaviour, "RuntimeShadowSphericalBlur", pointLight.RuntimeShadowSphericalBlur);
            SetUdonProgramVariable(udonBehaviour, "RuntimeShadowFacesPerFrame", pointLight.RuntimeShadowFacesPerFrame);
            SetUdonProgramVariable(udonBehaviour, "RuntimeShadowDirectOutput", pointLight.RuntimeShadowDirectOutput);
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

        // Runtime fields are published first; only duplicate authoring references are stripped afterwards.
        private static void ClearBuildOnlySerializedReferences(GameObject root) {
            if (root == null) return;

            _managerBuffer.Clear();
            root.GetComponentsInChildren(true, _managerBuffer);
            for (int i = 0; i < _managerBuffer.Count; i++) ClearManagerBuildOnlySerializedReferences(_managerBuffer[i]);

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
#if BAKERY_INCLUDED
            volume.BakeryVolume = null;
#endif

#if UDONSHARP
            UdonBehaviour udonBehaviour = GetBackingUdonBehaviour(volume);
            if (udonBehaviour == null) return;
            SetUdonProgramVariable(udonBehaviour, "Texture0", null);
            SetUdonProgramVariable(udonBehaviour, "Texture1", null);
            SetUdonProgramVariable(udonBehaviour, "Texture2", null);
#if BAKERY_INCLUDED
            SetUdonProgramVariable(udonBehaviour, "BakeryVolume", null);
#endif
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
            _managerBuffer.Clear();
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

            int issueCount;
            string issueSummary;
            if (LightVolumeMigration.ValidateLoadedSceneUdonPairs(out issueCount, out issueSummary)) return true;

            Debug.LogError("[LightVolume] Build blocked before UdonSharp upgrade: " + issueCount + " Light Volume setup issue(s) were found. " + issueSummary + ". Fix the reported scene setup, then reopen the scene.");
            return false;
        }
    }
#endif
}
