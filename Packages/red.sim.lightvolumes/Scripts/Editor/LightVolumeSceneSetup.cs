using System.Collections.Generic;
#if UDONSHARP
using UdonSharpEditor;
#endif
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("red.sim.LightVolumesUdon.EditorTests")]

namespace VRCLightVolumes {
    // Owns the one-time setup required when a Light Volumes hierarchy first enters a real scene.
    internal static class LightVolumeSceneSetup {
        private const string UndoName = "Set Up VRC Light Volumes";

        internal static bool IsMainStageSceneObject(GameObject gameObject) {
            if (gameObject == null || EditorUtility.IsPersistent(gameObject)) return false;
            Scene scene = gameObject.scene;
            return scene.IsValid()
                && scene.isLoaded
                && !EditorSceneManager.IsPreviewScene(scene)
                && !EditorSceneManager.IsPreviewSceneObject(gameObject)
                && PrefabStageUtility.GetPrefabStage(gameObject) == null
                && StageUtility.GetMainStageHandle().Contains(gameObject);
        }

        internal static bool IsMainStageScene(Scene scene) {
            if (!scene.IsValid() || !scene.isLoaded || EditorSceneManager.IsPreviewScene(scene)) return false;
            GameObject[] roots = scene.GetRootGameObjects();
            return roots.Length == 0 || IsMainStageSceneObject(roots[0]);
        }

        internal static bool ContainsAuthoringComponents(GameObject root) {
            if (root == null) return false;
            if (root.GetComponentInChildren<LightVolumeInstance>(true) != null
                || root.GetComponentInChildren<PointLightVolumeInstance>(true) != null) return true;
#if UDONSHARP
#pragma warning disable CS0618
            return LightVolumeMigration.CanMigrateHierarchy(root)
                || root.GetComponentInChildren<LightVolume>(true) != null
                || root.GetComponentInChildren<PointLightVolume>(true) != null;
#pragma warning restore CS0618
#else
            return false;
#endif
        }

        internal static bool OnboardHierarchy(GameObject root, out LightVolumeManager manager) {
            manager = null;
            if (!IsMainStageSceneObject(root) || !ContainsAuthoringComponents(root)) return false;
#if UDONSHARP
            if (!MigrateLegacySetupGraph(root, out bool legacyGraphMigrated)) return false;
            if (!CanOnboardHierarchy(root)) {
                int blockedLegacyCount = root.GetComponentsInChildren<LightVolume>(true).Length
                    + root.GetComponentsInChildren<PointLightVolume>(true).Length;
                if (blockedLegacyCount > 0) LogBlockedMigration(root, blockedLegacyCount);
                return false;
            }
#endif

            List<LightVolumeManager> managers = CollectSceneComponents<LightVolumeManager>(root.scene);
            if (managers.Count > 1) {
                Debug.LogError("[VRC Light Volumes] The hierarchy was not assigned because its scene contains multiple Light Volume Managers.", root);
                return false;
            }

            bool managerCreated = managers.Count == 0;
            manager = managerCreated ? CreateManager(root.scene) : managers[0];
            if (manager == null) return false;

#if UDONSHARP
            if (!LightVolumeMigration.IsReadyRuntimeComponent(manager)) {
                Debug.LogError("[VRC Light Volumes] The hierarchy was not assigned because the scene manager has no valid Udon backing program.", manager);
                manager = null;
                return false;
            }
            int migrated = LightVolumeMigration.MigrateHierarchy(root, manager, out int blocked);
            if (blocked > 0) LogBlockedMigration(root, blocked);
#else
            const bool legacyGraphMigrated = false;
            const int migrated = 0;
#endif

            bool hierarchyMigrated = legacyGraphMigrated || migrated > 0;
            // The updater treats true as a mutation signal. A successful reconciliation that finds
            // everything already registered must remain false and perform no serialized writes.
            bool changed = hierarchyMigrated;
            changed |= (managerCreated || legacyGraphMigrated)
                ? RegisterScene(root.scene, manager, hierarchyMigrated)
                : RegisterHierarchy(root, manager, hierarchyMigrated);
            if (!changed && managerCreated) {
                Undo.DestroyObjectImmediate(manager.gameObject);
                manager = null;
                return false;
            }
            return changed;
        }

#if UDONSHARP
        private static bool MigrateLegacySetupGraph(GameObject root, out bool migrated) {
            migrated = false;
#pragma warning disable CS0618
            if (root.GetComponentInChildren<LightVolumeSetup>(true) == null) return true;
            // Setup ownership and registry ambiguity are scene-wide invariants. Reuse the existing
            // coherent migration pass instead of validating a deceptively incomplete root subset.
            int blocked = 0;
            migrated = LightVolumeMigration.MigrateScene(root.scene, ref blocked) > 0;
            if (root.GetComponentInChildren<LightVolumeSetup>(true) == null) return true;
#pragma warning restore CS0618
            Debug.LogWarning($"[VRC Light Volumes] Legacy manager settings on '{root.name}' could not be migrated as a coherent Udon graph. Automatic registration stopped so the configured data stays intact.", root);
            return false;
        }

