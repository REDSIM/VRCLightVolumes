#if UDONSHARP
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.Udon;
using AtlasPostProcessor = VRCLightVolumes.Editor.AtlasPostProcessor;

namespace VRCLightVolumes {
    // This file intentionally consumes the obsolete serialized bridge schema.
#pragma warning disable CS0618

    // One-way migration and read-only validation for the unified Udon authoring model. It creates a complete Udon graph only for a coherent pure-v2 graph; existing Udon components are never repaired, replaced, guessed between duplicates, or resolved through stale links.
    [InitializeOnLoad]
    public static class LightVolumeMigration {
        private const string UndoName = "Migrate VRC Light Volumes";
        private const int MaxIssueExamples = 5;

        private static readonly Dictionary<string, Dictionary<ulong, string>> SceneLegacyRuntimeBlocksCache
            = new Dictionary<string, Dictionary<ulong, string>>();
        private static readonly string[] LegacyRuntimeYamlPrefixes = {
            "  RelativeRotation:",
            "  _legacyRelativeRotation:",
            "  BoundsUvwMax",
            "  _legacyBoundsUvwMax",
            "  PositionData:",
            "  _legacyPositionData:",
            "  DirectionData:",
            "  _legacyDirectionData:",
            "  CustomID:",
            "  _legacyCustomID:",
            "  AngleData:",
            "  _legacyAngleData:",
            "  ShadowmaskIndex:",
            "  _legacyShadowmaskIndex:"
        };
        // Keep the object alongside its instance ID: Unity can reuse IDs after a scene unload, and a bare HashSet<int> could then suppress migration for an unrelated component in a later scene.
        private static readonly Dictionary<int, Component> MigratedRuntimeComponents = new Dictionary<int, Component>();
        private static readonly List<string> IssueExamples = new List<string>(MaxIssueExamples);
        private static bool _migrationQueued;

        // Installs scene-open migration hooks and schedules the initial loaded-scene pass.
        static LightVolumeMigration() {
            EditorSceneManager.sceneOpened += OnSceneOpened;
            AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
            EditorApplication.quitting += Shutdown;
            QueueLoadedScenesMigration();
        }

        // Removes editor callbacks and discards transient migration caches before this editor domain ends.
        private static void Shutdown() {
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            AssemblyReloadEvents.beforeAssemblyReload -= Shutdown;
            EditorApplication.quitting -= Shutdown;
            EditorApplication.delayCall -= RunQueuedMigration;
            _migrationQueued = false;
            SceneLegacyRuntimeBlocksCache.Clear();
            MigratedRuntimeComponents.Clear();
            IssueExamples.Clear();
        }

        // Queues a coalesced migration after Unity and UdonSharp finish opening a scene.
        private static void OnSceneOpened(Scene scene, OpenSceneMode mode) {
            QueueLoadedScenesMigration();
        }

        // Schedules one coalesced migration pass after scene deserialization and UdonSharp setup finish.
        public static void QueueLoadedScenesMigration() {
            if (_migrationQueued) return;
            _migrationQueued = true;
            EditorApplication.delayCall += RunQueuedMigration;
        }

        // Runs deferred migration when the editor is stable, then queues normal hierarchy onboarding.
        private static void RunQueuedMigration() {
            EditorApplication.delayCall -= RunQueuedMigration;
            _migrationQueued = false;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || Undo.isProcessing) {
                QueueLoadedScenesMigration();
                return;
            }

            MigrateLoadedScenes();
            LightVolumeEditorUpdater.QueueLoadedSceneOnboarding();
        }

