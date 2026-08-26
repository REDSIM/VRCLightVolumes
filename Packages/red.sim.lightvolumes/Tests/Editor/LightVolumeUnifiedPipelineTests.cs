using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using VRCLightVolumes.Editor;

namespace VRCLightVolumes.Tests {
    [Category("Editor")]
    public class LightVolumeUnifiedPipelineTests {
        private const float Epsilon = 0.0001f;
        private static readonly BindingFlags _nonPublicStaticFlags = BindingFlags.Static | BindingFlags.NonPublic;

        private readonly List<Object> _createdObjects = new List<Object>();

        [TearDown]
        public void TearDown() {
            for (int i = _createdObjects.Count - 1; i >= 0; i--) {
                Object target = _createdObjects[i];
                if (target != null) Object.DestroyImmediate(target);
            }
            _createdObjects.Clear();
        }

        // Asset generation still depends on deterministic escaping after authoring moved to Udon components.
        [Test]
        public void AssetFileNameEscapingPreservesValidSegmentsAndEscapesInvalidCharacters() {
            string validName = "Valid Name (1) #%.[]{}+=,;!@'`~.asset";
            string invalidName = "<>:\"/\\|?*\u0001";

            Assert.That(LVUtils.EscapeFileName(validName), Is.EqualTo(validName));
            Assert.That(LVUtils.EscapeFileName(invalidName), Is.EqualTo("%3C%3E%3A%22%2F%5C%7C%3F%2A%01"));
            Assert.That(
                LVUtils.EscapeAssetPathFileName("Assets/Valid Folder/Name:Bad?.asset"),
                Is.EqualTo("Assets/Valid Folder/Name%3ABad%3F.asset"));
        }

        // A completed atlas still belongs to the backend until a live Manager accepts it.
        [Test]
        public void CompleteAtlasDestroysTextureRejectedWithoutManager() {
            Texture3D texture = CreateTexture3D("Rejected Atlas Texture");
            MethodInfo completeAtlas = typeof(LightVolumeManagerEditorBackend).GetMethod("CompleteAtlas", _nonPublicStaticFlags);
            Assert.That(completeAtlas, Is.Not.Null);

            completeAtlas.Invoke(null, new object[] {
                null,
                new LightVolumeInstance[0],
                new Atlas3D { Texture = texture, BoundsUvwMin = new Vector3[0], BoundsUvwMax = new Vector3[0] }
            });

            Assert.That(texture == null, Is.True, "A rejected native atlas must be destroyed instead of losing its owner.");
        }

        // A completed non-persistent atlas can outlive its generator while the scene is unsaved.
        // Entering Play Mode must release that backend-owned native texture even with domain reload disabled.
        [Test]
        public void PlayTransitionReleasesBackendOwnedTransientAtlas() {
            LightVolumeManager manager = CreateComponent<LightVolumeManager>("Transient Atlas Owner");
            Texture3D texture = CreateTexture3D("Accepted Unsaved Atlas Texture");
            manager.LightVolumeAtlasBase = texture;
            manager.LightVolumeAtlas = texture;
            MethodInfo trackTransientAtlas = typeof(LightVolumeManagerEditorBackend).GetMethod("TrackTransientAtlas", _nonPublicStaticFlags);
            MethodInfo onPlayModeStateChanged = typeof(LightVolumeManagerEditorBackend).GetMethod("OnPlayModeStateChanged", _nonPublicStaticFlags);
            Assert.That(trackTransientAtlas, Is.Not.Null);
            Assert.That(onPlayModeStateChanged, Is.Not.Null);

            trackTransientAtlas.Invoke(null, new object[] { manager, texture });
            onPlayModeStateChanged.Invoke(null, new object[] { PlayModeStateChange.ExitingEditMode });

            Assert.That(manager.LightVolumeAtlasBase, Is.Null);
            Assert.That(manager.LightVolumeAtlas, Is.Null);
            Assert.That(texture == null, Is.True, "Play transition must destroy an unsaved native atlas owned by the editor backend.");
        }

        // EnteredPlayMode can precede live Udon heap initialization. Runtime resources must be
        // published on a delayed editor turn instead of relying on an already-open Inspector to
        // copy its proxy after Udon becomes ready.
        [Test]
        public void EnteredPlayModeDefersRuntimeDependencyPublication() {
            System.Type preprocessorType = GetLightVolumePreprocessorType();
            MethodInfo onPlayModeStateChanged = preprocessorType.GetMethod("OnPlayModeStateChanged", _nonPublicStaticFlags);
            MethodInfo cancelQueued = preprocessorType.GetMethod("CancelQueuedPrimaryRuntimeDependencies", _nonPublicStaticFlags);
            FieldInfo queuedField = preprocessorType.GetField("_playModeRuntimePrepareQueued", _nonPublicStaticFlags);
            Assert.That(onPlayModeStateChanged, Is.Not.Null);
            Assert.That(cancelQueued, Is.Not.Null);
            Assert.That(queuedField, Is.Not.Null);

            onPlayModeStateChanged.Invoke(null, new object[] { PlayModeStateChange.EnteredPlayMode });
            Assert.That((bool)queuedField.GetValue(null), Is.True);

            cancelQueued.Invoke(null, null);
            Assert.That((bool)queuedField.GetValue(null), Is.False);
        }

