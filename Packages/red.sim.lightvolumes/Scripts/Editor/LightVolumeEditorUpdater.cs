using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRCLightVolumes {
    // Centralizes event-driven and continuous Edit Mode updates without per-object polling.
    [InitializeOnLoad]
    internal static class LightVolumeEditorUpdater {
        private static readonly HashSet<LightVolumeInstance> _volumes = new HashSet<LightVolumeInstance>();
        private static readonly HashSet<PointLightVolumeInstance> _pointLights = new HashSet<PointLightVolumeInstance>();
        private static readonly HashSet<GameObject> _hierarchyRoots = new HashSet<GameObject>();
        private static readonly HashSet<GameObject> _onboardingRoots = new HashSet<GameObject>();
        private static readonly List<LightVolumeInstance> _volumeBuffer = new List<LightVolumeInstance>();
        private static readonly List<PointLightVolumeInstance> _pointLightBuffer = new List<PointLightVolumeInstance>();
        private static readonly List<GameObject> _onboardingRootsInOrder = new List<GameObject>();
        private static LightVolumeManager _primaryManager;
        private static bool _managerUpdateQueued;
        private static bool _flushQueued;
        private static bool _isFlushing;

        // Installs editor change hooks and initializes the primary Manager cache.
        static LightVolumeEditorUpdater() {
            RefreshPrimaryManager();
            ObjectChangeEvents.changesPublished += OnChangesPublished;
            LightVolumeManager.EditorAtlasPostProcessorsChanged += OnAtlasPostProcessorsChanged;
            EditorApplication.hierarchyChanged += RefreshPrimaryManager;
            EditorApplication.update += UpdateAnimatedCookies;
            EditorSceneManager.sceneSaved += OnSceneSaved;
#if !UDONSHARP
            EditorSceneManager.sceneOpened += OnSceneOpened;
            QueueLoadedSceneOnboarding();
#endif
            AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
            EditorApplication.quitting += Shutdown;
            QueueManagerRecovery();
        }

        // Removes every editor callback and discards queued work before this editor domain ends.
        private static void Shutdown() {
            ObjectChangeEvents.changesPublished -= OnChangesPublished;
            LightVolumeManager.EditorAtlasPostProcessorsChanged -= OnAtlasPostProcessorsChanged;
            EditorApplication.hierarchyChanged -= RefreshPrimaryManager;
            EditorApplication.update -= UpdateAnimatedCookies;
            EditorSceneManager.sceneSaved -= OnSceneSaved;
#if !UDONSHARP
            EditorSceneManager.sceneOpened -= OnSceneOpened;
#endif
            AssemblyReloadEvents.beforeAssemblyReload -= Shutdown;
            EditorApplication.quitting -= Shutdown;
            EditorApplication.delayCall -= Flush;
            EditorApplication.delayCall -= RecoverAfterSceneSave;
            _flushQueued = false;
            _isFlushing = false;
            _primaryManager = null;
            Clear();
        }

#if !UDONSHARP
        // Queues automatic onboarding when a scene opens without the UdonSharp migration hook.
        private static void OnSceneOpened(Scene scene, OpenSceneMode mode) {
            QueueSceneOnboarding(scene);
        }
#endif

        // Queues a camera-independent Manager rebuild after scene serialization completes.
        private static void OnSceneSaved(Scene scene) {
            QueueManagerRecovery();
        }

        // Coalesces scene saves, Play Mode restores, asset imports and shadow bakes into one
        // editor-safe recovery callback.
        internal static void QueueManagerRecovery() {
            EditorApplication.delayCall -= RecoverAfterSceneSave;
            EditorApplication.delayCall += RecoverAfterSceneSave;
        }

        // Republishes all Manager shader state after Unity and UdonSharp finish saving the scene proxy.
        private static void RecoverAfterSceneSave() {
            EditorApplication.delayCall -= RecoverAfterSceneSave;
            if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || Undo.isProcessing) {
                QueueManagerRecovery();
                return;
            }

            // Apply queued authoring/proxy synchronization first, but let the recovery below own
            // the single Manager publication for this batch.
            if (_flushQueued && !_isFlushing) Flush(true);
            RefreshPrimaryManager();
            bool recoveredByPreview = LightVolumeClusteringPreview.ApplyPendingPreviewRefresh();
            if (!recoveredByPreview && IsEditableSceneObject(_primaryManager)) _primaryManager.RebuildEditorRuntimeState();
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }

        // Refreshes the only loaded Manager allowed to own global runtime data.
        private static void RefreshPrimaryManager() {
            _primaryManager = LightVolumeManagerEditorBackend.GetPrimaryManager();
        }

        // Refreshes animated projection sources for the primary Manager in Edit Mode.
        private static void UpdateAnimatedCookies() {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || Undo.isProcessing) return;
            LightVolumeManager manager = _primaryManager;
            if (manager == null || !manager.isActiveAndEnabled || !manager.AutoUpdateTextures || !manager.HasAutoCustomTextureUpdates) return;
            manager.UpdateAutoCustomTextures();
            SceneView.RepaintAll();
        }

        // Persists a changed atlas post-processor chain and queues the runtime mirror refresh.
        private static void OnAtlasPostProcessorsChanged(LightVolumeManager manager) {
            if (manager == null) return;
            LVUtils.MarkDirty(manager);
            LightVolumeManagerEditorBackend.CopyProxyToUdon(manager);
            LightVolumeManagerEditorBackend.QueueRuntimeManagerRefresh(manager);
        }

        // Translates Unity object-change events into coalesced Light Volumes update requests.
        private static void OnChangesPublished(ref ObjectChangeEventStream stream) {
            if (_isFlushing || EditorApplication.isPlayingOrWillChangePlaymode) return;
            for (int i = 0; i < stream.length; i++) {
                ObjectChangeKind kind = stream.GetEventType(i);
                switch (kind) {
                    case ObjectChangeKind.ChangeScene:
                        QueueManagerRefresh();
                        break;
                    case ObjectChangeKind.CreateGameObjectHierarchy:
                        stream.GetCreateGameObjectHierarchyEvent(i, out CreateGameObjectHierarchyEventArgs createData);
                        GameObject createdObject = GetGameObject(createData.instanceId);
                        QueueHierarchyForSetup(createdObject);
                        break;
                    case ObjectChangeKind.ChangeGameObjectStructure:
                        stream.GetChangeGameObjectStructureEvent(i, out ChangeGameObjectStructureEventArgs structureData);
                        GameObject structureObject = GetGameObject(structureData.instanceId);
                        QueueHierarchyForSetup(structureObject);
                        QueueManagerRefresh();
                        break;
                    case ObjectChangeKind.ChangeGameObjectStructureHierarchy:
                        stream.GetChangeGameObjectStructureHierarchyEvent(i, out ChangeGameObjectStructureHierarchyEventArgs structureHierarchyData);
                        GameObject structureHierarchyObject = GetGameObject(structureHierarchyData.instanceId);
                        QueueHierarchyForSetup(structureHierarchyObject);
                        QueueManagerRefresh();
                        break;
                    case ObjectChangeKind.ChangeGameObjectOrComponentProperties:
                        stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out ChangeGameObjectOrComponentPropertiesEventArgs propertyData);
                        QueueObject(EditorUtility.InstanceIDToObject(propertyData.instanceId));
                        break;
                    case ObjectChangeKind.ChangeGameObjectParent:
                        stream.GetChangeGameObjectParentEvent(i, out ChangeGameObjectParentEventArgs parentData);
                        QueueHierarchy(GetGameObject(parentData.instanceId));
                        QueueHierarchy(GetGameObject(parentData.previousParentInstanceId));
                        QueueHierarchy(GetGameObject(parentData.newParentInstanceId));
                        break;
                    case ObjectChangeKind.DestroyGameObjectHierarchy:
                        stream.GetDestroyGameObjectHierarchyEvent(i, out DestroyGameObjectHierarchyEventArgs destroyData);
                        QueueHierarchy(GetGameObject(destroyData.parentInstanceId));
                        QueueManagerRefresh();
                        break;
                    case ObjectChangeKind.UpdatePrefabInstances:
                        stream.GetUpdatePrefabInstancesEvent(i, out UpdatePrefabInstancesEventArgs prefabData);
                        for (int instanceIndex = 0; instanceIndex < prefabData.instanceIds.Length; instanceIndex++)
                            QueueHierarchyForSetup(GetGameObject(prefabData.instanceIds[instanceIndex]));
                        QueueManagerRefresh();
                        break;
                }
            }
        }

        // Queues both first-time onboarding and ordinary synchronization for a hierarchy.
        private static void QueueHierarchyForSetup(GameObject root) {
            QueueHierarchyOnboarding(root);
            QueueHierarchy(root);
        }

        // Resolves an object or component instance ID to its owning GameObject.
        private static GameObject GetGameObject(int instanceId) {
            UnityEngine.Object changedObject = EditorUtility.InstanceIDToObject(instanceId);
            if (changedObject is GameObject gameObject) return gameObject;
            return changedObject is Component component ? component.gameObject : null;
        }

        // Queues an eligible hierarchy for one-time migration and Manager assignment.
        internal static void QueueHierarchyOnboarding(GameObject root) {
            if (!LightVolumeSceneSetup.IsMainStageSceneObject(root) || !LightVolumeSceneSetup.ContainsAuthoringComponents(root) || !_onboardingRoots.Add(root)) return;
            _onboardingRootsInOrder.Add(root);
            QueueFlush();
        }

        // UdonSharp calls this after its coherent legacy migration pass. Without UdonSharp there is
        // no migration phase, so the updater queues each opened scene directly.
        internal static void QueueLoadedSceneOnboarding() {
            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++) {
                QueueSceneOnboarding(SceneManager.GetSceneAt(sceneIndex));
            }
        }

        // Queues every root hierarchy in an eligible loaded scene for onboarding.
        private static void QueueSceneOnboarding(Scene scene) {
            if (!LightVolumeSceneSetup.IsMainStageScene(scene)) return;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++) QueueHierarchyOnboarding(roots[rootIndex]);
        }

        // Routes a changed Unity object to the relevant Manager, volume or hierarchy queue.
        private static void QueueObject(UnityEngine.Object changedObject) {
            if (changedObject == null) return;
            if (changedObject is GameObject gameObject) {
                LightVolumeManager manager = gameObject.GetComponent<LightVolumeManager>();
                if (manager != null) QueueManagerRegistrationGraph(manager);
                QueueHierarchy(gameObject);
            }
            else if (changedObject is Transform transform) QueueHierarchy(transform.gameObject);
            else if (changedObject is LightVolumeManager manager) QueueManagerRegistrationGraph(manager);
            else if (changedObject is LightVolumeInstance volume) QueueVolume(volume);
            else if (changedObject is PointLightVolumeInstance pointLight) QueuePointLight(pointLight);
        }

        // Collects all Light Volumes components under a changed hierarchy into the update batch.
        private static void QueueHierarchy(GameObject root) {
            if (!IsEditableSceneObject(root) || !_hierarchyRoots.Add(root)) return;
            LightVolumeManager manager = root.GetComponentInChildren<LightVolumeManager>(true);
            _volumeBuffer.Clear();
            root.GetComponentsInChildren(true, _volumeBuffer);
            _pointLightBuffer.Clear();
            root.GetComponentsInChildren(true, _pointLightBuffer);
            bool hasRelevant = manager != null || _volumeBuffer.Count != 0 || _pointLightBuffer.Count != 0;
            if (!hasRelevant) {
                _hierarchyRoots.Remove(root);
                return;
            }
            QueueManager(manager);
            for (int i = 0; i < _volumeBuffer.Count; i++) QueueVolume(_volumeBuffer[i]);
            for (int i = 0; i < _pointLightBuffer.Count; i++) QueuePointLight(_pointLightBuffer[i]);
        }

        // Adds an editable Light Volume and its Manager to the current update batch.
        private static void QueueVolume(LightVolumeInstance volume) {
            if (!IsEditableSceneObject(volume)) return;
            _volumes.Add(volume);
            QueueManager(volume.LightVolumeManager);
        }

        // Adds an editable Point Light Volume and its Manager to the current update batch.
        private static void QueuePointLight(PointLightVolumeInstance pointLight) {
            if (!IsEditableSceneObject(pointLight)) return;
            _pointLights.Add(pointLight);
            QueueManager(pointLight.LightVolumeManager);
        }

        // Queues one rebuild for the cached primary Manager and schedules a deferred batch flush.
        private static void QueueManager(LightVolumeManager manager) {
            if (IsEditableSceneObject(manager)) {
                if (_primaryManager == null && LightVolumeSceneSetup.IsMainStageSceneObject(manager.gameObject)) _primaryManager = manager;
                if (manager == _primaryManager) _managerUpdateQueued = true;
            }
            if (!_isFlushing) QueueFlush();
        }

        // A Manager property change can include an Undo-restored registry array. Queue every authoring component in its scene so the derived graph is checked in the same batch.
        private static void QueueManagerRegistrationGraph(LightVolumeManager manager) {
            QueueManager(manager);
            if (!IsEditableSceneObject(manager) || !LightVolumeSceneSetup.IsMainStageSceneObject(manager.gameObject)) return;
            GameObject[] roots = manager.gameObject.scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++) QueueHierarchy(roots[i]);
        }

        // Refreshes the primary Manager at most once for a coalesced structural-change batch.
        private static void QueueManagerRefresh() {
            if (!_flushQueued) RefreshPrimaryManager();
            if (IsEditableSceneObject(_primaryManager)) _managerUpdateQueued = true;
            QueueFlush();
        }

        // Coalesces pending changes into one delayed editor update.
        private static void QueueFlush() {
            if (_flushQueued) return;
            _flushQueued = true;
            EditorApplication.delayCall += Flush;
        }

        // Applies the coalesced object-change batch before Scene View renders, avoiding one stale
        // camera frame while retaining delayCall as a fallback when no Scene View is visible.
        internal static void FlushPendingSceneChanges() {
            if (_flushQueued && !_isFlushing) Flush();
        }

        // Applies onboarding, authoring synchronization and Manager rebuilds for the queued batch.
        private static void Flush() {
            Flush(false);
        }

        // Optionally leaves the Manager publication to an immediately following scene recovery.
        private static void Flush(bool managerRecoveryFollows) {
            EditorApplication.delayCall -= Flush;
            _flushQueued = false;
            if (EditorApplication.isPlayingOrWillChangePlaymode) {
                Clear();
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || Undo.isProcessing) {
                QueueFlush();
                return;
            }

            _isFlushing = true;
            try {
                bool registrationGraphChanged = false;
                for (int i = 0; i < _onboardingRootsInOrder.Count; i++) {
                    GameObject root = _onboardingRootsInOrder[i];
                    // Registration arrays are a derived graph. Automatic restoration must not add a hidden Undo step above the user's create/delete/move operation.
                    if (!LightVolumeSceneSetup.OnboardHierarchy(root, out LightVolumeManager manager, false)) continue;
                    registrationGraphChanged = true;
                    QueueHierarchy(root);
                    QueueManager(manager);
                }
                foreach (LightVolumeInstance volume in _volumes) {
                    if (!IsEditableSceneObject(volume)) continue;
                    LightVolumeTools.ApplyRuntimeState(volume, false);
                    if (LightVolumeSceneSetup.ReconcileRegistration(_primaryManager, volume)) {
                        registrationGraphChanged = true;
                        _managerUpdateQueued = true;
                    }
                    LightVolumeManagerEditorBackend.CopyProxyToUdon(volume);
                }
                foreach (PointLightVolumeInstance pointLight in _pointLights) {
                    if (!IsEditableSceneObject(pointLight)) continue;
                    bool customTexturesChanged = pointLight.HasEditorCustomTextureChanges();
                    bool shadowTexturesChanged = pointLight.HasEditorShadowTextureChanges();
                    if (customTexturesChanged) PointLightVolumeEditorUtility.EnsureProjectionTextureImportSettings(pointLight);
                    pointLight.EditorApplyAuthoringData(customTexturesChanged, shadowTexturesChanged, false);
                    if (LightVolumeSceneSetup.ReconcileRegistration(_primaryManager, pointLight)) {
                        registrationGraphChanged = true;
                        _managerUpdateQueued = true;
                    }
                    LightVolumeManagerEditorBackend.CopyProxyToUdon(pointLight);
                }
                if (registrationGraphChanged) LightVolumeManagerEditorBackend.SynchronizeRegistryMetadata(_primaryManager);
                if (!managerRecoveryFollows && _managerUpdateQueued && IsEditableSceneObject(_primaryManager)) _primaryManager.UpdateVolumes();
            } finally {
                Clear();
                _isFlushing = false;
            }
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }

        // Checks whether a component belongs to an editable loaded scene object.
        private static bool IsEditableSceneObject(Component component) {
            return component != null && IsEditableSceneObject(component.gameObject);
        }

        // Rejects null, persistent and unloaded scene objects from automatic editor mutation.
        private static bool IsEditableSceneObject(GameObject gameObject) {
            return gameObject != null && gameObject.scene.IsValid() && gameObject.scene.isLoaded && !EditorUtility.IsPersistent(gameObject);
        }

        // Clears every coalescing buffer after a flush or play-mode transition.
        private static void Clear() {
            _volumes.Clear();
            _pointLights.Clear();
            _hierarchyRoots.Clear();
            _onboardingRoots.Clear();
            _onboardingRootsInOrder.Clear();
            _volumeBuffer.Clear();
            _pointLightBuffer.Clear();
            _managerUpdateQueued = false;
        }
    }
}
