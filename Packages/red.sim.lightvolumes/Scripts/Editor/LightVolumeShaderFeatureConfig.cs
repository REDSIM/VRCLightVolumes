using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UDONSHARP
using VRC.SDKBase.Editor;
using VRC.SDKBase.Editor.BuildPipeline;
#endif

namespace VRCLightVolumes {
    // Serialized as an int on the Manager so the proxy and Udon program share the same field layout.
    [Flags]
    internal enum LightVolumeShaderFeatures {
        None = 0,
        RegularVolumes = 1 << 0,
        AdditiveVolumes = 1 << 1,
        PointLights = 1 << 2,
        SpotLights = 1 << 3,
        AreaLights = 1 << 4,
        LightLuts = 1 << 5,
        PointCookies = 1 << 6,
        SpotCookies = 1 << 7,
        AreaCookies = 1 << 8,
        Shadows = 1 << 9,
        Clustering = 1 << 10,
        VolumeRotation = 1 << 11,
        WorldSpaceShadows = 1 << 12,
        CubemapShadows = 1 << 13,
        SingleSliceShadows = 1 << 14,
        LightProbesBlending = 1 << 15,
        SmoothBounds = 1 << 16,
        All = (1 << 17) - 1
    }

    // Keeps Play Mode's Auto snapshot independent from temporary build profiles and ordinary scene edits.
    internal sealed class LightVolumeShaderFeatureSession {
        internal bool IsPlaying { get; private set; }
        internal bool IsBuilding { get; private set; }
        internal bool IsAvatarBuild { get; private set; }
        internal bool Automatic { get; private set; }
        internal bool StrippingEnabled { get; private set; } = true;
        internal LightVolumeShaderFeatures DetectedFeatures { get; private set; } = LightVolumeShaderFeatures.All;
        internal LightVolumeShaderFeatures PlayFeatures { get; private set; } = LightVolumeShaderFeatures.All;
        private LightVolumeShaderFeatures _buildFeatures = LightVolumeShaderFeatures.All;

        // The master switch bypasses specialization without discarding the manual selection or rescanning Auto.
        internal void SetStrippingEnabled(bool enabled) {
            StrippingEnabled = enabled;
        }

        // Captures the scene once before Play Mode starts, including when domain reload is disabled.
        internal void BeginPlay(bool automatic, LightVolumeShaderFeatures manualFeatures, LightVolumeShaderFeatures detectedFeatures) {
            IsPlaying = true;
            Automatic = automatic;
            DetectedFeatures = detectedFeatures;
            PlayFeatures = LightVolumeShaderFeatureConfig.NormalizeFeatures(automatic ? detectedFeatures : manualFeatures);
        }

        // Only an explicit Auto off/on transition rescans a running scene; manual edits use the supplied mask directly.
        internal void ChangePlaySelection(bool automatic, LightVolumeShaderFeatures manualFeatures, Func<LightVolumeShaderFeatures> detectFeatures) {
            if (!IsPlaying || IsBuilding) return;
            if (automatic && !Automatic) DetectedFeatures = detectFeatures();
            Automatic = automatic;
            PlayFeatures = LightVolumeShaderFeatureConfig.NormalizeFeatures(automatic ? DetectedFeatures : manualFeatures);
        }

        // Returning to authoring restores the complete shader independently of the stored Manager selection.
        internal void EndPlay() {
            IsPlaying = false;
            PlayFeatures = LightVolumeShaderFeatures.All;
        }

        // A build temporarily overrides the editor profile without modifying the running session's Auto snapshot.
        internal void BeginBuild(bool avatarBuild, LightVolumeShaderFeatures features) {
            IsBuilding = true;
            IsAvatarBuild = avatarBuild;
            _buildFeatures = LightVolumeShaderFeatureConfig.NormalizeFeatures(features);
        }

        // Success, cancellation and errors all release the temporary build override.
        internal void EndBuild() {
            IsBuilding = false;
            IsAvatarBuild = false;
            _buildFeatures = LightVolumeShaderFeatures.All;
        }

        // Disallowed projects and Edit Mode always compile the full include; permitted play/build sessions specialize it.
        internal LightVolumeShaderFeatures GetFeatures(bool strippingAllowed) {
            if (!strippingAllowed) return LightVolumeShaderFeatures.All;
            if (IsBuilding) return IsAvatarBuild ? LightVolumeShaderFeatures.All : _buildFeatures;
            return IsPlaying && StrippingEnabled ? PlayFeatures : LightVolumeShaderFeatures.All;
        }
    }

