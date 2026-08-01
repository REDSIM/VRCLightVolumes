using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VRCLightVolumes {
    // Centralizes event-driven and continuous Edit Mode updates without per-object polling.
    [InitializeOnLoad]
    internal static class LightVolumeEditorUpdater {
        private static readonly HashSet<LightVolumeManager> _managers = new HashSet<LightVolumeManager>();
        private static readonly HashSet<LightVolumeInstance> _volumes = new HashSet<LightVolumeInstance>();
        private static readonly HashSet<PointLightVolumeInstance> _pointLights = new HashSet<PointLightVolumeInstance>();
        private static readonly HashSet<GameObject> _hierarchyRoots = new HashSet<GameObject>();
        private static readonly List<LightVolumeManager> _managerBuffer = new List<LightVolumeManager>();
        private static readonly List<LightVolumeInstance> _volumeBuffer = new List<LightVolumeInstance>();
        private static readonly List<PointLightVolumeInstance> _pointLightBuffer = new List<PointLightVolumeInstance>();
        private static LightVolumeManager[] _loadedManagers = Array.Empty<LightVolumeManager>();
        private static bool _refreshAllManagers;
        private static bool _flushQueued;
        private static bool _isFlushing;

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
        }

        private static void RefreshLoadedManagers() {
            _loadedManagers = UnityEngine.Object.FindObjectsByType<LightVolumeManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        }

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

        private static void OnAtlasPostProcessorsChanged(LightVolumeManager manager) {
            if (manager == null) return;
            LVUtils.MarkDirty(manager);
            LightVolumeManagerTools.CopyProxyToUdon(manager);
            LightVolumeManagerTools.QueueRuntimeManagerRefresh(manager);
        }

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
                        QueueHierarchy(GetGameObject(createData.instanceId));
                        break;
                    case ObjectChangeKind.ChangeGameObjectStructure:
                        stream.GetChangeGameObjectStructureEvent(i, out ChangeGameObjectStructureEventArgs structureData);
                        QueueHierarchy(GetGameObject(structureData.instanceId));
                        QueueAllManagers();
                        break;
                    case ObjectChangeKind.ChangeGameObjectStructureHierarchy:
                        stream.GetChangeGameObjectStructureHierarchyEvent(i, out ChangeGameObjectStructureHierarchyEventArgs structureHierarchyData);
                        QueueHierarchy(GetGameObject(structureHierarchyData.instanceId));
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

        private static GameObject GetGameObject(int instanceId) {
            return EditorUtility.InstanceIDToObject(instanceId) as GameObject;
        }

        private static void QueueObject(UnityEngine.Object changedObject) {
            if (changedObject == null) return;
            if (changedObject is GameObject gameObject) QueueHierarchy(gameObject);
            else if (changedObject is Transform transform) QueueHierarchy(transform.gameObject);
            else if (changedObject is LightVolumeManager manager) QueueManager(manager);
            else if (changedObject is LightVolumeInstance volume) QueueVolume(volume);
            else if (changedObject is PointLightVolumeInstance pointLight) QueuePointLight(pointLight);
        }

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

        private static void QueueVolume(LightVolumeInstance volume) {
            if (!IsEditableSceneObject(volume)) return;
            _volumes.Add(volume);
            QueueManager(volume.LightVolumeManager);
        }

        private static void QueuePointLight(PointLightVolumeInstance pointLight) {
            if (!IsEditableSceneObject(pointLight)) return;
            _pointLights.Add(pointLight);
            QueueManager(pointLight.LightVolumeManager);
        }

        private static void QueueManager(LightVolumeManager manager) {
            if (IsEditableSceneObject(manager)) _managers.Add(manager);
            QueueFlush();
        }

        private static void QueueAllManagers() {
            _refreshAllManagers = true;
            QueueFlush();
        }

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

        private static bool IsEditableSceneObject(Component component) {
            return component != null && IsEditableSceneObject(component.gameObject);
        }

        private static bool IsEditableSceneObject(GameObject gameObject) {
            return gameObject != null && gameObject.scene.IsValid() && gameObject.scene.isLoaded && !EditorUtility.IsPersistent(gameObject);
        }

        private static void Clear() {
            _managers.Clear();
            _volumes.Clear();
            _pointLights.Clear();
            _hierarchyRoots.Clear();
            _managerBuffer.Clear();
            _volumeBuffer.Clear();
            _pointLightBuffer.Clear();
            _refreshAllManagers = false;
        }
    }
}