        // Replacing an atlas must not make the backend the owner of a transient texture supplied by
        // external editor code. Only atlases previously tracked by this backend may be destroyed.
        [Test]
        public void CompleteAtlasPreservesExternallyOwnedPreviousTexture() {
            LightVolumeManager manager = CreateComponent<LightVolumeManager>("External Transient Atlas Owner");
            manager.transform.SetAsFirstSibling();
            Texture3D previous = CreateTexture3D("External Previous Atlas");
            Texture3D replacement = CreateTexture3D("Backend Replacement Atlas");
            manager.LightVolumeAtlasBase = previous;
            manager.LightVolumeAtlas = previous;
            MethodInfo completeAtlas = typeof(LightVolumeManagerEditorBackend).GetMethod("CompleteAtlas", _nonPublicStaticFlags);
            MethodInfo onPlayModeStateChanged = typeof(LightVolumeManagerEditorBackend).GetMethod("OnPlayModeStateChanged", _nonPublicStaticFlags);
            Assert.That(completeAtlas, Is.Not.Null);
            Assert.That(onPlayModeStateChanged, Is.Not.Null);

            completeAtlas.Invoke(null, new object[] {
                manager,
                new LightVolumeInstance[0],
                new Atlas3D { Texture = replacement, BoundsUvwMin = new Vector3[0], BoundsUvwMax = new Vector3[0] }
            });

            Assert.That(manager.LightVolumeAtlasBase, Is.SameAs(replacement));
            Assert.That(previous == null, Is.False, "The backend must preserve an externally-owned transient atlas.");

            onPlayModeStateChanged.Invoke(null, new object[] { PlayModeStateChange.ExitingEditMode });
            Assert.That(replacement == null, Is.True, "The backend must still release its own replacement atlas.");
            Assert.That(previous == null, Is.False);
        }

        [Test]
        public void ShadowDefaultsUseLowBiasAndPlatformSpecificVariance() {
            PointLightVolumeInstance point = CreateComponent<PointLightVolumeInstance>("Default Shadow Point");
            LightVolumeManager manager = CreateComponent<LightVolumeManager>("Default Shadow Manager");

            Assert.That(point.Bias, Is.EqualTo(0.01f).Within(Epsilon));
            Assert.That(manager.ShadowMinVarianceDesktop, Is.Zero);
            Assert.That(manager.ShadowMinVarianceMobile, Is.EqualTo(1f));
            Assert.That(manager.ShadowMinVariance, Is.EqualTo(0.0001f).Within(0.0000001f));
            Assert.That(LightVolumeManagerEditorBackend.GetShadowMinVarianceValue(0f), Is.EqualTo(0.0001f).Within(0.0000001f));
            Assert.That(LightVolumeManagerEditorBackend.GetShadowMinVarianceValue(1f), Is.EqualTo(1f).Within(Epsilon));

#pragma warning disable CS0618
            LightVolumeSetup legacySetup = CreateComponent<LightVolumeSetup>("Legacy Default Shadow Setup");
#pragma warning restore CS0618
            PointLightVolume legacyPoint = CreateComponent<PointLightVolume>("Legacy Default Shadow Point");

            Assert.That(legacySetup.ShadowMinVariance, Is.Zero);
            Assert.That(legacySetup.ShadowMinVarianceMobile, Is.EqualTo(1f));
            Assert.That(legacyPoint.Bias, Is.EqualTo(0.01f).Within(Epsilon));
        }