    // Specializes the shared include only for Play Mode and builds, without shader keywords or variants.
    [InitializeOnLoad]
    internal static class LightVolumeShaderFeatureConfig {
        private const string ConfigAssetPath = "Packages/red.sim.lightvolumes/Shaders/LightVolumesBuildConfig.cginc";
        private static readonly string[] _disableTags = {
            "VRCLV_DISABLE_REGULAR_VOLUMES", "VRCLV_DISABLE_ADDITIVE_VOLUMES",
            "VRCLV_DISABLE_POINT_LIGHTS", "VRCLV_DISABLE_SPOT_LIGHTS", "VRCLV_DISABLE_AREA_LIGHTS",
            "VRCLV_DISABLE_LIGHT_LUTS", "VRCLV_DISABLE_POINT_COOKIES", "VRCLV_DISABLE_SPOT_COOKIES",
            "VRCLV_DISABLE_AREA_COOKIES", "VRCLV_DISABLE_SHADOWS", "VRCLV_DISABLE_CLUSTERING",
            "VRCLV_DISABLE_VOLUME_ROTATION", "VRCLV_DISABLE_WORLD_SPACE_SHADOWS",
            "VRCLV_DISABLE_CUBEMAP_SHADOWS", "VRCLV_DISABLE_SINGLE_SLICE_SHADOWS",
            "VRCLV_DISABLE_LIGHT_PROBES_BLENDING", "VRCLV_DISABLE_SMOOTH_BOUNDS"
        };
        private const string SessionKey = "VRCLightVolumes.ShaderFeatures.";
        private static readonly LightVolumeShaderFeatureSession _session = new LightVolumeShaderFeatureSession();
        private static bool _refreshQueued;
        private static bool _writing;
        private static bool _detectionDirty = true;
        private static int _detectedSceneHandle;
        private static LightVolumeShaderFeatures _detectedFeatures;
        private static string _appliedSource;
        private static bool _sdkBuild;
#if UDONSHARP
        private static IVRCSdkBuilderApi _buildApi;
#endif

        // Package presence blocks stripping even in mixed Worlds/Avatars projects; standalone installations remain eligible.
        internal static bool HasAvatarSdk {
            get {
#if VRCLV_AVATARS_SDK
                return true;
#else
                return false;
#endif
            }
        }

        // Includes the SDK's asynchronous pre-export phase, during which Unity is not yet building an AssetBundle.
        internal static bool IsBuilding => _session.IsBuilding || BuildPipeline.isBuildingPlayer;

        // Restores a running snapshot across domain reload; otherwise authoring starts with the full shader.
        static LightVolumeShaderFeatureConfig() {
            if (EditorApplication.isPlayingOrWillChangePlaymode) {
                bool hasSnapshot = SessionState.GetBool(SessionKey + "Playing", false);
                bool automatic = !hasSnapshot || SessionState.GetBool(SessionKey + "Automatic", true);
                LightVolumeShaderFeatures manual = hasSnapshot ? (LightVolumeShaderFeatures)SessionState.GetInt(SessionKey + "PlayFeatures", (int)LightVolumeShaderFeatures.All) : LightVolumeShaderFeatures.All;
                LightVolumeShaderFeatures detected = hasSnapshot ? (LightVolumeShaderFeatures)SessionState.GetInt(SessionKey + "DetectedFeatures", (int)LightVolumeShaderFeatures.All) : LightVolumeShaderFeatures.All;
                _session.BeginPlay(automatic, manual, detected);
                _session.SetStrippingEnabled(!hasSnapshot || SessionState.GetBool(SessionKey + "StrippingEnabled", true));
            }
            if (SessionState.GetBool(SessionKey + "Building", false)) {
                _session.BeginBuild(SessionState.GetBool(SessionKey + "AvatarBuild", false), (LightVolumeShaderFeatures)SessionState.GetInt(SessionKey + "BuildFeatures", (int)LightVolumeShaderFeatures.All));
                _sdkBuild = SessionState.GetBool(SessionKey + "SdkBuild", false);
                AttachBuildApi();
                EditorApplication.delayCall += WatchBuild;
            }
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorSceneManager.sceneClosed += OnSceneClosed;
            EditorSceneManager.sceneSaved += OnSceneSaved;
            EditorApplication.hierarchyChanged += QueueRefresh;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            ObjectChangeEvents.changesPublished += OnChangesPublished;
            Undo.undoRedoPerformed += OnUndoRedo;
            AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
            EditorApplication.quitting += Shutdown;
            QueueUpdate();
        }

