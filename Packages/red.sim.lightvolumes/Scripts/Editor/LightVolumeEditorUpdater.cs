using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRCLightVolumes {
    // Centralizes event-driven and continuous Edit Mode updates without per-object polling.
    [InitializeOnLoad]
    internal static class LightVolumeEditorUpdater {
        private static readonly HashSet<LightVolumeManager> _managers = new HashSet<LightVolumeManager>();
        private static readonly HashSet<LightVolumeInstance> _volumes = new HashSet<LightVolumeInstance>();
        private static readonly HashSet<PointLightVolumeInstance> _pointLights = new HashSet<PointLightVolumeInstance>();
        private static readonly HashSet<GameObject> _hierarchyRoots = new HashSet<GameObject>();
        private static readonly HashSet<GameObject> _onboardingRoots = new HashSet<GameObject>();
        private static readonly List<LightVolumeManager> _managerBuffer = new List<LightVolumeManager>();
        private static readonly List<LightVolumeInstance> _volumeBuffer = new List<LightVolumeInstance>();
        private static readonly List<PointLightVolumeInstance> _pointLightBuffer = new List<PointLightVolumeInstance>();
        private static readonly List<GameObject> _onboardingRootsInOrder = new List<GameObject>();
        private static LightVolumeManager[] _loadedManagers = Array.Empty<LightVolumeManager>();
        private static bool _refreshAllManagers;
        private static bool _flushQueued;
        private static bool _isFlushing;

        // Installs editor change hooks and initializes the cache of loaded Managers.
        static LightVolumeEditorUpdater() {
            RefreshLoadedManagers();
            ObjectChangeEvents.changesPublished -= OnChangesPublished;
            ObjectChangeEvents.changesPublished += OnChangesPublished;
            LightVolumeManager.AtlasPostProcessorsChanged -= OnAtlasPostProcessorsChanged;
            LightVolumeManager.AtlasPostProcessorsChanged += OnAtlasPostProcessorsChanged;
            EditorApplication.hierarchyChanged -= RefreshLoadedManagers;
            EditorApplication.hierarchyChanged += RefreshLoadedManagers;
            EditorApplication.update -= UpdateAnimatedCookies;
            EditorApplication.update += UpdateAnimatedCookies;
#if !UDONSHARP
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            QueueLoadedSceneOnboarding();
#endif
        }

#if !UDONSHARP
        // Queues automatic onboarding when a scene opens without the UdonSharp migration hook.
        private static void OnSceneOpened(Scene scene, OpenSceneMode mode) {
            QueueSceneOnboarding(scene);
        }
#endif

        // Refreshes the cached set of loaded Manager components.
        private static void RefreshLoadedManagers() {
            _loadedManagers = UnityEngine.Object.FindObjectsByType<LightVolumeManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        }

        // Refreshes animated projection sources for Managers that opt into edit-mode updates.
        private static void UpdateAnimatedCookies() {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || Undo.isProcessing) return;
            bool repaint = false;
            for (int i = 0; i < _loadedManagers.Length; i++) {
                LightVolumeManager manager = _loadedManagers[i];
                if (manager == null || !manager.isActiveAndEnabled || !manager.AutoUpdateTextures || !manager.HasAutoCustomTextureUpdates) continue;
                manager.UpdateAutoCustomTextures();
                repaint = true;
            }
            if (repaint) SceneView.RepaintAll();
        }

        // Persists a changed atlas post-processor chain and queues the runtime mirror refresh.
        private static void OnAtlasPostProcessorsChanged(LightVolumeManager manager) {
            if (manager == null) return;
            LVUtils.MarkDirty(manager);
            LightVolumeManagerTools.CopyProxyToUdon(manager);
            LightVolumeManagerTools.QueueRuntimeManagerRefresh(manager);
        }

        // Translates Unity object-change events into coalesced Light Volumes update requests.
        private static void OnChangesPublished(ref ObjectChangeEventStream stream) {
            if (_isFlushing || EditorApplication.isPlayingOrWillChangePlaymode) return;
            for (int i = 0; i < stream.length; i++) {
                ObjectChangeKind kind = stream.GetEventType(i);
                switch (kind) {
                    case ObjectChangeKind.ChangeScene:
                        QueueAllManagers();
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
                        QueueAllManagers();
                        break;
                    case ObjectChangeKind.ChangeGameObjectStructureHierarchy:
                        stream.GetChangeGameObjectStructureHierarchyEvent(i, out ChangeGameObjectStructureHierarchyEventArgs structureHierarchyData);
                        GameObject structureHierarchyObject = GetGameObject(structureHierarchyData.instanceId);
                        QueueHierarchyForSetup(structureHierarchyObject);
                        QueueAllManagers();
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
                        QueueAllManagers();
                        break;
                    case ObjectChangeKind.UpdatePrefabInstances:
                        stream.GetUpdatePrefabInstancesEvent(i, out UpdatePrefabInstancesEventArgs prefabData);
                        for (int instanceIndex = 0; instanceIndex < prefabData.instanceIds.Length; instanceIndex++)
                            QueueHierarchy(GetGameObject(prefabData.instanceIds[instanceIndex]));
                        QueueAllManagers();
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
            if (changedObject is GameObject gameObject) QueueHierarchy(gameObject);
            else if (changedObject is Transform transform) QueueHierarchy(transform.gameObject);
            else if (changedObject is LightVolumeManager manager) QueueManager(manager);
            else if (changedObject is LightVolumeInstance volume) QueueVolume(volume);
            else if (changedObject is PointLightVolumeInstance pointLight) QueuePointLight(pointLight);
        }

        // Collects all Light Volumes components under a changed hierarchy into the update batch.
        private static void QueueHierarchy(GameObject root) {
            if (!IsEditableSceneObject(root) || !_hierarchyRoots.Add(root)) return;
            _managerBuffer.Clear();
            root.GetComponentsInChildren(true, _managerBuffer);
            _volumeBuffer.Clear();
            root.GetComponentsInChildren(true, _volumeBuffer);
            _pointLightBuffer.Clear();
            root.GetComponentsInChildren(true, _pointLightBuffer);
            bool hasRelevant = _managerBuffer.Count != 0 || _volumeBuffer.Count != 0 || _pointLightBuffer.Count != 0;
            if (!hasRelevant) {
                _hierarchyRoots.Remove(root);
                return;
            }
            for (int i = 0; i < _managerBuffer.Count; i++) QueueManager(_managerBuffer[i]);
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

        // Adds an editable Manager and schedules a deferred batch flush.
        private static void QueueManager(LightVolumeManager manager) {
            if (IsEditableSceneObject(manager)) _managers.Add(manager);
            if (!_isFlushing) QueueFlush();
        }

        // Requests that every loaded Manager join the next update batch.
        private static void QueueAllManagers() {
            _refreshAllManagers = true;
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
                for (int i = 0; i < _onboardingRootsInOrder.Count; i++) {
                    GameObject root = _onboardingRootsInOrder[i];
                    if (!LightVolumeSceneSetup.OnboardHierarchy(root, out LightVolumeManager manager)) continue;
                    QueueHierarchy(root);
                    _managers.Add(manager);
                }
                foreach (LightVolumeInstance volume in _volumes) {
                    if (!IsEditableSceneObject(volume)) continue;
                    LightVolumeTools.ApplyRuntimeState(volume, false);
                    volume.IsActive = volume.isActiveAndEnabled && volume.Intensity != 0f && volume.Color != Color.black;
                    LightVolumeManagerTools.CopyProxyToUdon(volume);
                }
                foreach (PointLightVolumeInstance pointLight in _pointLights) {
                    if (!IsEditableSceneObject(pointLight)) continue;
                    bool customTexturesChanged = pointLight.HasEditorCustomTextureChanges();
                    bool shadowTexturesChanged = pointLight.HasEditorShadowTextureChanges();
                    pointLight.EditorApplyAuthoringData(customTexturesChanged, shadowTexturesChanged, false);
                    pointLight.IsActive = pointLight.isActiveAndEnabled && pointLight.Intensity != 0f && pointLight.Color != Color.black;
                    LightVolumeManagerTools.CopyProxyToUdon(pointLight);
                }
                if (_refreshAllManagers) {
                    RefreshLoadedManagers();
                    for (int i = 0; i < _loadedManagers.Length; i++) {
                        if (IsEditableSceneObject(_loadedManagers[i])) _managers.Add(_loadedManagers[i]);
                    }
                }
                foreach (LightVolumeManager manager in _managers) {
                    if (IsEditableSceneObject(manager) && !manager.isActiveAndEnabled) manager.UpdateVolumes();
                }
                foreach (LightVolumeManager manager in _managers) {
                    if (IsEditableSceneObject(manager) && manager.isActiveAndEnabled) manager.UpdateVolumes();
                }
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
            _managers.Clear();
            _volumes.Clear();
            _pointLights.Clear();
            _hierarchyRoots.Clear();
            _onboardingRoots.Clear();
            _onboardingRootsInOrder.Clear();
            _managerBuffer.Clear();
            _volumeBuffer.Clear();
            _pointLightBuffer.Clear();
            _refreshAllManagers = false;
        }
    }
}
