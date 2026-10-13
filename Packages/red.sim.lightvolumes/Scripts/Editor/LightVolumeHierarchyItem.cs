#if UDONSHARP
using UdonSharpEditor;
#endif
using UnityEditor;
using UnityEngine;

namespace VRCLightVolumes {
    public static class HierarchyMenu {

        [MenuItem("GameObject/Light Volume", false, 9999)]
        // Creates and initializes a unified Light Volume under the current hierarchy selection.
        private static void CreateLightVolume(MenuCommand command) {
            GameObject gameObject = CreateGameObject("Light Volume", command);
#if UDONSHARP
            LightVolumeInstance volume = UdonSharpUndo.AddComponent<LightVolumeInstance>(gameObject);
#else
            LightVolumeInstance volume = Undo.AddComponent<LightVolumeInstance>(gameObject);
#endif

            Transform parent = volume.transform.parent;
            bool initializedFromProbe = parent != null && parent.TryGetComponent(out ReflectionProbe _);
            LightVolumeTools.ResetFromParentReflectionProbe(volume);
            if (!initializedFromProbe) LightVolumeTools.ApplyRuntimeState(volume, false);
            LightVolumeSceneSetup.OnboardHierarchy(gameObject, out _);
            LightVolumeManagerEditorBackend.CopyProxyToUdon(volume);
            Selection.activeGameObject = gameObject;
        }

        [MenuItem("GameObject/Point Light Volume", false, 9999)]
        // Creates and initializes a unified Point Light Volume under the current hierarchy selection.
        private static void CreatePointLightVolume(MenuCommand command) {
            GameObject gameObject = CreateGameObject("Point Light Volume", command);
#if UDONSHARP
            PointLightVolumeInstance volume = UdonSharpUndo.AddComponent<PointLightVolumeInstance>(gameObject);
#else
            PointLightVolumeInstance volume = Undo.AddComponent<PointLightVolumeInstance>(gameObject);
#endif

            LightVolumeSceneSetup.OnboardHierarchy(gameObject, out _);
            PointLightVolumeEditorUtility.Sync(volume);
            Selection.activeGameObject = gameObject;
        }

        // Creates an Undo-aware uniquely named GameObject aligned to the selected parent.
        private static GameObject CreateGameObject(string baseName, MenuCommand command) {
            GameObject parent = command.context as GameObject;
            Transform parentTransform = parent != null ? parent.transform : null;
            string name = GameObjectUtility.GetUniqueNameForSibling(parentTransform, baseName);
            GameObject gameObject = new GameObject(name);

            Undo.RegisterCreatedObjectUndo(gameObject, $"Create {baseName}");
            GameObjectUtility.SetParentAndAlign(gameObject, parent);
            return gameObject;
        }

    }
}