        // External lightmappers consume manager-scoped unified volumes and world-space voxel centers.
        [Test]
        public void ManagerCustomProbeApiUsesUnifiedRegistryAndWorldSpaceVoxelCenters() {
            LightVolumeManager manager = CreateComponent<LightVolumeManager>("Custom Probe Manager");
            LightVolumeInstance volume = CreateComponent<LightVolumeInstance>("Custom Probe Volume");
            volume.LightVolumeManager = manager;
            volume.Bake = true;
            volume.AdaptiveResolution = false;
            volume.Resolution = new Vector3Int(2, 1, 1);
            volume.transform.SetPositionAndRotation(new Vector3(5f, 2f, -3f), Quaternion.Euler(0f, 90f, 0f));
            volume.transform.localScale = new Vector3(4f, 2f, 2f);
            manager.LightVolumeInstances = new[] { volume };

            Assert.That(manager.Editor.GetCustomProbesCount(), Is.EqualTo(1));
            Vector3[] probes = manager.Editor.GetCustomProbes(0);
            Assert.That(probes, Has.Length.EqualTo(2));
            AssertVector3Close(
                LVUtils.TransformPoint(new Vector3(-0.25f, 0f, 0f), LightVolumeTools.GetPosition(volume), LightVolumeTools.GetRotation(volume), LightVolumeTools.GetScale(volume)),
                probes[0]);
            AssertVector3Close(
                LVUtils.TransformPoint(new Vector3(0.25f, 0f, 0f), LightVolumeTools.GetPosition(volume), LightVolumeTools.GetRotation(volume), LightVolumeTools.GetScale(volume)),
                probes[1]);

            LogAssert.Expect(LogType.Error, "[LightVolumes] Custom probe Light Volume ID -1 is invalid. Available volume count: 1.");
            Assert.That(manager.Editor.GetCustomProbes(-1), Is.Empty);

            volume.Bake = false;
            Assert.That(manager.Editor.GetCustomProbesCount(), Is.Zero);
            volume.Bake = true;
            volume.gameObject.SetActive(false);
            Assert.That(manager.Editor.GetCustomProbesCount(), Is.Zero);
        }

        // A canonical manager pass after scene load must not make an already-normalized scene dirty.
        [Test]
        public void ManagerAuthoringNoOpDoesNotMarkUnifiedProxyDirty() {
            LightVolumeManager manager = CreateComponent<LightVolumeManager>("No Op Authoring Manager");
            LightVolumeManagerEditorBackend.ApplySettings(manager, true, updateVolumes: false);
            EditorUtility.ClearDirty(manager);

            LightVolumeManagerEditorBackend.ApplySettings(manager, true, updateVolumes: false);

            Assert.That(EditorUtility.IsDirty(manager), Is.False);

            manager.CustomTexturesWidth = 31;
            LightVolumeManagerEditorBackend.ApplySettings(manager, true, updateVolumes: false);
            Assert.That(manager.CustomTexturesWidth, Is.EqualTo(31));
            Assert.That(manager.CustomTexturesHeight, Is.EqualTo(31));
            Assert.That(EditorUtility.IsDirty(manager), Is.True);
        }

        // Deferred Bakery bakes can destroy every pre-bake scene object before loading the scene again.
        [Test]
        public void BakeryCompletionResolvesPrimaryManagerFromRestoredSceneInsteadOfDestroyedCache() {
            LightVolumeManager restoredManager = CreateComponent<LightVolumeManager>("Restored Bakery Manager");
            restoredManager.BakingMode = 1;

            LightVolumeManager destroyedManager = CreateComponent<LightVolumeManager>("Destroyed Bakery Manager");
            destroyedManager.BakingMode = 1;
            Object.DestroyImmediate(destroyedManager.gameObject);

            System.Type bakerType = typeof(LightVolumeManagerEditorBackend).Assembly.GetType("VRCLightVolumes.LightVolumeBaker");
            FieldInfo cachedManagerField = bakerType?.GetField("_bakeryManager", _nonPublicStaticFlags);
            MethodInfo resolveManager = bakerType?.GetMethod("ResolveBakeryCompletionManager", _nonPublicStaticFlags);
            Assert.That(bakerType, Is.Not.Null);
            Assert.That(cachedManagerField, Is.Not.Null);
            Assert.That(resolveManager, Is.Not.Null);

            object previousCache = cachedManagerField.GetValue(null);
            try {
                cachedManagerField.SetValue(null, destroyedManager);
                LightVolumeManager result = (LightVolumeManager)resolveManager.Invoke(null, null);

                Assert.That(result, Is.SameAs(restoredManager));
                Assert.That(ReferenceEquals(result, destroyedManager), Is.False);
            } finally {
                cachedManagerField.SetValue(null, previousCache);
            }
        }

