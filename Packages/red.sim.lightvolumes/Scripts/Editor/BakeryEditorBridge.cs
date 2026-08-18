using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VRCLightVolumes {
    // Keeps Bakery optional without a compile-time assembly reference or global scripting define.
    // Bakery is an Asset Store integration rather than a versioned UPM dependency, so current asmdefs and legacy default assemblies are resolved at editor load.
    internal static class BakeryEditorBridge {
        private const BindingFlags StaticFields = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        internal enum ProbeRenderMode {
            None,
            L1,
            L2
        }

        private static readonly Type BakeryVolumeType = ResolveType("BakeryVolume", "BakeryRuntimeAssembly", "Assembly-CSharp");
        private static readonly Type BakeryGroupType = ResolveType("BakeryLightmapGroup", "BakeryRuntimeAssembly", "Assembly-CSharp");
        private static readonly Type BakeryStorageType = ResolveType("ftLightmapsStorage", "BakeryRuntimeAssembly", "Assembly-CSharp");
        private static readonly Type BakeryRendererType = ResolveType("ftRenderLightmap", "BakeryEditorAssembly", "Assembly-CSharp-Editor");
        private static readonly Type BakeryBuildGraphicsType = ResolveType("ftBuildGraphics", "BakeryEditorAssembly", "Assembly-CSharp-Editor");

        private static readonly EventInfo PreFullRenderEvent = BakeryRendererType?.GetEvent("OnPreFullRender", StaticFields);
        private static readonly EventInfo FinishedRenderEvent = BakeryRendererType?.GetEvent("OnFinishedFullRender", StaticFields);
        private static readonly EventInfo FinishedProbesEvent = BakeryRendererType?.GetEvent("OnFinishedProbes", StaticFields);
        private static readonly FieldInfo BakeInProgressField = BakeryRendererType?.GetField("bakeInProgress", StaticFields);
        private static readonly FieldInfo LightProbeModeField = BakeryRendererType?.GetField("lightProbeMode", StaticFields);
        private static readonly FieldInfo HasAnyProbesField = BakeryRendererType?.GetField("hasAnyProbes", StaticFields);
        private static readonly FieldInfo ApvField = BakeryRendererType?.GetField("apv", StaticFields);
        private static readonly FieldInfo FullSectorRenderField = BakeryRendererType?.GetField("fullSectorRender", StaticFields);
        private static readonly FieldInfo CurrentSectorField = BakeryRendererType?.GetField("curSector", StaticFields);
        private static readonly FieldInfo ProbesOnlyField = BakeryRendererType?.GetField("probesOnlyL1", InstanceFields);
        private static readonly FieldInfo SectorBakesChildProbesField = CurrentSectorField?.FieldType.GetField("bakeChildLightProbeGroups", InstanceFields);
        private static readonly FieldInfo LightProbeGroupField = BakeryBuildGraphicsType?.GetField("lightProbeLMGroup", StaticFields);
        private static readonly FieldInfo VolumeGroupField = BakeryBuildGraphicsType?.GetField("volumeLMGroup", StaticFields);
        private static readonly FieldInfo ImplicitGroupsField = BakeryStorageType?.GetField("implicitGroups", InstanceFields);
        private static readonly FieldInfo GroupBitmaskField = BakeryGroupType?.GetField("bitmask", InstanceFields);
        private static readonly FieldInfo FullRotationField = BakeryVolumeType?.GetField("_rotateAroundXYZ", InstanceFields);
        private static readonly FieldInfo YRotationField = BakeryVolumeType?.GetField("rotateAroundY", InstanceFields);
        private static readonly FieldInfo BakedTexture0Field = BakeryVolumeType?.GetField("bakedTexture0", InstanceFields);
        private static readonly FieldInfo BakedTexture1Field = BakeryVolumeType?.GetField("bakedTexture1", InstanceFields);
        private static readonly FieldInfo BakedTexture2Field = BakeryVolumeType?.GetField("bakedTexture2", InstanceFields);

        // Indicates whether Bakery integration is available.
        internal static bool IsAvailable => BakeryVolumeType != null && BakeryRendererType != null;

        // Distinguishes a missing asset from an installed version with an incompatible core schema.
        internal static bool IsInstalled => BakeryVolumeType != null || BakeryRendererType != null;

        // Indicates whether Bakery's stored and live implicit groups can accept bitmask overrides.
        internal static bool SupportsRuntimeBitmasks => IsAvailable
            && LightProbeGroupField != null
            && VolumeGroupField != null
            && ImplicitGroupsField != null
            && GroupBitmaskField?.FieldType == typeof(int);

        // Indicates whether this Bakery version exposes the dedicated full-render lifecycle used for safe finalization.
        internal static bool SupportsFullRenderLifecycle => IsAvailable
            && PreFullRenderEvent?.EventHandlerType == typeof(EventHandler)
            && FinishedRenderEvent?.EventHandlerType == typeof(EventHandler)
            && BakeInProgressField != null;

        // Checks whether Bakery volumes support full XYZ rotation.
        internal static bool SupportsFullRotation => FullRotationField != null;

        // Checks whether Bakery volumes support Y-axis rotation.
        internal static bool SupportsYRotation => YRotationField != null;

        // Whether a Bakery bake operation is currently running.
        internal static bool IsBaking => ReadStaticBool(BakeInProgressField);

        // Registers the exact lifecycle events exposed by supported Bakery releases.
        internal static void SetLifecycleCallbacks(EventHandler started, EventHandler finished, EventHandler probesFinished, bool subscribe) {
            if (SupportsFullRenderLifecycle) SetSubscription(PreFullRenderEvent, started, subscribe);
            SetSubscription(FinishedRenderEvent, finished, subscribe);
            SetSubscription(FinishedProbesEvent, probesFinished, subscribe);
        }

        // Checks whether an L1/L2 full-render completion came from the dedicated Light Probe command.
        internal static bool IsProbeOnlyRender(object renderer) {
            return IsRendererInstance(renderer) && ReadInstanceBool(renderer, ProbesOnlyField);
        }

        // Identifies a completed classic probe render and preserves Bakery's authoritative L1/L2 mode.
        internal static ProbeRenderMode GetCompletedProbeRenderMode(object renderer) {
            if (!IsRendererInstance(renderer) || LightProbeModeField?.FieldType.IsEnum != true || HasAnyProbesField?.FieldType != typeof(bool)) return ProbeRenderMode.None;
            bool fullSectorRender = ReadStaticBool(FullSectorRenderField);
            bool sectorIncludesProbes = !fullSectorRender;
            if (fullSectorRender) {
                object sector = ReadField(CurrentSectorField, null);
                sectorIncludesProbes = sector != null && ReadInstanceBool(sector, SectorBakesChildProbesField);
            }

            return ClassifyProbeRender(
                ReadField(LightProbeModeField, null)?.ToString(),
                !SupportedRenderingFeatures.active.overridesLightProbeSystem,
                ReadStaticBool(HasAnyProbesField),
                ReadStaticBool(ApvField),
                fullSectorRender,
                sectorIncludesProbes);
        }

        internal static ProbeRenderMode ClassifyProbeRender(string mode, bool supportsClassicProbes, bool hasAnyProbes,
            bool apv, bool fullSectorRender, bool sectorIncludesProbes) {
            if (!supportsClassicProbes || !hasAnyProbes || apv || fullSectorRender && !sectorIncludesProbes) return ProbeRenderMode.None;
            if (mode == "L1") return ProbeRenderMode.L1;
            return mode == "L2" ? ProbeRenderMode.L2 : ProbeRenderMode.None;
        }

        // Synchronizes the Bakery helper component for a light volume.
        internal static void SetupVolume(LightVolumeInstance volume, bool createIfMissing) {
            if (!IsAvailable || volume == null || volume.LightVolumeManager == null) return;

            LightVolumeManager manager = volume.LightVolumeManager;
            if (!TryFindOwnedVolume(volume, out Component bakeryVolume)) return;
            if (manager.EditorIsBakeryMode && volume.Bake && bakeryVolume == null) {
                if (!createIfMissing) return;
                GameObject helper = new GameObject($"Bakery Volume - {volume.gameObject.name}") { tag = "EditorOnly" };
                try {
                    Undo.RegisterCreatedObjectUndo(helper, "Create Bakery Volume");
                    helper.transform.SetParent(volume.transform, false);
                    bakeryVolume = helper.AddComponent(BakeryVolumeType);
                    if (bakeryVolume == null) throw new InvalidOperationException("Bakery Volume component creation returned null.");
                } catch {
                    if (helper != null) UnityEngine.Object.DestroyImmediate(helper);
                    throw;
                }
            } else if ((!manager.EditorIsBakeryMode || !volume.Bake) && bakeryVolume != null) {
                GameObject helper = bakeryVolume.gameObject;
                bool inheritedPrefabObject = PrefabUtility.IsPartOfPrefabInstance(helper) && PrefabUtility.GetCorrespondingObjectFromSource(helper) != null;
                UnityEngine.Object target = inheritedPrefabObject ? bakeryVolume : helper;
                Undo.DestroyObjectImmediate(target);
                return;
            }

            if (!manager.EditorIsBakeryMode || bakeryVolume == null) return;

            bakeryVolume.gameObject.name = $"Bakery Volume - {volume.gameObject.name}";
            bakeryVolume.gameObject.tag = "EditorOnly";
            if ((bakeryVolume.gameObject.hideFlags & HideFlags.HideInHierarchy) == 0) bakeryVolume.gameObject.hideFlags |= HideFlags.HideInHierarchy;
            if ((bakeryVolume.hideFlags & HideFlags.HideInInspector) == 0) bakeryVolume.hideFlags |= HideFlags.HideInInspector;
            if (bakeryVolume.transform.parent != volume.transform) Undo.SetTransformParent(bakeryVolume.transform, volume.transform, "Parent Bakery Volume");
            bakeryVolume.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            bakeryVolume.transform.localScale = Vector3.one;

            using (SerializedObject serialized = new SerializedObject(bakeryVolume)) {
                SetBounds(serialized, "bounds", new Bounds(LightVolumeTools.GetPosition(volume), LightVolumeTools.GetScale(volume)));
                SetBool(serialized, "enableBaking", true);
                SetBool(serialized, "denoise", manager.Denoise);
                SetBool(serialized, "adaptiveRes", false);
                SetInt(serialized, "resolutionX", volume.Resolution.x);
                SetInt(serialized, "resolutionY", volume.Resolution.y);
                SetInt(serialized, "resolutionZ", volume.Resolution.z);
                SetEnum(serialized, "encoding", 0);
                if (SupportsFullRotation) {
                    SetBool(serialized, "_rotateAroundXYZ", true);
                    SetBool(serialized, "rotateAroundY", false);
                } else if (SupportsYRotation) {
                    SetBool(serialized, "rotateAroundY", true);
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            LVUtils.MarkDirty(bakeryVolume);
        }

        // Imports baked Bakery textures into a light volume when needed.
        internal static bool TryImportTextures(LightVolumeInstance volume) {
            if (!IsAvailable || volume == null || !volume.Bake || !TryFindOwnedVolume(volume, out Component bakeryVolume)) return false;
            if (bakeryVolume == null) return false;

            Texture3D texture0 = ReadTexture3D(bakeryVolume, BakedTexture0Field);
            Texture3D texture1 = ReadTexture3D(bakeryVolume, BakedTexture1Field);
            Texture3D texture2 = ReadTexture3D(bakeryVolume, BakedTexture2Field);
            if (texture0 == null || volume.Texture0 == texture0 && volume.Texture1 == texture1 && volume.Texture2 == texture2) return false;

            volume.Texture0 = texture0;
            volume.Texture1 = texture1;
            volume.Texture2 = texture2;
            LVUtils.MarkDirty(volume);
            return true;
        }

        // Finds a Bakery helper owned directly by the provided light volume.
        internal static bool TryFindOwnedVolume(LightVolumeInstance volume, out Component bakeryVolume) {
            bakeryVolume = null;
            if (BakeryVolumeType == null || volume == null) return false;

            Component[] candidates = volume.GetComponentsInChildren(BakeryVolumeType, true);
            for (int i = 0; i < candidates.Length; i++) {
                Component candidate = candidates[i];
                if (candidate == null || candidate.transform.parent != volume.transform) continue;
                if (bakeryVolume == null) {
                    bakeryVolume = candidate;
                    continue;
                }

                Debug.LogWarning($"[LightVolumes] Multiple direct Bakery Volume helpers found on {volume.gameObject.name}. Automatic Bakery setup was skipped.", volume);
                bakeryVolume = null;
                return false;
            }
            return true;
        }

        // Clears Bakery implicit probe and volume group references.
        internal static void ClearImplicitProbeGroups() {
            SetField(LightProbeGroupField, null, null);
            SetField(VolumeGroupField, null, null);
        }

        // Updates groups reused when Bakery skips geometry export; rebuilt groups are handled by the live watcher.
        internal static void ApplyStoredBitmasks(int volumeBitmask, int probeBitmask) {
            if (!SupportsRuntimeBitmasks) return;
            UnityEngine.Object[] storages = Resources.FindObjectsOfTypeAll(BakeryStorageType);
            for (int i = 0; i < storages.Length; i++) {
                Component storage = storages[i] as Component;
                if (storage == null || !storage.gameObject.scene.IsValid() || !storage.gameObject.scene.isLoaded
                    || !(ReadField(ImplicitGroupsField, storage) is IList groups)) continue;
                for (int j = 0; j < groups.Count; j++) {
                    UnityEngine.Object group = groups[j] as UnityEngine.Object;
                    if (group == null) continue;
                    if (group.name == "probes") SetGroupBitmask(group, probeBitmask);
                    else if (group.name == "volumes") SetGroupBitmask(group, volumeBitmask);
                }
            }
        }

        // Applies runtime volume and probe bitmasks to Bakery state.
        internal static bool TryApplyRuntimeBitmasks(int volumeBitmask, int probeBitmask) {
            object lightProbeGroup = ReadField(LightProbeGroupField, null);
            object volumeGroup = ReadField(VolumeGroupField, null);
            if (lightProbeGroup == null && volumeGroup == null) return false;
            bool applied = SetGroupBitmask(lightProbeGroup, probeBitmask);
            applied |= SetGroupBitmask(volumeGroup, volumeBitmask);
            return applied;
        }

        // Resolves a type from Bakery's asmdef or from the default assemblies used by legacy releases.
        private static Type ResolveType(string typeName, string assemblyName, string legacyAssemblyName) {
            try {
                return Type.GetType(typeName + ", " + assemblyName, false)
                    ?? Type.GetType(typeName + ", " + legacyAssemblyName, false);
            } catch {
                return null;
            }
        }

        // Bakery 1.96+ uses public static EventHandler events; 1.45 has no lifecycle events.
        private static void SetSubscription(EventInfo eventInfo, EventHandler callback, bool subscribe) {
            if (eventInfo?.EventHandlerType != typeof(EventHandler) || callback == null) return;
            try {
                if (subscribe) eventInfo.AddEventHandler(null, callback);
                else eventInfo.RemoveEventHandler(null, callback);
            } catch {
            }
        }

        // Reads a static boolean field value if available.
        private static bool ReadStaticBool(FieldInfo field) {
            return ReadField(field, null) is bool value && value;
        }

        private static bool IsRendererInstance(object renderer) {
            return renderer != null && BakeryRendererType != null && BakeryRendererType.IsInstanceOfType(renderer);
        }

        private static bool ReadInstanceBool(object target, FieldInfo field) {
            return ReadField(field, target) is bool value && value;
        }

        private static object ReadField(FieldInfo field, object target) {
            try {
                return field?.GetValue(target);
            } catch {
                return null;
            }
        }

        private static bool SetField(FieldInfo field, object target, object value) {
            if (field == null) return false;
            try {
                field.SetValue(target, value);
                return true;
            } catch {
                return false;
            }
        }

        // Reads a cached Texture3D field from a Bakery component.
        private static Texture3D ReadTexture3D(Component component, FieldInfo field) {
            return ReadField(field, component) as Texture3D;
        }

        // Sets a Bakery group's bitmask field.
        private static bool SetGroupBitmask(object group, int value) {
            return group != null && SetField(GroupBitmaskField, group, value);
        }

        // Writes a boolean serialized property if it exists.
        private static void SetBool(SerializedObject serialized, string name, bool value) {
            SerializedProperty property = serialized.FindProperty(name);
            if (property != null && property.propertyType == SerializedPropertyType.Boolean) property.boolValue = value;
        }

        // Writes an int serialized property if it exists.
        private static void SetInt(SerializedObject serialized, string name, int value) {
            SerializedProperty property = serialized.FindProperty(name);
            if (property != null && property.propertyType == SerializedPropertyType.Integer) property.intValue = value;
        }

        // Writes an enum serialized property if it exists.
        private static void SetEnum(SerializedObject serialized, string name, int value) {
            SerializedProperty property = serialized.FindProperty(name);
            if (property != null && property.propertyType == SerializedPropertyType.Enum) property.enumValueIndex = value;
        }

        // Writes a bounds serialized property if it exists.
        private static void SetBounds(SerializedObject serialized, string name, Bounds value) {
            SerializedProperty property = serialized.FindProperty(name);
            if (property != null && property.propertyType == SerializedPropertyType.Bounds) property.boundsValue = value;
        }
    }
}
