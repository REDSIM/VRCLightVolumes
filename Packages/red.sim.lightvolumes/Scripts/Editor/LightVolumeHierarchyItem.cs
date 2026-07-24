using System;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace VRCLightVolumes {
    public static class HierarchyMenu {

        [MenuItem("GameObject/Light Volume Manager", false, 9998)]
        private static void CreateLightVolumeManager(MenuCommand command) {
            GameObject gameObject = CreateGameObject("Light Volume Manager", command);
            LightVolumeManager manager = gameObject.AddUdonSharpComponent<LightVolumeManager>();
            UdonSharpEditorUtility.CopyProxyToUdon(manager);
            Selection.activeGameObject = gameObject;
        }

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
            LightVolumeManager[] managers = UnityEngine.Object.FindObjectsByType<LightVolumeManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            LightVolumeManager result = null;
            for (int i = 0; i < managers.Length; i++) {
                LightVolumeManager manager = managers[i];
                if (manager == null || EditorUtility.IsPersistent(manager) || !manager.gameObject.scene.IsValid() || !manager.gameObject.scene.isLoaded) continue;
                if (result == null) {
                    result = manager;
                    continue;
                }

                Debug.LogWarning("[VRC Light Volumes] The new component was not assigned because multiple Light Volume Managers are loaded.", context);
                return null;
            }
            return result;
        }
    }
}