        // Bakery's Legacy probe command does not raise the full-render event. Its dedicated
        // completion callback must still schedule the same final SH post-process.
        [Test]
        public void BakeryLegacyProbeCompletionQueuesProbePostProcess() {
            MethodInfo reset = typeof(LightVolumeBaker).GetMethod("ResetBakeryBakeState", _nonPublicStaticFlags);
            MethodInfo finishedProbes = typeof(LightVolumeBaker).GetMethod("OnBakeryProbesFinished", _nonPublicStaticFlags);
            FieldInfo probePending = typeof(LightVolumeBaker).GetField("_bakeryProbePostProcessPending", _nonPublicStaticFlags);
            FieldInfo completionQueued = typeof(LightVolumeBaker).GetField("_bakeryCompletionQueued", _nonPublicStaticFlags);
            Assert.That(reset, Is.Not.Null);
            Assert.That(finishedProbes, Is.Not.Null);
            Assert.That(probePending, Is.Not.Null);
            Assert.That(completionQueued, Is.Not.Null);

            reset.Invoke(null, null);
            try {
                finishedProbes.Invoke(null, new object[] { null, System.EventArgs.Empty });

                Assert.That((bool)probePending.GetValue(null), Is.True);
                Assert.That((bool)completionQueued.GetValue(null), Is.True);
            } finally {
                reset.Invoke(null, null);
            }
        }

        [Test]
        public void BakeryProbeCompletionPolicyMatchesSupportedModes() {
            Assert.That(BakeryEditorBridge.ClassifyProbeRender("L1", true, true, false, false, true), Is.EqualTo(BakeryEditorBridge.ProbeRenderMode.L1));
            Assert.That(BakeryEditorBridge.ClassifyProbeRender("L2", true, true, false, false, true), Is.EqualTo(BakeryEditorBridge.ProbeRenderMode.L2));
            Assert.That(BakeryEditorBridge.ClassifyProbeRender("Legacy", true, true, false, false, true), Is.EqualTo(BakeryEditorBridge.ProbeRenderMode.None));
            Assert.That(BakeryEditorBridge.ClassifyProbeRender("L1", false, true, false, false, true), Is.EqualTo(BakeryEditorBridge.ProbeRenderMode.None));
            Assert.That(BakeryEditorBridge.ClassifyProbeRender("L1", true, false, false, false, true), Is.EqualTo(BakeryEditorBridge.ProbeRenderMode.None));
            Assert.That(BakeryEditorBridge.ClassifyProbeRender("L1", true, true, true, false, true), Is.EqualTo(BakeryEditorBridge.ProbeRenderMode.None));
            Assert.That(BakeryEditorBridge.ClassifyProbeRender("L1", true, true, false, true, false), Is.EqualTo(BakeryEditorBridge.ProbeRenderMode.None));
            Assert.That(BakeryEditorBridge.ClassifyProbeRender("L1", true, true, false, true, true), Is.EqualTo(BakeryEditorBridge.ProbeRenderMode.L1));
        }

        // Unified authoring mirrors every supported texture source without serialized type metadata.
        [Test]
        public void PointLightAuthoringResolvesProjectionTextureSources() {
            PointLightVolumeInstance point = CreateComponent<PointLightVolumeInstance>("Unified Projection Point");
            Texture2D staticCookie = CreateTexture2D("Static Cookie");
            RenderTexture animatedCookie = CreateRenderTexture("Animated Cookie", 4, 4, 1, TextureDimension.Tex2D);
            RenderTexture animatedCubemap = CreateRenderTexture("Animated Cubemap", 4, 4, 1, TextureDimension.Cube);

            point.LightType = 1; // spot
            point.Projection = 2; // custom
            point.Cookie = staticCookie;
            point.EditorApplyAuthoringData(true, false, false);
            Assert.That(point.CustomTexture, Is.SameAs(staticCookie));
            Assert.That(point.ProjectionMode, Is.EqualTo(2));

            point.Cookie = animatedCookie;
            point.EditorApplyAuthoringData(true, false, false);
            Assert.That(point.CustomTexture, Is.SameAs(animatedCookie));

            point.LightType = 0; // point
            point.Cubemap = animatedCubemap;
            point.EditorApplyAuthoringData(true, false, false);
            Assert.That(point.CustomTexture, Is.SameAs(animatedCubemap));
            Assert.That(point.CustomTexture.dimension, Is.EqualTo(TextureDimension.Cube));

            point.LightType = 2; // area always uses Cookie as its projection source
            point.Cookie = staticCookie;
            point.EditorApplyAuthoringData(true, false, false);
            Assert.That(point.CustomTexture, Is.SameAs(staticCookie));
            Assert.That(point.ProjectionMode, Is.EqualTo(2));
        }