        // Detaches callbacks before domain reload, leaving the applied include intact during Play Mode.
        private static void Shutdown() {
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorSceneManager.sceneClosed -= OnSceneClosed;
            EditorSceneManager.sceneSaved -= OnSceneSaved;
            EditorApplication.hierarchyChanged -= QueueRefresh;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            ObjectChangeEvents.changesPublished -= OnChangesPublished;
            Undo.undoRedoPerformed -= OnUndoRedo;
            AssemblyReloadEvents.beforeAssemblyReload -= Shutdown;
            EditorApplication.quitting -= Shutdown;
            EditorApplication.delayCall -= Refresh;
            EditorApplication.delayCall -= WatchBuild;
            EditorApplication.update -= CheckBuildFinished;
            _refreshQueued = false;
#if UDONSHARP
            DetachBuildApi();
#endif
        }

        // Scene ordering is owned by GetPrimaryManager. Opening a secondary scene never replaces that choice.
        private static void OnSceneOpened(Scene scene, OpenSceneMode mode) {
            if (BuildPipeline.isBuildingPlayer || EditorSceneManager.IsPreviewScene(scene)) return;
            QueueRefresh();
        }

        // Re-evaluates ownership after the primary scene closes, including the no-Manager/full-profile case.
        private static void OnSceneClosed(Scene scene) {
            if (BuildPipeline.isBuildingPlayer || EditorSceneManager.IsPreviewScene(scene)) return;
            QueueRefresh();
        }

        // Saving can finish deferred Udon proxy setup; Auto reads the canonical scene objects afterwards.
        private static void OnSceneSaved(Scene scene) {
            QueueRefresh();
        }

        // Compiles a fresh snapshot before entering Play Mode and restores all features on return, including a cancelled entry.
        private static void OnPlayModeStateChanged(PlayModeStateChange state) {
            if (state == PlayModeStateChange.ExitingEditMode) {
                LightVolumeManager manager = LightVolumeManagerEditorBackend.GetPrimaryManager();
                LightVolumeShaderFeatures detected = manager != null && !HasAvatarSdk ? DetectSceneFeatures(manager.gameObject.scene) : LightVolumeShaderFeatures.All;
                bool automatic = manager == null || manager.AutoShaderFeatures;
                _session.BeginPlay(automatic, GetConfiguredManualFeatures(manager), detected);
                _session.SetStrippingEnabled(manager == null || manager.ShaderStripping);
                SavePlaySession();
                if (!WriteConfig(_session.GetFeatures(!HasAvatarSdk))) EditorApplication.isPlaying = false;
                return;
            }
            if (state == PlayModeStateChange.EnteredPlayMode) {
                QueueUpdate();
                return;
            }
            if (state != PlayModeStateChange.EnteredEditMode) return;
            _session.EndPlay();
            SavePlaySession();
            QueueRefresh();
        }

        // Stores only the frozen profile; reloading editor assemblies must not rescan runtime-created objects.
        private static void SavePlaySession() {
            SessionState.SetBool(SessionKey + "Playing", _session.IsPlaying);
            SessionState.SetBool(SessionKey + "Automatic", _session.Automatic);
            SessionState.SetBool(SessionKey + "StrippingEnabled", _session.StrippingEnabled);
            SessionState.SetInt(SessionKey + "PlayFeatures", (int)_session.PlayFeatures);
            SessionState.SetInt(SessionKey + "DetectedFeatures", (int)_session.DetectedFeatures);
        }

        // Undoing scene edits only refreshes authoring hints; undoing Play Mode feature selections applies that selection.
        private static void OnUndoRedo() {
            if (!EditorApplication.isPlaying) {
                QueueRefresh();
                return;
            }
            LightVolumeManager manager = LightVolumeManagerEditorBackend.GetPrimaryManager();
            if (manager != null) NotifySettingsChanged(manager);
            else QueueUpdate();
        }

        // Invalidates Auto only for relevant component edits; hierarchy changes have their own coalesced hook.
        private static void OnChangesPublished(ref ObjectChangeEventStream stream) {
            if (_writing || EditorApplication.isPlayingOrWillChangePlaymode || _session.IsBuilding || BuildPipeline.isBuildingPlayer) return;
            for (int i = 0; i < stream.length; i++) {
                ObjectChangeKind kind = stream.GetEventType(i);
                if (kind == ObjectChangeKind.UpdatePrefabInstances || kind == ObjectChangeKind.ChangeGameObjectStructure || kind == ObjectChangeKind.ChangeGameObjectStructureHierarchy || kind == ObjectChangeKind.ChangeScene) {
                    QueueRefresh();
                    return;
                }
                if (kind != ObjectChangeKind.ChangeGameObjectOrComponentProperties) continue;
                stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out ChangeGameObjectOrComponentPropertiesEventArgs change);
                UnityEngine.Object changed = EditorUtility.InstanceIDToObject(change.instanceId);
                if (!AffectsDetectedFeatures(changed)) continue;
                QueueRefresh();
                return;
            }
        }