        // Migrates all loaded scenes. A clean unified scene is not dirtied.
        public static int MigrateLoadedScenes() {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return 0;

            SceneLegacyRuntimeBlocksCache.Clear();
            int migrated = 0;
            int blocked = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++) {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!LightVolumeSceneSetup.IsMainStageScene(scene)) continue;
                migrated += MigrateScene(scene, ref blocked);
            }
            if (blocked > 0) Debug.LogWarning($"[LightVolumes] Left {blocked} legacy component(s) unchanged because neither a coherent pure-v2 graph nor a complete existing unified Udon graph was available. Existing Udon components were not repaired or replaced.");
            return migrated;
        }

        // Migrates coherent legacy graphs and packed runtime payloads in one loaded main-stage scene.
        internal static int MigrateScene(Scene scene, ref int blocked) {
            if (!LightVolumeSceneSetup.IsMainStageScene(scene)) return 0;

            // Disk-only packed fields are authoritative only before anything has modified the loaded scene. Never replay stale saved YAML over unsaved proxy edits after a domain reload.
            bool savedSceneYamlIsAuthoritative = !scene.isDirty;
            GameObject[] roots = scene.GetRootGameObjects();
            List<LightVolumeSetup> setups = Collect<LightVolumeSetup>(roots);
            List<LightVolume> volumes = Collect<LightVolume>(roots);
            List<PointLightVolume> pointLights = Collect<PointLightVolume>(roots);

            // A complete v2 authoring graph has no Udon components yet. Convert that graph once, before validation; existing partial or broken Udon graphs are never repaired here.
            CreatePureLegacyGraphs(setups, volumes, pointLights);

            List<LightVolumeManager> managers = Collect<LightVolumeManager>(roots);
            List<LightVolumeInstance> runtimeVolumes = Collect<LightVolumeInstance>(roots);
            List<PointLightVolumeInstance> runtimePointLights = Collect<PointLightVolumeInstance>(roots);

            bool changed = MigrateLegacyRuntimePayload(managers, runtimeVolumes, runtimePointLights, savedSceneYamlIsAuthoritative);
            if (setups.Count == 0 && volumes.Count == 0 && pointLights.Count == 0) {
                if (changed) {
                    RefreshManagerRuntimeState(managers);
                    EditorSceneManager.MarkSceneDirty(scene);
                }
                return 0;
            }

            // Validate the complete one-to-one graph before touching a legacy destination. Unique co-located destinations and unambiguous registries are authoritative.
            Dictionary<LightVolume, LightVolumeInstance> volumeDestinations = new Dictionary<LightVolume, LightVolumeInstance>();
            Dictionary<PointLightVolume, PointLightVolumeInstance> pointDestinations = new Dictionary<PointLightVolume, PointLightVolumeInstance>();
            Dictionary<LightVolumeSetup, LightVolumeManager> setupDestinations = new Dictionary<LightVolumeSetup, LightVolumeManager>();
            Dictionary<LightVolumeInstance, int> volumeDestinationUse = new Dictionary<LightVolumeInstance, int>();
            Dictionary<PointLightVolumeInstance, int> pointDestinationUse = new Dictionary<PointLightVolumeInstance, int>();
            Dictionary<LightVolumeManager, int> managerDestinationUse = new Dictionary<LightVolumeManager, int>();
            Dictionary<LightVolume, LightVolumeSetup> volumeRegistryOwners = new Dictionary<LightVolume, LightVolumeSetup>();
            Dictionary<PointLightVolume, LightVolumeSetup> pointRegistryOwners = new Dictionary<PointLightVolume, LightVolumeSetup>();
            HashSet<LightVolume> ambiguousVolumeOwners = new HashSet<LightVolume>();
            HashSet<PointLightVolume> ambiguousPointOwners = new HashSet<PointLightVolume>();

            for (int i = 0; i < volumes.Count; i++) {
                LightVolume source = volumes[i];
                LightVolumeInstance destination = ResolveLightVolumeInstance(source);
                volumeDestinations[source] = destination;
                IncrementUseCount(volumeDestinationUse, destination);
            }
            for (int i = 0; i < pointLights.Count; i++) {
                PointLightVolume source = pointLights[i];
                PointLightVolumeInstance destination = ResolvePointLightVolumeInstance(source);
                pointDestinations[source] = destination;
                IncrementUseCount(pointDestinationUse, destination);
            }
            for (int i = 0; i < setups.Count; i++) {
                LightVolumeSetup source = setups[i];
                LightVolumeManager destination = ResolveManager(source);
                setupDestinations[source] = destination;
                IncrementUseCount(managerDestinationUse, destination);
                RegisterRegistryOwners(source, volumeRegistryOwners, ambiguousVolumeOwners, pointRegistryOwners, ambiguousPointOwners);
            }

            Dictionary<LightVolumeSetup, bool> setupReady = new Dictionary<LightVolumeSetup, bool>();
            for (int i = 0; i < setups.Count; i++) {
                LightVolumeSetup source = setups[i];
                LightVolumeManager destination = setupDestinations[source];
                bool ready = IsUniqueReadyDestination(destination, managerDestinationUse) && CanMapRegistries(source, destination, volumes, pointLights, volumeDestinations, pointDestinations, volumeDestinationUse, pointDestinationUse, volumeRegistryOwners, pointRegistryOwners, ambiguousVolumeOwners, ambiguousPointOwners);
                setupReady[source] = ready;
                if (!ready) blocked++;
            }

            bool[] volumeReady = new bool[volumes.Count];
            LightVolumeManager[] volumeManagers = new LightVolumeManager[volumes.Count];
            for (int i = 0; i < volumes.Count; i++) {
                LightVolume source = volumes[i];
                LightVolumeInstance destination = volumeDestinations[source];
                bool ownerValid = TryResolveOwner(source.LightVolumeSetup, source, volumeRegistryOwners, ambiguousVolumeOwners, out LightVolumeSetup owner);
                LightVolumeManager manager = ResolveValidatedManager(owner, destination != null ? destination.LightVolumeManager : null, setupReady, setupDestinations);
                bool ready = ownerValid && IsUniqueReadyDestination(destination, volumeDestinationUse) && IsExactReadyRuntimeComponent(manager) && (destination.LightVolumeManager == null || destination.LightVolumeManager == manager);
                volumeReady[i] = ready;
                volumeManagers[i] = manager;
                if (!ready) blocked++;
            }

            bool[] pointReady = new bool[pointLights.Count];
            LightVolumeManager[] pointManagers = new LightVolumeManager[pointLights.Count];
            for (int i = 0; i < pointLights.Count; i++) {
                PointLightVolume source = pointLights[i];
                PointLightVolumeInstance destination = pointDestinations[source];
                bool ownerValid = TryResolveOwner(source.LightVolumeSetup, source, pointRegistryOwners, ambiguousPointOwners, out LightVolumeSetup owner);
                LightVolumeManager manager = ResolveValidatedManager(owner, destination != null ? destination.LightVolumeManager : null, setupReady, setupDestinations);
                bool ready = ownerValid && IsUniqueReadyDestination(destination, pointDestinationUse) && IsExactReadyRuntimeComponent(manager) && (destination.LightVolumeManager == null || destination.LightVolumeManager == manager);
                pointReady[i] = ready;
                pointManagers[i] = manager;
                if (!ready) blocked++;
            }

            HashSet<LightVolumeManager> changedManagers = new HashSet<LightVolumeManager>();
            HashSet<LightVolumeSetup> migratedSetups = new HashSet<LightVolumeSetup>();
            for (int i = 0; i < setups.Count; i++) {
                LightVolumeSetup source = setups[i];
                if (!setupReady[source]) continue;
                LightVolumeManager destination = setupDestinations[source];
                Undo.RecordObject(destination, UndoName);
                CopyLegacySetup(source, destination);
                MarkDirty(destination);
                changedManagers.Add(destination);
                migratedSetups.Add(source);
                changed = true;
            }

            for (int i = 0; i < volumes.Count; i++) {
                if (!volumeReady[i]) continue;
                LightVolume source = volumes[i];
                LightVolumeInstance destination = volumeDestinations[source];
                Undo.RecordObject(destination, UndoName);
                CopyLegacyLightVolume(source, destination);
                destination.LightVolumeManager = volumeManagers[i];
                CopyProxyToUdon(destination);
                MarkDirty(destination);
                changedManagers.Add(volumeManagers[i]);
                changed = true;
            }

            for (int i = 0; i < pointLights.Count; i++) {
                if (!pointReady[i]) continue;
                PointLightVolume source = pointLights[i];
                PointLightVolumeInstance destination = pointDestinations[source];
                Undo.RecordObject(destination, UndoName);
                CopyLegacyPointLight(source, destination);
                destination.LightVolumeManager = pointManagers[i];
                destination.EditorApplyAuthoringData(true, true, false);
                destination.CacheEditorObservedValues();
                CopyProxyToUdon(destination);
                MarkDirty(destination);
                changedManagers.Add(pointManagers[i]);
                changed = true;
            }

            // Canonicalize and rebuild each affected manager once after the whole batch is coherent.
            foreach (LightVolumeManager manager in changedManagers) {
                LightVolumeManagerEditorBackend.ApplySettings(manager, false);
                MarkDirty(manager);
            }

            int removed = 0;
            for (int i = 0; i < volumes.Count; i++) {
                LightVolume source = volumes[i];
                if (!volumeReady[i] || source == null) continue;
                RemoveLegacyComponent(source);
                removed++;
            }
            for (int i = 0; i < pointLights.Count; i++) {
                PointLightVolume source = pointLights[i];
                if (!pointReady[i] || source == null) continue;
                RemoveLegacyComponent(source);
                removed++;
            }
            foreach (LightVolumeSetup setup in migratedSetups) {
                if (setup == null) continue;
                RemoveLegacyComponent(setup);
                removed++;
            }

            if (changed || removed > 0) {
                RefreshManagerRuntimeState(managers);
                EditorSceneManager.MarkSceneDirty(scene);
            }
            return removed;
        }

        // Migrates obsolete helper components only inside a hierarchy that has just entered a real scene. Existing unified prefab components are reused; UdonSharp cannot safely create a new backing behaviour as an added override on a prefab instance, so that case stays untouched.
        internal static int MigrateHierarchy(GameObject root, LightVolumeManager manager, out int blocked) {
            blocked = 0;
            if (!LightVolumeSceneSetup.IsMainStageSceneObject(root)
                || !IsExactReadyRuntimeComponent(manager)
                || manager.gameObject.scene != root.scene) return 0;

            int removed = 0;
            List<LightVolume> volumes = new List<LightVolume>();
            root.GetComponentsInChildren(true, volumes);
            for (int i = 0; i < volumes.Count; i++) {
                if (TryMigrateHierarchyVolume(volumes[i], manager)) removed++;
                else blocked++;
            }

            List<PointLightVolume> pointLights = new List<PointLightVolume>();
            root.GetComponentsInChildren(true, pointLights);
            for (int i = 0; i < pointLights.Count; i++) {
                if (TryMigrateHierarchyPointLight(pointLights[i], manager)) removed++;
                else blocked++;
            }
            return removed;
        }

        // Checks whether a hierarchy contains at least one legacy helper with a safe unified destination.
        internal static bool CanMigrateHierarchy(GameObject root) {
            if (root == null) return false;
            List<LightVolume> volumes = new List<LightVolume>();
            root.GetComponentsInChildren(true, volumes);
            for (int i = 0; i < volumes.Count; i++) {
                if (CanMigrateHierarchyComponent<LightVolume, LightVolumeInstance>(volumes[i])) return true;
            }

            List<PointLightVolume> pointLights = new List<PointLightVolume>();
            root.GetComponentsInChildren(true, pointLights);
            for (int i = 0; i < pointLights.Count; i++) {
                if (CanMigrateHierarchyComponent<PointLightVolume, PointLightVolumeInstance>(pointLights[i])) return true;
            }
            return false;
        }

        // Checks whether a known unified component has one exact ready backing Udon program.
        internal static bool IsReadyRuntimeComponent(Component component) {
            if (component is LightVolumeManager manager) return IsExactReadyRuntimeComponent(manager);
            if (component is LightVolumeInstance volume) return IsExactReadyRuntimeComponent(volume);
            if (component is PointLightVolumeInstance pointLight) return IsExactReadyRuntimeComponent(pointLight);
            return false;
        }

        // Checks whether one legacy component can reuse a unique destination or safely create one.
        private static bool CanMigrateHierarchyComponent<TLegacy, TUnified>(TLegacy source)
            where TLegacy : Component where TUnified : Component {
            if (source == null || source.GetComponents<TLegacy>().Length != 1) return false;
            TUnified[] destinations = source.GetComponents<TUnified>();
            if (destinations.Length == 1) return IsExactReadyRuntimeComponent(destinations[0]);
            return destinations.Length == 0 && !PrefabUtility.IsPartOfPrefabInstance(source.gameObject) && !HasUnmatchedUdonSharpBacking(source.gameObject);
        }

        // Migrates and registers one hierarchy Light Volume with rollback on any incomplete mutation.
        private static bool TryMigrateHierarchyVolume(LightVolume source, LightVolumeManager manager) {
            if (!TryResolveOrCreateHierarchyDestination(source, out LightVolumeInstance destination, out bool created)) return false;
            try {
                Undo.RecordObject(destination, UndoName);
                CopyLegacyLightVolume(source, destination);
                if (!LightVolumeManagerEditorBackend.EnsureRegistered(manager, destination, UndoName, out _)) throw new InvalidOperationException("Could not register LightVolumeInstance.");
                MarkDirty(destination);
                CopyProxyToUdon(destination);
                if (!IsRegistered(manager.LightVolumeInstances, destination, manager)) throw new InvalidOperationException("LightVolumeInstance registration did not persist.");
                RemoveLegacyComponent(source);
                return true;
            } catch (Exception exception) {
                if (created) {
                    Undo.RecordObject(manager, UndoName);
                    manager.DeinitializeLightVolume(destination);
                    MarkDirty(manager);
                    CopyProxyToUdon(manager);
                    RollbackCreatedProxies<LightVolumeInstance>(source.gameObject);
                }
                Debug.LogWarning($"[LightVolumes] Could not migrate legacy LightVolume on '{source.gameObject.name}'. It was left unchanged. {exception.Message}", source);
                return false;
            }
        }

        // Migrates and registers one hierarchy Point Light Volume with rollback on failure.
        private static bool TryMigrateHierarchyPointLight(PointLightVolume source, LightVolumeManager manager) {
            if (!TryResolveOrCreateHierarchyDestination(source, out PointLightVolumeInstance destination, out bool created)) return false;
            try {
                Undo.RecordObject(destination, UndoName);
                CopyLegacyPointLight(source, destination);
                if (!LightVolumeManagerEditorBackend.EnsureRegistered(manager, destination, UndoName, out _)) throw new InvalidOperationException("Could not register PointLightVolumeInstance.");
                destination.EditorApplyAuthoringData(true, true, false);
                destination.CacheEditorObservedValues();
                MarkDirty(destination);
                CopyProxyToUdon(destination);
                if (!IsRegistered(manager.PointLightVolumeInstances, destination, manager)) throw new InvalidOperationException("PointLightVolumeInstance registration did not persist.");
                RemoveLegacyComponent(source);
                return true;
            } catch (Exception exception) {
                if (created) {
                    Undo.RecordObject(manager, UndoName);
                    manager.DeinitializePointLightVolume(destination, true, true);
                    MarkDirty(manager);
                    CopyProxyToUdon(manager);
                    RollbackCreatedProxies<PointLightVolumeInstance>(source.gameObject);
                }
                Debug.LogWarning($"[LightVolumes] Could not migrate legacy PointLightVolume on '{source.gameObject.name}'. It was left unchanged. {exception.Message}", source);
                return false;
            }
        }

        // Resolves an exact attached unified component or creates one only on safe non-prefab objects.
        private static bool TryResolveOrCreateHierarchyDestination<TLegacy, TUnified>(TLegacy source, out TUnified destination, out bool created)
            where TLegacy : Component where TUnified : UdonSharpBehaviour {
            destination = null;
            created = false;
            if (source == null || source.GetComponents<TLegacy>().Length != 1) return false;

            TUnified[] destinations = source.GetComponents<TUnified>();
            if (destinations.Length == 1) {
                destination = destinations[0];
                return IsExactReadyRuntimeComponent(destination);
            }
            if (destinations.Length != 0 || PrefabUtility.IsPartOfPrefabInstance(source.gameObject) || HasUnmatchedUdonSharpBacking(source.gameObject)) return false;

            try {
                destination = UdonSharpUndo.AddComponent<TUnified>(source.gameObject);
                created = true;
            } catch (Exception) {
                RollbackCreatedProxies<TUnified>(source.gameObject);
                destination = null;
                return false;
            }
            if (IsExactReadyRuntimeComponent(destination)) return true;
            RollbackCreatedProxies<TUnified>(source.gameObject);
            destination = null;
            created = false;
            return false;
        }

        // Verifies that a component is present in a registry and points back to the same Manager.
        private static bool IsRegistered<T>(T[] registry, T component, LightVolumeManager manager) where T : Component {
            if (registry == null || Array.IndexOf(registry, component) < 0) return false;
            if (component is LightVolumeInstance volume) return volume.LightVolumeManager == manager;
            if (component is PointLightVolumeInstance pointLight) return pointLight.LightVolumeManager == manager;
            return false;
        }

        // UdonSharp proxy lifecycle events do not run in Edit Mode. Refresh valid managers only after migration actually changed their serialized runtime data.
        private static void RefreshManagerRuntimeState(List<LightVolumeManager> managers) {
            for (int i = 0; i < managers.Count; i++) {
                LightVolumeManager manager = managers[i];
                if (!IsExactReadyRuntimeComponent(manager) || !manager.isActiveAndEnabled) continue;
                manager.RequestUpdateVolumes();
            }
        }

        // Collects components of one type below all supplied roots, including inactive objects.
        private static List<T> Collect<T>(GameObject[] roots) where T : Component {
            List<T> result = new List<T>();
            List<T> rootComponents = new List<T>();
            for (int i = 0; i < roots.Length; i++) {
                GameObject root = roots[i];
                if (root == null) continue;
                rootComponents.Clear();
                root.GetComponentsInChildren(true, rootComponents);
                result.AddRange(rootComponents);
            }
            return result;
        }

        // Collects backing UdonBehaviours already owned by UdonSharp proxies in a scene.
        private static HashSet<UdonBehaviour> CollectOwnedUdonBackings(GameObject[] roots) {
            List<UdonSharpBehaviour> proxies = Collect<UdonSharpBehaviour>(roots);
            HashSet<UdonBehaviour> ownedBackings = new HashSet<UdonBehaviour>();
            for (int i = 0; i < proxies.Count; i++) {
                UdonSharpBehaviour proxy = proxies[i];
                if (proxy == null) continue;
                UdonBehaviour backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy);
                if (backing != null) ownedBackings.Add(backing);
            }
            return ownedBackings;
        }

        private sealed class PureLegacyGroup {
            public LightVolumeSetup Setup;
            public readonly List<LightVolume> Volumes = new List<LightVolume>();
            public readonly List<PointLightVolume> PointLights = new List<PointLightVolume>();
        }

        // v2 scenes contain only the three editor authoring components. Creating their Udon counterparts is migration, not repair: the whole ownership group must be unambiguous and contain no existing counterpart before any component is added.
        private static void CreatePureLegacyGraphs(List<LightVolumeSetup> setups, List<LightVolume> volumes, List<PointLightVolume> pointLights) {
            if (setups.Count == 0) return;
            Dictionary<LightVolume, LightVolumeSetup> volumeOwners = new Dictionary<LightVolume, LightVolumeSetup>();
            Dictionary<PointLightVolume, LightVolumeSetup> pointOwners = new Dictionary<PointLightVolume, LightVolumeSetup>();
            HashSet<LightVolume> ambiguousVolumes = new HashSet<LightVolume>();
            HashSet<PointLightVolume> ambiguousPointLights = new HashSet<PointLightVolume>();
            for (int i = 0; i < setups.Count; i++) RegisterRegistryOwners(setups[i], volumeOwners, ambiguousVolumes, pointOwners, ambiguousPointLights);

            for (int i = 0; i < setups.Count; i++) {
                if (!TryCollectPureLegacyGroup(setups[i], volumes, pointLights, volumeOwners, pointOwners, ambiguousVolumes, ambiguousPointLights, out PureLegacyGroup group)) continue;
                TryCreatePureLegacyGroup(group);
            }
        }

        // Validates and collects one complete v2 setup group without mutating any components.
        private static bool TryCollectPureLegacyGroup(LightVolumeSetup setup, List<LightVolume> sceneVolumes, List<PointLightVolume> scenePointLights,
            Dictionary<LightVolume, LightVolumeSetup> volumeOwners, Dictionary<PointLightVolume, LightVolumeSetup> pointOwners,
            HashSet<LightVolume> ambiguousVolumes, HashSet<PointLightVolume> ambiguousPointLights, out PureLegacyGroup group) {
            group = null;
            if (setup == null || setup.GetComponents<LightVolumeSetup>().Length != 1 || HasUnifiedLightVolumeComponent(setup.gameObject) || setup.LightVolumeManager != null || HasUnmatchedUdonSharpBacking(setup.gameObject)) return false;

            if (setup.LightVolumeDataList != null) {
                for (int i = 0; i < setup.LightVolumeDataList.Count; i++) {
                    if (setup.LightVolumeDataList[i].LightVolumeInstance != null) return false;
                }
            }

            PureLegacyGroup candidate = new PureLegacyGroup { Setup = setup };
            HashSet<LightVolume> seenVolumes = new HashSet<LightVolume>();
            bool hasVolumeRegistry = setup.LightVolumes != null && setup.LightVolumes.Count > 0;
            if (hasVolumeRegistry) {
                for (int i = 0; i < setup.LightVolumes.Count; i++) {
                    LightVolume source = setup.LightVolumes[i];
                    if (source == null || !sceneVolumes.Contains(source) || ambiguousVolumes.Contains(source) || !volumeOwners.TryGetValue(source, out LightVolumeSetup owner) || owner != setup || !seenVolumes.Add(source)) return false;
                    candidate.Volumes.Add(source);
                }
            }
            for (int i = 0; i < sceneVolumes.Count; i++) {
                LightVolume source = sceneVolumes[i];
                bool registryOwned = volumeOwners.TryGetValue(source, out LightVolumeSetup owner);
                bool belongs = registryOwned ? owner == setup : source.LightVolumeSetup == setup;
                if (!belongs) continue;
                if (ambiguousVolumes.Contains(source)) return false;
                if (hasVolumeRegistry) {
                    if (!seenVolumes.Contains(source)) return false;
                } else if (seenVolumes.Add(source)) {
                    candidate.Volumes.Add(source);
                }
            }

            HashSet<PointLightVolume> seenPointLights = new HashSet<PointLightVolume>();
            bool hasPointRegistry = setup.PointLightVolumes != null && setup.PointLightVolumes.Count > 0;
            if (hasPointRegistry) {
                for (int i = 0; i < setup.PointLightVolumes.Count; i++) {
                    PointLightVolume source = setup.PointLightVolumes[i];
                    if (source == null || !scenePointLights.Contains(source) || ambiguousPointLights.Contains(source) || !pointOwners.TryGetValue(source, out LightVolumeSetup owner) || owner != setup || !seenPointLights.Add(source)) return false;
                    candidate.PointLights.Add(source);
                }
            }
            for (int i = 0; i < scenePointLights.Count; i++) {
                PointLightVolume source = scenePointLights[i];
                bool registryOwned = pointOwners.TryGetValue(source, out LightVolumeSetup owner);
                bool belongs = registryOwned ? owner == setup : source.LightVolumeSetup == setup;
                if (!belongs) continue;
                if (ambiguousPointLights.Contains(source)) return false;
                if (hasPointRegistry) {
                    if (!seenPointLights.Contains(source)) return false;
                } else if (seenPointLights.Add(source)) {
                    candidate.PointLights.Add(source);
                }
            }

            for (int i = 0; i < candidate.Volumes.Count; i++) {
                LightVolume source = candidate.Volumes[i];
                if (source.GetComponents<LightVolume>().Length != 1 || HasUnifiedLightVolumeComponent(source.gameObject) || source.LightVolumeInstance != null || HasUnmatchedUdonSharpBacking(source.gameObject)) return false;
            }
            for (int i = 0; i < candidate.PointLights.Count; i++) {
                PointLightVolume source = candidate.PointLights[i];
                if (source.GetComponents<PointLightVolume>().Length != 1 || HasUnifiedLightVolumeComponent(source.gameObject) || source.PointLightVolumeInstance != null || HasUnmatchedUdonSharpBacking(source.gameObject)) return false;
            }

            group = candidate;
            return true;
        }

        // Checks whether a GameObject already contains any unified Light Volumes component.
        private static bool HasUnifiedLightVolumeComponent(GameObject gameObject) {
            return gameObject.GetComponents<LightVolumeManager>().Length != 0 || gameObject.GetComponents<LightVolumeInstance>().Length != 0 || gameObject.GetComponents<PointLightVolumeInstance>().Length != 0;
        }

        // Detects backing UdonBehaviours that cannot be paired with a local UdonSharp proxy.
        private static bool HasUnmatchedUdonSharpBacking(GameObject gameObject) {
            UdonBehaviour[] backings = gameObject.GetComponents<UdonBehaviour>();
            if (backings.Length == 0) return false;
            UdonSharpBehaviour[] proxies = gameObject.GetComponents<UdonSharpBehaviour>();
            for (int i = 0; i < backings.Length; i++) {
                UdonBehaviour backing = backings[i];
                if (backing == null || backing.programSource == null) return true;
                if (!UdonSharpEditorUtility.IsUdonSharpBehaviour(backing)) continue;
                bool matched = false;
                for (int j = 0; j < proxies.Length; j++) {
                    if (UdonSharpEditorUtility.GetBackingUdonBehaviour(proxies[j]) != backing) continue;
                    matched = true;
                    break;
                }
                if (!matched) return true;
            }
            return false;
        }

        // Creates the complete unified Udon graph for one validated pure-v2 group atomically.
        private static bool TryCreatePureLegacyGroup(PureLegacyGroup group) {
            try {
                LightVolumeManager manager = UdonSharpUndo.AddComponent<LightVolumeManager>(group.Setup.gameObject);
                RequireReadyCreatedProxy(manager);

                LightVolumeInstance[] volumes = new LightVolumeInstance[group.Volumes.Count];
                for (int i = 0; i < volumes.Length; i++) {
                    LightVolumeInstance instance = UdonSharpUndo.AddComponent<LightVolumeInstance>(group.Volumes[i].gameObject);
                    RequireReadyCreatedProxy(instance);
                    instance.LightVolumeManager = manager;
                    volumes[i] = instance;
                }

                PointLightVolumeInstance[] pointLights = new PointLightVolumeInstance[group.PointLights.Count];
                for (int i = 0; i < pointLights.Length; i++) {
                    PointLightVolumeInstance instance = UdonSharpUndo.AddComponent<PointLightVolumeInstance>(group.PointLights[i].gameObject);
                    RequireReadyCreatedProxy(instance);
                    instance.LightVolumeManager = manager;
                    pointLights[i] = instance;
                }

                manager.LightVolumeInstances = volumes;
                manager.PointLightVolumeInstances = pointLights;
                for (int i = 0; i < volumes.Length; i++) CopyProxyToUdon(volumes[i]);
                for (int i = 0; i < pointLights.Length; i++) CopyProxyToUdon(pointLights[i]);
                CopyProxyToUdon(manager);
                return true;
            } catch (Exception exception) {
                RollbackCreatedLegacyGroup(group);
                Debug.LogWarning($"[LightVolumes] Could not create the unified Udon graph for legacy setup '{group.Setup.gameObject.name}'. The complete legacy group was left unchanged. " + exception.Message, group.Setup);
                return false;
            }
        }

        // Preflight guarantees that these exact proxy types did not exist before creation, so a failed group can safely remove every counterpart even when AddComponent threw before return.
        private static void RollbackCreatedLegacyGroup(PureLegacyGroup group) {
            for (int i = group.PointLights.Count - 1; i >= 0; i--) RollbackCreatedProxies<PointLightVolumeInstance>(group.PointLights[i].gameObject);
            for (int i = group.Volumes.Count - 1; i >= 0; i--) RollbackCreatedProxies<LightVolumeInstance>(group.Volumes[i].gameObject);
            RollbackCreatedProxies<LightVolumeManager>(group.Setup.gameObject);
        }

        // Removes every newly created proxy of one type after a failed migration transaction.
        private static void RollbackCreatedProxies<T>(GameObject gameObject) where T : UdonSharpBehaviour {
            T[] proxies = gameObject.GetComponents<T>();
            for (int i = proxies.Length - 1; i >= 0; i--) {
                try {
                    UdonSharpUndo.DestroyImmediate(proxies[i]);
                } catch (Exception exception) {
                    Debug.LogError($"[LightVolumes] {exception}", proxies[i]);
                }
            }
        }

        // Throws when UdonSharp did not create a complete backing program for a new proxy.
        private static void RequireReadyCreatedProxy(UdonSharpBehaviour proxy) {
            if (!IsReadyProxy(proxy)) throw new InvalidOperationException($"{proxy.GetType().Name} was added without a complete Udon backing program.");
        }

        // Resolves the unique unified Light Volume attached beside a legacy helper.
        private static LightVolumeInstance ResolveLightVolumeInstance(LightVolume source) {
            return ResolveExactAttached<LightVolumeInstance>(source);
        }

        // Resolves the unique unified Point Light Volume attached beside a legacy helper.
        private static PointLightVolumeInstance ResolvePointLightVolumeInstance(PointLightVolume source) {
            return ResolveExactAttached<PointLightVolumeInstance>(source);
        }

        // Resolves the unique unified Manager attached beside a legacy setup.
        private static LightVolumeManager ResolveManager(LightVolumeSetup setup) {
            return ResolveExactAttached<LightVolumeManager>(setup);
        }

        // Co-location is the invariant shared by every supported legacy version. A unique attached destination is authoritative; serialized bridge links may be stale after prefab overrides.
        private static T ResolveExactAttached<T>(Component source) where T : Component {
            if (source == null) return null;
            T[] attached = source.GetComponents<T>();
            return attached.Length == 1 ? attached[0] : null;
        }

        // Removes a migrated legacy helper through Undo, preserving prefab override semantics.
        private static void RemoveLegacyComponent(Component component) {
            if (component == null) return;

            // On an inherited prefab component Unity records a removed-component override. The prefab stays connected and its unified sibling receives the migrated instance overrides.
            Undo.DestroyObjectImmediate(component);
        }

        // Counts how many legacy sources resolve to one prospective unified destination.
        private static void IncrementUseCount<T>(Dictionary<T, int> counts, T destination) where T : Component {
            if (destination == null) return;
            counts.TryGetValue(destination, out int count);
            counts[destination] = count + 1;
        }

        // Checks that a destination is ready and referenced by exactly one legacy source.
        private static bool IsUniqueReadyDestination<T>(T destination, Dictionary<T, int> counts) where T : Component {
            return destination != null && counts.TryGetValue(destination, out int count) && count == 1 && IsExactReadyRuntimeComponent(destination);
        }

        // Checks proxy/backing integrity and rejects duplicate components of the same runtime type.
        private static bool IsExactReadyRuntimeComponent<T>(T component) where T : Component {
            if (component == null || !IsReadyProxy(component)) return false;
            T[] attached = component.GetComponents<T>();
            return attached.Length == 1 && attached[0] == component;
        }

        // Checks whether a component originates from a prefab rather than an added instance override.
        private static bool IsInheritedPrefabComponent(Component component) {
            return component != null && PrefabUtility.IsPartOfPrefabInstance(component) && PrefabUtility.GetCorrespondingObjectFromSource(component) != null;
        }

        // Records legacy registry ownership and marks components referenced by multiple setups as ambiguous.
        private static void RegisterRegistryOwners(LightVolumeSetup setup, Dictionary<LightVolume, LightVolumeSetup> volumeOwners, HashSet<LightVolume> ambiguousVolumes,
            Dictionary<PointLightVolume, LightVolumeSetup> pointOwners, HashSet<PointLightVolume> ambiguousPointLights) {
            if (setup.LightVolumes != null) {
                for (int i = 0; i < setup.LightVolumes.Count; i++)
                    RegisterRegistryOwner(setup.LightVolumes[i], setup, volumeOwners, ambiguousVolumes);
            }
            if (setup.PointLightVolumes != null) {
                for (int i = 0; i < setup.PointLightVolumes.Count; i++)
                    RegisterRegistryOwner(setup.PointLightVolumes[i], setup, pointOwners, ambiguousPointLights);
            }
        }

        // Records one component owner or marks the component ambiguous when another owner already exists.
        private static void RegisterRegistryOwner<T>(T source, LightVolumeSetup setup, Dictionary<T, LightVolumeSetup> owners, HashSet<T> ambiguous) where T : Component {
            if (source == null) return;
            if (!owners.TryGetValue(source, out _)) {
                owners[source] = setup;
                return;
            }
            ambiguous.Add(source);
        }

        // Resolves authoritative registry ownership, falling back to the helper's explicit setup link.
        private static bool TryResolveOwner<T>(LightVolumeSetup explicitOwner, T source, Dictionary<T, LightVolumeSetup> registryOwners, HashSet<T> ambiguous, out LightVolumeSetup owner) where T : Component {
            owner = null;
            if (ambiguous.Contains(source)) return false;
            if (registryOwners.TryGetValue(source, out LightVolumeSetup registryOwner)) {
                owner = registryOwner;
                return true;
            }
            owner = explicitOwner;
            return true;
        }

        // Resolves the ready Manager owned by a validated setup without accepting conflicting fallback links.
        private static LightVolumeManager ResolveValidatedManager(LightVolumeSetup owner, LightVolumeManager fallback, Dictionary<LightVolumeSetup, bool> setupReady,
            Dictionary<LightVolumeSetup, LightVolumeManager> setupDestinations) {
            if (owner == null) return fallback;
            if (!setupReady.TryGetValue(owner, out bool ready) || !ready) return null;
            LightVolumeManager manager = setupDestinations[owner];
            return fallback == null || fallback == manager ? manager : null;
        }

        // Validates that complete legacy registries map one-to-one onto an existing unified Manager graph.
        private static bool CanMapRegistries(LightVolumeSetup setup, LightVolumeManager manager, List<LightVolume> volumes, List<PointLightVolume> pointLights,
            Dictionary<LightVolume, LightVolumeInstance> volumeDestinations, Dictionary<PointLightVolume, PointLightVolumeInstance> pointDestinations,
            Dictionary<LightVolumeInstance, int> volumeDestinationUse, Dictionary<PointLightVolumeInstance, int> pointDestinationUse,
            Dictionary<LightVolume, LightVolumeSetup> volumeRegistryOwners, Dictionary<PointLightVolume, LightVolumeSetup> pointRegistryOwners,
            HashSet<LightVolume> ambiguousVolumeOwners, HashSet<PointLightVolume> ambiguousPointOwners) {
            if (!ValidateExistingRegistry(manager.LightVolumeInstances, manager) || !ValidateExistingRegistry(manager.PointLightVolumeInstances, manager)) return false;

            bool hasLegacyVolumes = setup.LightVolumes != null && setup.LightVolumes.Count > 0;
            if (hasLegacyVolumes) {
                for (int i = 0; i < setup.LightVolumes.Count; i++) {
                    LightVolume source = setup.LightVolumes[i];
                    if (source == null) continue;
                    if (!volumeDestinations.TryGetValue(source, out LightVolumeInstance destination) || ambiguousVolumeOwners.Contains(source) || volumeRegistryOwners[source] != setup || !IsUniqueReadyDestination(destination, volumeDestinationUse) || destination.LightVolumeManager != null && destination.LightVolumeManager != manager) 
                        return false;
                }
            }
            for (int i = 0; i < volumes.Count; i++) {
                LightVolume source = volumes[i];
                if (!IsOwnedBySetup(source, source.LightVolumeSetup, setup, volumeRegistryOwners)) continue;
                if (hasLegacyVolumes && setup.LightVolumes.Contains(source)) continue;
                if (!hasLegacyVolumes && manager.LightVolumeInstances != null && volumeDestinations.TryGetValue(source, out LightVolumeInstance destination) && Array.IndexOf(manager.LightVolumeInstances, destination) >= 0) continue;
                return false;
            }

            bool hasLegacyPointLights = setup.PointLightVolumes != null && setup.PointLightVolumes.Count > 0;
            if (hasLegacyPointLights) {
                for (int i = 0; i < setup.PointLightVolumes.Count; i++) {
                    PointLightVolume source = setup.PointLightVolumes[i];
                    if (source == null) continue;
                    if (!pointDestinations.TryGetValue(source, out PointLightVolumeInstance destination) || ambiguousPointOwners.Contains(source) || pointRegistryOwners[source] != setup || !IsUniqueReadyDestination(destination, pointDestinationUse) || destination.LightVolumeManager != null && destination.LightVolumeManager != manager)
                        return false;
                }
            }
            for (int i = 0; i < pointLights.Count; i++) {
                PointLightVolume source = pointLights[i];
                if (!IsOwnedBySetup(source, source.LightVolumeSetup, setup, pointRegistryOwners)) continue;
                if (hasLegacyPointLights && setup.PointLightVolumes.Contains(source)) continue;
                if (!hasLegacyPointLights && manager.PointLightVolumeInstances != null && pointDestinations.TryGetValue(source, out PointLightVolumeInstance destination) && Array.IndexOf(manager.PointLightVolumeInstances, destination) >= 0) continue;
                return false;
            }
            return true;
        }

        // Checks registry ownership first and uses the explicit setup link only when unregistered.
        private static bool IsOwnedBySetup<T>(T source, LightVolumeSetup explicitOwner, LightVolumeSetup setup, Dictionary<T, LightVolumeSetup> registryOwners) where T : Component {
            return registryOwners.TryGetValue(source, out LightVolumeSetup registryOwner) ? registryOwner == setup : explicitOwner == setup;
        }

        // Rejects duplicate, broken or cross-owned entries in an existing unified registry.
        private static bool ValidateExistingRegistry<T>(T[] registry, LightVolumeManager manager) where T : Component {
            if (registry == null) return true;
            HashSet<T> seen = new HashSet<T>();
            for (int i = 0; i < registry.Length; i++) {
                T component = registry[i];
                if (component == null) continue;
                LightVolumeManager assigned = GetAssignedManager(component);
                if (!seen.Add(component) || !IsExactReadyRuntimeComponent(component) || assigned != null && assigned != manager) return false;
            }
            return true;
        }

        // Returns the Manager referenced by either supported unified child component type.
        private static LightVolumeManager GetAssignedManager(Component component) {
            if (component is LightVolumeInstance volume) return volume.LightVolumeManager;
            if (component is PointLightVolumeInstance pointLight) return pointLight.LightVolumeManager;
            return null;
        }

        // Copies v2 Light Volume authoring values and recalculates unified runtime state.
        private static void CopyLegacyLightVolume(LightVolume source, LightVolumeInstance destination) {
            destination.enabled = source.enabled;
            destination.IsDynamic = source.Dynamic;
            destination.IsAdditive = source.Additive;
            destination.Color = source.Color;
            destination.Intensity = source.Intensity;
            destination.SmoothBlending = source.SmoothBlending;
            destination.Texture0 = source.Texture0;
            destination.Texture1 = source.Texture1;
            destination.Texture2 = source.Texture2;
            destination.BakeryVolume = source.BakeryVolume;
            destination.Exposure = source.Exposure;
            destination.Shadows = source.Shadows;
            destination.Highlights = source.Highlights;
            destination.Bake = source.Bake;
            destination.ReserveUVSpace = source.ReserveUVSpace;
            destination.AdaptiveResolution = source.AdaptiveResolution;
            destination.VoxelsPerUnit = source.VoxelsPerUnit;
            destination.Resolution = source.Resolution;
            LightVolumeTools.ApplyRuntimeState(destination, false);
        }

        // Copies v2 Point Light authoring, projection and shadow values into the unified component.
        private static void CopyLegacyPointLight(PointLightVolume source, PointLightVolumeInstance destination) {
            destination.enabled = source.enabled;
            int lightType = (int)source.Type;
            int projection = (int)source.Projection;
            UnityEngine.Object falloffLut = source.FalloffLUT;
            UnityEngine.Object cookie = source.Cookie;
            UnityEngine.Object cubemap = source.Cubemap;
            // Authoring data stays authoritative. Recover only a missing active source when the old runtime cache agrees on the same light type, non-parametric mode and source kind.
            if (GetLegacyProjectionSource(lightType, projection, falloffLut, cookie, cubemap) == null
                && TryGetMatchingLegacyRuntimeProjectionSource(lightType, projection, destination, out UnityEngine.Object runtimeProjectionSource)) {
                if (lightType == 2 || (lightType == 1 && projection == 2)) cookie = runtimeProjectionSource;
                else if (projection == 1) falloffLut = runtimeProjectionSource;
                else if (lightType == 0 && projection == 2) cubemap = runtimeProjectionSource;
            }

            destination.IsDynamic = source.Dynamic;
            destination.LightType = lightType;
            destination.LightSourceSize = source.LightSourceSize;
            destination.Range = source.Range;
            destination.Color = source.Color;
            destination.Intensity = source.Intensity;
            destination.ShadingStrength = source.ShadingStrength;
            destination.Projection = projection;
            destination.Angle = source.Angle * Mathf.Deg2Rad * 0.5f;
            destination.Falloff = source.Falloff;
            destination.FalloffLUT = falloffLut;
            destination.Cookie = cookie;
            destination.SpotCookieAspect = source.SpotCookieAspect;
            destination.Cubemap = cubemap;
            destination.BakeIntoProbes = source.BakeIntoProbes;
            destination.DebugRange = source.DebugRange;
            destination.Shadows = source.Shadows;
            destination.BakeInGame = source.BakeInGame;
            destination.RebakeShadows = source.RebakeShadows;
            destination.LayerMask = source.LayerMask.value;
            destination.Bias = source.Bias;
            destination.NearClip = source.NearPlane;
            destination.FarClip = source.FarPlane;
            destination.DebugClipPlanes = source.DebugClipPlanes;
            destination.Blur = source.Blur;
            destination.ContactHardening = source.ContactHardening;
            destination.WorldSpaceShadows = source.UseWorldSpace;
            destination.ForceCubemapShadows = source.ForceCubemapShadows;
            destination.ShadowMap = source.ShadowMap;
        }

        // Resolves the active authoring source from legacy light type and projection fields.
        private static UnityEngine.Object GetLegacyProjectionSource(int lightType, int projection, UnityEngine.Object falloffLut, UnityEngine.Object cookie, UnityEngine.Object cubemap) {
            if (lightType == 2) return cookie;
            if (projection == 1) return falloffLut;
            if (projection != 2) return null;
            return lightType == 0 ? cubemap : lightType == 1 ? cookie : null;
        }

        // Recovers a missing legacy authoring source only when cached runtime metadata matches exactly.
        private static bool TryGetMatchingLegacyRuntimeProjectionSource(int lightType, int projection, PointLightVolumeInstance destination, out UnityEngine.Object source) {
            source = destination.CustomTexture != null ? (UnityEngine.Object)destination.CustomTexture : destination.CustomTextureMaterial;
            int expectedProjectionMode = lightType == 2 ? 2 : projection;
            if (expectedProjectionMode == 0 || destination.LightType != lightType || destination.ProjectionMode != expectedProjectionMode) return false;
            if (source is Texture) return destination.ProjectionType == 1;
            if (source is Material) return destination.ProjectionType == 2;
            return false;
        }

        // Copies v2 Manager settings, registries and atlas post processors into the unified Manager.
        private static void CopyLegacySetup(LightVolumeSetup source, LightVolumeManager destination) {
            destination.enabled = source.enabled;
            destination.CustomTexturesWidth = (int)source.CookieResolution;
            destination.CustomTexturesHeight = destination.CustomTexturesWidth;
            destination.LightsBrightnessCutoff = source.BrightnessCutoff;
            destination.ShadowTexturesWidth = (int)source.ShadowResolution;
            destination.ShadowTexturesHeight = destination.ShadowTexturesWidth;
            destination.ShadowTextureFormat = (int)source.ShadowTextureFormat;
            destination.ShadowBleedReduction = source.ShadowBleedReduction;
            destination.ShadowMinVarianceDesktop = source.ShadowMinVariance;
            destination.ShadowMinVarianceMobile = source.ShadowMinVarianceMobile;
            destination.Clustering = source.Clustering;
            destination.FroxelDensity = source.FroxelDensity;
            destination.FroxelSlices = source.FroxelSlices;
            destination.FroxelCoarse = source.FroxelCoarse;
            destination.ClusteringMinLights = source.ClusteringMinLights;
            destination.BakingMode = (int)source.BakingMode;
            destination.VolumeBitmask = source.VolumeBitmask;
            destination.ProbeBitmask = source.ProbeBitmask;
            destination.Denoise = source.Denoise;
            destination.DilateInvalidProbes = source.DilateInvalidProbes;
            destination.DilationIterations = source.DilationIterations;
            destination.DilationBackfaceBias = source.DilationBackfaceBias;
            destination.FixLightProbesL1 = source.FixLightProbesL1;
            destination.DownscaleVolumes = (int)source.DownscaleVolumes;
            destination.LightProbesBlending = source.LightProbesBlending;
            destination.SharpBounds = source.SharpBounds;
            destination.AutoUpdateVolumes = source.AutoUpdateVolumes;
            destination.AutoUpdateTextures = source.AutoUpdateTextures;
            destination.AdditiveMaxOverdraw = source.AdditiveMaxOverdraw;
            destination.ForceSceneLighting = source.ForceSceneLighting;

            destination.LightVolumeInstances = MapLightVolumes(source, destination);
            destination.PointLightVolumeInstances = MapPointLights(source, destination);
            CopyLegacyAtlasPostProcessors(source, destination);
        }

        private struct LegacyLightVolumeEntry {
            public LightVolumeInstance Instance;
            public float Weight;

            // Stores one migrated volume together with its authoritative legacy weight.
            public LegacyLightVolumeEntry(LightVolumeInstance instance, float weight) {
                Instance = instance;
                Weight = weight;
            }
        }

        // Merges legacy and existing Light Volume registries while preserving descending weights.
        private static LightVolumeInstance[] MapLightVolumes(LightVolumeSetup setup, LightVolumeManager destination) {
            List<LegacyLightVolumeEntry> entries = new List<LegacyLightVolumeEntry>();
            HashSet<LightVolumeInstance> seen = new HashSet<LightVolumeInstance>();
            if (setup.LightVolumes != null && setup.LightVolumes.Count > 0) {
                for (int i = 0; i < setup.LightVolumes.Count; i++) {
                    LightVolumeInstance instance = ResolveLightVolumeInstance(setup.LightVolumes[i]);
                    if (instance == null || !seen.Add(instance)) continue;
                    InsertLightVolume(entries, instance, GetLegacyWeight(setup, instance, i));
                }
            }
            LightVolumeInstance[] existing = destination.LightVolumeInstances;
            if (existing != null) {
                for (int i = 0; i < existing.Length; i++) {
                    LightVolumeInstance instance = existing[i];
                    if (instance == null || !seen.Add(instance)) continue;
                    InsertLightVolume(entries, instance, instance.RegistryWeight);
                }
            }

            LightVolumeInstance[] result = new LightVolumeInstance[entries.Count];
            for (int i = 0; i < entries.Count; i++) {
                LegacyLightVolumeEntry entry = entries[i];
                result[i] = entry.Instance;
                UpdateRegistryMetadata(entry.Instance, destination, i, entry.Weight);
            }
            return result;
        }

        // Stably inserts a migrated Light Volume by descending registry weight.
        private static void InsertLightVolume(List<LegacyLightVolumeEntry> entries, LightVolumeInstance instance, float weight) {
            int insertIndex = entries.Count;
            while (insertIndex > 0) {
                LegacyLightVolumeEntry previous = entries[insertIndex - 1];
                if (previous.Weight >= weight) break;
                insertIndex--;
            }
            entries.Insert(insertIndex, new LegacyLightVolumeEntry(instance, weight));
        }

        // Writes migrated Manager ownership, order and weight to a regular Light Volume.
        private static void UpdateRegistryMetadata(LightVolumeInstance instance, LightVolumeManager manager, int order, float weight) {
            if (instance.LightVolumeManager == manager && instance.RegistryOrder == order && instance.RegistryWeight == weight) return;
            instance.LightVolumeManager = manager;
            instance.RegistryOrder = order;
            instance.RegistryWeight = weight;
            MarkDirty(instance);
            CopyProxyToUdon(instance);
        }

        // Resolves a Light Volume's weight from every supported legacy registry representation.
        private static float GetLegacyWeight(LightVolumeSetup setup, LightVolumeInstance instance, int index) {
            if (setup.LightVolumesWeights != null && index < setup.LightVolumesWeights.Count) return setup.LightVolumesWeights[index];
            if (setup.LightVolumeDataList != null) {
                for (int i = 0; i < setup.LightVolumeDataList.Count; i++) {
                    LightVolumeData data = setup.LightVolumeDataList[i];
                    if (data.LightVolumeInstance == instance) return data.Weight;
                }
            }
            return instance.RegistryWeight;
        }

        // Merges legacy and existing Point Light registries without duplicate instances.
        private static PointLightVolumeInstance[] MapPointLights(LightVolumeSetup setup, LightVolumeManager destination) {
            List<PointLightVolumeInstance> result = new List<PointLightVolumeInstance>();
            HashSet<PointLightVolumeInstance> seen = new HashSet<PointLightVolumeInstance>();
            if (setup.PointLightVolumes != null && setup.PointLightVolumes.Count > 0) {
                for (int i = 0; i < setup.PointLightVolumes.Count; i++) {
                    PointLightVolumeInstance instance = ResolvePointLightVolumeInstance(setup.PointLightVolumes[i]);
                    if (instance == null || !seen.Add(instance)) continue;
                    UpdateRegistryMetadata(instance, destination, result.Count, 0f);
                    result.Add(instance);
                }
            }
            PointLightVolumeInstance[] existing = destination.PointLightVolumeInstances;
            if (existing != null) {
                for (int i = 0; i < existing.Length; i++) {
                    PointLightVolumeInstance instance = existing[i];
                    if (instance == null || !seen.Add(instance)) continue;
                    UpdateRegistryMetadata(instance, destination, result.Count, 0f);
                    result.Add(instance);
                }
            }
            return result.ToArray();
        }

        // Writes migrated Manager ownership and stable order to a Point Light Volume.
        private static void UpdateRegistryMetadata(PointLightVolumeInstance instance, LightVolumeManager manager, int order, float weight) {
            if (instance.LightVolumeManager == manager && instance.RegistryOrder == order && instance.RegistryWeight == weight) return;
            instance.LightVolumeManager = manager;
            instance.RegistryOrder = order;
            instance.RegistryWeight = weight;
            MarkDirty(instance);
            CopyProxyToUdon(instance);
        }

        // Converts legacy atlas post processors to the unified Manager representation.
        private static void CopyLegacyAtlasPostProcessors(LightVolumeSetup source, LightVolumeManager destination) {
            LightVolumeSetup.PostProcessor[] processors = source.AtlasPostProcessors;
            if (processors == null) return;

            AtlasPostProcessor[] migrated = new AtlasPostProcessor[processors.Length];
            for (int i = 0; i < processors.Length; i++) {
                LightVolumeSetup.PostProcessor processor = processors[i];
                migrated[i] = new AtlasPostProcessor {
                    Target = processor.RT,
                    Material = processor.Mat,
                    InputTextureProperty = processor.TextureName,
                    Update = processor.Update,
                    UpdateWithInput = processor.UpdateWithInput
                };
            }
            destination.EditorSetAtlasPostProcessors(migrated);
        }

        // Checks that a UdonSharp proxy has a co-located backing behaviour of the exact proxy type.
        private static bool IsReadyProxy(Component component) {
            UdonSharpBehaviour proxy = component as UdonSharpBehaviour;
            if (proxy == null) return false;
            UdonBehaviour backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy);
            return backing != null && backing.gameObject == proxy.gameObject && UdonSharpEditorUtility.GetUdonSharpBehaviourType(backing) == proxy.GetType();
        }

        // Copies a migrated UdonSharp proxy into its backing UdonBehaviour.
        private static void CopyProxyToUdon(Component component) {
            UdonSharpBehaviour proxy = component as UdonSharpBehaviour;
            if (proxy != null) UdonSharpEditorUtility.CopyProxyToUdon(proxy);
        }

        // Read-only preflight. It reports broken/duplicate pairs but never changes the scene.
        public static bool ValidateLoadedSceneUdonPairs(out int issueCount, out string issueSummary) {
            issueCount = 0;
            IssueExamples.Clear();
            Dictionary<LightVolumeInstance, LightVolumeManager> volumeRegistryOwners = new Dictionary<LightVolumeInstance, LightVolumeManager>();
            Dictionary<PointLightVolumeInstance, LightVolumeManager> pointRegistryOwners = new Dictionary<PointLightVolumeInstance, LightVolumeManager>();
            int loadedManagerCount = 0;
            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++) {
                Scene scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.isLoaded) continue;
                GameObject[] roots = scene.GetRootGameObjects();
                List<LightVolumeManager> managers = Collect<LightVolumeManager>(roots);
                for (int i = 0; i < managers.Count; i++) {
                    LightVolumeManager manager = managers[i];
                    if (manager != null && !manager.CompareTag("EditorOnly")) loadedManagerCount++;
                }
                ValidateComponents(managers, ref issueCount);
                ValidateComponents(Collect<LightVolumeInstance>(roots), ref issueCount);
                ValidateComponents(Collect<PointLightVolumeInstance>(roots), ref issueCount);
                ValidateManagerRegistries(managers, volumeRegistryOwners, pointRegistryOwners, ref issueCount);
                ValidateUnownedLightVolumeBackings(roots, ref issueCount);
                RegisterLegacyComponents(Collect<LightVolumeSetup>(roots), ref issueCount);
                RegisterLegacyComponents(Collect<LightVolume>(roots), ref issueCount);
                RegisterLegacyComponents(Collect<PointLightVolume>(roots), ref issueCount);
            }
            if (loadedManagerCount > 1) RegisterIssue($"loaded scene setup contains {loadedManagerCount} Light Volume Managers; only the primary Manager is supported", ref issueCount);
            issueSummary = IssueExamples.Count == 0 ? string.Empty : string.Join("; ", IssueExamples.ToArray());
            return issueCount == 0;
        }

        // Reports duplicates, cross-ownership and mismatched back-references across Manager registries.
        private static void ValidateManagerRegistries(List<LightVolumeManager> managers, Dictionary<LightVolumeInstance, LightVolumeManager> volumeRegistryOwners,
            Dictionary<PointLightVolumeInstance, LightVolumeManager> pointRegistryOwners, ref int issueCount) {
            for (int managerIndex = 0; managerIndex < managers.Count; managerIndex++) {
                LightVolumeManager manager = managers[managerIndex];
                if (manager == null) continue;

                HashSet<LightVolumeInstance> localVolumes = new HashSet<LightVolumeInstance>();
                LightVolumeInstance[] volumes = manager.LightVolumeInstances ?? Array.Empty<LightVolumeInstance>();
                for (int i = 0; i < volumes.Length; i++) {
                    LightVolumeInstance volume = volumes[i];
                    if (volume == null) continue;
                    if (!localVolumes.Add(volume)) {
                        RegisterIssue($"'{manager.gameObject.name}' contains duplicate LightVolumeInstance '{volume.gameObject.name}'", ref issueCount);
                        continue;
                    }
                    if (volume.LightVolumeManager != manager) {
                        string ownerName = volume.LightVolumeManager == null ? "null" : $"'{volume.LightVolumeManager.gameObject.name}'";
                        RegisterIssue($"LightVolumeInstance '{volume.gameObject.name}' in '{manager.gameObject.name}' references manager {ownerName}", ref issueCount);
                    }
                    if (volumeRegistryOwners.TryGetValue(volume, out LightVolumeManager firstOwner)) {
                        if (firstOwner != manager) RegisterIssue($"LightVolumeInstance '{volume.gameObject.name}' is registered by both '{firstOwner.gameObject.name}' and '{manager.gameObject.name}'", ref issueCount);
                    } else {
                        volumeRegistryOwners.Add(volume, manager);
                    }
                }

                HashSet<PointLightVolumeInstance> localPointLights = new HashSet<PointLightVolumeInstance>();
                PointLightVolumeInstance[] pointLights = manager.PointLightVolumeInstances ?? Array.Empty<PointLightVolumeInstance>();
                for (int i = 0; i < pointLights.Length; i++) {
                    PointLightVolumeInstance pointLight = pointLights[i];
                    if (pointLight == null) continue;
                    if (!localPointLights.Add(pointLight)) {
                        RegisterIssue($"'{manager.gameObject.name}' contains duplicate PointLightVolumeInstance '{pointLight.gameObject.name}'", ref issueCount);
                        continue;
                    }
                    if (pointLight.LightVolumeManager != manager) {
                        string ownerName = pointLight.LightVolumeManager == null ? "null" : $"'{pointLight.LightVolumeManager.gameObject.name}'";
                        RegisterIssue($"PointLightVolumeInstance '{pointLight.gameObject.name}' in '{manager.gameObject.name}' references manager {ownerName}", ref issueCount);
                    }
                    if (pointRegistryOwners.TryGetValue(pointLight, out LightVolumeManager firstOwner)) {
                        if (firstOwner != manager) RegisterIssue($"PointLightVolumeInstance '{pointLight.gameObject.name}' is registered by both '{firstOwner.gameObject.name}' and '{manager.gameObject.name}'", ref issueCount);
                    } else {
                        pointRegistryOwners.Add(pointLight, manager);
                    }
                }
            }
        }

        // Reports Light Volumes backing UdonBehaviours that no UdonSharp proxy owns.
        private static void ValidateUnownedLightVolumeBackings(GameObject[] roots, ref int issueCount) {
            HashSet<UdonBehaviour> ownedBackings = CollectOwnedUdonBackings(roots);
            List<UdonBehaviour> backings = Collect<UdonBehaviour>(roots);
            for (int i = 0; i < backings.Count; i++) {
                UdonBehaviour backing = backings[i];
                if (backing == null || ownedBackings.Contains(backing)) continue;
                Type proxyType = UdonSharpEditorUtility.GetUdonSharpBehaviourType(backing);
                if (!IsKnownLightVolumeProxyType(proxyType)) continue;
                RegisterIssue($"unowned {proxyType.Name} backing UdonBehaviour remains on '{backing.gameObject.name}'", ref issueCount);
            }
        }

        // Checks whether a UdonSharp proxy type belongs to the unified Light Volumes graph.
        private static bool IsKnownLightVolumeProxyType(Type proxyType) {
            return proxyType == typeof(LightVolumeManager) || proxyType == typeof(LightVolumeInstance) || proxyType == typeof(PointLightVolumeInstance);
        }

        // Reports missing exact backing programs and duplicate unified components of one type.
        private static void ValidateComponents<T>(List<T> components, ref int issueCount) where T : Component {
            for (int i = 0; i < components.Count; i++) {
                T component = components[i];
                if (component == null) continue;
                if (!IsReadyProxy(component)) RegisterIssue($"{component.GetType().Name} on '{component.gameObject.name}' has no exact backing Udon program", ref issueCount);
                T[] sameType = component.GetComponents<T>();
                if (sameType.Length > 1 && sameType[0] == component) RegisterIssue($"'{component.gameObject.name}' has {sameType.Length} {component.GetType().Name} components", ref issueCount);
            }
        }

        // Reports every obsolete helper component that remains in loaded scenes.
        private static void RegisterLegacyComponents<T>(List<T> components, ref int issueCount) where T : Component {
            for (int i = 0; i < components.Count; i++) {
                T component = components[i];
                if (component != null) RegisterIssue($"legacy {component.GetType().Name} remains on '{component.gameObject.name}'", ref issueCount);
            }
        }

        // Increments the validation count and retains a bounded set of user-facing examples.
        private static void RegisterIssue(string issue, ref int issueCount) {
            issueCount++;
            if (IssueExamples.Count < MaxIssueExamples) IssueExamples.Add(issue);
        }

        // Marks migrated data dirty and records inherited prefab instance modifications.
        private static void MarkDirty(UnityEngine.Object target) {
            if (target is Component component && IsInheritedPrefabComponent(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            if (target != null) EditorUtility.SetDirty(target);
        }

        // Converts packed 2.x runtime fields still present only in scene YAML.
        private static bool MigrateLegacyRuntimePayload(List<LightVolumeManager> managers, List<LightVolumeInstance> volumes, List<PointLightVolumeInstance> pointLights, bool savedSceneYamlIsAuthoritative) {
            bool changed = false;
            for (int i = 0; i < managers.Count; i++) if (MigrateLegacyManagerRuntimeTextures(managers[i])) changed = true;
            if (!savedSceneYamlIsAuthoritative) return changed;
            for (int i = 0; i < volumes.Count; i++) if (MigrateLegacyLightVolumeData(volumes[i])) changed = true;
            for (int i = 0; i < pointLights.Count; i++) if (MigrateLegacyPointLightData(pointLights[i])) changed = true;
            return changed;
        }

        // Recovers packed rotation and atlas bounds stored only in legacy scene YAML.
        private static bool MigrateLegacyLightVolumeData(LightVolumeInstance volume) {
            if (volume == null || IsInheritedPrefabComponent(volume) || !IsExactReadyRuntimeComponent(volume) || WasRuntimeComponentMigrated(volume)) return false;
            if (!TryGetSceneObjectYamlBlock(volume, out string serializedBlock)) return false;
            bool changed = false;
            Vector4 legacyRotation;
            if (volume.RelativeRotationRow0 == Vector3.zero && volume.RelativeRotationRow1 == Vector3.zero
                && TryReadVector4(serializedBlock, "RelativeRotation", "_legacyRelativeRotation", out legacyRotation)
                && legacyRotation != Vector4.zero && legacyRotation != new Vector4(0, 0, 0, 1)) {
                Undo.RecordObject(volume, UndoName);
                Quaternion rotation = new Quaternion(legacyRotation.x, legacyRotation.y, legacyRotation.z, legacyRotation.w);
                Matrix4x4 matrix = Matrix4x4.Rotate(rotation);
                volume.RelativeRotationRow0 = matrix.GetRow(0);
                volume.RelativeRotationRow1 = matrix.GetRow(1);
                volume.IsRotated = Mathf.Abs(Quaternion.Dot(rotation, Quaternion.identity)) < 0.999999f;
                changed = true;
            }
            if (TryReadVector4(serializedBlock, "BoundsUvwMax0", "_legacyBoundsUvwMax0", out Vector4 max0) && MigrateLegacyBoundsScale(ref volume.BoundsUvwMin0, max0, 0, volume, changed)) changed = true;
            if (TryReadVector4(serializedBlock, "BoundsUvwMax1", "_legacyBoundsUvwMax1", out Vector4 max1) && MigrateLegacyBoundsScale(ref volume.BoundsUvwMin1, max1, 1, volume, changed)) changed = true;
            if (TryReadVector4(serializedBlock, "BoundsUvwMax2", "_legacyBoundsUvwMax2", out Vector4 max2) && MigrateLegacyBoundsScale(ref volume.BoundsUvwMin2, max2, 2, volume, changed)) changed = true;
            if (!changed) return false;
            RememberMigratedRuntimeComponent(volume);
            CopyProxyToUdon(volume);
            MarkDirty(volume);
            return true;
        }

        // Converts one legacy min/max atlas axis to the unified min-plus-scale representation.
        private static bool MigrateLegacyBoundsScale(ref Vector4 uvwMin, Vector4 legacyMax, int axis, LightVolumeInstance volume, bool undoRecorded) {
            if (uvwMin.w != 0f || legacyMax == Vector4.zero) return false;
            if (!undoRecorded) Undo.RecordObject(volume, UndoName);
            float min = axis == 0 ? uvwMin.x : axis == 1 ? uvwMin.y : uvwMin.z;
            float max = axis == 0 ? legacyMax.x : axis == 1 ? legacyMax.y : legacyMax.z;
            uvwMin.w = max - min;
            return true;
        }

        // Decodes packed v2 Point Light runtime vectors from authoritative saved scene YAML.
        private static bool MigrateLegacyPointLightData(PointLightVolumeInstance pointLight) {
            if (pointLight == null || IsInheritedPrefabComponent(pointLight) || !IsExactReadyRuntimeComponent(pointLight) || WasRuntimeComponentMigrated(pointLight)) return false;
            if (!TryGetSceneObjectYamlBlock(pointLight, out string block) || HasCurrentPointLightData(block)) return false;
            TryReadVector4(block, "PositionData", "_legacyPositionData", out Vector4 positionData);
            TryReadVector4(block, "DirectionData", "_legacyDirectionData", out Vector4 directionData);
            TryReadFloat(block, "CustomID", "_legacyCustomID", out float customId);
            TryReadFloat(block, "AngleData", "_legacyAngleData", out float angleData);
            TryReadInt(block, "ShadowmaskIndex", "_legacyShadowmaskIndex", out int shadowmaskIndex);
            if (positionData == Vector4.zero && directionData == Vector4.zero && customId == 0f && angleData == 0f && shadowmaskIndex < 0) return false;

            Undo.RecordObject(pointLight, UndoName);
            pointLight.Position = new Vector3(positionData.x, positionData.y, positionData.z);
            pointLight.LightType = positionData.w < 0f ? 1 : angleData > 1.5f ? 2 : 0;
            pointLight.ProjectionMode = customId > 0f ? 1 : customId < 0f ? 2 : 0;
            pointLight.ProjectionType = pointLight.ProjectionMode == 0 ? 0 : 1;
            if (pointLight.LightType == 2) {
                pointLight.Width = Mathf.Max(Mathf.Abs(positionData.w), 0.001f);
                pointLight.Height = Mathf.Max(angleData - 2f, 0.001f);
                pointLight.Rotation = LegacyVectorToQuaternion(directionData);
            } else {
                if (pointLight.ProjectionMode == 1) {
                    pointLight.InverseSquaredRange = Mathf.Max(Mathf.Abs(positionData.w), 0.000001f);
                    pointLight.LightSourceSize = 1f / Mathf.Sqrt(pointLight.InverseSquaredRange);
                } else {
                    pointLight.LightSourceSize = Mathf.Sqrt(Mathf.Max(Mathf.Abs(positionData.w), 0.0001f));
                    pointLight.InverseSquaredRange = 1f / Mathf.Max(pointLight.LightSourceSize * pointLight.LightSourceSize, 0.000001f);
                }
                if (pointLight.LightType == 1 && pointLight.ProjectionMode != 2) {
                    pointLight.Direction = new Vector3(directionData.x, directionData.y, directionData.z);
                    pointLight.ConeFalloff = directionData.w;
                } else pointLight.Rotation = LegacyVectorToQuaternion(directionData);
                if (pointLight.LightType == 1 && pointLight.ProjectionMode == 2) pointLight.OuterAngleTan = angleData;
                else pointLight.OuterAngleCos = angleData;
            }
            pointLight.CustomTexture = null;
            pointLight.CustomTextureMaterial = null;
            pointLight.AutoUpdateCustomTexture = false;
            pointLight.ShadowMapID = -1f;
            pointLight.IsRangeDirty = true;
            RememberMigratedRuntimeComponent(pointLight);
            CopyProxyToUdon(pointLight);
            MarkDirty(pointLight);
            return true;
        }

        // Checks whether this exact component instance already consumed its legacy YAML payload.
        private static bool WasRuntimeComponentMigrated(Component component) {
            int id = component.GetInstanceID();
            if (!MigratedRuntimeComponents.TryGetValue(id, out Component migrated)) return false;
            if (ReferenceEquals(migrated, component)) return true;
            MigratedRuntimeComponents.Remove(id);
            return false;
        }

        // Records the exact component instance that consumed legacy runtime data.
        private static void RememberMigratedRuntimeComponent(Component component) {
            MigratedRuntimeComponents[component.GetInstanceID()] = component;
        }

        // Detects unified Point Light fields that make replaying legacy packed data unsafe.
        private static bool HasCurrentPointLightData(string block) {
            return TryReadYamlLine(block, "LightType", out _) || TryReadYamlLine(block, "Position", out _) || TryReadYamlLine(block, "InverseSquaredRange", out _) || TryReadYamlLine(block, "ProjectionMode", out _);
        }

        // Converts a packed legacy vector to a quaternion with an identity fallback.
        private static Quaternion LegacyVectorToQuaternion(Vector4 value) {
            return value == Vector4.zero ? Quaternion.identity : new Quaternion(value.x, value.y, value.z, value.w);
        }

        // Clears obsolete or missing projection texture references that cannot be valid runtime arrays.
        private static bool MigrateLegacyManagerRuntimeTextures(LightVolumeManager manager) {
            if (manager == null || IsInheritedPrefabComponent(manager) || !IsExactReadyRuntimeComponent(manager)) return false;
            SerializedObject serializedManager = new SerializedObject(manager);
            SerializedProperty property = serializedManager.FindProperty("CustomTextures");
            if (property == null) return false;
            bool clear;
            try {
                UnityEngine.Object value = property.objectReferenceValue;
                clear = value != null && !(value is RenderTexture) || value == null && property.objectReferenceInstanceIDValue != 0;
            } catch (MissingReferenceException) {
                clear = true;
            }
            if (!clear) return false;
            Undo.RecordObject(manager, UndoName);
            property.objectReferenceValue = null;
            serializedManager.ApplyModifiedPropertiesWithoutUndo();
            CopyProxyToUdon(manager);
            MarkDirty(manager);
            return true;
        }

        // Retrieves the cached legacy YAML document matching one scene component's global object ID.
        private static bool TryGetSceneObjectYamlBlock(Component component, out string block) {
            block = null;
            if (component == null) return false;
            Scene scene = component.gameObject.scene;
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path) || !File.Exists(scene.path)) return false;
            if (!SceneLegacyRuntimeBlocksCache.TryGetValue(scene.path, out Dictionary<ulong, string> blocks)) {
                blocks = BuildLegacyRuntimeBlocks(File.ReadAllText(scene.path));
                SceneLegacyRuntimeBlocksCache[scene.path] = blocks;
            }
            if (blocks.Count == 0) return false;
            GlobalObjectId id = GlobalObjectId.GetGlobalObjectIdSlow(component);
            return blocks.TryGetValue(id.targetObjectId, out block);
        }

        // Extracts only legacy MonoBehaviour documents in one forward pass over scene YAML.
        private static Dictionary<ulong, string> BuildLegacyRuntimeBlocks(string yaml) {
            Dictionary<ulong, string> blocks = new Dictionary<ulong, string>();
            if (string.IsNullOrEmpty(yaml)) return blocks;

            const string documentMarker = "--- !u!";
            const string monoBehaviourMarker = "--- !u!114 &";
            int documentStart = yaml.StartsWith(documentMarker, StringComparison.Ordinal) ? 0 : yaml.IndexOf("\n" + documentMarker, StringComparison.Ordinal);
            if (documentStart > 0) documentStart++;
            while (documentStart >= 0) {
                int headerEnd = yaml.IndexOf('\n', documentStart);
                if (headerEnd < 0) break;
                int nextMarker = yaml.IndexOf("\n" + documentMarker, headerEnd + 1, StringComparison.Ordinal);
                int blockStart = headerEnd + 1;
                int blockEnd = nextMarker >= 0 ? nextMarker : yaml.Length;

                if (headerEnd - documentStart >= monoBehaviourMarker.Length
                    && string.CompareOrdinal(yaml, documentStart, monoBehaviourMarker, 0, monoBehaviourMarker.Length) == 0
                    && ContainsLegacyRuntimeData(yaml, blockStart, blockEnd)) {
                    int idStart = documentStart + monoBehaviourMarker.Length;
                    int idEnd = idStart;
                    while (idEnd < headerEnd && yaml[idEnd] >= '0' && yaml[idEnd] <= '9') idEnd++;
                    if (idEnd > idStart
                        && (idEnd == headerEnd || yaml[idEnd] == ' ' || yaml[idEnd] == '\r')
                        && ulong.TryParse(yaml.Substring(idStart, idEnd - idStart), NumberStyles.None, CultureInfo.InvariantCulture, out ulong objectId)
                        && !blocks.ContainsKey(objectId)) {
                        blocks.Add(objectId, yaml.Substring(blockStart, blockEnd - blockStart));
                    }
                }

                documentStart = nextMarker >= 0 ? nextMarker + 1 : -1;
            }
            return blocks;
        }

        // Scans one YAML document for any known packed v2 runtime field prefix.
        private static bool ContainsLegacyRuntimeData(string yaml, int blockStart, int blockEnd) {
            int lineStart = blockStart;
            while (lineStart < blockEnd) {
                int lineEnd = yaml.IndexOf('\n', lineStart);
                if (lineEnd < 0 || lineEnd > blockEnd) lineEnd = blockEnd;
                int lineLength = lineEnd - lineStart;
                if (lineLength > 0 && yaml[lineEnd - 1] == '\r') lineLength--;
                for (int i = 0; i < LegacyRuntimeYamlPrefixes.Length; i++) {
                    string prefix = LegacyRuntimeYamlPrefixes[i];
                    if (lineLength >= prefix.Length && string.CompareOrdinal(yaml, lineStart, prefix, 0, prefix.Length) == 0) return true;
                }
                lineStart = lineEnd + 1;
            }
            return false;
        }

        // Reads a Vector4 by its current or fallback legacy field name.
        private static bool TryReadVector4(string block, string name, string fallback, out Vector4 value) {
            if (TryReadVector4(block, name, out value)) return true;
            return TryReadVector4(block, fallback, out value);
        }

        // Parses a four-component vector from one YAML field line.
        private static bool TryReadVector4(string block, string name, out Vector4 value) {
            value = Vector4.zero;
            if (!TryReadYamlLine(block, name, out string line)) return false;
            return TryReadYamlFloatComponent(line, "x:", out value.x)
                && TryReadYamlFloatComponent(line, "y:", out value.y)
                && TryReadYamlFloatComponent(line, "z:", out value.z)
                && TryReadYamlFloatComponent(line, "w:", out value.w);
        }

        // Reads a floating-point value by its current or fallback legacy field name.
        private static bool TryReadFloat(string block, string name, string fallback, out float value) {
            if (TryReadFloat(block, name, out value)) return true;
            return TryReadFloat(block, fallback, out value);
        }

        // Parses an invariant floating-point value from one YAML field line.
        private static bool TryReadFloat(string block, string name, out float value) {
            value = 0f;
            if (!TryReadYamlLine(block, name, out string line)) return false;
            int colon = line.IndexOf(':');
            return colon >= 0 && float.TryParse(line.Substring(colon + 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        // Reads an integer by its current or fallback legacy field name.
        private static bool TryReadInt(string block, string name, string fallback, out int value) {
            if (TryReadInt(block, name, out value)) return true;
            return TryReadInt(block, fallback, out value);
        }

        // Parses an invariant integer value from one YAML field line.
        private static bool TryReadInt(string block, string name, out int value) {
            value = -1;
            if (!TryReadYamlLine(block, name, out string line)) return false;
            int colon = line.IndexOf(':');
            return colon >= 0 && int.TryParse(line.Substring(colon + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        // Finds one top-level serialized field line inside a component YAML document.
        private static bool TryReadYamlLine(string block, string name, out string line) {
            line = null;
            string prefix = "  " + name + ":";
            int start = block.StartsWith(prefix, StringComparison.Ordinal) ? 0 : block.IndexOf("\n" + prefix, StringComparison.Ordinal);
            if (start < 0) return false;
            if (start > 0) start++;
            int end = block.IndexOf('\n', start);
            line = end >= 0 ? block.Substring(start, end - start) : block.Substring(start);
            return true;
        }

        // Parses one named floating-point component from an inline YAML mapping.
        private static bool TryReadYamlFloatComponent(string line, string key, out float value) {
            value = 0f;
            int keyIndex = line.IndexOf(key, StringComparison.Ordinal);
            if (keyIndex < 0) return false;
            int start = keyIndex + key.Length;
            while (start < line.Length && line[start] == ' ') start++;
            int end = start;
            while (end < line.Length && line[end] != ',' && line[end] != '}') end++;
            return end > start && float.TryParse(line.Substring(start, end - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
#endif