        // Spot shadow layout, animated source metadata and the public Shadows toggle share one unified source of truth.
        [Test]
        public void PointLightAuthoringResolvesSpotShadowLayoutAndDisabledState() {
            PointLightVolumeInstance point = CreateComponent<PointLightVolumeInstance>("Unified Shadow Point");
            Texture2D planarShadow = CreateTexture2D("Planar Shadow");
            RenderTexture cubemapShadow = CreateRenderTexture("Cubemap Shadow", 4, 4, 1, TextureDimension.Cube);

            point.LightType = 1; // spot
            point.Angle = 30f * Mathf.Deg2Rad;
            point.Shadows = true;
            point.ShadowMap = planarShadow;
            point.EditorApplyAuthoringData(false, true, false);

            Assert.That(point.ShouldBakeCubemapShadows(), Is.False);
            Assert.That(point.ShadowMapTexture, Is.SameAs(planarShadow));
            Assert.That(point.ShadowMapUsesCubemap, Is.False);
            Assert.That(point.AutoUpdateShadowMap, Is.False);
            Assert.That(point.ShadowMapID, Is.EqualTo(0f).Within(Epsilon));

            point.ShadowMap = cubemapShadow;
            point.EditorApplyAuthoringData(false, true, false);
            Assert.That(point.ShadowMapTextureIsCubemap, Is.True);
            Assert.That(point.ShadowMapUsesCubemap, Is.True);
            Assert.That(point.AutoUpdateShadowMap, Is.True);

            point.Shadows = false;
            point.EditorApplyAuthoringData(false, true, false);
            Assert.That(point.ShadowMapTexture, Is.Null);
            Assert.That(point.ShadowMapMaterial, Is.Null);
            Assert.That(point.ShadowMapUsesCubemap, Is.False);
            Assert.That(point.AutoUpdateShadowMap, Is.False);
            Assert.That(point.ShadowMapID, Is.EqualTo(-1f).Within(Epsilon));
        }

        // Shared and readback-only point-light dependencies are runtime caches, never scene payload.
        [Test]
        public void PointLightRuntimeCachesAreNotSerialized() {
            AssertRuntimeCacheFieldIsNonSerialized(nameof(PointLightVolumeInstance.RuntimeShadowCamera));
            AssertRuntimeCacheFieldIsNonSerialized(nameof(PointLightVolumeInstance.RuntimeShadowDepthEncodeMaterial));
            AssertRuntimeCacheFieldIsNonSerialized(nameof(PointLightVolumeInstance.RuntimeShadowBlurMaterial));
            AssertRuntimeCacheFieldIsNonSerialized(nameof(PointLightVolumeInstance.AreaLightFallbackColor));
            AssertRuntimeCacheFieldIsNonSerialized(nameof(PointLightVolumeInstance.AreaCookieAverageCustomId));
            AssertRuntimeCacheFieldIsNonSerialized(nameof(PointLightVolumeInstance.AreaCookieAverageReadbackPending));
            AssertRuntimeCacheFieldIsNonSerialized(nameof(PointLightVolumeInstance.AreaCookieAverageReadbackDirty));
        }

        // Registration reuses the manager-owned runtime shadow dependencies instead of creating per-light copies.
        [Test]
        public void ManagerAssignsSharedRuntimeShadowDependenciesToRegisteredPointLight() {
            LightVolumeManager manager = CreateComponent<LightVolumeManager>("Shared Shadow Manager");
            manager.EnsureRuntimeShadowCamera();
            manager.RuntimeShadowDepthEncodeMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowDepthEncode");
            manager.RuntimeShadowBlurMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowRuntimeBlur");
            PointLightVolumeInstance point = CreateComponent<PointLightVolumeInstance>("Shared Shadow Point");

            point.LightVolumeManager = manager;
            point.RuntimeShadowCamera = null;
            point.RuntimeShadowDepthEncodeMaterial = null;
            point.RuntimeShadowBlurMaterial = null;
            manager.InitializePointLightVolume(point);

            Assert.That(point.RuntimeShadowCamera, Is.SameAs(manager.RuntimeShadowCamera));
            Assert.That(point.RuntimeShadowDepthEncodeMaterial, Is.SameAs(manager.RuntimeShadowDepthEncodeMaterial));
            Assert.That(point.RuntimeShadowBlurMaterial, Is.SameAs(manager.RuntimeShadowBlurMaterial));
            Assert.That(manager.PointLightVolumeInstances, Has.Member(point));
        }

