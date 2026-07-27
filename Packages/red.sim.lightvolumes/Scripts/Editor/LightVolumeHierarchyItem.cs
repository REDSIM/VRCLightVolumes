using System;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRCLightVolumes {
    public static class HierarchyMenu {

        [MenuItem("GameObject/Light Volume", false, 9999)]
        private static void CreateLightVolume(MenuCommand command) {
            GameObject gameObject = CreateGameObject("Light Volume", command);
            LightVolumeInstance volume = gameObject.AddUdonSharpComponent<LightVolumeInstance>();

            LightVolumeTools.ResetFromParentReflectionProbe(volume);
            LightVolumeTools.ApplyRuntimeState(volume, false);
            BindToSingleManager(volume);
#if BAKERY_INCLUDED
            if (volume.LightVolumeManager != null && volume.LightVolumeManager.IsBakeryMode)
                LightVolumeTools.SetupBakeryDependencies(volume, true);
#endif
            UdonSharpEditorUtility.CopyProxyToUdon(volume);
            Selection.activeGameObject = gameObject;
        }

        [MenuItem("GameObject/Point Light Volume", false, 9999)]
        private static void CreatePointLightVolume(MenuCommand command) {
            GameObject gameObject = CreateGameObject("Point Light Volume", command);
            PointLightVolumeInstance volume = gameObject.AddUdonSharpComponent<PointLightVolumeInstance>();

            BindToSingleManager(volume);
            PointLightVolumeEditorUtility.Sync(volume, false, false);
            Selection.activeGameObject = gameObject;
        }

        private static GameObject CreateGameObject(string baseName, MenuCommand command) {
            GameObject parent = command.context as GameObject;
            Transform parentTransform = parent != null ? parent.transform : null;
            string name = GameObjectUtility.GetUniqueNameForSibling(parentTransform, baseName);
            GameObject gameObject = new GameObject(name);

            Undo.RegisterCreatedObjectUndo(gameObject, $"Create {baseName}");
            GameObjectUtility.SetParentAndAlign(gameObject, parent);
            return gameObject;
        }

        private static void BindToSingleManager(LightVolumeInstance volume) {
            LightVolumeManager manager = FindSingleManager(volume);
            if (manager == null) return;

            Undo.RecordObject(manager, "Register Light Volume");
            LightVolumeInstance[] volumes = manager.LightVolumeInstances ?? Array.Empty<LightVolumeInstance>();
            int index = volumes.Length;
            Array.Resize(ref volumes, index + 1);
            volumes[index] = volume;
            manager.LightVolumeInstances = volumes;
            volume.LightVolumeManager = manager;
            volume.RegistryOrder = index;

            EditorUtility.SetDirty(manager);
            UdonSharpEditorUtility.CopyProxyToUdon(manager);
        }

        private static void BindToSingleManager(PointLightVolumeInstance volume) {
            LightVolumeManager manager = FindSingleManager(volume);
            if (manager == null) return;

            Undo.RecordObject(manager, "Register Point Light Volume");
            PointLightVolumeInstance[] volumes = manager.PointLightVolumeInstances ?? Array.Empty<PointLightVolumeInstance>();
            int index = volumes.Length;
            Array.Resize(ref volumes, index + 1);
            volumes[index] = volume;
            manager.PointLightVolumeInstances = volumes;
            volume.LightVolumeManager = manager;
            volume.RegistryOrder = index;

            EditorUtility.SetDirty(manager);
            UdonSharpEditorUtility.CopyProxyToUdon(manager);
        }

        private static LightVolumeManager FindSingleManager(UnityEngine.Object context) {
            Component component = context as Component;
            Scene targetScene = component != null ? component.gameObject.scene : SceneManager.GetActiveScene();
            LightVolumeManager[] managers = UnityEngine.Object.FindObjectsByType<LightVolumeManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            LightVolumeManager result = null;
            for (int i = 0; i < managers.Length; i++) {
                LightVolumeManager manager = managers[i];
                if (manager == null || EditorUtility.IsPersistent(manager) || manager.gameObject.scene != targetScene) continue;
                if (result == null) {
                    result = manager;
                    continue;
                }

                Debug.LogError("[VRC Light Volumes] The new component was not assigned because its scene contains multiple Light Volume Managers.", context);
                return null;
            }

            if (result != null) return result;

            GameObject managerObject = new GameObject("Light Volume Manager");
            Undo.RegisterCreatedObjectUndo(managerObject, "Create Light Volume Manager");
            if (targetScene.IsValid() && managerObject.scene != targetScene)
                SceneManager.MoveGameObjectToScene(managerObject, targetScene);
            LightVolumeManager createdManager = managerObject.AddUdonSharpComponent<LightVolumeManager>();
            UdonSharpEditorUtility.CopyProxyToUdon(createdManager);
            return createdManager;
        }
    }
}
