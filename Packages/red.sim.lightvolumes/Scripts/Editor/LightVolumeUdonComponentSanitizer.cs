using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRCLightVolumes {
    [InitializeOnLoad]
    public static class LightVolumeUdonComponentSanitizer {
        private const string BackingUdonBehaviourFieldName = "_udonSharpBackingUdonBehaviour";
        private const string ProgramSourceFieldName = "programSource";
        private const string UdonBehaviourTypeName = "VRC.Udon.UdonBehaviour";
        private const string UndoName = "Sanitize Light Volume Udon Components";

        private static bool _isSanitizeQueued = false;
        private static bool _isSanitizing = false;
        private static bool _isBackingUdonBehaviourFieldCached = false;
        private static FieldInfo _backingUdonBehaviourField = null;
        private static FieldInfo _programSourceField = null;
        private static Type _programSourceFieldOwner = null;
        private static bool _needsAuthoringSyncAfterMigration = false;

        // Registers delayed cleanup so duplicated UdonSharp proxy components are removed after editor reloads and hierarchy edits
        static LightVolumeUdonComponentSanitizer() {
            EditorApplication.delayCall += QueueSanitizeLoadedScenes;
            EditorApplication.hierarchyChanged += QueueSanitizeLoadedScenes;
            EditorSceneManager.sceneOpened += QueueSanitizeOpenedScene;
        }

        // Removes duplicated Light Volume system Udon components from every loaded scene object
        public static int SanitizeLoadedScenes() {
            if (_isSanitizing) return 0;

            _isSanitizing = true;
            try {
                int removedCount = 0;

                GameObject[] gameObjects = Resources.FindObjectsOfTypeAll<GameObject>();
                for (int i = 0; i < gameObjects.Length; i++) {
                    removedCount += SanitizeGameObject(gameObjects[i]);
                }

                if (_needsAuthoringSyncAfterMigration) {
                    _needsAuthoringSyncAfterMigration = false;
                    SyncAuthoringComponentsToMigratedRuntime();
                }

                return removedCount;
            } finally {
                _isSanitizing = false;
            }
        }

        // Removes duplicated Light Volume system Udon components from one scene object
        public static int SanitizeGameObject(GameObject gameObject) {
            if (!ShouldSanitizeGameObject(gameObject)) return 0;

            int removedCount = 0;
            removedCount += SanitizeManagers(gameObject);
            removedCount += SanitizeLightVolumeInstances(gameObject);
            removedCount += SanitizePointLightVolumeInstances(gameObject);
            bool migrated = MigrateLegacyRuntimeComponents(gameObject);

            if (removedCount > 0 || migrated) MarkSceneDirty(gameObject);
            if (migrated) _needsAuthoringSyncAfterMigration = true;
            if (_needsAuthoringSyncAfterMigration && !_isSanitizing) {
                _needsAuthoringSyncAfterMigration = false;
                SyncAuthoringComponentsToMigratedRuntime();
            }
            return removedCount;
        }

        // Queues cleanup after a scene is opened and all scene objects are available
        private static void QueueSanitizeOpenedScene(Scene scene, OpenSceneMode mode) {
            QueueSanitizeLoadedScenes();
        }

        // Coalesces editor callbacks into one delayed cleanup pass
        private static void QueueSanitizeLoadedScenes() {
            if (_isSanitizeQueued || _isSanitizing) return;
            if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return;

            _isSanitizeQueued = true;
            EditorApplication.delayCall += RunQueuedSanitizeLoadedScenes;
        }

        // Runs a queued cleanup pass once Unity finishes the current editor event
        private static void RunQueuedSanitizeLoadedScenes() {
            _isSanitizeQueued = false;
            if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;

            int removedCount = SanitizeLoadedScenes();
            if (removedCount > 0) Debug.Log($"[LightVolume] Removed {removedCount} duplicate system Udon component(s)");
        }

        // Removes duplicated manager proxies and their matching extra backing UdonBehaviour components
        private static int SanitizeManagers(GameObject gameObject) {
            LightVolumeManager[] managers = gameObject.GetComponents<LightVolumeManager>();
            if (managers.Length == 0) return 0;

            LightVolumeManager keeper = GetManagerKeeper(gameObject, managers);
            if (keeper == null) return 0;

            LightVolumeSetup setup = gameObject.GetComponent<LightVolumeSetup>();
            if (setup != null && setup.LightVolumeManager != keeper) {
                Undo.RecordObject(setup, UndoName);
                setup.LightVolumeManager = keeper;
                MarkObjectDirty(setup);
            }

            return RemoveDuplicateComponents(gameObject, managers, keeper);
        }

        // Removes duplicated light volume proxies and their matching extra backing UdonBehaviour components
        private static int SanitizeLightVolumeInstances(GameObject gameObject) {
            LightVolumeInstance[] instances = gameObject.GetComponents<LightVolumeInstance>();
            if (instances.Length == 0) return 0;

            LightVolumeInstance keeper = GetLightVolumeInstanceKeeper(gameObject, instances);
            if (keeper == null) return 0;

            LightVolume volume = gameObject.GetComponent<LightVolume>();
            if (volume != null && volume.LightVolumeInstance != keeper) {
                Undo.RecordObject(volume, UndoName);
                volume.LightVolumeInstance = keeper;
                MarkObjectDirty(volume);
            }

            return RemoveDuplicateComponents(gameObject, instances, keeper);
        }

        // Removes duplicated point light volume proxies and their matching extra backing UdonBehaviour components
        private static int SanitizePointLightVolumeInstances(GameObject gameObject) {
            PointLightVolumeInstance[] instances = gameObject.GetComponents<PointLightVolumeInstance>();
            if (instances.Length == 0) return 0;

            PointLightVolumeInstance keeper = GetPointLightVolumeInstanceKeeper(gameObject, instances);
            if (keeper == null) return 0;

            PointLightVolume pointLight = gameObject.GetComponent<PointLightVolume>();
            if (pointLight != null && pointLight.PointLightVolumeInstance != keeper) {
                Undo.RecordObject(pointLight, UndoName);
                pointLight.PointLightVolumeInstance = keeper;
                MarkObjectDirty(pointLight);
            }

            return RemoveDuplicateComponents(gameObject, instances, keeper);
        }

        // Migrates serialized 2.x runtime component data after Unity loads the scene with 3.x scripts.
        private static bool MigrateLegacyRuntimeComponents(GameObject gameObject) {
            bool migrated = false;

            LightVolumeInstance[] lightVolumes = gameObject.GetComponents<LightVolumeInstance>();
            for (int i = 0; i < lightVolumes.Length; i++) {
                LightVolumeInstance lightVolume = lightVolumes[i];
                if (lightVolume == null) continue;
                bool wasMigrated = lightVolume.MigrateLegacyVolumeData();
                bool needsDirty = lightVolume.ConsumeLegacyVolumeDataMigrationDirty();
                if (!wasMigrated && !needsDirty) continue;
                MarkObjectDirty(lightVolume);
                migrated = true;
            }

            PointLightVolumeInstance[] pointLights = gameObject.GetComponents<PointLightVolumeInstance>();
            for (int i = 0; i < pointLights.Length; i++) {
                PointLightVolumeInstance pointLight = pointLights[i];
                if (pointLight == null) continue;
                bool wasMigrated = pointLight.MigrateLegacyPackedData();
                bool needsDirty = pointLight.ConsumeLegacyPackedDataMigrationDirty();
                if (!wasMigrated && !needsDirty) continue;
                MarkObjectDirty(pointLight);
                migrated = true;
            }

            return migrated;
        }

        // Re-syncs authoring MonoBehaviours into their runtime Udon components after legacy field migration.
        private static void SyncAuthoringComponentsToMigratedRuntime() {
            LightVolumeSetup[] setups = Resources.FindObjectsOfTypeAll<LightVolumeSetup>();
            for (int i = 0; i < setups.Length; i++) {
                LightVolumeSetup setup = setups[i];
                if (!ShouldSanitizeComponent(setup)) continue;
                setup.SyncUdonScript();
                MarkObjectDirty(setup);
            }
        }

        // Returns the healthiest manager, preferring valid Udon backing and existing runtime data over possibly stale authoring references
        private static LightVolumeManager GetManagerKeeper(GameObject gameObject, LightVolumeManager[] managers) {
            LightVolumeSetup setup = gameObject.GetComponent<LightVolumeSetup>();
            return GetBestKeeper(managers, setup != null ? setup.LightVolumeManager : null);
        }

        // Returns the healthiest light volume instance, preferring valid Udon backing and existing runtime data over possibly stale authoring references
        private static LightVolumeInstance GetLightVolumeInstanceKeeper(GameObject gameObject, LightVolumeInstance[] instances) {
            LightVolume volume = gameObject.GetComponent<LightVolume>();
            return GetBestKeeper(instances, volume != null ? volume.LightVolumeInstance : null);
        }

        // Returns the healthiest point light volume instance, preferring valid Udon backing and existing runtime data over possibly stale authoring references
        private static PointLightVolumeInstance GetPointLightVolumeInstanceKeeper(GameObject gameObject, PointLightVolumeInstance[] instances) {
            PointLightVolume pointLight = gameObject.GetComponent<PointLightVolume>();
            return GetBestKeeper(instances, pointLight != null ? pointLight.PointLightVolumeInstance : null);
        }

        // Removes every component except the selected keeper and keeps the matching hidden UdonBehaviour backing component intact
        private static int RemoveDuplicateComponents<T>(GameObject gameObject, T[] components, T keeper) where T : Component {
            int removedCount = 0;
            Component keeperBackingUdonBehaviour = GetBackingUdonBehaviour(keeper);

            for (int i = 0; i < components.Length; i++) {
                T duplicate = components[i];
                if (duplicate == null || duplicate == keeper) continue;

                ReplaceReferences(duplicate, keeper);

                Component duplicateBackingUdonBehaviour = GetBackingUdonBehaviour(duplicate);
                Undo.DestroyObjectImmediate(duplicate);
                removedCount++;

                if (duplicateBackingUdonBehaviour != null && duplicateBackingUdonBehaviour != keeperBackingUdonBehaviour) {
                    Undo.DestroyObjectImmediate(duplicateBackingUdonBehaviour);
                    removedCount++;
                }
            }

            removedCount += RemoveExtraBackingUdonBehaviours(gameObject, keeper);
            return removedCount;
        }

        // Replaces scene references before a duplicated component is destroyed
        private static void ReplaceReferences(Component duplicate, Component keeper) {
            LightVolumeManager duplicateManager = duplicate as LightVolumeManager;
            if (duplicateManager != null) {
                ReplaceManagerReferences(duplicateManager, keeper as LightVolumeManager);
                return;
            }

            LightVolumeInstance duplicateLightVolume = duplicate as LightVolumeInstance;
            if (duplicateLightVolume != null) {
                ReplaceLightVolumeInstanceReferences(duplicateLightVolume, keeper as LightVolumeInstance);
                return;
            }

            PointLightVolumeInstance duplicatePointLight = duplicate as PointLightVolumeInstance;
            if (duplicatePointLight != null) ReplacePointLightVolumeInstanceReferences(duplicatePointLight, keeper as PointLightVolumeInstance);
        }

        // Repoints setup and runtime references from a duplicated manager to the kept manager
        private static void ReplaceManagerReferences(LightVolumeManager duplicate, LightVolumeManager keeper) {
            if (keeper == null) return;

            LightVolumeSetup[] setups = Resources.FindObjectsOfTypeAll<LightVolumeSetup>();
            for (int i = 0; i < setups.Length; i++) {
                LightVolumeSetup setup = setups[i];
                if (!ShouldSanitizeComponent(setup) || setup.LightVolumeManager != duplicate) continue;
                Undo.RecordObject(setup, UndoName);
                setup.LightVolumeManager = keeper;
                MarkObjectDirty(setup);
            }

            LightVolumeInstance[] lightVolumes = Resources.FindObjectsOfTypeAll<LightVolumeInstance>();
            for (int i = 0; i < lightVolumes.Length; i++) {
                LightVolumeInstance lightVolume = lightVolumes[i];
                if (!ShouldSanitizeComponent(lightVolume) || lightVolume.LightVolumeManager != duplicate) continue;
                Undo.RecordObject(lightVolume, UndoName);
                lightVolume.LightVolumeManager = keeper;
                MarkObjectDirty(lightVolume);
            }

            PointLightVolumeInstance[] pointLights = Resources.FindObjectsOfTypeAll<PointLightVolumeInstance>();
            for (int i = 0; i < pointLights.Length; i++) {
                PointLightVolumeInstance pointLight = pointLights[i];
                if (!ShouldSanitizeComponent(pointLight) || pointLight.LightVolumeManager != duplicate) continue;
                Undo.RecordObject(pointLight, UndoName);
                pointLight.LightVolumeManager = keeper;
                MarkObjectDirty(pointLight);
            }
        }

        // Repoints authoring, setup and manager references from a duplicated light volume instance to the kept instance
        private static void ReplaceLightVolumeInstanceReferences(LightVolumeInstance duplicate, LightVolumeInstance keeper) {
            if (keeper == null) return;

            LightVolume[] volumes = Resources.FindObjectsOfTypeAll<LightVolume>();
            for (int i = 0; i < volumes.Length; i++) {
                LightVolume volume = volumes[i];
                if (!ShouldSanitizeComponent(volume) || volume.LightVolumeInstance != duplicate) continue;
                Undo.RecordObject(volume, UndoName);
                volume.LightVolumeInstance = keeper;
                MarkObjectDirty(volume);
            }

            LightVolumeManager[] managers = Resources.FindObjectsOfTypeAll<LightVolumeManager>();
            for (int i = 0; i < managers.Length; i++) {
                LightVolumeManager manager = managers[i];
                if (!ShouldSanitizeComponent(manager) || manager.LightVolumeInstances == null) continue;

                bool changed = false;
                for (int j = 0; j < manager.LightVolumeInstances.Length; j++) {
                    if (manager.LightVolumeInstances[j] != duplicate) continue;
                    if (!changed) Undo.RecordObject(manager, UndoName);
                    manager.LightVolumeInstances[j] = keeper;
                    changed = true;
                }
                if (changed) MarkObjectDirty(manager);
            }

            LightVolumeSetup[] setups = Resources.FindObjectsOfTypeAll<LightVolumeSetup>();
            for (int i = 0; i < setups.Length; i++) {
                LightVolumeSetup setup = setups[i];
                if (!ShouldSanitizeComponent(setup) || setup.LightVolumeDataList == null) continue;

                bool changed = false;
                for (int j = 0; j < setup.LightVolumeDataList.Count; j++) {
                    LightVolumeData data = setup.LightVolumeDataList[j];
                    if (data.LightVolumeInstance != duplicate) continue;
                    if (!changed) Undo.RecordObject(setup, UndoName);
                    data.LightVolumeInstance = keeper;
                    setup.LightVolumeDataList[j] = data;
                    changed = true;
                }
                if (changed) MarkObjectDirty(setup);
            }
        }

        // Repoints authoring and manager references from a duplicated point light volume instance to the kept instance
        private static void ReplacePointLightVolumeInstanceReferences(PointLightVolumeInstance duplicate, PointLightVolumeInstance keeper) {
            if (keeper == null) return;

            PointLightVolume[] pointLights = Resources.FindObjectsOfTypeAll<PointLightVolume>();
            for (int i = 0; i < pointLights.Length; i++) {
                PointLightVolume pointLight = pointLights[i];
                if (!ShouldSanitizeComponent(pointLight) || pointLight.PointLightVolumeInstance != duplicate) continue;
                Undo.RecordObject(pointLight, UndoName);
                pointLight.PointLightVolumeInstance = keeper;
                MarkObjectDirty(pointLight);
            }

            LightVolumeManager[] managers = Resources.FindObjectsOfTypeAll<LightVolumeManager>();
            for (int i = 0; i < managers.Length; i++) {
                LightVolumeManager manager = managers[i];
                if (!ShouldSanitizeComponent(manager) || manager.PointLightVolumeInstances == null) continue;

                bool changed = false;
                for (int j = 0; j < manager.PointLightVolumeInstances.Length; j++) {
                    if (manager.PointLightVolumeInstances[j] != duplicate) continue;
                    if (!changed) Undo.RecordObject(manager, UndoName);
                    manager.PointLightVolumeInstances[j] = keeper;
                    changed = true;
                }
                if (changed) MarkObjectDirty(manager);
            }
        }

        // Removes orphaned or duplicated hidden UdonBehaviour components with the same program source as the kept proxy
        private static int RemoveExtraBackingUdonBehaviours(GameObject gameObject, Component keeper) {
            Component keeperBackingUdonBehaviour = GetBackingUdonBehaviour(keeper);
            UnityEngine.Object keeperProgramSource = GetProgramSource(keeperBackingUdonBehaviour);
            if (keeperBackingUdonBehaviour == null || keeperProgramSource == null) return 0;

            int removedCount = 0;
            Component[] components = gameObject.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++) {
                Component component = components[i];
                if (component == null || component == keeperBackingUdonBehaviour || !IsUdonBehaviour(component)) continue;
                if ((component.hideFlags & HideFlags.HideInInspector) == 0) continue;
                if (GetProgramSource(component) != keeperProgramSource) continue;

                Undo.DestroyObjectImmediate(component);
                removedCount++;
            }

            return removedCount;
        }

        // Returns the hidden UdonBehaviour assigned to a UdonSharp proxy component
        private static Component GetBackingUdonBehaviour(Component component) {
            if (component == null) return null;

            FieldInfo field = GetBackingUdonBehaviourField(component.GetType());
            if (field == null) return null;

            return field.GetValue(component) as Component;
        }

        // Finds the private UdonSharp backing field without a hard dependency on UdonSharp.Editor
        private static FieldInfo GetBackingUdonBehaviourField(Type componentType) {
            if (_isBackingUdonBehaviourFieldCached) return _backingUdonBehaviourField;

            Type type = componentType;
            while (type != null) {
                FieldInfo field = type.GetField(BackingUdonBehaviourFieldName, BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null) {
                    _backingUdonBehaviourField = field;
                    break;
                }
                type = type.BaseType;
            }

            _isBackingUdonBehaviourFieldCached = true;
            return _backingUdonBehaviourField;
        }

        // Reads UdonBehaviour.programSource through reflection so this utility can avoid Udon editor assembly dependencies
        private static UnityEngine.Object GetProgramSource(Component component) {
            if (component == null || !IsUdonBehaviour(component)) return null;

            Type componentType = component.GetType();
            if (_programSourceField == null || _programSourceFieldOwner != componentType) {
                _programSourceField = componentType.GetField(ProgramSourceFieldName, BindingFlags.Instance | BindingFlags.Public);
                _programSourceFieldOwner = componentType;
            }

            return _programSourceField != null ? _programSourceField.GetValue(component) as UnityEngine.Object : null;
        }

        // Returns true when this component is the hidden or visible Udon VM component, not a UdonSharp proxy
        private static bool IsUdonBehaviour(Component component) {
            return component != null && component.GetType().FullName == UdonBehaviourTypeName;
        }

        // Returns true when this scene object can be safely sanitized
        private static bool ShouldSanitizeGameObject(GameObject gameObject) {
            if (gameObject == null || EditorUtility.IsPersistent(gameObject)) return false;
            if (!gameObject.scene.IsValid()) return false;
            return (gameObject.hideFlags & HideFlags.DontSaveInEditor) == 0;
        }

        // Returns true when this component belongs to a loaded scene object
        private static bool ShouldSanitizeComponent(Component component) {
            return component != null && ShouldSanitizeGameObject(component.gameObject);
        }

        // Returns the best keeper candidate from duplicated components using Udon health first and authoring references only as a tie-breaker
        private static T GetBestKeeper<T>(T[] components, T preferred) where T : Component {
            T best = null;
            int bestScore = -1;

            for (int i = 0; i < components.Length; i++) {
                T component = components[i];
                if (component == null) continue;

                int score = GetKeeperScore(component, component == preferred);
                if (score <= bestScore) continue;

                best = component;
                bestScore = score;
            }

            return best;
        }

        // Scores one duplicate candidate so a newly created broken proxy cannot replace the original component
        private static int GetKeeperScore(Component component, bool isPreferred) {
            int score = 0;

            Component backingUdonBehaviour = GetBackingUdonBehaviour(component);
            bool hasLocalBacking = backingUdonBehaviour != null && backingUdonBehaviour.gameObject == component.gameObject;
            if (hasLocalBacking && GetProgramSource(backingUdonBehaviour) != null) score += 100000; // Best signal that the UdonSharp proxy is wired to a real Udon program
            else if (hasLocalBacking) score += 10000; // Still better than a proxy with no backing UdonBehaviour at all
            if (hasLocalBacking && IsBackingImmediatelyAfterProxy(component, backingUdonBehaviour)) score += 50000; // UdonSharp places the hidden UdonBehaviour directly after its proxy

            score += GetRuntimeDataScore(component) * 100;
            if (isPreferred) score += 10; // Authoring references can be stale after duplication, so they only break close ties

            return score;
        }

        // Returns true when component order matches the normal UdonSharp proxy followed by hidden backing UdonBehaviour layout
        private static bool IsBackingImmediatelyAfterProxy(Component proxy, Component backingUdonBehaviour) {
            Component[] components = proxy.gameObject.GetComponents<Component>();
            for (int i = 0; i < components.Length - 1; i++) {
                if (components[i] == proxy) return components[i + 1] == backingUdonBehaviour;
            }
            return false;
        }

        // Gives a small preference to the component that already contains generated runtime references
        private static int GetRuntimeDataScore(Component component) {
            LightVolumeManager manager = component as LightVolumeManager;
            if (manager != null) {
                int score = 0;
                if (manager.LightVolumeAtlas != null || manager.LightVolumeAtlasBase != null) score += 4;
                if (manager.CustomTextures != null) score += 2;
                if (manager.ShadowTextures != null) score += 2;
                if (manager.LightVolumeInstances != null && manager.LightVolumeInstances.Length > 0) score += 1;
                if (manager.PointLightVolumeInstances != null && manager.PointLightVolumeInstances.Length > 0) score += 1;
                return score;
            }

            LightVolumeInstance lightVolume = component as LightVolumeInstance;
            if (lightVolume != null) return lightVolume.LightVolumeManager != null ? 1 : 0;

            PointLightVolumeInstance pointLight = component as PointLightVolumeInstance;
            if (pointLight != null) return pointLight.LightVolumeManager != null ? 1 : 0;

            return 0;
        }

        // Marks a modified object dirty and preserves prefab instance overrides
        private static void MarkObjectDirty(UnityEngine.Object target) {
            if (target == null) return;
            EditorUtility.SetDirty(target);
            if (PrefabUtility.IsPartOfPrefabInstance(target)) PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            Component component = target as Component;
            if (component != null) MarkSceneDirty(component.gameObject);
        }

        // Marks the owning scene dirty after component removal
        private static void MarkSceneDirty(GameObject gameObject) {
            if (gameObject == null) return;
            Scene scene = gameObject.scene;
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
        }
    }
}