        // Build cleanup strips duplicate editor payload while preserving final atlas, registries and runtime texture sources.
        [Test]
        public void BuildCleanupStripsUnifiedAuthoringReferencesAndPreservesRuntimePayload() {
            LightVolumeManager manager = CreateComponent<LightVolumeManager>("Build Cleanup Manager");
            LightVolumeInstance volume = CreateChildComponent<LightVolumeInstance>(manager.transform, "Build Cleanup Volume");
            PointLightVolumeInstance point = CreateChildComponent<PointLightVolumeInstance>(manager.transform, "Build Cleanup Point");
            GameObject excludedObject = CreateGameObject("Build Cleanup Shadow Exclusion");
            MeshRenderer excludedRenderer = excludedObject.AddComponent<MeshRenderer>();
            Texture3D atlasBase = CreateTexture3D("Build Cleanup Base Atlas");
            Texture3D finalAtlas = CreateTexture3D("Build Cleanup Final Atlas");
            Texture3D volumeTexture0 = CreateTexture3D("Build Cleanup Volume 0");
            Texture3D volumeTexture1 = CreateTexture3D("Build Cleanup Volume 1");
            Texture3D volumeTexture2 = CreateTexture3D("Build Cleanup Volume 2");
            Texture2D falloff = CreateTexture2D("Build Cleanup Falloff");
            Texture2D cookie = CreateTexture2D("Build Cleanup Cookie");
            Cubemap cubemap = CreateCubemap("Build Cleanup Cubemap");
            Texture2D shadow = CreateTexture2D("Build Cleanup Shadow");
            RenderTexture customArray = CreateRenderTexture("Build Cleanup Custom Array", 4, 4, 1, TextureDimension.Tex2DArray);
            RenderTexture shadowArray = CreateRenderTexture("Build Cleanup Shadow Array", 4, 4, 1, TextureDimension.Tex2DArray);
            RenderTexture processorTarget = CreateRenderTexture("Build Cleanup Processor", 4, 4, 1, TextureDimension.Tex3D);
            Material processorMaterial = CreateMaterial("Hidden/CubeFace");

            manager.LightVolumeAtlasBase = atlasBase;
            manager.LightVolumeAtlas = finalAtlas;
            manager.CustomTextures = customArray;
            manager.ShadowTextures = shadowArray;
            manager.AtlasPostProcessorTargets = new[] { processorTarget };
            manager.AtlasPostProcessorMaterials = new[] { processorMaterial };
            manager.AtlasPostProcessorTextureNames = new[] { "_MainTex" };
            manager.LightVolumeInstances = new[] { volume };
            manager.PointLightVolumeInstances = new[] { point };

            volume.Texture0 = volumeTexture0;
            volume.Texture1 = volumeTexture1;
            volume.Texture2 = volumeTexture2;
            point.FalloffLUT = falloff;
            point.Cookie = cookie;
            point.Cubemap = cubemap;
            point.ShadowMap = shadow;
            point.ExclusionMask = new Renderer[] { excludedRenderer };
            point.CustomTexture = cookie;
            point.CustomTextureMaterial = null;
            point.ProjectionMode = 2;
            point.ShadowMapTexture = shadow;
            point.ShadowMapMaterial = null;
            point.AutoUpdateShadowMap = true;
            point.ShadowMapID = 0f;
            point.ShadowMapTextureIsCubemap = true;
            point.ShadowMapTextureHasDepthSlices = true;
            point.ShadowBakeResolution = 256;
            point.RuntimeShadowBlurSamplePreset = 1;
            point.RuntimeShadowSphericalBlur = false;

            InvokePreprocessor("ClearManagerBuildOnlySerializedReferences", manager);
            InvokePreprocessor("ClearBuildOnlySerializedReferences", manager.gameObject);

            Assert.That(manager.LightVolumeAtlas, Is.SameAs(finalAtlas));
            Assert.That(manager.LightVolumeAtlasBase, Is.Null);
            Assert.That(manager.CustomTextures, Is.Null);
            Assert.That(manager.ShadowTextures, Is.Null);
            Assert.That(manager.AtlasPostProcessorTargets, Is.Empty);
            Assert.That(manager.AtlasPostProcessorMaterials, Is.Empty);
            Assert.That(manager.AtlasPostProcessorTextureNames, Is.Empty);
            Assert.That(manager.LightVolumeInstances, Is.EqualTo(new[] { volume }));
            Assert.That(manager.PointLightVolumeInstances, Is.EqualTo(new[] { point }));
            Assert.That(volume.Texture0, Is.Null);
            Assert.That(volume.Texture1, Is.Null);
            Assert.That(volume.Texture2, Is.Null);
            Assert.That(point.FalloffLUT, Is.Null);
            Assert.That(point.Cookie, Is.Null);
            Assert.That(point.Cubemap, Is.Null);
            Assert.That(point.ShadowMap, Is.Null);
            Assert.That(point.ExclusionMask, Is.EqualTo(new Renderer[] { excludedRenderer }));
            Assert.That(point.CustomTexture, Is.SameAs(cookie));
            Assert.That(point.ProjectionMode, Is.EqualTo(2));
            Assert.That(point.ShadowMapTexture, Is.SameAs(shadow));
            Assert.That(point.AutoUpdateShadowMap, Is.True);
            Assert.That(point.ShadowMapID, Is.EqualTo(0f).Within(Epsilon));
            Assert.That(point.ShadowMapTextureIsCubemap, Is.True);
            Assert.That(point.ShadowMapTextureHasDepthSlices, Is.True);
            Assert.That(point.ShadowBakeResolution, Is.EqualTo(256));
            Assert.That(point.RuntimeShadowBlurSamplePreset, Is.EqualTo(1));
            Assert.That(point.RuntimeShadowSphericalBlur, Is.False);
        }