        // Transform edits can change a static volume's rotation relative to its bake, including through an inactive ancestor.
        internal static bool AffectsDetectedFeatures(UnityEngine.Object changed) {
            if (changed is LightVolumeManager || changed is LightVolumeInstance || changed is PointLightVolumeInstance || changed is PointLightShadowRuntimeBaker) return true;
            Transform changedTransform = changed as Transform;
            return changedTransform != null && changedTransform.GetComponentInChildren<LightVolumeInstance>(true) != null;
        }

        // Play Mode hierarchy changes check only Manager presence; authoring changes can also refresh the cached Auto preview.
        internal static void QueueRefresh() {
            if (_session.IsBuilding || BuildPipeline.isBuildingPlayer) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) {
                if (EditorApplication.isPlaying) QueueUpdate();
                return;
            }
            _detectionDirty = true;
            QueueUpdate();
        }

        // Coalesces header restoration without invalidating a running Auto snapshot.
        private static void QueueUpdate() {
            if (_refreshQueued) return;
            _refreshQueued = true;
            EditorApplication.delayCall += Refresh;
        }

        // Authoring changes refresh only the Inspector's cached hints; the compiled edit-time profile always remains full.
        private static void Refresh() {
            EditorApplication.delayCall -= Refresh;
            _refreshQueued = false;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || Undo.isProcessing) {
                QueueUpdate();
                return;
            }
            if (!EditorApplication.isPlayingOrWillChangePlaymode && !_session.IsBuilding && !BuildPipeline.isBuildingPlayer) {
                _session.EndPlay();
                SavePlaySession();
                LightVolumeManager manager = LightVolumeManagerEditorBackend.GetPrimaryManager();
                if (manager != null && !HasAvatarSdk) CacheSceneFeatures(manager.gameObject.scene);
            }
            LightVolumeShaderFeatures features = _session.GetFeatures(!HasAvatarSdk);
            // Wait until scene reload finishes before checking a play owner; build exports keep their captured profile.
            if (features != LightVolumeShaderFeatures.All && EditorApplication.isPlaying && !_session.IsBuilding && LightVolumeManagerEditorBackend.GetPrimaryManager() == null) features = LightVolumeShaderFeatures.All;
            WriteConfig(features);
        }

        // Returns the selected scene profile, never unioning data from another loaded scene or Manager.
        internal static LightVolumeShaderFeatures GetConfiguredFeatures(LightVolumeManager manager) {
            if (manager == null || HasAvatarSdk || !manager.ShaderStripping) return LightVolumeShaderFeatures.All;
            if (!manager.AutoShaderFeatures) return GetConfiguredManualFeatures(manager);
            return GetDetectedFeatures(manager);
        }

        // Reads the remembered manual selection without touching the scene detector.
        private static LightVolumeShaderFeatures GetConfiguredManualFeatures(LightVolumeManager manager) {
            return manager == null ? LightVolumeShaderFeatures.All : NormalizeFeatures(GetManualFeatures(manager.ShaderFeatures, manager.ShaderFeaturesSchema));
        }

        // Old manual masks never opted out of features added later, so preserve those paths until the user edits the expanded selection.
        internal static LightVolumeShaderFeatures GetManualFeatures(int mask, int schema) {
            LightVolumeShaderFeatures features = (LightVolumeShaderFeatures)mask & LightVolumeShaderFeatures.All;
            if (schema < 1) features |= LightVolumeShaderFeatures.All & ~(LightVolumeShaderFeatures)((1 << 11) - 1);
            return features;
        }

        // Checks prerequisites rather than the feature's own selection, so unchecked children can still be enabled in the Inspector.
        internal static bool IsFeatureAvailable(LightVolumeShaderFeatures feature, LightVolumeShaderFeatures features) {
            const LightVolumeShaderFeatures lights = LightVolumeShaderFeatures.PointLights | LightVolumeShaderFeatures.SpotLights | LightVolumeShaderFeatures.AreaLights;
            switch (feature) {
                case LightVolumeShaderFeatures.RegularVolumes:
                case LightVolumeShaderFeatures.AdditiveVolumes:
                case LightVolumeShaderFeatures.PointLights:
                case LightVolumeShaderFeatures.SpotLights:
                case LightVolumeShaderFeatures.AreaLights:
                    return true;
                case LightVolumeShaderFeatures.VolumeRotation:
                    return (features & (LightVolumeShaderFeatures.RegularVolumes | LightVolumeShaderFeatures.AdditiveVolumes)) != 0;
                case LightVolumeShaderFeatures.LightProbesBlending:
                case LightVolumeShaderFeatures.SmoothBounds:
                    return (features & LightVolumeShaderFeatures.RegularVolumes) != 0;
                case LightVolumeShaderFeatures.PointCookies:
                    return (features & LightVolumeShaderFeatures.PointLights) != 0;
                case LightVolumeShaderFeatures.SpotCookies:
                    return (features & LightVolumeShaderFeatures.SpotLights) != 0;
                case LightVolumeShaderFeatures.AreaCookies:
                    return (features & LightVolumeShaderFeatures.AreaLights) != 0;
                case LightVolumeShaderFeatures.LightLuts:
                    return (features & (LightVolumeShaderFeatures.PointLights | LightVolumeShaderFeatures.SpotLights)) != 0;
                case LightVolumeShaderFeatures.Clustering:
                case LightVolumeShaderFeatures.Shadows:
                    return (features & lights) != 0;
                case LightVolumeShaderFeatures.CubemapShadows:
                    return IsSelectedFeatureAvailable(LightVolumeShaderFeatures.Shadows, features);
                case LightVolumeShaderFeatures.SingleSliceShadows:
                    return (features & LightVolumeShaderFeatures.SpotLights) != 0 && IsSelectedFeatureAvailable(LightVolumeShaderFeatures.Shadows, features);
                case LightVolumeShaderFeatures.WorldSpaceShadows:
                    return IsSelectedFeatureAvailable(LightVolumeShaderFeatures.CubemapShadows, features) || IsSelectedFeatureAvailable(LightVolumeShaderFeatures.SingleSliceShadows, features);
                default:
                    return false;
            }
        }

        // Follows the short acyclic dependency chain, including parents which are selected but unavailable themselves.
        private static bool IsSelectedFeatureAvailable(LightVolumeShaderFeatures feature, LightVolumeShaderFeatures features) {
            return (features & feature) != 0 && IsFeatureAvailable(feature, features);
        }

        // Removes unavailable children without enabling parents or overwriting the user's remembered manual selections.
        internal static LightVolumeShaderFeatures NormalizeFeatures(LightVolumeShaderFeatures features) {
            features &= LightVolumeShaderFeatures.All;
            for (int i = 0; i < _disableTags.Length; i++) {
                LightVolumeShaderFeatures feature = (LightVolumeShaderFeatures)(1 << i);
                if ((features & feature) != 0 && !IsFeatureAvailable(feature, features)) features &= ~feature;
            }
            return features;
        }

        // The primary Manager's foldout reads its frozen Play Mode snapshot without searching objects during repaint.
        internal static LightVolumeShaderFeatures GetDetectedFeatures(LightVolumeManager manager) {
            if (manager == null) return LightVolumeShaderFeatures.None;
            if (_session.IsPlaying) return _session.DetectedFeatures;
            Scene scene = manager.gameObject.scene;
            if (EditorSceneManager.IsPreviewScene(scene)) return DetectSceneFeatures(scene);
            return CacheSceneFeatures(scene);
        }

        // Caches only the requested scene; scene and object-change events invalidate the cached scan.
        private static LightVolumeShaderFeatures CacheSceneFeatures(Scene scene) {
            if (_detectionDirty || _detectedSceneHandle != scene.handle) {
                _detectedFeatures = DetectSceneFeatures(scene);
                _detectedSceneHandle = scene.handle;
                _detectionDirty = false;
            }
            return _detectedFeatures;
        }

        // Reads authoring objects, including inactive and zero-intensity objects, without interpreting user scripts or other scenes.
        internal static LightVolumeShaderFeatures DetectSceneFeatures(Scene scene) {
            if (!scene.IsValid() || !scene.isLoaded) return LightVolumeShaderFeatures.None;
            LightVolumeShaderFeatures features = LightVolumeShaderFeatures.None;
            LightVolumeManager manager = null;
            int potentialLightCount = 0;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++) {
                GameObject root = roots[i];
                if (manager == null) manager = root.GetComponentInChildren<LightVolumeManager>(true);
                LightVolumeInstance[] volumes = root.GetComponentsInChildren<LightVolumeInstance>(true);
                for (int j = 0; j < volumes.Length; j++) {
                    LightVolumeInstance volume = volumes[j];
                    features |= volume.IsAdditive ? LightVolumeShaderFeatures.AdditiveVolumes : LightVolumeShaderFeatures.RegularVolumes;
                    Quaternion relativeRotation = volume.transform.localToWorldMatrix.rotation * volume.InvBakedRotation;
                    if (volume.IsDynamic || Mathf.Abs(relativeRotation.w) < 0.999999f) features |= LightVolumeShaderFeatures.VolumeRotation;
                }
                PointLightVolumeInstance[] lights = root.GetComponentsInChildren<PointLightVolumeInstance>(true);
                potentialLightCount += lights.Length;
                for (int j = 0; j < lights.Length; j++) features |= DetectLightFeatures(lights[j]);
                PointLightShadowRuntimeBaker[] bakers = root.GetComponentsInChildren<PointLightShadowRuntimeBaker>(true);
                for (int j = 0; j < bakers.Length; j++) {
                    PointLightVolumeInstance target = bakers[j].TargetPointLightVolume;
                    if (target != null && target.gameObject.scene == scene) features |= DetectShadowFeatures(target, true);
                }
            }
            // Scene objects are an upper bound on active lights; runtime clustering cannot reach its threshold below this count.
            if (manager != null && manager.Clustering && potentialLightCount >= Mathf.Clamp(manager.ClusteringMinLights, 1, 128)) features |= LightVolumeShaderFeatures.Clustering;
            if ((features & LightVolumeShaderFeatures.RegularVolumes) != 0) {
                if (manager == null || manager.LightProbesBlending) features |= LightVolumeShaderFeatures.LightProbesBlending;
                if (manager == null || !manager.SharpBounds) features |= LightVolumeShaderFeatures.SmoothBounds;
            }
            return NormalizeFeatures(features);
        }

        // Includes both authoring projection selections and valid runtime sources so pending proxy synchronization cannot strip a used path.
        private static LightVolumeShaderFeatures DetectLightFeatures(PointLightVolumeInstance light) {
            LightVolumeShaderFeatures features;
            switch (light.LightType) {
                case 0: features = LightVolumeShaderFeatures.PointLights; break;
                case 1: features = LightVolumeShaderFeatures.SpotLights; break;
                case 2: features = LightVolumeShaderFeatures.AreaLights; break;
                default: return LightVolumeShaderFeatures.All;
            }
            bool authoringSource = light.GetProjectionSource() != null && light.HasProjectionSource();
            bool runtimeSource = light.CustomTexture != null || light.CustomTextureMaterial != null;
            bool lut = light.LightType != 2 && ((authoringSource && light.Projection == 1) || (runtimeSource && light.ProjectionMode == 1));
            bool cookie = (authoringSource && (light.LightType == 2 || light.Projection == 2)) || (runtimeSource && light.ProjectionMode == 2);
            if (lut) features |= LightVolumeShaderFeatures.LightLuts;
            if (cookie) {
                if (light.LightType == 0) features |= LightVolumeShaderFeatures.PointCookies;
                else if (light.LightType == 1) features |= LightVolumeShaderFeatures.SpotCookies;
                else features |= LightVolumeShaderFeatures.AreaCookies;
            }
            return features | DetectShadowFeatures(light, false);
        }

        // Authoring and runtime layouts can briefly disagree while a proxy is being synchronized; keep both required receiver paths in that case.
        private static LightVolumeShaderFeatures DetectShadowFeatures(PointLightVolumeInstance light, bool externalBaker) {
            bool authoringShadow = light.Shadows || light.BakeInGame || light.ShadowMap != null;
            bool runtimeShadow = light.ShadowMapTexture != null || light.ShadowMapMaterial != null || light.RuntimeShadowDirectOutput || externalBaker;
            if (!authoringShadow && !runtimeShadow) return LightVolumeShaderFeatures.None;
            LightVolumeShaderFeatures features = LightVolumeShaderFeatures.Shadows;
            if (light.WorldSpaceShadows) features |= LightVolumeShaderFeatures.WorldSpaceShadows;
            if (light.LightType != 1) return features | LightVolumeShaderFeatures.CubemapShadows;
            if (authoringShadow || externalBaker) features |= light.UsesCubemapShadows() ? LightVolumeShaderFeatures.CubemapShadows : LightVolumeShaderFeatures.SingleSliceShadows;
            if (runtimeShadow) features |= light.ShadowMapUsesCubemap ? LightVolumeShaderFeatures.CubemapShadows : LightVolumeShaderFeatures.SingleSliceShadows;
            return features;
        }

        // Writes only disable tags. No tags is the portable full-feature default for avatars and unconfigured projects.
        internal static string BuildConfigSource(LightVolumeShaderFeatures features) {
            features = NormalizeFeatures(features);
            StringBuilder source = new StringBuilder();
            source.Append("#ifndef VRC_LIGHT_VOLUMES_BUILD_CONFIG_INCLUDED\n#define VRC_LIGHT_VOLUMES_BUILD_CONFIG_INCLUDED\n\n");
            source.Append("// Generated for the primary VRChat world scene. No disable tags means all features are available.\n\n");
            for (int i = 0; i < _disableTags.Length; i++) {
                if (((int)features & (1 << i)) == 0) source.Append("#define ").Append(_disableTags[i]).Append('\n');
            }
            source.Append("#endif\n");
            return source.ToString();
        }

        // Restores the current session profile if a package update or source control replaces the generated include.
        internal static void OnConfigImported(string assetPath) {
            if (_writing) return;
            if (!string.Equals(assetPath, ConfigAssetPath, StringComparison.Ordinal)) return;
            _appliedSource = null;
            QueueUpdate();
        }

        // Inspector changes are immediate in Play Mode; authoring only saves the selection for the next play/build session.
        internal static void NotifySettingsChanged(LightVolumeManager manager) {
            if (HasAvatarSdk) {
                WriteConfig(LightVolumeShaderFeatures.All);
                return;
            }
            if (manager == null || manager != LightVolumeManagerEditorBackend.GetPrimaryManager() || _session.IsBuilding || BuildPipeline.isBuildingPlayer) return;
            if (!EditorApplication.isPlaying) {
                QueueRefresh();
                return;
            }
            _session.SetStrippingEnabled(manager.ShaderStripping);
            _session.ChangePlaySelection(manager.AutoShaderFeatures, GetConfiguredManualFeatures(manager), () => DetectSceneFeatures(manager.gameObject.scene));
            SavePlaySession();
            WriteConfig(_session.GetFeatures(!HasAvatarSdk));
        }

        // Captures the world again immediately before compilation, independently of any previous Play Mode snapshot.
        internal static bool PrepareBuild(bool avatarBuild, bool sdkBuild = true) {
            if (_session.IsBuilding) return WriteConfig(_session.GetFeatures(!HasAvatarSdk));
            LightVolumeManager manager = LightVolumeManagerEditorBackend.GetPrimaryManager();
            LightVolumeShaderFeatures features = LightVolumeShaderFeatures.All;
            if (!avatarBuild && !HasAvatarSdk && manager != null && manager.ShaderStripping) features = manager.AutoShaderFeatures ? DetectSceneFeatures(manager.gameObject.scene) : GetConfiguredManualFeatures(manager);
            _session.BeginBuild(avatarBuild, features);
            _sdkBuild = sdkBuild;
            SessionState.SetBool(SessionKey + "Building", true);
            SessionState.SetBool(SessionKey + "AvatarBuild", avatarBuild);
            SessionState.SetBool(SessionKey + "SdkBuild", sdkBuild);
            SessionState.SetInt(SessionKey + "BuildFeatures", (int)features);
            AttachBuildApi();
            // Start checking only after the synchronous preflight has returned to the editor loop.
            EditorApplication.delayCall -= WatchBuild;
            EditorApplication.delayCall += WatchBuild;
            if (WriteConfig(_session.GetFeatures(!HasAvatarSdk))) return true;
            FinishBuild();
            return false;
        }

        // A short-lived watcher also handles rejected SDK preflights and failed/cancelled Unity builds without a completion callback.
        private static void WatchBuild() {
            EditorApplication.delayCall -= WatchBuild;
            if (!_session.IsBuilding) return;
            EditorApplication.update -= CheckBuildFinished;
            EditorApplication.update += CheckBuildFinished;
            CheckBuildFinished();
        }

        // Checks build state only, never scene contents, and keeps the profile through the SDK's asynchronous pre-export interval.
        private static void CheckBuildFinished() {
            if (BuildPipeline.isBuildingPlayer) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
#if UDONSHARP
            if (_sdkBuild && _buildApi != null && _buildApi.BuildState == SdkBuildState.Building) return;
#endif
            FinishBuild();
        }

        // Releases both world and avatar overrides, returning to full Edit Mode or the unchanged Play Mode snapshot.
        internal static void FinishBuild() {
            if (!_session.IsBuilding) return;
            EditorApplication.delayCall -= WatchBuild;
            EditorApplication.update -= CheckBuildFinished;
#if UDONSHARP
            DetachBuildApi();
#endif
            _session.EndBuild();
            _sdkBuild = false;
            SessionState.SetBool(SessionKey + "Building", false);
            QueueUpdate();
        }

        // Subscribes before SDK validation/export, and reconnects after an editor domain reload during a build.
        private static void AttachBuildApi() {
#if UDONSHARP
            DetachBuildApi();
            if (_sdkBuild && VRCSdkControlPanel.TryGetBuilder<IVRCSdkBuilderApi>(out _buildApi)) {
                _buildApi.OnSdkBuildFinish += OnBuildFinished;
                _buildApi.OnSdkBuildError += OnBuildFinished;
            }
#endif
        }

