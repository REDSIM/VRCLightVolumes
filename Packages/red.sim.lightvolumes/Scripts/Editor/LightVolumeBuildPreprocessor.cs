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
        private static readonly List<PointLightShadowRuntimeBaker> _shadowBakerBuffer = new List<PointLightShadowRuntimeBaker>();
        private static readonly List<Camera> _cameraBuffer = new List<Camera>();
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
            PrepareRuntimeDependencies(roots, false);
            CleanupEditorComponents(roots);
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
                for (int j = 0; j < _managerBuffer.Count; j++) PrepareManagerMaterial(_managerBuffer[j], cubemapFaceShader, editorTemporary);

                _shadowBakerBuffer.Clear();
                root.GetComponentsInChildren(true, _shadowBakerBuffer);
                for (int j = 0; j < _shadowBakerBuffer.Count; j++) PrepareShadowBakerDependencies(_shadowBakerBuffer[j], shadowDepthEncodeShader, shadowBlurShader, editorTemporary);
            }

            _managerBuffer.Clear();
            _setupBuffer.Clear();
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
                for (int j = 0; j < _managerBuffer.Count; j++) ApplyManagerMaterial(_managerBuffer[j]);

                _shadowBakerBuffer.Clear();
                root.GetComponentsInChildren(true, _shadowBakerBuffer);
                for (int j = 0; j < _shadowBakerBuffer.Count; j++) ApplyShadowBakerDependencies(_shadowBakerBuffer[j]);
            }

            _managerBuffer.Clear();
            _shadowBakerBuffer.Clear();
        }

        // Clears editor-only runtime dependencies from Light Volumes Udon components under the given roots
        static void ClearRuntimeDependencies(GameObject[] roots) {
            for (int i = 0; i < roots.Length; i++) {
                GameObject root = roots[i];
                if (root == null) continue;

                _managerBuffer.Clear();
                root.GetComponentsInChildren(true, _managerBuffer);
                for (int j = 0; j < _managerBuffer.Count; j++) ClearManagerMaterial(_managerBuffer[j]);

                _shadowBakerBuffer.Clear();
                root.GetComponentsInChildren(true, _shadowBakerBuffer);
                for (int j = 0; j < _shadowBakerBuffer.Count; j++) ClearShadowBakerDependencies(_shadowBakerBuffer[j]);
            }

            _managerBuffer.Clear();
            _shadowBakerBuffer.Clear();
        }

        // Prepares the cubemap face unwrap material for one runtime manager
        static void PrepareManagerMaterial(LightVolumeManager manager, Shader cubemapFaceShader, bool editorTemporary) {
            if (manager == null) return;
            manager.CubemapFaceMaterial = CreateRuntimeMaterialInstance(cubemapFaceShader, manager.CubemapFaceMaterial, manager.name + "_CubemapFaceRuntime", editorTemporary);
            ApplyManagerMaterial(manager);
        }

        // Prepares the shadow depth camera, encode material and blur material for one runtime shadow baker
        static void PrepareShadowBakerDependencies(PointLightShadowRuntimeBaker baker, Shader depthEncodeShader, Shader blurShader, bool editorTemporary) {
            if (baker == null) return;
            baker.ShadowCamera = CreateRuntimeShadowCamera(baker, editorTemporary);
            baker.RuntimeShadowDepthEncodeMaterial = CreateRuntimeMaterialInstance(depthEncodeShader, baker.RuntimeShadowDepthEncodeMaterial, baker.name + "_ShadowDepthEncodeRuntime", editorTemporary);
            baker.RuntimeShadowBlurMaterial = CreateRuntimeMaterialInstance(blurShader, baker.RuntimeShadowBlurMaterial, baker.name + "_ShadowBlurRuntime", editorTemporary);
            ApplyShadowBakerDependencies(baker);
        }

        // Pushes the prepared cubemap face material into one backing UdonBehaviour
        static void ApplyManagerMaterial(LightVolumeManager manager) {
            if (manager == null) return;
            Component udonBehaviour = GetBackingUdonBehaviour(manager);
            if (udonBehaviour == null) return;
            SetUdonProgramVariable(udonBehaviour, "CubemapFaceMaterial", manager.CubemapFaceMaterial);
        }

        // Pushes the prepared runtime shadow dependencies into one backing UdonBehaviour
        static void ApplyShadowBakerDependencies(PointLightShadowRuntimeBaker baker) {
            if (baker == null) return;
            Component udonBehaviour = GetBackingUdonBehaviour(baker);
            if (udonBehaviour == null) return;
            SetUdonProgramVariable(udonBehaviour, "ShadowCamera", baker.ShadowCamera);
            SetUdonProgramVariable(udonBehaviour, "RuntimeShadowDepthEncodeMaterial", baker.RuntimeShadowDepthEncodeMaterial);
            SetUdonProgramVariable(udonBehaviour, "RuntimeShadowBlurMaterial", baker.RuntimeShadowBlurMaterial);
        }

        // Clears one manager's temporary cubemap face material reference
        static void ClearManagerMaterial(LightVolumeManager manager) {
            if (manager == null) return;
            DestroyRuntimeMaterialInstance(manager.CubemapFaceMaterial);
            manager.CubemapFaceMaterial = null;
        }

        // Clears one shadow baker's temporary runtime shadow dependencies
        static void ClearShadowBakerDependencies(PointLightShadowRuntimeBaker baker) {
            if (baker == null) return;
            bool clearCameraReference = baker.ShadowCamera != null && (baker.ShadowCamera.hideFlags & HideFlags.DontSaveInEditor) != 0;
            DestroyRuntimeShadowCamera(baker.ShadowCamera);
            DestroyRuntimeMaterialInstance(baker.RuntimeShadowDepthEncodeMaterial);
            DestroyRuntimeMaterialInstance(baker.RuntimeShadowBlurMaterial);
            if (clearCameraReference) baker.ShadowCamera = null;
            baker.RuntimeShadowDepthEncodeMaterial = null;
            baker.RuntimeShadowBlurMaterial = null;
        }

        // Creates the hidden disabled depth camera used by one runtime shadow baker
        static Camera CreateRuntimeShadowCamera(PointLightShadowRuntimeBaker baker, bool editorTemporary) {
            Camera camera = baker.ShadowCamera;
            if (!IsOwnedRuntimeShadowCamera(baker, camera)) {
                camera = null;
                baker.GetComponents(_cameraBuffer);
                for (int i = 0; i < _cameraBuffer.Count; i++) {
                    Camera candidate = _cameraBuffer[i];
                    if (!IsOwnedRuntimeShadowCamera(baker, candidate)) continue;
                    camera = candidate;
                    break;
                }
                _cameraBuffer.Clear();
            }
            if (camera == null) camera = baker.gameObject.AddComponent<Camera>();

            camera.hideFlags = editorTemporary ? HideFlags.HideInInspector | HideFlags.DontSaveInEditor : HideFlags.HideInInspector;
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.Depth;
            camera.backgroundColor = Color.white;
            camera.orthographic = false;
            camera.fieldOfView = 90f;
            camera.aspect = 1f;
            camera.depthTextureMode = DepthTextureMode.None;
            camera.renderingPath = RenderingPath.Forward;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.useOcclusionCulling = false;
            camera.stereoTargetEye = StereoTargetEyeMask.None;
            camera.ResetReplacementShader();
            return camera;
        }

        // Returns true only for the temporary camera owned by this baker
        static bool IsOwnedRuntimeShadowCamera(PointLightShadowRuntimeBaker baker, Camera camera) {
            if (baker == null || camera == null || camera.gameObject != baker.gameObject) return false;
            return (camera.hideFlags & HideFlags.DontSaveInEditor) != 0 || (camera == baker.ShadowCamera && camera.hideFlags == HideFlags.HideInInspector);
        }

        // Destroys one editor-only runtime shadow camera created before entering play mode
        static void DestroyRuntimeShadowCamera(Camera camera) {
            if (camera == null || (camera.hideFlags & HideFlags.DontSaveInEditor) == 0) return;
            UnityEngine.Object.DestroyImmediate(camera);
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
            Cleanup<LightVolumeSetup>(roots);
            Cleanup<LightVolume>(roots);
            Cleanup<PointLightVolume>(roots);
        }

        // Removes one authoring-only component type from the scene copy used by the build pipeline
        static void Cleanup<T>(GameObject[] roots) where T : Component {
            var temp = new List<T>();
            for (int i = 0; i < roots.Length; i++) {
                GameObject root = roots[i];
                if (root == null) continue;

                temp.Clear();
                root.GetComponentsInChildren(true, temp);
                for (int j = 0; j < temp.Count; j++) {
                    T component = temp[j];
                    if (component != null) UnityEngine.Object.DestroyImmediate(component);
                }
            }
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

        // Writes one object reference into a playing UdonBehaviour
        static void SetUdonProgramVariable(Component udonBehaviour, string variableName, UnityEngine.Object value) {
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