        // Bake In Game keeps its editor source before build stripping but starts with an empty runtime shadow source.
        [Test]
        public void BuildPreparationClearsBakeInGameRuntimeShadowSourceBeforeAuthoringStrip() {
            LightVolumeManager manager = CreateComponent<LightVolumeManager>("Build Runtime Shadow Manager");
            PointLightVolumeInstance point = CreateComponent<PointLightVolumeInstance>("Build Runtime Shadow Point");
            Texture2D shadow = CreateTexture2D("Build Runtime Shadow Source");
            manager.ShadowTexturesWidth = 512;
            manager.ShadowTexturesHeight = 512;
            point.LightVolumeManager = manager;
            point.Shadows = true;
            point.BakeInGame = true;
            point.RuntimeShadowBlurSamplePreset = 1;
            point.RuntimeShadowSphericalBlur = false;
            point.ShadowBakeResolution = 128;
            point.ShadowMap = shadow;
            point.ShadowMapTexture = shadow;
            point.AutoUpdateShadowMap = true;
            point.ShadowMapID = 0f;
            point.ShadowMapTextureIsCubemap = true;
            point.ShadowMapTextureHasDepthSlices = true;

            MethodInfo prepare = GetLightVolumePreprocessorType().GetMethod("PreparePointLightForRuntime", _nonPublicStaticFlags);
            Assert.That(prepare, Is.Not.Null);
            prepare.Invoke(null, new object[] { point, manager, true });

            Assert.That(point.ShadowMap, Is.SameAs(shadow));
            Assert.That(point.BakeInGame, Is.True);
            Assert.That(point.RuntimeShadowResolution, Is.EqualTo(128));
            Assert.That(point.RuntimeShadowBlurSamplePreset, Is.EqualTo(1));
            Assert.That(point.RuntimeShadowSphericalBlur, Is.False);
            Assert.That(point.RuntimeShadowDirectOutput, Is.False);
            Assert.That(point.RuntimeShadowCamera, Is.SameAs(manager.RuntimeShadowCamera));
            Assert.That(point.ShadowMapTexture, Is.Null);
            Assert.That(point.ShadowMapMaterial, Is.Null);
            Assert.That(point.AutoUpdateShadowMap, Is.False);
            Assert.That(point.ShadowMapID, Is.EqualTo(-1f).Within(Epsilon));
            Assert.That(point.ShadowMapTextureIsCubemap, Is.False);
            Assert.That(point.ShadowMapTextureHasDepthSlices, Is.False);

            MethodInfo strip = GetLightVolumePreprocessorType().GetMethod("ClearPointLightBuildOnlySerializedReferences", _nonPublicStaticFlags);
            Assert.That(strip, Is.Not.Null);
            strip.Invoke(null, new object[] { point });
            Assert.That(point.ShadowMap, Is.Null);
        }

        // The shared authoring selector inherits the Manager size at Default and overrides both editor and runtime bake resolution explicitly.
        [Test]
        public void ShadowBakeResolutionResolverUsesManagerDefaultAndPerLightOverride() {
            LightVolumeManager manager = CreateComponent<LightVolumeManager>("Shadow Resolution Resolver Manager");
            PointLightVolumeInstance point = CreateComponent<PointLightVolumeInstance>("Shadow Resolution Resolver Point");
            manager.ShadowTexturesWidth = 512;

            point.ShadowBakeResolution = 0;
            Assert.That(PointLightShadowBaker.ResolveShadowBakeResolution(point, manager), Is.EqualTo(512));

            point.ShadowBakeResolution = 128;
            Assert.That(PointLightShadowBaker.ResolveShadowBakeResolution(point, manager), Is.EqualTo(128));

            point.ShadowBakeResolution = -1;
            Assert.That(PointLightShadowBaker.ResolveShadowBakeResolution(point, manager), Is.EqualTo(512));

            point.ShadowBakeResolution = 1;
            Assert.That(PointLightShadowBaker.ResolveShadowBakeResolution(point, manager), Is.EqualTo(16));

            point.ShadowBakeResolution = 4096;
            Assert.That(PointLightShadowBaker.ResolveShadowBakeResolution(point, manager), Is.EqualTo(2048));

            point.ShadowBakeResolution = 0;
            point.RuntimeShadowResolution = 4096;
            Assert.That(PointLightShadowBaker.ResolveShadowBakeResolution(point, null), Is.EqualTo(2048));
            Assert.That(PointLightShadowBaker.ResolveShadowBakeResolution(null, null), Is.EqualTo(16));
        }

