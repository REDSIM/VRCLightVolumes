using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRCLightVolumes {
    [InitializeOnLoad]
    public static class LightVolumeUdonComponentSanitizer {
        private const string BackingUdonBehaviourFieldName = "_udonSharpBackingUdonBehaviour";
        private const string ProgramSourceFieldName = "programSource";
        private const string SourceCsScriptPropertyName = "sourceCsScript";
        private const string UdonBehaviourTypeName = "VRC.Udon.UdonBehaviour";
        private const string UndoName = "Sanitize Light Volume Udon Components";
        private const string LegacyMigrationSessionKeyPrefix = "VRCLightVolumes.LegacyMigration.";
        private const string HasLegacyMigrationSessionKeys = LegacyMigrationSessionKeyPrefix + "Any";

        private static bool _isSanitizeQueued = false;
        private static bool _isSanitizing = false;
        private static bool _queuedSanitizeIncludesMigration = false;
        private static FieldInfo _programSourceField = null;
        private static Type _programSourceFieldOwner = null;
        private static bool _needsAuthoringSyncAfterMigration = false;
        private static readonly Dictionary<Type, FieldInfo> _backingUdonBehaviourFields = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<string, string> _sceneYamlCache = new Dictionary<string, string>();
        private static readonly Dictionary<string, bool> _sceneHasLegacyDataCache = new Dictionary<string, bool>();
        private static readonly Dictionary<int, Type> _programSourceTypeCache = new Dictionary<int, Type>();
        private static readonly ConditionalWeakTable<Component, object> _legacyMigratedComponents = new ConditionalWeakTable<Component, object>();
        private static readonly object _legacyMigrationMarker = new object();
        private static readonly List<GameObject> _sanitizeCandidates = new List<GameObject>();
        private static readonly HashSet<int> _sanitizeCandidateIds = new HashSet<int>();
        private static readonly HashSet<int> _seenSystemComponentGameObjectIds = new HashSet<int>();
        private static readonly HashSet<int> _checkedDuplicateGameObjectIds = new HashSet<int>();

        // Registers delayed cleanup so duplicated UdonSharp proxy components are removed after editor reloads and hierarchy edits
        static LightVolumeUdonComponentSanitizer() {
            EditorApplication.delayCall += QueueInitialSanitizeLoadedScenes;
            EditorApplication.hierarchyChanged += QueueHierarchySanitizeLoadedScenes;
            EditorSceneManager.sceneOpened += QueueSanitizeOpenedScene;
        }

        // Removes duplicated Light Volume system Udon components from every loaded scene object
        public static int SanitizeLoadedScenes() {
            return SanitizeLoadedScenes(true);
        }

        private static int SanitizeLoadedScenes(bool migrateLegacyData) {
            if (_isSanitizing) return 0;

            _isSanitizing = true;
            try {
                int removedCount = 0;

                bool duplicatesOnly = !migrateLegacyData;
                CollectSanitizeCandidates(Resources.FindObjectsOfTypeAll<LightVolumeManager>(), duplicatesOnly);
                CollectSanitizeCandidates(Resources.FindObjectsOfTypeAll<LightVolumeInstance>(), duplicatesOnly);
                CollectSanitizeCandidates(Resources.FindObjectsOfTypeAll<PointLightVolumeInstance>(), duplicatesOnly);
                for (int i = 0; i < _sanitizeCandidates.Count; i++) {
                    removedCount += SanitizeGameObject(_sanitizeCandidates[i], migrateLegacyData);
                }

                if (SyncAuthoringAfterMigrationIfNeeded()) removedCount += SanitizeDuplicateCandidates();

                return removedCount;
            } finally {
                ClearPassCaches();
                _isSanitizing = false;
            }
        }

        // Adds each loaded scene object once, optionally requiring a duplicate proxy of this runtime type.
        private static void CollectSanitizeCandidates<T>(T[] components, bool duplicatesOnly) where T : Component {
            if (duplicatesOnly) _seenSystemComponentGameObjectIds.Clear();
            for (int i = 0; i < components.Length; i++) {
                T component = components[i];
                if (!ShouldSanitizeComponent(component)) continue;

                GameObject gameObject = component.gameObject;
                int instanceId = gameObject.GetInstanceID();
                if (duplicatesOnly && _seenSystemComponentGameObjectIds.Add(instanceId)) continue;
                if (!_sanitizeCandidateIds.Add(instanceId)) continue;
                _sanitizeCandidates.Add(gameObject);
            }
            if (duplicatesOnly) _seenSystemComponentGameObjectIds.Clear();
        }

        // Removes duplicated Light Volume system Udon components from one scene object
        public static int SanitizeGameObject(GameObject gameObject) {
            if (_isSanitizing) return 0;

            bool queuePostMigrationCleanup = false;
            _isSanitizing = true;
            try {
                int removedCount = SanitizeGameObject(gameObject, true);
                queuePostMigrationCleanup = SyncAuthoringAfterMigrationIfNeeded();
                return removedCount;
            } finally {
                ClearPassCaches();
                _isSanitizing = false;
                if (queuePostMigrationCleanup) QueueSanitizeLoadedScenes(false);
            }
        }

        private static int SanitizeGameObject(GameObject gameObject, bool migrateLegacyData) {
            if (!ShouldSanitizeGameObject(gameObject)) return 0;

            LightVolumeSetup setup = gameObject.GetComponent<LightVolumeSetup>();
            LightVolume lightVolume = gameObject.GetComponent<LightVolume>();
            PointLightVolume pointLight = gameObject.GetComponent<PointLightVolume>();
            LightVolumeManager[] managers = gameObject.GetComponents<LightVolumeManager>();
            LightVolumeInstance[] lightVolumeInstances = gameObject.GetComponents<LightVolumeInstance>();
            PointLightVolumeInstance[] pointLightInstances = gameObject.GetComponents<PointLightVolumeInstance>();

            bool migrated = migrateLegacyData && MigrateLegacyRuntimeComponents(managers, lightVolumeInstances, pointLightInstances);
            if (migrated) _needsAuthoringSyncAfterMigration = true;

            int removedCount = 0;
            removedCount += SanitizeComponents(gameObject, managers, setup, setup != null ? setup.LightVolumeManager : null);
            removedCount += SanitizeComponents(gameObject, lightVolumeInstances, lightVolume, lightVolume != null ? lightVolume.LightVolumeInstance : null);
            removedCount += SanitizeComponents(gameObject, pointLightInstances, pointLight, pointLight != null ? pointLight.PointLightVolumeInstance : null);

            if (removedCount > 0 || migrated) MarkSceneDirty(gameObject);
            return removedCount;
        }

        // Runs one bounded duplicate-only pass after migration synchronization may have added or rewired proxies.
        private static int SanitizeDuplicateCandidates() {
            _sanitizeCandidates.Clear();
            _sanitizeCandidateIds.Clear();
            _seenSystemComponentGameObjectIds.Clear();
            _checkedDuplicateGameObjectIds.Clear();
            _programSourceTypeCache.Clear();

            CollectSanitizeCandidates(Resources.FindObjectsOfTypeAll<LightVolumeManager>(), true);
            CollectSanitizeCandidates(Resources.FindObjectsOfTypeAll<LightVolumeInstance>(), true);
            CollectSanitizeCandidates(Resources.FindObjectsOfTypeAll<PointLightVolumeInstance>(), true);

            int removedCount = 0;
            for (int i = 0; i < _sanitizeCandidates.Count; i++) removedCount += SanitizeGameObject(_sanitizeCandidates[i], false);
            return removedCount;
        }

        // Clears data cached only for the duration of one sanitizer pass.
        private static void ClearPassCaches() {
            _sceneYamlCache.Clear();
            _sceneHasLegacyDataCache.Clear();
            _programSourceTypeCache.Clear();
            _sanitizeCandidates.Clear();
            _sanitizeCandidateIds.Clear();
            _seenSystemComponentGameObjectIds.Clear();
            _checkedDuplicateGameObjectIds.Clear();
        }

        // Queues one full cleanup and legacy migration pass after editor assemblies load.
        private static void QueueInitialSanitizeLoadedScenes() {
            QueueSanitizeLoadedScenes(true);
        }

        // Queues cleanup after a scene is opened and all scene objects are available
        private static void QueueSanitizeOpenedScene(Scene scene, OpenSceneMode mode) {
            QueueSanitizeLoadedScenes(true);
        }

        // Queues a cheap duplicate-only pass only when a hierarchy edit actually produced duplicate system proxies.
        private static void QueueHierarchySanitizeLoadedScenes() {
            if (_isSanitizeQueued || _isSanitizing) return;
            if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!HasDuplicateSystemComponents()) return;
            QueueSanitizeLoadedScenes(false);
        }

        // Coalesces editor callbacks into one delayed cleanup pass
        private static void QueueSanitizeLoadedScenes(bool includeLegacyMigration) {
            if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (_isSanitizing) return;

            _queuedSanitizeIncludesMigration |= includeLegacyMigration;
            if (_isSanitizeQueued) return;
            _isSanitizeQueued = true;
            EditorApplication.delayCall += RunQueuedSanitizeLoadedScenes;
        }

        // Runs a queued cleanup pass once Unity finishes the current editor event
        private static void RunQueuedSanitizeLoadedScenes() {
            if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) {
                _isSanitizeQueued = false;
                _queuedSanitizeIncludesMigration = false;
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) {
                EditorApplication.delayCall += RunQueuedSanitizeLoadedScenes;
                return;
            }

            bool includeLegacyMigration = _queuedSanitizeIncludesMigration;
            _isSanitizeQueued = false;
            _queuedSanitizeIncludesMigration = false;
            int removedCount = SanitizeLoadedScenes(includeLegacyMigration);
            if (removedCount > 0) Debug.Log($"[LightVolume] Removed {removedCount} duplicate system Udon component(s)");
        }

        // Returns true when at least two loaded proxies of one Light Volume runtime type share a GameObject.
        private static bool HasDuplicateSystemComponents() {
            _programSourceTypeCache.Clear();
            try {
                return HasDuplicateComponents(Resources.FindObjectsOfTypeAll<LightVolumeManager>())
                    || HasDuplicateComponents(Resources.FindObjectsOfTypeAll<LightVolumeInstance>())
                    || HasDuplicateComponents(Resources.FindObjectsOfTypeAll<PointLightVolumeInstance>());
            } finally {
                _seenSystemComponentGameObjectIds.Clear();
                _checkedDuplicateGameObjectIds.Clear();
                _programSourceTypeCache.Clear();
            }
        }

        // Detects duplicate owners only when one candidate is ready for safe cleanup in the current UdonSharp state.
        private static bool HasDuplicateComponents<T>(T[] components) where T : Component {
            _seenSystemComponentGameObjectIds.Clear();
            _checkedDuplicateGameObjectIds.Clear();
            for (int i = 0; i < components.Length; i++) {
                T component = components[i];
                if (!ShouldSanitizeComponent(component)) continue;
                int gameObjectId = component.gameObject.GetInstanceID();
                if (_seenSystemComponentGameObjectIds.Add(gameObjectId)) continue;
                if (!_checkedDuplicateGameObjectIds.Add(gameObjectId)) continue;
                if (HasReadyCleanupCandidate(component.gameObject.GetComponents<T>())) return true;
            }
            _seenSystemComponentGameObjectIds.Clear();
            _checkedDuplicateGameObjectIds.Clear();
            return false;
        }

        // Requires a ready migrated keeper when legacy data was restored; otherwise any ready proxy can lead cleanup.
        private static bool HasReadyCleanupCandidate<T>(T[] components) where T : Component {
            bool preserveMigratedData = false;
            for (int i = 0; i < components.Length; i++) {
                if (!WasLegacyMigrated(components[i])) continue;
                preserveMigratedData = true;
                break;
            }

            for (int i = 0; i < components.Length; i++) {
                T component = components[i];
                if (component == null || (preserveMigratedData && !WasLegacyMigrated(component))) continue;
                Component backingUdonBehaviour;
                UnityEngine.Object programSource;
                if (TryGetReadyProxy(component, out backingUdonBehaviour, out programSource)) return true;
            }
            return false;
        }

        // Keeps the healthiest proxy of one runtime type, repairs its authoring reference and removes safe duplicates.
        private static int SanitizeComponents<T>(GameObject gameObject, T[] components, Component authoringComponent, T preferred) where T : Component {
            if (components.Length == 0) return 0;

            T keeper = GetBestKeeper(components, preferred);
            Component keeperBackingUdonBehaviour;
            UnityEngine.Object keeperProgramSource;
            if (!TryGetReadyProxy(keeper, out keeperBackingUdonBehaviour, out keeperProgramSource)) return 0;

            UpdateAuthoringReference(authoringComponent, keeper);
            return RemoveDuplicateComponents(gameObject, components, keeper, keeperBackingUdonBehaviour, keeperProgramSource);
        }

        // Points the authoring component at the selected runtime proxy before any duplicate is destroyed.
        private static void UpdateAuthoringReference(Component authoringComponent, Component keeper) {
            LightVolumeManager manager = keeper as LightVolumeManager;
            if (manager != null) {
                LightVolumeSetup setup = authoringComponent as LightVolumeSetup;
                if (setup == null || setup.LightVolumeManager == manager) return;
                Undo.RecordObject(setup, UndoName);
                setup.LightVolumeManager = manager;
                MarkObjectDirty(setup);
                return;
            }

            LightVolumeInstance lightVolumeInstance = keeper as LightVolumeInstance;
            if (lightVolumeInstance != null) {
                LightVolume lightVolume = authoringComponent as LightVolume;
                if (lightVolume == null || lightVolume.LightVolumeInstance == lightVolumeInstance) return;
                Undo.RecordObject(lightVolume, UndoName);
                lightVolume.LightVolumeInstance = lightVolumeInstance;
                MarkObjectDirty(lightVolume);
                return;
            }

            PointLightVolumeInstance pointLightInstance = keeper as PointLightVolumeInstance;
            if (pointLightInstance == null) return;
            PointLightVolume pointLight = authoringComponent as PointLightVolume;
            if (pointLight == null || pointLight.PointLightVolumeInstance == pointLightInstance) return;
            Undo.RecordObject(pointLight, UndoName);
            pointLight.PointLightVolumeInstance = pointLightInstance;
            MarkObjectDirty(pointLight);
        }

        // Migrates serialized 2.x runtime component data after Unity loads the scene with 3.x scripts.
        private static bool MigrateLegacyRuntimeComponents(LightVolumeManager[] managers, LightVolumeInstance[] lightVolumes, PointLightVolumeInstance[] pointLights) {
            bool migrated = false;

            for (int i = 0; i < managers.Length; i++) {
                LightVolumeManager manager = managers[i];
                if (manager == null) continue;
                if (MigrateLegacyManagerRuntimeTextures(manager)) migrated = true;
            }

            for (int i = 0; i < lightVolumes.Length; i++) {
                LightVolumeInstance lightVolume = lightVolumes[i];
                if (lightVolume == null) continue;
                if (!MigrateLegacyLightVolumeData(lightVolume)) continue;
                MarkObjectDirty(lightVolume);
                migrated = true;
            }

            for (int i = 0; i < pointLights.Length; i++) {
                PointLightVolumeInstance pointLight = pointLights[i];
                if (pointLight == null) continue;
                if (!MigrateLegacyPointLightData(pointLight)) continue;
                MarkObjectDirty(pointLight);
                migrated = true;
            }

            return migrated;
        }

        // Converts old scene YAML volume fields into the current runtime layout without adding legacy fields to UdonSharp proxies.
        private static bool MigrateLegacyLightVolumeData(LightVolumeInstance lightVolume) {
            string serializedBlock;
            if (!TryGetSceneObjectYamlBlock(lightVolume, out serializedBlock)) return false;
            return MigrateLegacyLightVolumeData(lightVolume, serializedBlock);
        }

        // Converts old scene YAML volume fields into the current runtime layout.
        private static bool MigrateLegacyLightVolumeData(LightVolumeInstance lightVolume, string serializedBlock) {
            if (WasLegacyMigrated(lightVolume)) return false;

            bool changed = false;

            Vector4 legacyRelativeRotation;
            if (lightVolume.RelativeRotationRow0 == Vector3.zero && lightVolume.RelativeRotationRow1 == Vector3.zero && TryReadVector4(serializedBlock, "RelativeRotation", "_legacyRelativeRotation", out legacyRelativeRotation) && legacyRelativeRotation != new Vector4(0, 0, 0, 1) && legacyRelativeRotation != Vector4.zero) {
                Undo.RecordObject(lightVolume, UndoName);
                Quaternion relativeRotation = new Quaternion(legacyRelativeRotation.x, legacyRelativeRotation.y, legacyRelativeRotation.z, legacyRelativeRotation.w);
                Matrix4x4 rotationMatrix = Matrix4x4.Rotate(relativeRotation);
                lightVolume.RelativeRotationRow0 = rotationMatrix.GetRow(0);
                lightVolume.RelativeRotationRow1 = rotationMatrix.GetRow(1);
                lightVolume.IsRotated = Mathf.Abs(Quaternion.Dot(relativeRotation, Quaternion.identity)) < 0.999999f;
                changed = true;
            }

            Vector4 legacyBoundsUvwMax;
            if (TryReadVector4(serializedBlock, "BoundsUvwMax0", "_legacyBoundsUvwMax0", out legacyBoundsUvwMax) && MigrateLegacyBoundsScale(ref lightVolume.BoundsUvwMin0, legacyBoundsUvwMax, 0, lightVolume, changed)) changed = true;
            if (TryReadVector4(serializedBlock, "BoundsUvwMax1", "_legacyBoundsUvwMax1", out legacyBoundsUvwMax) && MigrateLegacyBoundsScale(ref lightVolume.BoundsUvwMin1, legacyBoundsUvwMax, 1, lightVolume, changed)) changed = true;
            if (TryReadVector4(serializedBlock, "BoundsUvwMax2", "_legacyBoundsUvwMax2", out legacyBoundsUvwMax) && MigrateLegacyBoundsScale(ref lightVolume.BoundsUvwMin2, legacyBoundsUvwMax, 2, lightVolume, changed)) changed = true;

            if (changed) MarkLegacyMigrated(lightVolume);
            return changed;
        }

        // Restores BoundsUvwMin.w scale from old explicit max vectors if a scene had only legacy bounds serialized.
        private static bool MigrateLegacyBoundsScale(ref Vector4 uvwMin, Vector4 legacyMax, int axis, LightVolumeInstance lightVolume, bool undoRecorded) {
            if (uvwMin.w != 0f || legacyMax == Vector4.zero) return false;
            if (!undoRecorded) Undo.RecordObject(lightVolume, UndoName);
            float min = axis == 0 ? uvwMin.x : axis == 1 ? uvwMin.y : uvwMin.z;
            float max = axis == 0 ? legacyMax.x : axis == 1 ? legacyMax.y : legacyMax.z;
            uvwMin.w = max - min;
            return true;
        }

        // Converts old scene YAML packed point light fields into explicit 3.x fields without adding legacy fields to UdonSharp proxies.
        private static bool MigrateLegacyPointLightData(PointLightVolumeInstance pointLight) {
            string serializedBlock;
            if (!TryGetSceneObjectYamlBlock(pointLight, out serializedBlock)) return false;
            return MigrateLegacyPointLightData(pointLight, serializedBlock);
        }

        // Converts old scene YAML packed point light fields into explicit 3.x fields.
        private static bool MigrateLegacyPointLightData(PointLightVolumeInstance pointLight, string serializedBlock) {
            if (WasLegacyMigrated(pointLight)) return false;

            Vector4 positionData;
            Vector4 directionData;
            float customID;
            float angleData;
            int shadowmaskIndex;

            bool hasPositionData = TryReadVector4(serializedBlock, "PositionData", "_legacyPositionData", out positionData);
            bool hasDirectionData = TryReadVector4(serializedBlock, "DirectionData", "_legacyDirectionData", out directionData);
            bool hasCustomID = TryReadFloat(serializedBlock, "CustomID", "_legacyCustomID", out customID);
            bool hasAngleData = TryReadFloat(serializedBlock, "AngleData", "_legacyAngleData", out angleData);
            bool hasShadowmaskIndex = TryReadInt(serializedBlock, "ShadowmaskIndex", "_legacyShadowmaskIndex", out shadowmaskIndex);

            if (!hasPositionData) positionData = Vector4.zero;
            if (!hasDirectionData) directionData = Vector4.zero;
            if (!hasCustomID) customID = 0f;
            if (!hasAngleData) angleData = 0f;
            if (!hasShadowmaskIndex) shadowmaskIndex = -1;
            if (!HasLegacyPackedPointLightData(positionData, directionData, customID, angleData, shadowmaskIndex)) return false;
            if (HasCurrentPointLightData(serializedBlock)) return false;

            Undo.RecordObject(pointLight, UndoName);
            pointLight.Position = new Vector3(positionData.x, positionData.y, positionData.z);
            pointLight.LightType = GetLegacyPointLightType(positionData, angleData);
            pointLight.ProjectionMode = GetLegacyPointLightProjectionMode(customID);
            pointLight.ProjectionType = pointLight.ProjectionMode == 0 ? 0 : 1;
            pointLight.Angle = angleData > 1.5f ? pointLight.Angle : Mathf.Max(pointLight.Angle, 0f);

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
                } else {
                    pointLight.Rotation = LegacyVectorToQuaternion(directionData);
                }

                if (pointLight.LightType == 1 && pointLight.ProjectionMode == 2) pointLight.OuterAngleTan = angleData;
                else pointLight.OuterAngleCos = angleData;
            }

            pointLight.CustomTexture = null;
            pointLight.CustomTextureMaterial = null;
            pointLight.AutoUpdateCustomTexture = false;
            pointLight.ShadowMapID = -1f;
            pointLight.IsRangeDirty = true;
            MarkLegacyMigrated(pointLight);
            return true;
        }

        // Returns true when this exact component already received legacy data during the current loaded-scene session.
        private static bool WasLegacyMigrated(Component component) {
            object marker;
            if (component == null) return false;
            if (_legacyMigratedComponents.TryGetValue(component, out marker)) return true;
            if (!SessionState.GetBool(HasLegacyMigrationSessionKeys, false)) return false;

            string sessionKey;
            if (!TryGetLegacyMigrationSessionKey(component, out sessionKey) || !SessionState.GetBool(sessionKey, false)) return false;
            _legacyMigratedComponents.Add(component, _legacyMigrationMarker);
            return true;
        }

        // Remembers a migrated component without retaining destroyed objects and persists saved-scene identity across domain reloads.
        private static void MarkLegacyMigrated(Component component) {
            object marker;
            if (component == null || _legacyMigratedComponents.TryGetValue(component, out marker)) return;
            _legacyMigratedComponents.Add(component, _legacyMigrationMarker);

            string sessionKey;
            if (!TryGetLegacyMigrationSessionKey(component, out sessionKey)) return;
            SessionState.SetBool(HasLegacyMigrationSessionKeys, true);
            SessionState.SetBool(sessionKey, true);
        }

        // Builds a reload-stable key for one component while keeping separate scene load instances independent.
        private static bool TryGetLegacyMigrationSessionKey(Component component, out string sessionKey) {
            sessionKey = null;
            if (!ShouldSanitizeComponent(component)) return false;

            GlobalObjectId globalObjectId = GlobalObjectId.GetGlobalObjectIdSlow(component);
            if (globalObjectId.assetGUID.Equals(default(GUID)) || globalObjectId.targetObjectId == 0) return false;
            sessionKey = LegacyMigrationSessionKeyPrefix + component.gameObject.scene.handle + "." + globalObjectId;
            return true;
        }

        // Returns true when old packed point light values were present in serialized YAML.
        private static bool HasLegacyPackedPointLightData(Vector4 positionData, Vector4 directionData, float customID, float angleData, int shadowmaskIndex) {
            return positionData != Vector4.zero || directionData != Vector4.zero || customID != 0f || angleData != 0f || shadowmaskIndex >= 0;
        }

        // Returns true when the scene YAML already contains the explicit 3.x point light layout.
        private static bool HasCurrentPointLightData(string serializedBlock) {
            string line;
            return TryReadYamlLine(serializedBlock, "LightType", out line)
                || TryReadYamlLine(serializedBlock, "Position", out line)
                || TryReadYamlLine(serializedBlock, "InverseSquaredRange", out line)
                || TryReadYamlLine(serializedBlock, "Direction", out line)
                || TryReadYamlLine(serializedBlock, "Rotation", out line)
                || TryReadYamlLine(serializedBlock, "OuterAngleCos", out line)
                || TryReadYamlLine(serializedBlock, "ProjectionType", out line)
                || TryReadYamlLine(serializedBlock, "ProjectionMode", out line);
        }

        // Resolves the old packed type from PositionData.w and AngleData.
        private static int GetLegacyPointLightType(Vector4 positionData, float angleData) {
            if (positionData.w < 0f) return 1; // 1: spot
            if (angleData > 1.5f) return 2; // 2: area
            return 0; // 0: point
        }

        // Resolves the old packed projection mode from CustomID sign.
        private static int GetLegacyPointLightProjectionMode(float customID) {
            if (customID > 0f) return 1; // 1: LUT
            if (customID < 0f) return 2; // 2: custom cookie or cubemap
            return 0; // 0: parametric
        }

        // Converts a serialized Vector4 quaternion and falls back to identity when old data did not store rotation.
        private static Quaternion LegacyVectorToQuaternion(Vector4 value) {
            if (value == Vector4.zero) return Quaternion.identity;
            return new Quaternion(value.x, value.y, value.z, value.w);
        }

        // Finds this component's YAML document in the scene file so old unknown serialized fields can be read before Unity removes them on save.
        private static bool TryGetSceneObjectYamlBlock(Component component, out string serializedBlock) {
            serializedBlock = null;
            if (component == null) return false;
            Scene scene = component.gameObject.scene;
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path) || !File.Exists(scene.path)) return false;

            string sceneYaml;
            if (!_sceneYamlCache.TryGetValue(scene.path, out sceneYaml)) {
                sceneYaml = File.ReadAllText(scene.path);
                _sceneYamlCache.Add(scene.path, sceneYaml);
            }

            bool hasLegacyData;
            if (!_sceneHasLegacyDataCache.TryGetValue(scene.path, out hasLegacyData)) {
                hasLegacyData = ContainsLegacyRuntimeData(sceneYaml);
                _sceneHasLegacyDataCache.Add(scene.path, hasLegacyData);
            }
            if (!hasLegacyData) return false;

            GlobalObjectId globalObjectId = GlobalObjectId.GetGlobalObjectIdSlow(component);
            return TryExtractSceneObjectYamlBlock(sceneYaml, globalObjectId.targetObjectId, out serializedBlock);
        }

        // Rejects modern scene files before any per-component GlobalObjectId lookup or YAML document search.
        private static bool ContainsLegacyRuntimeData(string sceneYaml) {
            return sceneYaml.IndexOf("\n  RelativeRotation:", StringComparison.Ordinal) >= 0
                || sceneYaml.IndexOf("\n  _legacyRelativeRotation:", StringComparison.Ordinal) >= 0
                || sceneYaml.IndexOf("\n  BoundsUvwMax", StringComparison.Ordinal) >= 0
                || sceneYaml.IndexOf("\n  _legacyBoundsUvwMax", StringComparison.Ordinal) >= 0
                || sceneYaml.IndexOf("\n  PositionData:", StringComparison.Ordinal) >= 0
                || sceneYaml.IndexOf("\n  _legacyPositionData:", StringComparison.Ordinal) >= 0
                || sceneYaml.IndexOf("\n  DirectionData:", StringComparison.Ordinal) >= 0
                || sceneYaml.IndexOf("\n  _legacyDirectionData:", StringComparison.Ordinal) >= 0
                || sceneYaml.IndexOf("\n  CustomID:", StringComparison.Ordinal) >= 0
                || sceneYaml.IndexOf("\n  _legacyCustomID:", StringComparison.Ordinal) >= 0
                || sceneYaml.IndexOf("\n  AngleData:", StringComparison.Ordinal) >= 0
                || sceneYaml.IndexOf("\n  _legacyAngleData:", StringComparison.Ordinal) >= 0
                || sceneYaml.IndexOf("\n  ShadowmaskIndex:", StringComparison.Ordinal) >= 0
                || sceneYaml.IndexOf("\n  _legacyShadowmaskIndex:", StringComparison.Ordinal) >= 0;
        }

        // Extracts one MonoBehaviour YAML document while requiring an exact file ID match instead of a numeric prefix.
        private static bool TryExtractSceneObjectYamlBlock(string sceneYaml, ulong targetObjectId, out string serializedBlock) {
            serializedBlock = null;
            if (string.IsNullOrEmpty(sceneYaml)) return false;

            string marker = "--- !u!114 &" + targetObjectId;
            int start = 0;
            while (true) {
                start = sceneYaml.IndexOf(marker, start, StringComparison.Ordinal);
                if (start < 0) return false;

                int markerEnd = start + marker.Length;
                bool startsLine = start == 0 || sceneYaml[start - 1] == '\n';
                bool endsFileId = markerEnd == sceneYaml.Length || sceneYaml[markerEnd] == ' ' || sceneYaml[markerEnd] == '\r' || sceneYaml[markerEnd] == '\n';
                if (startsLine && endsFileId) break;
                start = markerEnd;
            }

            int lineEnd = sceneYaml.IndexOf('\n', start);
            if (lineEnd < 0) return false;

            int nextObject = sceneYaml.IndexOf("\n--- !u!", lineEnd + 1, StringComparison.Ordinal);
            serializedBlock = nextObject >= 0 ? sceneYaml.Substring(lineEnd + 1, nextObject - lineEnd - 1) : sceneYaml.Substring(lineEnd + 1);
            return true;
        }

        // Reads a one-line YAML Vector4 value.
        private static bool TryReadVector4(string serializedBlock, string fieldName, out Vector4 value) {
            value = Vector4.zero;
            string line;
            if (!TryReadYamlLine(serializedBlock, fieldName, out line)) return false;
            return TryReadYamlFloatComponent(line, "x:", out value.x)
                && TryReadYamlFloatComponent(line, "y:", out value.y)
                && TryReadYamlFloatComponent(line, "z:", out value.z)
                && TryReadYamlFloatComponent(line, "w:", out value.w);
        }

        // Reads a one-line YAML Vector4 value with a fallback name used by the previous editor-only migration attempt.
        private static bool TryReadVector4(string serializedBlock, string fieldName, string fallbackFieldName, out Vector4 value) {
            if (TryReadVector4(serializedBlock, fieldName, out value)) return true;
            return TryReadVector4(serializedBlock, fallbackFieldName, out value);
        }

        // Reads a one-line YAML float value.
        private static bool TryReadFloat(string serializedBlock, string fieldName, out float value) {
            value = 0f;
            string line;
            if (!TryReadYamlLine(serializedBlock, fieldName, out line)) return false;
            int colon = line.IndexOf(':');
            if (colon < 0 || colon + 1 >= line.Length) return false;
            return float.TryParse(line.Substring(colon + 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        // Reads a one-line YAML float value with a fallback name used by the previous editor-only migration attempt.
        private static bool TryReadFloat(string serializedBlock, string fieldName, string fallbackFieldName, out float value) {
            if (TryReadFloat(serializedBlock, fieldName, out value)) return true;
            return TryReadFloat(serializedBlock, fallbackFieldName, out value);
        }

        // Reads a one-line YAML integer value.
        private static bool TryReadInt(string serializedBlock, string fieldName, out int value) {
            value = 0;
            string line;
            if (!TryReadYamlLine(serializedBlock, fieldName, out line)) return false;
            int colon = line.IndexOf(':');
            if (colon < 0 || colon + 1 >= line.Length) return false;
            return int.TryParse(line.Substring(colon + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        // Reads a one-line YAML integer value with a fallback name used by the previous editor-only migration attempt.
        private static bool TryReadInt(string serializedBlock, string fieldName, string fallbackFieldName, out int value) {
            if (TryReadInt(serializedBlock, fieldName, out value)) return true;
            return TryReadInt(serializedBlock, fallbackFieldName, out value);
        }

        // Returns the full YAML line for a serialized field.
        private static bool TryReadYamlLine(string serializedBlock, string fieldName, out string line) {
            line = null;
            string prefix = "  " + fieldName + ":";
            int start = serializedBlock.StartsWith(prefix, StringComparison.Ordinal) ? 0 : serializedBlock.IndexOf("\n" + prefix, StringComparison.Ordinal);
            if (start < 0) return false;
            if (start > 0) start++;

            int end = serializedBlock.IndexOf('\n', start);
            line = end >= 0 ? serializedBlock.Substring(start, end - start) : serializedBlock.Substring(start);
            return true;
        }

        // Reads a named float component from a compact YAML vector line.
        private static bool TryReadYamlFloatComponent(string line, string key, out float value) {
            value = 0f;
            int keyIndex = line.IndexOf(key, StringComparison.Ordinal);
            if (keyIndex < 0) return false;

            int valueStart = keyIndex + key.Length;
            while (valueStart < line.Length && line[valueStart] == ' ') valueStart++;
            int valueEnd = valueStart;
            while (valueEnd < line.Length && line[valueEnd] != ',' && line[valueEnd] != '}') valueEnd++;
            if (valueEnd <= valueStart) return false;

            return float.TryParse(line.Substring(valueStart, valueEnd - valueStart), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        // Clears 2.x generated custom texture arrays that no longer match the 3.x RenderTexture runtime cache field.
        private static bool MigrateLegacyManagerRuntimeTextures(LightVolumeManager manager) {
            SerializedObject serializedManager = new SerializedObject(manager);
            SerializedProperty customTextures = serializedManager.FindProperty("CustomTextures");
            if (customTextures == null) return false;

            bool shouldClear = false;
            try {
                UnityEngine.Object reference = customTextures.objectReferenceValue;
                shouldClear = (reference != null && !(reference is RenderTexture)) || (reference == null && customTextures.objectReferenceInstanceIDValue != 0);
            } catch (MissingReferenceException) {
                shouldClear = true;
            }

            if (!shouldClear) return false;

            Undo.RecordObject(manager, UndoName);
            customTextures.objectReferenceValue = null;
            serializedManager.ApplyModifiedProperties();
            MarkObjectDirty(manager);
            return true;
        }

        // Re-syncs migrated runtime data once and keeps the request pending if synchronization throws.
        private static bool SyncAuthoringAfterMigrationIfNeeded() {
            if (!_needsAuthoringSyncAfterMigration) return false;
            SyncAuthoringComponentsToMigratedRuntime();
            _needsAuthoringSyncAfterMigration = false;
            return true;
        }

        // Re-syncs authoring MonoBehaviours into their runtime Udon components after legacy field migration.
        private static void SyncAuthoringComponentsToMigratedRuntime() {
            LightVolumeSetup[] setups = Resources.FindObjectsOfTypeAll<LightVolumeSetup>();
            for (int i = 0; i < setups.Length; i++) {
                LightVolumeSetup setup = setups[i];
                if (!ShouldSanitizeComponent(setup)) continue;
                setup.SetupDependencies();
                setup.RefreshVolumesList();
                MarkObjectDirty(setup);
            }
        }

        // Resolves one complete proxy/backing/program tuple and rejects transient UdonSharp restore states.
        private static bool TryGetReadyProxy(Component proxy, out Component backingUdonBehaviour, out UnityEngine.Object programSource) {
            backingUdonBehaviour = GetBackingUdonBehaviour(proxy);
            programSource = null;
            if (proxy == null || backingUdonBehaviour == null || backingUdonBehaviour.gameObject != proxy.gameObject) return false;

            programSource = GetProgramSource(backingUdonBehaviour);
            return programSource != null && GetProgramSourceType(programSource) == proxy.GetType();
        }

        // Resolves and caches the source C# type once per Udon program asset during a sanitizer pass.
        private static Type GetProgramSourceType(UnityEngine.Object programSource) {
            if (programSource == null) return null;

            int instanceId = programSource.GetInstanceID();
            Type sourceType;
            if (_programSourceTypeCache.TryGetValue(instanceId, out sourceType)) return sourceType;

            SerializedObject serializedProgramSource = new SerializedObject(programSource);
            SerializedProperty sourceScriptProperty = serializedProgramSource.FindProperty(SourceCsScriptPropertyName);
            MonoScript sourceScript = sourceScriptProperty != null ? sourceScriptProperty.objectReferenceValue as MonoScript : null;
            sourceType = sourceScript != null ? sourceScript.GetClass() : null;
            _programSourceTypeCache.Add(instanceId, sourceType);
            return sourceType;
        }

        // Removes every component except the selected keeper and keeps the matching hidden UdonBehaviour backing component intact
        private static int RemoveDuplicateComponents<T>(GameObject gameObject, T[] components, T keeper, Component keeperBackingUdonBehaviour, UnityEngine.Object keeperProgramSource) where T : Component {
            int removedCount = 0;
            Component[] allComponents = gameObject.GetComponents<Component>();

            for (int i = 0; i < components.Length; i++) {
                T duplicate = components[i];
                if (duplicate == null || duplicate == keeper) continue;

                ReplaceReferences(duplicate, keeper);

                Component duplicateBackingUdonBehaviour = GetBackingUdonBehaviour(duplicate);
                bool destroyDuplicateBacking = CanDestroyDuplicateBacking(gameObject, allComponents, duplicate, duplicateBackingUdonBehaviour, keeperBackingUdonBehaviour, keeperProgramSource);
                DetachBackingUdonBehaviour(duplicate);
                Undo.DestroyObjectImmediate(duplicate);
                removedCount++;

                if (destroyDuplicateBacking && duplicateBackingUdonBehaviour != null) {
                    Undo.DestroyObjectImmediate(duplicateBackingUdonBehaviour);
                    removedCount++;
                }
            }

            removedCount += RemoveExtraBackingUdonBehaviours(gameObject, keeperBackingUdonBehaviour, keeperProgramSource);
            return removedCount;
        }

        // Allows explicit backing deletion only for an exclusive local hidden UdonBehaviour of the keeper's program.
        private static bool CanDestroyDuplicateBacking(GameObject gameObject, Component[] components, Component duplicate, Component duplicateBackingUdonBehaviour, Component keeperBackingUdonBehaviour, UnityEngine.Object keeperProgramSource) {
            if (duplicateBackingUdonBehaviour == null || duplicateBackingUdonBehaviour == keeperBackingUdonBehaviour) return false;
            if (duplicateBackingUdonBehaviour.gameObject != gameObject || EditorUtility.IsPersistent(duplicateBackingUdonBehaviour)) return false;
            if (!IsUdonBehaviour(duplicateBackingUdonBehaviour) || (duplicateBackingUdonBehaviour.hideFlags & HideFlags.HideInInspector) == 0) return false;
            if (GetProgramSource(duplicateBackingUdonBehaviour) != keeperProgramSource) return false;
            return !IsBackingOwnedByProxy(components, duplicateBackingUdonBehaviour, duplicate);
        }

        // Detaches the serialized backing reference so UdonSharp destruction callbacks cannot delete a shared or foreign backing.
        private static void DetachBackingUdonBehaviour(Component proxy) {
            Component backingUdonBehaviour = GetBackingUdonBehaviour(proxy);
            if (backingUdonBehaviour == null) return;

            FieldInfo field = GetBackingUdonBehaviourField(proxy.GetType());
            if (field == null) return;
            Undo.RegisterCompleteObjectUndo(proxy, UndoName);
            field.SetValue(proxy, null);
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
                if (!ContainsReference(manager.LightVolumeInstances, duplicate)) continue;

                Undo.RecordObject(manager, UndoName);
                manager.LightVolumeInstances = ReplaceAndDeduplicateReferences(manager.LightVolumeInstances, duplicate, keeper);
                MarkObjectDirty(manager);
            }

            LightVolumeSetup[] setups = Resources.FindObjectsOfTypeAll<LightVolumeSetup>();
            for (int i = 0; i < setups.Length; i++) {
                LightVolumeSetup setup = setups[i];
                if (!ShouldSanitizeComponent(setup) || setup.LightVolumeDataList == null) continue;

                bool changed = false;
                bool hasExistingKeeper = false;
                for (int j = 0; j < setup.LightVolumeDataList.Count; j++) {
                    if (setup.LightVolumeDataList[j].LightVolumeInstance == keeper) {
                        hasExistingKeeper = true;
                        break;
                    }
                }

                bool keeperAdded = false;
                for (int j = 0; j < setup.LightVolumeDataList.Count; j++) {
                    LightVolumeData data = setup.LightVolumeDataList[j];
                    bool isDuplicate = data.LightVolumeInstance == duplicate;
                    bool isKeeper = data.LightVolumeInstance == keeper;
                    if (!isDuplicate && !isKeeper) continue;
                    if (isKeeper && !keeperAdded) {
                        keeperAdded = true;
                        continue;
                    }
                    if (!changed) Undo.RecordObject(setup, UndoName);
                    if (hasExistingKeeper || keeperAdded) {
                        setup.LightVolumeDataList.RemoveAt(j);
                        j--;
                        changed = true;
                        continue;
                    }
                    data.LightVolumeInstance = keeper;
                    setup.LightVolumeDataList[j] = data;
                    keeperAdded = true;
                    changed = true;
                }
                if (changed) MarkObjectDirty(setup);
            }

            LightVolumeAudioLink[] audioLinks = Resources.FindObjectsOfTypeAll<LightVolumeAudioLink>();
            for (int i = 0; i < audioLinks.Length; i++) {
                LightVolumeAudioLink audioLink = audioLinks[i];
                if (!ShouldSanitizeComponent(audioLink) || audioLink.TargetLightVolumes == null || !ContainsReference(audioLink.TargetLightVolumes, duplicate)) continue;
                Undo.RecordObject(audioLink, UndoName);
                audioLink.TargetLightVolumes = ReplaceAndDeduplicateReferences(audioLink.TargetLightVolumes, duplicate, keeper);
                MarkObjectDirty(audioLink);
            }

            LightVolumeTVGI[] tvgiComponents = Resources.FindObjectsOfTypeAll<LightVolumeTVGI>();
            for (int i = 0; i < tvgiComponents.Length; i++) {
                LightVolumeTVGI tvgi = tvgiComponents[i];
                if (!ShouldSanitizeComponent(tvgi) || tvgi.TargetLightVolumes == null || !ContainsReference(tvgi.TargetLightVolumes, duplicate)) continue;
                Undo.RecordObject(tvgi, UndoName);
                tvgi.TargetLightVolumes = ReplaceAndDeduplicateReferences(tvgi.TargetLightVolumes, duplicate, keeper);
                MarkObjectDirty(tvgi);
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
                if (!ContainsReference(manager.PointLightVolumeInstances, duplicate)) continue;

                Undo.RecordObject(manager, UndoName);
                manager.PointLightVolumeInstances = ReplaceAndDeduplicateReferences(manager.PointLightVolumeInstances, duplicate, keeper);
                MarkObjectDirty(manager);
            }

            LightVolumeAudioLink[] audioLinks = Resources.FindObjectsOfTypeAll<LightVolumeAudioLink>();
            for (int i = 0; i < audioLinks.Length; i++) {
                LightVolumeAudioLink audioLink = audioLinks[i];
                if (!ShouldSanitizeComponent(audioLink) || audioLink.TargetPointLightVolumes == null || !ContainsReference(audioLink.TargetPointLightVolumes, duplicate)) continue;
                Undo.RecordObject(audioLink, UndoName);
                audioLink.TargetPointLightVolumes = ReplaceAndDeduplicateReferences(audioLink.TargetPointLightVolumes, duplicate, keeper);
                MarkObjectDirty(audioLink);
            }

            LightVolumeTVGI[] tvgiComponents = Resources.FindObjectsOfTypeAll<LightVolumeTVGI>();
            for (int i = 0; i < tvgiComponents.Length; i++) {
                LightVolumeTVGI tvgi = tvgiComponents[i];
                if (!ShouldSanitizeComponent(tvgi) || tvgi.TargetPointLightVolumes == null || !ContainsReference(tvgi.TargetPointLightVolumes, duplicate)) continue;
                Undo.RecordObject(tvgi, UndoName);
                tvgi.TargetPointLightVolumes = ReplaceAndDeduplicateReferences(tvgi.TargetPointLightVolumes, duplicate, keeper);
                MarkObjectDirty(tvgi);
            }

            PointLightShadowRuntimeBaker[] shadowBakers = Resources.FindObjectsOfTypeAll<PointLightShadowRuntimeBaker>();
            for (int i = 0; i < shadowBakers.Length; i++) {
                PointLightShadowRuntimeBaker shadowBaker = shadowBakers[i];
                if (!ShouldSanitizeComponent(shadowBaker) || shadowBaker.TargetPointLightVolume != duplicate) continue;
                Undo.RecordObject(shadowBaker, UndoName);
                shadowBaker.TargetPointLightVolume = keeper;
                MarkObjectDirty(shadowBaker);
            }
        }

        // Returns true when an object-reference array contains the requested component.
        private static bool ContainsReference<T>(T[] references, T target) where T : UnityEngine.Object {
            for (int i = 0; i < references.Length; i++) {
                if (references[i] == target) return true;
            }
            return false;
        }

        // Preserves an existing keeper's registry position, or replaces only the first duplicate when no keeper exists.
        private static T[] ReplaceAndDeduplicateReferences<T>(T[] references, T duplicate, T keeper) where T : UnityEngine.Object {
            int writeIndex = 0;
            int existingKeeperIndex = -1;
            for (int i = 0; i < references.Length; i++) {
                if (references[i] != keeper) continue;
                existingKeeperIndex = i;
                break;
            }
            bool keeperAdded = false;

            for (int i = 0; i < references.Length; i++) {
                T reference = references[i];
                if (reference == duplicate || reference == keeper) {
                    if (existingKeeperIndex >= 0 && i != existingKeeperIndex) continue;
                    if (keeperAdded) continue;
                    reference = keeper;
                    keeperAdded = true;
                }
                references[writeIndex++] = reference;
            }

            if (writeIndex == references.Length) return references;
            T[] compactedReferences = new T[writeIndex];
            Array.Copy(references, compactedReferences, writeIndex);
            return compactedReferences;
        }

        // Removes orphaned or duplicated hidden UdonBehaviour components with the same program source as the kept proxy
        private static int RemoveExtraBackingUdonBehaviours(GameObject gameObject, Component keeperBackingUdonBehaviour, UnityEngine.Object keeperProgramSource) {
            int removedCount = 0;
            Component[] components = gameObject.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++) {
                Component component = components[i];
                if (component == null || component == keeperBackingUdonBehaviour || !IsUdonBehaviour(component)) continue;
                if ((component.hideFlags & HideFlags.HideInInspector) == 0) continue;
                if (GetProgramSource(component) != keeperProgramSource) continue;
                if (IsBackingOwnedByProxy(components, component, null)) continue;

                Undo.DestroyObjectImmediate(component);
                removedCount++;
            }

            return removedCount;
        }

        // Returns true when a hidden UdonBehaviour is still explicitly paired with any live UdonSharp proxy on this object.
        private static bool IsBackingOwnedByProxy(Component[] components, Component backingUdonBehaviour, Component excludedProxy) {
            for (int i = 0; i < components.Length; i++) {
                Component component = components[i];
                if (component == null || ReferenceEquals(component, excludedProxy)) continue;
                FieldInfo field = GetBackingUdonBehaviourField(component.GetType());
                if (field == null || field.DeclaringType == null || !field.DeclaringType.IsAssignableFrom(component.GetType())) continue;
                if (field.GetValue(component) as Component == backingUdonBehaviour) return true;
            }
            return false;
        }

        // Returns the hidden UdonBehaviour assigned to a UdonSharp proxy component
        private static Component GetBackingUdonBehaviour(Component component) {
            if (component == null) return null;

            FieldInfo field = GetBackingUdonBehaviourField(component.GetType());
            if (field == null || field.DeclaringType == null || !field.DeclaringType.IsAssignableFrom(component.GetType())) return null;

            return field.GetValue(component) as Component;
        }

        // Finds the private UdonSharp backing field without a hard dependency on UdonSharp.Editor
        private static FieldInfo GetBackingUdonBehaviourField(Type componentType) {
            FieldInfo cachedField;
            if (_backingUdonBehaviourFields.TryGetValue(componentType, out cachedField)) return cachedField;

            Type type = componentType;
            FieldInfo backingField = null;
            while (type != null) {
                FieldInfo field = type.GetField(BackingUdonBehaviourFieldName, BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null) {
                    backingField = field;
                    break;
                }
                type = type.BaseType;
            }

            _backingUdonBehaviourFields.Add(componentType, backingField);
            return backingField;
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
            if (!gameObject.scene.IsValid() || !gameObject.scene.isLoaded) return false;
            if (EditorSceneManager.IsPreviewSceneObject(gameObject)) {
                var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
                if (prefabStage == null || prefabStage.scene != gameObject.scene) return false;
            }
            return (gameObject.hideFlags & HideFlags.DontSaveInEditor) == 0;
        }

        // Returns true when this component belongs to a loaded scene object
        private static bool ShouldSanitizeComponent(Component component) {
            return component != null && ShouldSanitizeGameObject(component.gameObject);
        }

        // Returns the best keeper candidate from duplicated components using Udon health first and authoring references only as a tie-breaker
        private static T GetBestKeeper<T>(T[] components, T preferred) where T : Component {
            if (components.Length == 1) return components[0];

            T best = null;
            int bestScore = -1;
            bool preserveMigratedData = false;

            for (int i = 0; i < components.Length; i++) {
                if (!WasLegacyMigrated(components[i])) continue;
                preserveMigratedData = true;
                break;
            }

            for (int i = 0; i < components.Length; i++) {
                T component = components[i];
                if (component == null) continue;
                if (preserveMigratedData && !WasLegacyMigrated(component)) continue;

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
            UnityEngine.Object programSource = hasLocalBacking ? GetProgramSource(backingUdonBehaviour) : null;
            bool isReady = programSource != null && GetProgramSourceType(programSource) == component.GetType();
            if (isReady) score += 100000; // Best signal that the UdonSharp proxy is wired to its resolved Udon program
            else if (hasLocalBacking) score += 10000; // Still better than a proxy with no backing UdonBehaviour at all

            score += GetRuntimeDataScore(component) * 100;
            if (isPreferred) score += 10; // Authoring references can be stale after duplication, so they only break close ties
            if (hasLocalBacking && IsBackingImmediatelyAfterProxy(component, backingUdonBehaviour)) score += 1; // Component order is only a weak tie-breaker on prefab instances

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
                SerializedObject serializedManager = new SerializedObject(manager);
                int score = 0;
                if (SafeHasSerializedObjectReference(serializedManager.FindProperty(nameof(LightVolumeManager.LightVolumeAtlas)), null) || SafeHasSerializedObjectReference(serializedManager.FindProperty(nameof(LightVolumeManager.LightVolumeAtlasBase)), typeof(Texture3D))) score += 4;
                if (SafeHasSerializedObjectReference(serializedManager.FindProperty(nameof(LightVolumeManager.CustomTextures)), typeof(RenderTexture))) score += 2;
                if (SafeHasSerializedObjectReference(serializedManager.FindProperty(nameof(LightVolumeManager.ShadowTextures)), typeof(RenderTexture))) score += 2;
                if (SafeSerializedArraySize(serializedManager.FindProperty(nameof(LightVolumeManager.LightVolumeInstances))) > 0) score += 1;
                if (SafeSerializedArraySize(serializedManager.FindProperty(nameof(LightVolumeManager.PointLightVolumeInstances))) > 0) score += 1;
                return score;
            }

            LightVolumeInstance lightVolume = component as LightVolumeInstance;
            if (lightVolume != null) {
                int score = lightVolume.LightVolumeManager != null ? 1 : 0;
                if (lightVolume.BoundsUvwMin0 != Vector4.zero || lightVolume.BoundsUvwMin1 != Vector4.zero || lightVolume.BoundsUvwMin2 != Vector4.zero) score += 4;
                if (lightVolume.InvLocalEdgeSmoothing != Vector4.zero) score += 2;
                if (lightVolume.InvWorldMatrix != Matrix4x4.identity || lightVolume.RelativeRotationRow0 != Vector3.zero || lightVolume.RelativeRotationRow1 != Vector3.zero) score += 2;
                if (lightVolume.IsDynamic || lightVolume.IsAdditive || lightVolume.Color != Color.white || lightVolume.Intensity != 1f || lightVolume.RegistryWeight != 0f) score++;
                return score;
            }

            PointLightVolumeInstance pointLight = component as PointLightVolumeInstance;
            if (pointLight != null) {
                int score = pointLight.LightVolumeManager != null ? 1 : 0;
                if (pointLight.Position != Vector3.zero || pointLight.LightType != 0) score += 2;
                if (pointLight.ProjectionMode != 0 || pointLight.ProjectionType != 0 || pointLight.CustomTexture != null || pointLight.CustomTextureMaterial != null) score += 4;
                if (pointLight.ShadowMapID >= 0f || pointLight.ShadowMapTexture != null || pointLight.ShadowMapMaterial != null) score += 4;
                if (pointLight.Direction != Vector3.forward || pointLight.Rotation != Quaternion.identity || pointLight.LightSourceSize != 0.025f || pointLight.Width != 1f || pointLight.Height != 1f) score += 2;
                if (pointLight.Color != Color.white || pointLight.Intensity != 100f || pointLight.ShadingStrength != 1f || pointLight.RegistryWeight != 0f) score++;
                if (pointLight.BakeInGame) score += 2;
                return score;
            }

            return 0;
        }

        // Reads object references through SerializedObject so stale UdonSharp proxy variables cannot throw during migration.
        private static bool SafeHasSerializedObjectReference(SerializedProperty property, Type expectedType) {
            if (property == null) return false;

            try {
                UnityEngine.Object reference = property.objectReferenceValue;
                return reference != null && (expectedType == null || expectedType.IsInstanceOfType(reference));
            } catch (MissingReferenceException) {
                return false;
            }
        }

        // Reads serialized array size without touching UdonSharp proxy fields.
        private static int SafeSerializedArraySize(SerializedProperty property) {
            if (property == null || !property.isArray) return 0;
            return property.arraySize;
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
