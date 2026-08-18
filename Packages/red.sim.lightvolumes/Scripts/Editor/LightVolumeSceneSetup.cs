using System;
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

        // Checks whether an object belongs to a loaded main-stage scene rather than a prefab or preview stage.
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

        // Checks whether a scene is loaded in the main stage and safe for automatic setup.
        internal static bool IsMainStageScene(Scene scene) {
            if (!scene.IsValid() || !scene.isLoaded || EditorSceneManager.IsPreviewScene(scene)) return false;
            GameObject[] roots = scene.GetRootGameObjects();
            return roots.Length == 0 || IsMainStageSceneObject(roots[0]);
        }

        // Checks whether a hierarchy contains unified or migratable Light Volumes authoring components.
        internal static bool ContainsAuthoringComponents(GameObject root) {
            if (root == null) return false;
            if (root.GetComponentInChildren<LightVolumeInstance>(true) != null || root.GetComponentInChildren<PointLightVolumeInstance>(true) != null) return true;
#if UDONSHARP
#pragma warning disable CS0618
            return LightVolumeMigration.CanMigrateHierarchy(root) || root.GetComponentInChildren<LightVolume>(true) != null || root.GetComponentInChildren<PointLightVolume>(true) != null;
#pragma warning restore CS0618
#else
            return false;
#endif
        }

        // Migrates and registers a hierarchy with the single global Manager, creating one when required.
        internal static bool OnboardHierarchy(GameObject root, out LightVolumeManager manager, bool recordRegistrationUndo = true) {
            manager = null;
            if (!IsMainStageSceneObject(root) || !ContainsAuthoringComponents(root)) return false;

            // Reject another loaded scene before migration can modify this hierarchy.
            manager = LightVolumeManagerEditorBackend.GetPrimaryManager();
            if (manager != null && manager.gameObject.scene != root.scene) {
                Debug.LogError($"[LightVolumes] The hierarchy was not assigned because the primary Light Volume Manager belongs to another loaded scene ('{manager.gameObject.scene.name}'). Close that scene or move the hierarchy before setup.", root);
                manager = null;
                return false;
            }
#if UDONSHARP
            if (!MigrateLegacySetupGraph(root, out bool legacyGraphMigrated)) return false;
            if (!CanOnboardHierarchy(root)) {
                int blockedLegacyCount = root.GetComponentsInChildren<LightVolume>(true).Length + root.GetComponentsInChildren<PointLightVolume>(true).Length;
                if (blockedLegacyCount > 0) LogBlockedMigration(root, blockedLegacyCount);
                return false;
            }
#endif

            // Legacy graph migration can create the scene's unified Manager.
            manager = LightVolumeManagerEditorBackend.GetPrimaryManager();

            bool managerCreated = manager == null;
            if (managerCreated) manager = CreateManager(root.scene);
            if (manager == null) return false;

#if UDONSHARP
            if (!LightVolumeMigration.IsReadyRuntimeComponent(manager)) {
                Debug.LogError("[LightVolumes] The hierarchy was not assigned because the scene manager has no valid Udon backing program.", manager);
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
            // Manager creation and legacy migration already own an Undo group. Keep every registry mutation in that same coherent setup operation; only pure repair on an existing graph is derived/no-Undo.
            bool registrationUndo = recordRegistrationUndo || managerCreated || hierarchyMigrated;
            // The updater treats true as a mutation signal. A successful reconciliation that finds everything already registered must remain false and perform no serialized writes.
            bool changed = hierarchyMigrated;
            changed |= (managerCreated || legacyGraphMigrated) ? RegisterScene(root.scene, manager, hierarchyMigrated, registrationUndo) : RegisterHierarchy(root, manager, hierarchyMigrated, registrationUndo);
            if (!changed && managerCreated) {
                Undo.DestroyObjectImmediate(manager.gameObject);
                manager = null;
                return false;
            }
            return changed;
        }

#if UDONSHARP
        // Migrates a legacy LightVolumeSetup graph before individual hierarchy registration begins.
        private static bool MigrateLegacySetupGraph(GameObject root, out bool migrated) {
            migrated = false;
#pragma warning disable CS0618
            if (root.GetComponentInChildren<LightVolumeSetup>(true) == null) return true;
            // Setup ownership and registry ambiguity are scene-wide invariants. Reuse the existing coherent migration pass instead of validating a deceptively incomplete root subset.
            int blocked = 0;
            migrated = LightVolumeMigration.MigrateScene(root.scene, ref blocked) > 0;
            if (root.GetComponentInChildren<LightVolumeSetup>(true) == null) return true;
#pragma warning restore CS0618
            Debug.LogWarning($"[LightVolumes] Legacy manager settings on '{root.name}' could not be migrated as a coherent Udon graph. Automatic registration stopped so the configured data stays intact.", root);
            return false;
        }

        // Checks whether a hierarchy has at least one ready unified component or a safe migration path.
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

        // Reports legacy helpers that automatic onboarding intentionally left unchanged.
        private static void LogBlockedMigration(GameObject root, int blocked) {
            Debug.LogWarning($"[LightVolumes] Left {blocked} legacy helper component(s) on '{root.name}' unchanged because no complete unified Udon component was available. Prefab assets and Prefab Stage contents are never modified automatically.", root);
        }
#endif

        // Repairs one existing Light Volume's derived registry link without adding a user-facing Undo operation.
        internal static bool ReconcileRegistration(LightVolumeManager manager, LightVolumeInstance volume) {
            if (!CanReconcileRegistration(manager, volume)) return false;
            LightVolumeInstance[] registry = manager.LightVolumeInstances;
            int index = registry == null ? -1 : Array.IndexOf(registry, volume);
            if (index >= 0 && volume.LightVolumeManager == manager && volume.RegistryOrder == index) return false;
#if UDONSHARP
            if (!LightVolumeMigration.IsReadyRuntimeComponent(manager) || !LightVolumeMigration.IsReadyRuntimeComponent(volume)) return false;
#endif
            return LightVolumeManagerEditorBackend.EnsureRegistered(manager, volume, UndoName, false, out bool changed) && changed;
        }

        // Repairs one existing Point Light Volume's derived registry link without adding a user-facing Undo operation.
        internal static bool ReconcileRegistration(LightVolumeManager manager, PointLightVolumeInstance pointLight) {
            if (!CanReconcileRegistration(manager, pointLight)) return false;
            PointLightVolumeInstance[] registry = manager.PointLightVolumeInstances;
            int index = registry == null ? -1 : Array.IndexOf(registry, pointLight);
            if (index >= 0 && pointLight.LightVolumeManager == manager && pointLight.RegistryOrder == index) return false;
#if UDONSHARP
            if (!LightVolumeMigration.IsReadyRuntimeComponent(manager) || !LightVolumeMigration.IsReadyRuntimeComponent(pointLight)) return false;
#endif
            return LightVolumeManagerEditorBackend.EnsureRegistered(manager, pointLight, UndoName, false, out bool changed) && changed;
        }

        // Restricts automatic reconciliation to editable components owned by the primary Manager's scene.
        private static bool CanReconcileRegistration(LightVolumeManager manager, Component component) {
            return manager != null && component != null && manager.gameObject.scene == component.gameObject.scene && IsMainStageSceneObject(manager.gameObject) && IsMainStageSceneObject(component.gameObject);
        }

        // Registers every ready Light Volume below one hierarchy with the selected Manager.
        private static bool RegisterHierarchy(GameObject root, LightVolumeManager manager, bool hierarchyMigrated, bool recordRegistrationUndo) {
            bool changed = false;
            List<LightVolumeInstance> volumes = new List<LightVolumeInstance>();
            root.GetComponentsInChildren(true, volumes);
            for (int i = 0; i < volumes.Count; i++) {
                LightVolumeInstance volume = volumes[i];
#if UDONSHARP
                if (!LightVolumeMigration.IsReadyRuntimeComponent(volume)) continue;
#endif
                if (!LightVolumeManagerEditorBackend.EnsureRegistered(manager, volume, UndoName, recordRegistrationUndo, out bool volumeChanged)) continue;
                changed |= volumeChanged;
                if (BakeryEditorBridge.IsAvailable && (volumeChanged || hierarchyMigrated) && manager.EditorIsBakeryMode && volume.Bake) LightVolumeTools.SetupBakeryDependencies(volume, true);
            }

            List<PointLightVolumeInstance> pointLights = new List<PointLightVolumeInstance>();
            root.GetComponentsInChildren(true, pointLights);
            for (int i = 0; i < pointLights.Count; i++) {
                PointLightVolumeInstance pointLight = pointLights[i];
#if UDONSHARP
                if (!LightVolumeMigration.IsReadyRuntimeComponent(pointLight)) continue;
#endif
                if (LightVolumeManagerEditorBackend.EnsureRegistered(manager, pointLight, UndoName, recordRegistrationUndo, out bool pointLightChanged)) changed |= pointLightChanged;
            }
            return changed;
        }

        // Registers authoring components from every root hierarchy in a scene.
        private static bool RegisterScene(Scene scene, LightVolumeManager manager, bool hierarchyMigrated, bool recordRegistrationUndo) {
            bool changed = false;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++) changed |= RegisterHierarchy(roots[i], manager, hierarchyMigrated, recordRegistrationUndo);
            return changed;
        }

        // Creates an Undo-aware unified Manager with a valid Udon backing component.
        private static LightVolumeManager CreateManager(Scene scene) {
            GameObject managerObject = new GameObject("Light Volume Manager");
            if (managerObject.scene != scene) SceneManager.MoveGameObjectToScene(managerObject, scene);
            Undo.RegisterCreatedObjectUndo(managerObject, UndoName);

#if UDONSHARP
            LightVolumeManager manager;
            try {
                manager = UdonSharpUndo.AddComponent<LightVolumeManager>(managerObject);
            } catch (System.Exception exception) {
                Undo.DestroyObjectImmediate(managerObject);
                Debug.LogWarning($"[LightVolumes] Could not create the scene manager. {exception.Message}");
                return null;
            }
            if (!LightVolumeMigration.IsReadyRuntimeComponent(manager)) {
                Undo.DestroyObjectImmediate(managerObject);
                return null;
            }
#else
            LightVolumeManager manager = Undo.AddComponent<LightVolumeManager>(managerObject);
#endif
            LightVolumeManagerEditorBackend.CopyProxyToUdon(manager);
            return manager;
        }
    }
}
