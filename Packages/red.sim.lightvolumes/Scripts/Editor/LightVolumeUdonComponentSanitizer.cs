using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
        private static readonly Dictionary<string, string> _sceneYamlCache = new Dictionary<string, string>();
        private static readonly HashSet<int> _migratedLegacyPointLightInstanceIds = new HashSet<int>();

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
                _sceneYamlCache.Clear();
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

            LightVolumeManager[] managers = gameObject.GetComponents<LightVolumeManager>();
            for (int i = 0; i < managers.Length; i++) {
                LightVolumeManager manager = managers[i];
                if (manager == null) continue;
                if (MigrateLegacyManagerRuntimeTextures(manager)) migrated = true;
            }

            LightVolumeInstance[] lightVolumes = gameObject.GetComponents<LightVolumeInstance>();
            for (int i = 0; i < lightVolumes.Length; i++) {
                LightVolumeInstance lightVolume = lightVolumes[i];
                if (lightVolume == null) continue;
                if (!MigrateLegacyLightVolumeData(lightVolume)) continue;
                MarkObjectDirty(lightVolume);
                migrated = true;
            }

            PointLightVolumeInstance[] pointLights = gameObject.GetComponents<PointLightVolumeInstance>();
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
            bool changed = false;

            Vector4 legacyRelativeRotation;
            if (lightVolume.RelativeRotationRow0 == Vector3.zero && lightVolume.RelativeRotationRow1 == Vector3.zero && TryReadVector4(serializedBlock, "RelativeRotation", "_legacyRelativeRotation", out legacyRelativeRotation) && legacyRelativeRotation != new Vector4(0, 0, 0, 1) && legacyRelativeRotation != Vector4.zero) {
                Undo.RecordObject(lightVolume, UndoName);
                Quaternion relativeRotation = new Quaternion(legacyRelativeRotation.x, legacyRelativeRotation.y, legacyRelativeRotation.z, legacyRelativeRotation.w);
                Matrix4x4 rotationMatrix = Matrix4x4.Rotate(relativeRotation);
                lightVolume.RelativeRotationRow0 = rotationMatrix.GetRow(0);
                lightVolume.RelativeRotationRow1 = rotationMatrix.GetRow(1);
                lightVolume.IsRotated = Quaternion.Dot(relativeRotation, Quaternion.identity) < 0.999999f;
                changed = true;
            }

            Vector4 legacyBoundsUvwMax;
            if (TryReadVector4(serializedBlock, "BoundsUvwMax0", "_legacyBoundsUvwMax0", out legacyBoundsUvwMax) && MigrateLegacyBoundsScale(ref lightVolume.BoundsUvwMin0, legacyBoundsUvwMax, 0, lightVolume, changed)) changed = true;
            if (TryReadVector4(serializedBlock, "BoundsUvwMax1", "_legacyBoundsUvwMax1", out legacyBoundsUvwMax) && MigrateLegacyBoundsScale(ref lightVolume.BoundsUvwMin1, legacyBoundsUvwMax, 1, lightVolume, changed)) changed = true;
            if (TryReadVector4(serializedBlock, "BoundsUvwMax2", "_legacyBoundsUvwMax2", out legacyBoundsUvwMax) && MigrateLegacyBoundsScale(ref lightVolume.BoundsUvwMin2, legacyBoundsUvwMax, 2, lightVolume, changed)) changed = true;

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
            if (_migratedLegacyPointLightInstanceIds.Contains(pointLight.GetInstanceID())) return false;

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
            _migratedLegacyPointLightInstanceIds.Add(pointLight.GetInstanceID());
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

            GlobalObjectId globalObjectId = GlobalObjectId.GetGlobalObjectIdSlow(component);
            string marker = "--- !u!114 &" + globalObjectId.targetObjectId;
            int start = sceneYaml.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return false;

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

        // Re-syncs authoring MonoBehaviours into their runtime Udon components after legacy field migration.
        private static void SyncAuthoringComponentsToMigratedRuntime() {
            LightVolumeSetup[] setups = Resources.FindObjectsOfTypeAll<LightVolumeSetup>();
            for (int i = 0; i < setups.Length; i++) {
                LightVolumeSetup setup = setups[i];
                if (!ShouldSanitizeComponent(setup)) continue;
                setup.SetupDependencies();
                setup.RefreshVolumesList();
                SyncPointLightAuthoringComponents(setup);
                setup.SyncUdonScript();
                MarkObjectDirty(setup);
            }
        }

        // Copies authoring point light texture and shadow sources before the setup rebuilds manager runtime arrays.
        private static void SyncPointLightAuthoringComponents(LightVolumeSetup setup) {
            int count = setup.PointLightVolumes.Count;
            for (int i = 0; i < count; i++) {
                PointLightVolume pointLight = setup.PointLightVolumes[i];
                if (pointLight != null) pointLight.SyncUdonScript();
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
                if (SafeHasSerializedObjectReference(manager, "LightVolumeAtlas", null) || SafeHasSerializedObjectReference(manager, "LightVolumeAtlasBase", typeof(Texture3D))) score += 4;
                if (SafeHasSerializedObjectReference(manager, "CustomTextures", typeof(RenderTexture))) score += 2;
                if (SafeHasSerializedObjectReference(manager, "ShadowTextures", typeof(RenderTexture))) score += 2;
                if (SafeSerializedArraySize(manager, "LightVolumeInstances") > 0) score += 1;
                if (SafeSerializedArraySize(manager, "PointLightVolumeInstances") > 0) score += 1;
                return score;
            }

            LightVolumeInstance lightVolume = component as LightVolumeInstance;
            if (lightVolume != null) return lightVolume.LightVolumeManager != null ? 1 : 0;

            PointLightVolumeInstance pointLight = component as PointLightVolumeInstance;
            if (pointLight != null) return pointLight.LightVolumeManager != null ? 1 : 0;

            return 0;
        }

        // Reads object references through SerializedObject so stale UdonSharp proxy variables cannot throw during migration.
        private static bool SafeHasSerializedObjectReference(UnityEngine.Object target, string propertyName, Type expectedType) {
            SerializedObject serializedTarget = new SerializedObject(target);
            SerializedProperty property = serializedTarget.FindProperty(propertyName);
            if (property == null) return false;

            try {
                UnityEngine.Object reference = property.objectReferenceValue;
                return reference != null && (expectedType == null || expectedType.IsInstanceOfType(reference));
            } catch (MissingReferenceException) {
                return false;
            }
        }

        // Reads serialized array size without touching UdonSharp proxy fields.
        private static int SafeSerializedArraySize(UnityEngine.Object target, string propertyName) {
            SerializedObject serializedTarget = new SerializedObject(target);
            SerializedProperty property = serializedTarget.FindProperty(propertyName);
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