#if UDONSHARP
        // SDK completion and errors share the same restoration path; duplicate notifications are harmless.
        private static void OnBuildFinished(object sender, string message) {
            FinishBuild();
        }

        // Avoids rooting a closed SDK panel or retaining callbacks across consecutive build requests.
        private static void DetachBuildApi() {
            if (_buildApi == null) return;
            _buildApi.OnSdkBuildFinish -= OnBuildFinished;
            _buildApi.OnSdkBuildError -= OnBuildFinished;
            _buildApi = null;
        }
#endif

        // A content comparison avoids reimporting every dependent shader when only a light's transform/color/count changes.
        private static bool WriteConfig(LightVolumeShaderFeatures features) {
            try {
                if (HasAvatarSdk) features = LightVolumeShaderFeatures.All;
                string source = BuildConfigSource(features);
                if (string.Equals(_appliedSource, source, StringComparison.Ordinal)) return true;
                string existing = File.Exists(ConfigAssetPath) ? File.ReadAllText(ConfigAssetPath) : null;
                if (!string.Equals(existing, source, StringComparison.Ordinal)) {
                    _writing = true;
                    File.WriteAllText(ConfigAssetPath, source, new UTF8Encoding(false));
                    AssetDatabase.ImportAsset(ConfigAssetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                }
                _appliedSource = source;
                return true;
            } catch (Exception exception) {
                Debug.LogError("[LightVolumes] Could not apply shader features. The shader configuration must be writable before building. " + exception.Message);
                return false;
            } finally {
                _writing = false;
            }
        }
    }

    // Keeps an externally restored package include consistent with the scene's serialized settings.
    internal sealed class LightVolumeShaderFeatureAssetPostprocessor : AssetPostprocessor {
        // Synchronous imports initiated by the writer are ignored, preventing refresh loops.
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths) {
            for (int i = 0; i < importedAssets.Length; i++) {
                if (!importedAssets[i].EndsWith("/LightVolumesBuildConfig.cginc", StringComparison.Ordinal)) continue;
                LightVolumeShaderFeatureConfig.OnConfigImported(importedAssets[i]);
            }
        }
    }

    // Target switches can change authoring sources and manager settings before the next scene initialization.
    internal sealed class LightVolumeShaderFeatureBuildTargetChanged : IActiveBuildTargetChanged {
        public int callbackOrder => 0;

        // Refreshes the same primary scene when Unity completes a build-target switch.
        public void OnActiveBuildTargetChanged(BuildTarget previousTarget, BuildTarget newTarget) {
            LightVolumeShaderFeatureConfig.QueueRefresh();
        }
    }

    // Standalone Player builds use the same primary-scene policy as world exports; AssetBundle exports use the SDK callback below.
    internal sealed class LightVolumeShaderFeaturePlayerBuild : IPreprocessBuildWithReport, IPostprocessBuildWithReport {
        public int callbackOrder => 95;

        // Specializes before Unity compiles shaders, aborting a build if the generated include cannot be written.
        public void OnPreprocessBuild(BuildReport report) {
            if (!LightVolumeShaderFeatureConfig.PrepareBuild(false, false)) throw new BuildFailedException("Could not prepare Light Volumes shader features.");
        }

        // Successful Player builds have a completion callback; the scoped watcher covers errors and cancellation.
        public void OnPostprocessBuild(BuildReport report) {
            LightVolumeShaderFeatureConfig.FinishBuild();
        }
    }

#if UDONSHARP
    // Runs after the existing integrity preflight and before UdonSharp; shader features are ready before bundle compilation begins.
    internal sealed class LightVolumeShaderFeatureBuildPreprocessor : IVRCSDKBuildRequestedCallback {
        public int callbackOrder => 95;

        // Avatar requests always clear scene specialization, even if both SDK types happen to be installed.
        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType) {
            return LightVolumeShaderFeatureConfig.PrepareBuild(requestedBuildType != VRCSDKRequestedBuildType.Scene);
        }
    }
#endif
}