        // Manager-created texture arrays must never become serialized scene or asset payload.
        [Test]
        public void RuntimeTextureArraysUseHideAndDontSave() {
            LightVolumeManager manager = CreateComponent<LightVolumeManager>("Runtime Texture Cache Manager");
            PointLightVolumeInstance point = CreateComponent<PointLightVolumeInstance>("Runtime Texture Cache Point");
            RenderTexture customSource = CreateRenderTexture("Runtime Cookie Source", 4, 4, 1, TextureDimension.Tex2D);
            RenderTexture shadowSource = CreateRenderTexture("Runtime Shadow Source", 4, 4, 6, TextureDimension.Tex2DArray);
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;
            manager.ShadowTexturesWidth = 4;
            manager.ShadowTexturesHeight = 4;
            point.LightVolumeManager = manager;
            point.IsActive = true;
            point.LightType = 1; // spot keeps the cookie in one 2D array slice
            point.SetCustomTexture(customSource);
            point.ShadowMapID = 0f;
            point.ShadowMapTexture = shadowSource;
            point.AutoUpdateShadowMap = true;
            point.ShadowMapTextureHasDepthSlices = true;
            point.ShadowMapUsesCubemap = true;
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeCustomTextures();
            manager.ReinitializeShadowTextures();

            Assert.That(manager.CustomTextures, Is.Not.Null);
            Assert.That(manager.ShadowTextures, Is.Not.Null);
            Assert.That(manager.CustomTextures.hideFlags, Is.EqualTo(HideFlags.HideAndDontSave));
            Assert.That(manager.ShadowTextures.hideFlags, Is.EqualTo(HideFlags.HideAndDontSave));
        }

        private T CreateComponent<T>(string name) where T : Component {
            return CreateGameObject(name).AddComponent<T>();
        }

        private T CreateChildComponent<T>(Transform parent, string name) where T : Component {
            GameObject gameObject = CreateGameObject(name);
            gameObject.transform.SetParent(parent, false);
            return gameObject.AddComponent<T>();
        }

        private GameObject CreateGameObject(string name) {
            GameObject gameObject = new GameObject(name);
            _createdObjects.Add(gameObject);
            return gameObject;
        }

        private Texture2D CreateTexture2D(string name) {
            Texture2D texture = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = name };
            _createdObjects.Add(texture);
            return texture;
        }

        private Texture3D CreateTexture3D(string name) {
            Texture3D texture = new Texture3D(1, 1, 1, TextureFormat.RGBAHalf, false) { name = name };
            _createdObjects.Add(texture);
            return texture;
        }

        private Cubemap CreateCubemap(string name) {
            Cubemap texture = new Cubemap(4, TextureFormat.RGBA32, false) { name = name };
            _createdObjects.Add(texture);
            return texture;
        }

        private RenderTexture CreateRenderTexture(string name, int width, int height, int depth, TextureDimension dimension) {
            RenderTexture texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) {
                name = name,
                dimension = dimension,
                volumeDepth = depth
            };
            texture.Create();
            _createdObjects.Add(texture);
            return texture;
        }

        private Material CreateMaterial(string shaderName) {
            Shader shader = Shader.Find(shaderName);
            Assert.That(shader, Is.Not.Null, $"Required test shader '{shaderName}' was not found.");
            Material material = new Material(shader);
            _createdObjects.Add(material);
            return material;
        }

        private static void InvokePreprocessor(string methodName, object argument) {
            MethodInfo method = GetLightVolumePreprocessorType().GetMethod(methodName, _nonPublicStaticFlags);
            Assert.That(method, Is.Not.Null);
            method.Invoke(null, new[] { argument });
        }

        private static System.Type GetLightVolumePreprocessorType() {
            System.Reflection.Assembly[] assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++) {
                System.Type type = assemblies[i].GetType("VRCLightVolumes.LightVolumePreprocessor");
                if (type != null) return type;
            }

            Assert.Fail("LightVolumePreprocessor type was not found.");
            return null;
        }

        private static void AssertRuntimeCacheFieldIsNonSerialized(string fieldName) {
            FieldInfo field = typeof(PointLightVolumeInstance).GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
            Assert.That(field, Is.Not.Null);
            Assert.That(field.IsNotSerialized, Is.True, $"{fieldName} must stay a runtime-only cache.");
        }

        private static void AssertVector3Close(Vector3 expected, Vector3 actual) {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(Epsilon));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(Epsilon));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(Epsilon));
        }
    }
}
