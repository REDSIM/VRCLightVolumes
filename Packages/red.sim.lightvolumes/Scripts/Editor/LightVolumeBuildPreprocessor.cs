using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRCLightVolumes {
    [InitializeOnLoad]
    internal static class LightVolumePreprocessor {
        private const string CubemapFaceShaderName = "Hidden/CubeFace";
        private const string ShadowDepthEncodeShaderName = "Hidden/VRCLV/PointLightShadowDepthEncode";
        private const string ShadowBlurShaderName = "Hidden/VRCLV/PointLightShadowRuntimeBlur";
        private const string BackingUdonBehaviourFieldName = "_udonSharpBackingUdonBehaviour";
        private const string UdonBehaviourTypeName = "VRC.Udon.UdonBehaviour";
        private const string SetProgramVariableMethodName = "SetProgramVariable";

        private static readonly List<LightVolumeManager> _managerBuffer = new List<LightVolumeManager>();
        private static readonly List<LightVolumeSetup> _setupBuffer = new List<LightVolumeSetup>();
        private static readonly List<LightVolume> _authoringLightVolumeBuffer = new List<LightVolume>();
        private static readonly List<PointLightVolume> _authoringPointLightBuffer = new List<PointLightVolume>();
        private static readonly List<PointLightVolumeInstance> _pointLightBuffer = new List<PointLightVolumeInstance>();
        private static readonly List<PointLightShadowRuntimeBaker> _shadowBakerBuffer = new List<PointLightShadowRuntimeBaker>();
        private static readonly object[] _setProgramVariableArgs = new object[2];

        private static MethodInfo _setProgramVariableMethod;
        private static Type _setProgramVariableMethodOwner;

        // Registers editor play-mode preparation for materials that Udon cannot instantiate at runtime
        static LightVolumePreprocessor() {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        [PostProcessScene]
        // Prepares Udon runtime materials in the temporary build scene, then removes authoring-only components
        static void OnPostProcessScene() {
            if (!BuildPipeline.isBuildingPlayer) return;

            GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects();
            PrepareAndCleanupBuildScene(roots);
        }

        // Strips one temporary build scene without letting authoring lifecycle callbacks overwrite prepared runtime data.
        static void PrepareAndCleanupBuildScene(GameObject[] roots) {
            bool previousCleanupState = LightVolumeSetup.IsBuildSceneCleanupInProgress;
            LightVolumeSetup.IsBuildSceneCleanupInProgress = true;
            try {
                PrepareRuntimeDependencies(roots, false);
                CleanupEditorComponents(roots);
            } finally {
                LightVolumeSetup.IsBuildSceneCleanupInProgress = previousCleanupState;
            }
        }

        // Mirrors build-scene dependency preparation for editor play mode and clears temporary editor-only copies afterwards
        static void OnPlayModeStateChanged(PlayModeStateChange state) {
            if (state == PlayModeStateChange.ExitingEditMode) {
                LightVolumeUdonComponentSanitizer.SanitizeLoadedScenes();
                PrepareRuntimeDependenciesForOpenScenes();
            } else if (state == PlayModeStateChange.EnteredPlayMode) {
                PrepareRuntimeDependenciesForOpenScenes();
                ApplyRuntimeDependenciesForOpenScenes();
            } else if (state == PlayModeStateChange.EnteredEditMode) {
                ClearRuntimeDependenciesForOpenScenes();
            }
        }

        // Creates or refreshes hidden runtime dependencies in every loaded scene
        static void PrepareRuntimeDependenciesForOpenScenes() {
            for (int i = 0; i < SceneManager.sceneCount; i++) {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded) PrepareRuntimeDependencies(scene.GetRootGameObjects(), true);
            }
        }

        // Applies Android-safe import settings to assigned EXR projection sources in every loaded scene
        internal static void PrepareProjectionTextureImportsForOpenScenes() {
            for (int i = 0; i < SceneManager.sceneCount; i++) {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded) PrepareProjectionTextureImports(scene.GetRootGameObjects());
            }
        }

        // Pushes prepared runtime dependencies directly into playing UdonBehaviour proxies in every loaded scene
        static void ApplyRuntimeDependenciesForOpenScenes() {
            for (int i = 0; i < SceneManager.sceneCount; i++) {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded) ApplyRuntimeDependencies(scene.GetRootGameObjects());
            }
        }

        // Removes temporary editor runtime dependencies from every loaded scene after play mode exits
        static void ClearRuntimeDependenciesForOpenScenes() {
            for (int i = 0; i < SceneManager.sceneCount; i++) {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded) ClearRuntimeDependencies(scene.GetRootGameObjects());
            }
        }

        // Creates or reuses all runtime dependencies needed by Light Volumes Udon components under the given roots
        static void PrepareRuntimeDependencies(GameObject[] roots, bool editorTemporary) {
            if (editorTemporary) PrepareProjectionTextureImports(roots);

            Shader cubemapFaceShader = Shader.Find(CubemapFaceShaderName);
            Shader shadowDepthEncodeShader = Shader.Find(ShadowDepthEncodeShaderName);
            Shader shadowBlurShader = Shader.Find(ShadowBlurShaderName);

            for (int i = 0; i < roots.Length; i++) {
                GameObject root = roots[i];
                if (root == null) continue;

                _managerBuffer.Clear();
                root.GetComponentsInChildren(true, _managerBuffer);
                for (int j = 0; j < _managerBuffer.Count; j++) PrepareManagerRuntimeDependencies(_managerBuffer[j], cubemapFaceShader, shadowDepthEncodeShader, shadowBlurShader, editorTemporary);

                _authoringPointLightBuffer.Clear();
                root.GetComponentsInChildren(true, _authoringPointLightBuffer);
                for (int j = 0; j < _authoringPointLightBuffer.Count; j++) PrepareAuthoringPointLightRuntimeShadowDependencies(_authoringPointLightBuffer[j], shadowDepthEncodeShader, shadowBlurShader, editorTemporary);

                _pointLightBuffer.Clear();
                root.GetComponentsInChildren(true, _pointLightBuffer);
                for (int j = 0; j < _pointLightBuffer.Count; j++) {
                    PointLightVolumeInstance pointLight = _pointLightBuffer[j];
                    if (pointLight == null) continue;
                    if (pointLight.BakeInGame && pointLight.GetComponent<PointLightVolume>() == null) PreparePointLightRuntimeShadowDependencies(pointLight, shadowDepthEncodeShader, shadowBlurShader, editorTemporary);
                }

                _shadowBakerBuffer.Clear();
                root.GetComponentsInChildren(true, _shadowBakerBuffer);
                for (int j = 0; j < _shadowBakerBuffer.Count; j++) {
                    PointLightShadowRuntimeBaker baker = _shadowBakerBuffer[j];
                    if (baker != null) PreparePointLightRuntimeShadowDependencies(baker.TargetPointLightVolume, shadowDepthEncodeShader, shadowBlurShader, editorTemporary);
                }

                if (!editorTemporary) ClearBuildOnlySerializedReferences(root);
            }

            _managerBuffer.Clear();
            _setupBuffer.Clear();
            _authoringLightVolumeBuffer.Clear();
            _authoringPointLightBuffer.Clear();
            _pointLightBuffer.Clear();
            _shadowBakerBuffer.Clear();
        }

        // Applies Android-safe import settings to assigned EXR projection sources before play-mode texture caches sample them
        static void PrepareProjectionTextureImports(GameObject[] roots) {
            for (int i = 0; i < roots.Length; i++) {
                GameObject root = roots[i];
                if (root == null) continue;

                _setupBuffer.Clear();
                root.GetComponentsInChildren(true, _setupBuffer);
                for (int j = 0; j < _setupBuffer.Count; j++) {
                    LightVolumeSetup setup = _setupBuffer[j];
                    if (setup != null) setup.PrepareCustomProjectionTextureImports();
                }
            }

            _setupBuffer.Clear();
        }

        // Pushes already prepared runtime dependencies into UdonBehaviours under the given roots
        static void ApplyRuntimeDependencies(GameObject[] roots) {
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

        // Clears editor-only runtime dependencies from Light Volumes Udon components under the given roots
        static void ClearRuntimeDependencies(GameObject[] roots) {
            for (int i = 0; i < roots.Length; i++) {
                GameObject root = roots[i];
                if (root == null) continue;

                _managerBuffer.Clear();
                root.GetComponentsInChildren(true, _managerBuffer);
                for (int j = 0; j < _managerBuffer.Count; j++) ClearManagerMaterial(_managerBuffer[j]);
            }

            _managerBuffer.Clear();
        }

        // Prepares all shared runtime materials and the shared shadow bake camera for one runtime manager.
        static void PrepareManagerRuntimeDependencies(LightVolumeManager manager, Shader cubemapFaceShader, Shader depthEncodeShader, Shader blurShader, bool editorTemporary) {
            if (manager == null) return;
            PrepareManagerRuntimeShadowDependencies(manager, depthEncodeShader, blurShader, editorTemporary, false);
            manager.CubemapFaceMaterial = CreateRuntimeMaterialInstance(cubemapFaceShader, manager.CubemapFaceMaterial, manager.name + "_CubemapFaceRuntime", editorTemporary);
            ApplyManagerRuntimeDependencies(manager);
        }

        // Prepares shared runtime shadow bake dependencies for one runtime manager.
        static void PrepareManagerRuntimeShadowDependencies(LightVolumeManager manager, Shader depthEncodeShader, Shader blurShader, bool editorTemporary, bool apply) {
            if (manager == null) return;
            manager.EnsureRuntimeShadowCamera();
            manager.RuntimeShadowDepthEncodeMaterial = CreateRuntimeMaterialInstance(depthEncodeShader, manager.RuntimeShadowDepthEncodeMaterial, manager.name + "_ShadowDepthEncodeRuntime", editorTemporary);
            manager.RuntimeShadowBlurMaterial = CreateRuntimeMaterialInstance(blurShader, manager.RuntimeShadowBlurMaterial, manager.name + "_ShadowBlurRuntime", editorTemporary);
            ResetManagerRuntimeShadowBlurState(manager);
            if (apply) ApplyManagerRuntimeDependencies(manager);
        }

        // Uses the authoring Bake In Game state as the build-time source of truth and removes baked shadow asset references from the temporary build scene.
        static void PrepareAuthoringPointLightRuntimeShadowDependencies(PointLightVolume authoringPointLight, Shader depthEncodeShader, Shader blurShader, bool editorTemporary) {
            if (authoringPointLight == null) return;
            PointLightVolumeInstance pointLight = authoringPointLight.PointLightVolumeInstance;
            if (pointLight == null) return;
            if (!authoringPointLight.BakeInGame) {
                bool hadBakeInGame = pointLight.BakeInGame;
                if (hadBakeInGame) pointLight.BakeInGame = false;
                if (hadBakeInGame) ApplyPointLightRuntimeShadowBakeSettings(pointLight, GetBackingUdonBehaviour(pointLight));
                return;
            }
            if (!editorTemporary) ClearAuthoringPointLightRuntimeShadowSource(authoringPointLight);

            if (!authoringPointLight.Shadows) {
                pointLight.BakeInGame = false;
                ClearPointLightRuntimeShadowSource(pointLight);
                ApplyPointLightRuntimeShadowDependencies(pointLight);
                Component udonBehaviour = GetBackingUdonBehaviour(pointLight);
                ApplyPointLightRuntimeShadowSource(pointLight, udonBehaviour);
                return;
            }

            int runtimeShadowResolution = authoringPointLight.LightVolumeSetup != null ? (int)authoringPointLight.LightVolumeSetup.ShadowResolution : pointLight.RuntimeShadowResolution;
            pointLight.BakeInGame = true;
            pointLight.RuntimeShadowResolution = Mathf.Max(runtimeShadowResolution, 16);
            pointLight.RuntimeShadowBlurSamplePreset = 2;
            pointLight.RuntimeShadowSphericalBlur = true;
            pointLight.RuntimeShadowFacesPerFrame = 6;
            pointLight.RuntimeShadowDirectOutput = false;
            PreparePointLightRuntimeShadowDependencies(pointLight, depthEncodeShader, blurShader, editorTemporary);
        }

        // Prepares one point light to use manager-owned runtime shadow bake dependencies.
        static void PreparePointLightRuntimeShadowDependencies(PointLightVolumeInstance pointLight, Shader depthEncodeShader, Shader blurShader, bool editorTemporary) {
            if (pointLight == null) return;
            LightVolumeManager manager = ResolvePointLightManager(pointLight);
            if (manager != null) {
                manager.EnsureRuntimeShadowCamera();
                if (manager.RuntimeShadowCamera == null || !IsRuntimeMaterialReady(manager.RuntimeShadowDepthEncodeMaterial, depthEncodeShader) || !IsRuntimeMaterialReady(manager.RuntimeShadowBlurMaterial, blurShader)) PrepareManagerRuntimeShadowDependencies(manager, depthEncodeShader, blurShader, editorTemporary, true);
                else ApplyManagerRuntimeDependencies(manager);
                pointLight.LightVolumeManager = manager;
            }
            if (pointLight.BakeInGame) ClearPointLightRuntimeShadowSource(pointLight);
            ApplyPointLightRuntimeShadowDependencies(pointLight);
        }

        // Pushes the prepared manager runtime dependencies into one backing UdonBehaviour.
        static void ApplyManagerRuntimeDependencies(LightVolumeManager manager) {
            if (manager == null) return;
            Component udonBehaviour = GetBackingUdonBehaviour(manager);
            if (udonBehaviour == null) return;
            SetUdonProgramVariable(udonBehaviour, "CubemapFaceMaterial", manager.CubemapFaceMaterial);
            SetUdonProgramVariable(udonBehaviour, "RuntimeShadowCamera", manager.RuntimeShadowCamera);
            SetUdonProgramVariable(udonBehaviour, "RuntimeShadowDepthEncodeMaterial", manager.RuntimeShadowDepthEncodeMaterial);
            SetUdonProgramVariable(udonBehaviour, "RuntimeShadowBlurMaterial", manager.RuntimeShadowBlurMaterial);
            SetUdonProgramVariable(udonBehaviour, "RuntimeShadowBlurQualityPreset", manager.RuntimeShadowBlurQualityPreset);
            SetUdonProgramVariable(udonBehaviour, "RuntimeShadowBlurUniformKeyword", manager.RuntimeShadowBlurUniformKeyword);
            SetUdonProgramVariable(udonBehaviour, "RuntimeShadowBlurDirectKeyword", manager.RuntimeShadowBlurDirectKeyword);
            SetUdonProgramVariable(udonBehaviour, "RuntimeShadowBlurSphericalKeyword", manager.RuntimeShadowBlurSphericalKeyword);
        }

        // Pushes the prepared point light runtime shadow dependencies into one backing UdonBehaviour.
        static void ApplyPointLightRuntimeShadowDependencies(PointLightVolumeInstance pointLight) {
            if (pointLight == null) return;
            Component udonBehaviour = GetBackingUdonBehaviour(pointLight);
            if (udonBehaviour == null) return;
            ApplyPointLightRuntimeShadowBakeSettings(pointLight, udonBehaviour);
            if (pointLight.BakeInGame) ApplyPointLightRuntimeShadowSource(pointLight, udonBehaviour);
        }

        // Returns true when this point light has runtime shadow bake data that needs pushing into Udon.
        static bool HasPointLightRuntimeShadowDependencies(PointLightVolumeInstance pointLight) {
            return pointLight != null && pointLight.BakeInGame;
        }

        // Clears one manager's temporary runtime material references.
        static void ClearManagerMaterial(LightVolumeManager manager) {
            if (manager == null) return;
            DestroyRuntimeMaterialInstance(manager.CubemapFaceMaterial);
            DestroyRuntimeMaterialInstance(manager.RuntimeShadowDepthEncodeMaterial);
            DestroyRuntimeMaterialInstance(manager.RuntimeShadowBlurMaterial);
            manager.CubemapFaceMaterial = null;
            manager.RuntimeShadowDepthEncodeMaterial = null;
            manager.RuntimeShadowBlurMaterial = null;
            ResetManagerRuntimeShadowBlurState(manager);
        }

        // Resolves the manager that owns runtime dependencies for one point light, falling back to the authoring setup when needed.
        static LightVolumeManager ResolvePointLightManager(PointLightVolumeInstance pointLight) {
            if (pointLight == null) return null;
            LightVolumeManager manager = pointLight.LightVolumeManager;
            if (manager != null) return manager;
            PointLightVolume authoringPointLight = pointLight.GetComponent<PointLightVolume>();
            if (authoringPointLight == null || authoringPointLight.LightVolumeSetup == null) return null;
            return authoringPointLight.LightVolumeSetup.LightVolumeManager;
        }

        // Returns true when a runtime material already uses the shader requested by the current preparation pass.
        static bool IsRuntimeMaterialReady(Material material, Shader shader) {
            return material != null && shader != null && material.shader == shader;
        }

        // Invalidates cached keyword state for the shared runtime shadow blur material.
        static void ResetManagerRuntimeShadowBlurState(LightVolumeManager manager) {
            if (manager == null) return;
            manager.RuntimeShadowBlurQualityPreset = -1;
            manager.RuntimeShadowBlurUniformKeyword = -1;
            manager.RuntimeShadowBlurDirectKeyword = -1;
            manager.RuntimeShadowBlurSphericalKeyword = -1;
        }

        // Clears serialized runtime shadow source fields so Bake In Game lights do not include baked shadow assets in play/build runtime state.
        static void ClearPointLightRuntimeShadowSource(PointLightVolumeInstance pointLight) {
            if (pointLight == null) return;
            pointLight.ShadowMapTexture = null;
            pointLight.ShadowMapMaterial = null;
            pointLight.AutoUpdateShadowMap = false;
            pointLight.ShadowMapID = -1f;
            pointLight.ShadowMapTextureIsCubemap = false;
            pointLight.ShadowMapTextureHasDepthSlices = false;
        }

        // Clears stale runtime custom projection source fields that are not used by the current authoring projection mode.
        static void ClearPointLightRuntimeCustomSource(PointLightVolumeInstance pointLight) {
            if (pointLight == null) return;
            pointLight.CustomTexture = null;
            pointLight.CustomTextureMaterial = null;
            pointLight.ProjectionType = 0;
            pointLight.ProjectionMode = 0;
            pointLight.AutoUpdateCustomTexture = false;
            pointLight.CustomTextureIsCubemap = false;
            pointLight.CustomTextureHasDepthSlices = false;
        }

        // Pushes runtime shadow source fields into one backing UdonBehaviour after build-time source cleanup.
        static void ApplyPointLightRuntimeShadowSource(PointLightVolumeInstance pointLight, Component udonBehaviour) {
            if (pointLight == null) return;
            if (udonBehaviour == null) return;
            SetUdonProgramVariable(udonBehaviour, "ShadowMapTexture", pointLight.ShadowMapTexture);
            SetUdonProgramVariable(udonBehaviour, "ShadowMapMaterial", pointLight.ShadowMapMaterial);
            SetUdonProgramVariable(udonBehaviour, "AutoUpdateShadowMap", pointLight.AutoUpdateShadowMap);
            SetUdonProgramVariable(udonBehaviour, "ShadowMapID", pointLight.ShadowMapID);
            SetUdonProgramVariable(udonBehaviour, "ShadowMapTextureIsCubemap", pointLight.ShadowMapTextureIsCubemap);
            SetUdonProgramVariable(udonBehaviour, "ShadowMapTextureHasDepthSlices", pointLight.ShadowMapTextureHasDepthSlices);
            SetUdonProgramVariable(udonBehaviour, "ShadowMapUsesCubemap", pointLight.ShadowMapUsesCubemap);
        }

        // Pushes runtime shadow bake settings into one backing UdonBehaviour after build-time authoring sync.
        static void ApplyPointLightRuntimeShadowBakeSettings(PointLightVolumeInstance pointLight, Component udonBehaviour) {
            if (pointLight == null) return;
            if (udonBehaviour == null) return;
            SetUdonProgramVariable(udonBehaviour, "BakeInGame", pointLight.BakeInGame);
            SetUdonProgramVariable(udonBehaviour, "RuntimeShadowResolution", pointLight.RuntimeShadowResolution);
            SetUdonProgramVariable(udonBehaviour, "RuntimeShadowBlurSamplePreset", pointLight.RuntimeShadowBlurSamplePreset);
            SetUdonProgramVariable(udonBehaviour, "RuntimeShadowSphericalBlur", pointLight.RuntimeShadowSphericalBlur);
            SetUdonProgramVariable(udonBehaviour, "RuntimeShadowFacesPerFrame", pointLight.RuntimeShadowFacesPerFrame);
            SetUdonProgramVariable(udonBehaviour, "RuntimeShadowDirectOutput", pointLight.RuntimeShadowDirectOutput);
        }

        // Pushes runtime custom projection source fields into one backing UdonBehaviour after build-time source cleanup.
        static void ApplyPointLightRuntimeCustomSource(PointLightVolumeInstance pointLight) {
            if (pointLight == null) return;
            Component udonBehaviour = GetBackingUdonBehaviour(pointLight);
            if (udonBehaviour == null) return;
            SetUdonProgramVariable(udonBehaviour, "CustomTexture", pointLight.CustomTexture);
            SetUdonProgramVariable(udonBehaviour, "CustomTextureMaterial", pointLight.CustomTextureMaterial);
            SetUdonProgramVariable(udonBehaviour, "ProjectionType", pointLight.ProjectionType);
            SetUdonProgramVariable(udonBehaviour, "ProjectionMode", pointLight.ProjectionMode);
            SetUdonProgramVariable(udonBehaviour, "AutoUpdateCustomTexture", pointLight.AutoUpdateCustomTexture);
            SetUdonProgramVariable(udonBehaviour, "CustomTextureIsCubemap", pointLight.CustomTextureIsCubemap);
            SetUdonProgramVariable(udonBehaviour, "CustomTextureHasDepthSlices", pointLight.CustomTextureHasDepthSlices);
        }

        // Clears serialized editor-only references from the temporary build scene before authoring components are removed.
        static void ClearBuildOnlySerializedReferences(GameObject root) {
            if (root == null) return;

            _managerBuffer.Clear();
            root.GetComponentsInChildren(true, _managerBuffer);
            for (int i = 0; i < _managerBuffer.Count; i++) ClearManagerBuildOnlySerializedReferences(_managerBuffer[i]);

            _setupBuffer.Clear();
            root.GetComponentsInChildren(true, _setupBuffer);
            for (int i = 0; i < _setupBuffer.Count; i++) ClearSetupBuildOnlySerializedReferences(_setupBuffer[i]);

            _authoringLightVolumeBuffer.Clear();
            root.GetComponentsInChildren(true, _authoringLightVolumeBuffer);
            for (int i = 0; i < _authoringLightVolumeBuffer.Count; i++) ClearAuthoringLightVolumeBuildOnlySerializedReferences(_authoringLightVolumeBuffer[i]);

            _authoringPointLightBuffer.Clear();
            root.GetComponentsInChildren(true, _authoringPointLightBuffer);
            for (int i = 0; i < _authoringPointLightBuffer.Count; i++) ClearAuthoringPointLightBuildOnlySerializedReferences(_authoringPointLightBuffer[i]);
        }

        // Clears manager fields that are runtime-generated outputs, while preserving serialized atlas references.
        static void ClearManagerBuildOnlySerializedReferences(LightVolumeManager manager) {
            if (manager == null) return;
            Texture finalAtlas = manager.LightVolumeAtlas;
            if (finalAtlas == null && manager.LightVolumeAtlasBase != null) {
                finalAtlas = manager.LightVolumeAtlasBase;
                manager.LightVolumeAtlas = finalAtlas;
            }
            manager.CustomTextures = null;
            manager.ShadowTextures = null;

            Component udonBehaviour = GetBackingUdonBehaviour(manager);
            if (udonBehaviour == null) return;
            if (finalAtlas != null) SetUdonProgramVariable(udonBehaviour, "LightVolumeAtlas", finalAtlas);
            SetUdonProgramVariable(udonBehaviour, "CustomTextures", null);
            SetUdonProgramVariable(udonBehaviour, "ShadowTextures", null);
        }

        // Clears setup-only registries and post processor references that are not consumed by runtime Udon.
        static void ClearSetupBuildOnlySerializedReferences(LightVolumeSetup setup) {
            if (setup == null) return;
            if (setup.LightVolumes != null) setup.LightVolumes.Clear();
            if (setup.LightVolumesWeights != null) setup.LightVolumesWeights.Clear();
            if (setup.PointLightVolumes != null) setup.PointLightVolumes.Clear();
            if (setup.LightVolumeDataList != null) setup.LightVolumeDataList.Clear();
            setup.AtlasPostProcessors = null;
        }

        // Clears source 3D textures after their packed atlas data has already been copied to LightVolumeManager.
        static void ClearAuthoringLightVolumeBuildOnlySerializedReferences(LightVolume volume) {
            if (volume == null) return;
            volume.Texture0 = null;
            volume.Texture1 = null;
            volume.Texture2 = null;
#if BAKERY_INCLUDED
            volume.BakeryVolume = null;
#endif
        }

        // Clears authoring-only baked shadow asset references from the temporary build scene for Bake In Game lights.
        static void ClearAuthoringPointLightRuntimeShadowSource(PointLightVolume pointLight) {
            if (pointLight == null || !pointLight.BakeInGame) return;
            pointLight.ShadowMap = null;
        }

        // Clears point-light bake-only authoring data and stale runtime projection state that has no active source.
        static void ClearAuthoringPointLightBuildOnlySerializedReferences(PointLightVolume pointLight) {
            if (pointLight == null) return;
            pointLight.ObjectMask = Array.Empty<GameObject>();
            UnityEngine.Object activeProjectionSource = pointLight.HasProjectionSource() ? pointLight.GetProjectionSource() : null;
            if (activeProjectionSource == null) {
                ClearPointLightRuntimeCustomSource(pointLight.PointLightVolumeInstance);
                ApplyPointLightRuntimeCustomSource(pointLight.PointLightVolumeInstance);
            }
        }

        // Creates or reuses a non-asset runtime material for one Udon component field
        static Material CreateRuntimeMaterialInstance(Shader shader, Material existing, string name, bool editorTemporary) {
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

        // Destroys one non-asset runtime material instance
        static void DestroyRuntimeMaterialInstance(Material material) {
            if (material == null || AssetDatabase.Contains(material)) return;
            UnityEngine.Object.DestroyImmediate(material);
        }

        // Removes authoring-only components from the temporary build scene in dependency order
        static void CleanupEditorComponents(GameObject[] roots) {
            CleanupComponents(roots, _setupBuffer);
            CleanupComponents(roots, _authoringLightVolumeBuffer);
            CleanupComponents(roots, _authoringPointLightBuffer);
        }

        // Removes one authoring-only component type from the scene copy used by the build pipeline
        static void CleanupComponents<T>(GameObject[] roots, List<T> buffer) where T : Component {
            for (int i = 0; i < roots.Length; i++) {
                GameObject root = roots[i];
                if (root == null) continue;

                buffer.Clear();
                root.GetComponentsInChildren(true, buffer);
                for (int j = 0; j < buffer.Count; j++) {
                    T component = buffer[j];
                    if (component != null) UnityEngine.Object.DestroyImmediate(component);
                }
            }

            buffer.Clear();
        }

        // Returns the hidden UdonBehaviour assigned to one UdonSharp proxy
        static Component GetBackingUdonBehaviour(Component proxy) {
            if (proxy == null) return null;

            FieldInfo backingField = GetBackingUdonBehaviourField(proxy.GetType());
            Component backing = backingField != null ? backingField.GetValue(proxy) as Component : null;
            if (IsUdonBehaviour(backing)) return backing;

            Component[] components = proxy.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++) {
                Component component = components[i];
                if (IsUdonBehaviour(component)) return component;
            }
            return null;
        }

        // Finds the UdonSharp backing UdonBehaviour field on the proxy type hierarchy
        static FieldInfo GetBackingUdonBehaviourField(Type proxyType) {
            Type type = proxyType;
            while (type != null) {
                FieldInfo field = type.GetField(BackingUdonBehaviourFieldName, BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null) return field;
                type = type.BaseType;
            }
            return null;
        }

        // Writes one value into a playing UdonBehaviour.
        static void SetUdonProgramVariable(Component udonBehaviour, string variableName, object value) {
            MethodInfo method = GetSetProgramVariableMethod(udonBehaviour);
            if (method == null) return;

            _setProgramVariableArgs[0] = variableName;
            _setProgramVariableArgs[1] = value;
            method.Invoke(udonBehaviour, _setProgramVariableArgs);
            _setProgramVariableArgs[0] = null;
            _setProgramVariableArgs[1] = null;
        }

        // Returns the cached UdonBehaviour.SetProgramVariable(string, object) method for the current UdonBehaviour type
        static MethodInfo GetSetProgramVariableMethod(Component udonBehaviour) {
            Type type = udonBehaviour.GetType();
            if (_setProgramVariableMethod != null && _setProgramVariableMethodOwner == type) return _setProgramVariableMethod;

            _setProgramVariableMethodOwner = type;
            _setProgramVariableMethod = type.GetMethod(SetProgramVariableMethodName, BindingFlags.Instance | BindingFlags.Public, null, new Type[] { typeof(string), typeof(object) }, null);
            return _setProgramVariableMethod;
        }

        // Returns true when a component is a VRChat UdonBehaviour
        static bool IsUdonBehaviour(Component component) {
            return component != null && component.GetType().FullName == UdonBehaviourTypeName;
        }
    }

    internal class LightVolumeTextureImportBuildPreprocessor : IPreprocessBuildWithReport {
        public int callbackOrder => -1000;

        // Applies EXR projection import settings before Unity packs Android build textures
        public void OnPreprocessBuild(BuildReport report) {
            LightVolumePreprocessor.PrepareProjectionTextureImportsForOpenScenes();
        }
    }
}