        private static bool CanOnboardHierarchy(GameObject root) {
            List<LightVolumeInstance> volumes = new List<LightVolumeInstance>();
            root.GetComponentsInChildren(true, volumes);
            for (int i = 0; i < volumes.Count; i++) {
                if (LightVolumeMigration.IsReadyRuntimeComponent(volumes[i])) return true;
            }

            List<PointLightVolumeInstance> pointLights = new List<PointLightVolumeInstance>();
            root.GetComponentsInChildren(true, pointLights);
            for (int i = 0; i < pointLights.Count; i++) {
                if (LightVolumeMigration.IsReadyRuntimeComponent(pointLights[i])) return true;
            }
            return LightVolumeMigration.CanMigrateHierarchy(root);
        }

        private static void LogBlockedMigration(GameObject root, int blocked) {
            Debug.LogWarning($"[VRC Light Volumes] Left {blocked} legacy helper component(s) on '{root.name}' unchanged because no complete unified Udon component was available. Prefab assets and Prefab Stage contents are never modified automatically.", root);
        }
#endif

        private static bool RegisterHierarchy(GameObject root, LightVolumeManager manager, bool hierarchyMigrated) {
            bool changed = false;
            List<LightVolumeInstance> volumes = new List<LightVolumeInstance>();
            root.GetComponentsInChildren(true, volumes);
            for (int i = 0; i < volumes.Count; i++) {
                LightVolumeInstance volume = volumes[i];
#if UDONSHARP
                if (!LightVolumeMigration.IsReadyRuntimeComponent(volume)) continue;
#endif
                if (!LightVolumeManagerTools.EnsureRegistered(manager, volume, UndoName, out bool volumeChanged)) continue;
                changed |= volumeChanged;
#if BAKERY_INCLUDED
                if ((volumeChanged || hierarchyMigrated) && manager.IsBakeryMode && volume.Bake)
                    LightVolumeTools.SetupBakeryDependencies(volume, true);
#endif
            }

            List<PointLightVolumeInstance> pointLights = new List<PointLightVolumeInstance>();
            root.GetComponentsInChildren(true, pointLights);
            for (int i = 0; i < pointLights.Count; i++) {
                PointLightVolumeInstance pointLight = pointLights[i];
#if UDONSHARP
                if (!LightVolumeMigration.IsReadyRuntimeComponent(pointLight)) continue;
#endif
                if (LightVolumeManagerTools.EnsureRegistered(manager, pointLight, UndoName, out bool pointLightChanged))
                    changed |= pointLightChanged;
            }
            return changed;
        }

        private static bool RegisterScene(Scene scene, LightVolumeManager manager, bool hierarchyMigrated) {
            bool changed = false;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
                changed |= RegisterHierarchy(roots[i], manager, hierarchyMigrated);
            return changed;
        }

        private static LightVolumeManager CreateManager(Scene scene) {
            GameObject managerObject = new GameObject("Light Volume Manager");
            Undo.RegisterCreatedObjectUndo(managerObject, UndoName);
            if (managerObject.scene != scene) SceneManager.MoveGameObjectToScene(managerObject, scene);

#if UDONSHARP
            LightVolumeManager manager;
            try {
                manager = UdonSharpUndo.AddComponent<LightVolumeManager>(managerObject);
            } catch (System.Exception exception) {
                Undo.DestroyObjectImmediate(managerObject);
                Debug.LogWarning($"[VRC Light Volumes] Could not create the scene manager. {exception.Message}");
                return null;
            }
            if (!LightVolumeMigration.IsReadyRuntimeComponent(manager)) {
                Undo.DestroyObjectImmediate(managerObject);
                return null;
            }
#else
            LightVolumeManager manager = Undo.AddComponent<LightVolumeManager>(managerObject);
#endif
            LightVolumeManagerTools.CopyProxyToUdon(manager);
            return manager;
        }

        private static List<T> CollectSceneComponents<T>(Scene scene) where T : Component {
            List<T> result = new List<T>();
            List<T> buffer = new List<T>();
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++) {
                buffer.Clear();
                roots[i].GetComponentsInChildren(true, buffer);
                result.AddRange(buffer);
            }
            return result;
        }
    }
}
