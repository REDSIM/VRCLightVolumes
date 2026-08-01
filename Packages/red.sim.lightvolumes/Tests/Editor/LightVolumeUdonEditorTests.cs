using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VRCLightVolumes.Tests {
    [Category("Udon")]
    public class LightVolumeUdonEditorTests {
        private const float Epsilon = 0.0001f;
        private const string CustomRenderTextureInfoProperty = "_CustomRenderTextureInfo";
        private const string LightVolumesIncludePath = "Shaders/LightVolumes.cginc";
        private const string RuntimeShadowBlurShaderPath = "Shaders/Internal/PointLightShadowRuntimeBlur.shader";

        private static readonly int _lightVolumeInvLocalEdgeSmoothID = Shader.PropertyToID("_UdonLightVolumeInvLocalEdgeSmooth");
        private static readonly int _lightVolumeColorID = Shader.PropertyToID("_UdonLightVolumeColor");
        private static readonly int _lightVolumeCountID = Shader.PropertyToID("_UdonLightVolumeCount");
        private static readonly int _lightVolumeAdditiveCountID = Shader.PropertyToID("_UdonLightVolumeAdditiveCount");
        private static readonly int _lightVolumeAdditiveMaxOverdrawID = Shader.PropertyToID("_UdonLightVolumeAdditiveMaxOverdraw");
        private static readonly int _lightVolumeEnabledID = Shader.PropertyToID("_UdonLightVolumeEnabled");
        private static readonly int _lightVolumeProbesBlendID = Shader.PropertyToID("_UdonLightVolumeProbesBlend");
        private static readonly int _lightVolumeSharpBoundsID = Shader.PropertyToID("_UdonLightVolumeSharpBounds");
        private static readonly int _lightVolumeRotationID = Shader.PropertyToID("_UdonLightVolumeRotation");
        private static readonly int _lightVolumeInvWorldMatrixID = Shader.PropertyToID("_UdonLightVolumeInvWorldMatrix");
        private static readonly int _lightVolumeUvwScaleID = Shader.PropertyToID("_UdonLightVolumeUvwScale");
        private static readonly int _lightVolumeUvwID = Shader.PropertyToID("_UdonLightVolumeUvw");
        private static readonly int _lightVolumeOcclusionCountID = Shader.PropertyToID("_UdonLightVolumeOcclusionCount");
        private static readonly int _pointLightPositionID = Shader.PropertyToID("_UdonPointLightVolumePosition");
        private static readonly int _pointLightColorID = Shader.PropertyToID("_UdonPointLightVolumeColor");
        private static readonly int _pointLightDirectionID = Shader.PropertyToID("_UdonPointLightVolumeDirection");
        private static readonly int _pointLightExtraDataID = Shader.PropertyToID("_UdonPointLightVolumeExtraData");
        private static readonly int _pointLightCustomIdID = Shader.PropertyToID("_UdonPointLightVolumeCustomID");
        private static readonly int _pointLightCountID = Shader.PropertyToID("_UdonPointLightVolumeCount");
        private static readonly int _pointLightCubeCountID = Shader.PropertyToID("_UdonPointLightVolumeCubeCount");
        private static readonly int _pointLightTextureID = Shader.PropertyToID("_UdonPointLightVolumeTexture");
        private static readonly int _pointLightTextureTexelCountID = Shader.PropertyToID("_UdonPointLightVolumeTextureTexelCount");
        private static readonly int _pointLightShadowReprojectionDataID = Shader.PropertyToID("_UdonPointLightVolumeShadowReprojectionData");
        private static readonly int _pointLightShadowRotationDataID = Shader.PropertyToID("_UdonPointLightVolumeShadowRotationData");
        private static readonly int _pointLightShadowCubeCountID = Shader.PropertyToID("_UdonPointLightVolumeShadowCubeCount");
        private static readonly int _pointLightShadowCountID = Shader.PropertyToID("_UdonPointLightVolumeShadowCount");
        private static readonly int _pointLightShadowTextureID = Shader.PropertyToID("_UdonPointLightVolumeShadowTexture");
        private static readonly int _pointLightShadowReceiverParamsID = Shader.PropertyToID("_UdonPointLightVolumeShadowReceiverParams");
        private static readonly int _lightBrightnessCutoffID = Shader.PropertyToID("_UdonLightBrightnessCutoff");
        private static readonly int _forceSceneLightingID = Shader.PropertyToID("_UdonForceSceneLighting");
        private static readonly BindingFlags _lifecycleMethodFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly FieldInfo _customTexturesDepthField = typeof(LightVolumeManager).GetField("_customTextureArrayDepth", _lifecycleMethodFlags);
        private static readonly FieldInfo _shadowTexturesDepthField = typeof(LightVolumeManager).GetField("_shadowTextureArrayDepth", _lifecycleMethodFlags);
        private static readonly FieldInfo _customCubemapTextureCountField = typeof(LightVolumeManager).GetField("_customCubemapTextureCount", _lifecycleMethodFlags);
        private static readonly FieldInfo _customSingleTextureCountField = typeof(LightVolumeManager).GetField("_customSingleTextureCount", _lifecycleMethodFlags);
        private static readonly FieldInfo _customSingleMaterialCountField = typeof(LightVolumeManager).GetField("_customSingleMaterialCount", _lifecycleMethodFlags);
        private static readonly FieldInfo _customSingleMaterialAutoUpdatesField = typeof(LightVolumeManager).GetField("_customSingleMaterialAutoUpdates", _lifecycleMethodFlags);
        private static readonly FieldInfo _shadowCubemapTextureCountField = typeof(LightVolumeManager).GetField("_shadowCubemapTextureCount", _lifecycleMethodFlags);
        private static readonly FieldInfo _shadowSingleTextureCountField = typeof(LightVolumeManager).GetField("_shadowSingleTextureCount", _lifecycleMethodFlags);
        private static readonly FieldInfo _pointLightCustomIDsField = typeof(LightVolumeManager).GetField("_pointLightCustomIDs", _lifecycleMethodFlags);
        private static readonly FieldInfo _pointLightShadowIDsField = typeof(LightVolumeManager).GetField("_pointLightShadowIDs", _lifecycleMethodFlags);
        private static readonly FieldInfo _pointLightAreaCookieAverageColorsField = typeof(LightVolumeManager).GetField("_pointLightAreaCookieAverageColors", _lifecycleMethodFlags);
        private static readonly FieldInfo _pointLightArraysDirtyField = typeof(LightVolumeManager).GetField("_pointLightArraysDirty", _lifecycleMethodFlags);
        private static readonly FieldInfo _updatePointLightBuffersField = typeof(LightVolumeManager).GetField("_updatePointLightBuffers", _lifecycleMethodFlags);
        private static readonly FieldInfo _updatePointLightPositionBufferField = typeof(LightVolumeManager).GetField("_updatePointLightPositionBuffer", _lifecycleMethodFlags);
        private static readonly FieldInfo _enabledPointIDsField = typeof(LightVolumeManager).GetField("_enabledPointIDs", _lifecycleMethodFlags);
        private static readonly FieldInfo _volumeDataUpdateRequestedField = typeof(LightVolumeManager).GetField("_volumeDataUpdateRequested", _lifecycleMethodFlags);
        private static readonly FieldInfo _isUpdatingVolumesField = typeof(LightVolumeManager).GetField("_isUpdatingVolumes", _lifecycleMethodFlags);
        private static readonly FieldInfo _dummyRTField = typeof(LightVolumeManager).GetField("_dummyRT", _lifecycleMethodFlags);
        private static readonly MethodInfo _uploadAreaCookieAverageColorMethod = typeof(LightVolumeManager).GetMethod("UploadAreaCookieAverageColor", _lifecycleMethodFlags);
        private static readonly MethodInfo _updateAutoUpdatedVolumeChangesMethod = typeof(LightVolumeManager).GetMethod("UpdateAutoUpdatedVolumeChanges", _lifecycleMethodFlags);
        private static readonly MethodInfo _uploadAutoUpdatedVolumeChangesMethod = typeof(LightVolumeManager).GetMethod("UploadAutoUpdatedVolumeChanges", _lifecycleMethodFlags);
        private static readonly BindingFlags _staticMigrationMethodFlags = BindingFlags.Static | BindingFlags.NonPublic;

        private readonly List<UnityEngine.Object> _createdObjects = new List<UnityEngine.Object>();

        // Resets process-wide shader globals before every test case.
        [SetUp]
        public void SetUp() {
            ResetShaderGlobals();
        }

        // The migration cache keeps only exact legacy MonoBehaviour documents in one YAML pass.
        [Test]
        public void SceneYamlLegacyRuntimeBlockExtractionFiltersDocumentsAndIds() {
            string sceneYaml =
                "--- !u!114 &12\n" +
                "MonoBehaviour:\n" +
                "  RelativeRotation: {x: 0, y: 0, z: 0, w: 1}\n" +
                "--- !u!114 &120\n" +
                "MonoBehaviour:\n" +
                "  PositionData: {x: 1, y: 2, z: 3, w: 4}\n" +
                "--- !u!114 &23\n" +
                "MonoBehaviour:\n" +
                "  Position: {x: 1, y: 2, z: 3}\n" +
                "  RelativeRotationRow0: {x: 1, y: 0, z: 0}\n" +
                "  BoundsUvwMin0: {x: 0, y: 0, z: 0, w: 1}\n" +
                "--- !u!1 &34\n" +
                "GameObject:\n" +
                "  PositionData: ignored\n" +
                "--- !u!114 &45\n" +
                "MonoBehaviour:\n" +
                "  m_Name: Current Data Only\n" +
                "--- !u!114 &56\n" +
                "MonoBehaviour:\n" +
                "  _legacyDirectionData: {x: 0, y: 0, z: 1, w: 0}\n";

            MethodInfo method = typeof(LightVolumeMigration).GetMethod("BuildLegacyRuntimeBlocks", _staticMigrationMethodFlags);
            Assert.That(method, Is.Not.Null);
            Dictionary<ulong, string> blocks = (Dictionary<ulong, string>)method.Invoke(null, new object[] { sceneYaml });

            Assert.That(blocks.Count, Is.EqualTo(3));
            Assert.That(blocks.ContainsKey(12), Is.True);
            Assert.That(blocks.ContainsKey(120), Is.True);
            Assert.That(blocks.ContainsKey(56), Is.True);
            Assert.That(blocks[12], Does.Contain("RelativeRotation:"));
            Assert.That(blocks[120], Does.Contain("PositionData:"));
            Assert.That(blocks[56], Does.Contain("_legacyDirectionData:"));
            Assert.That(blocks.ContainsKey(23), Is.False);
            Assert.That(blocks.ContainsKey(34), Is.False);
            Assert.That(blocks.ContainsKey(45), Is.False);
        }

        // Destroys all temporary scene and texture objects created by a test case.
        [TearDown]
        public void TearDown() {
            AsyncGPUReadback.WaitAllRequests();
            ResetShaderGlobals();
            for (int i = _createdObjects.Count - 1; i >= 0; i--) {
                DestroyTestObject(_createdObjects[i]);
            }
            _createdObjects.Clear();
        }

        // Returns a private LightVolumeManager field used by focused regression tests.
        private static T GetManagerField<T>(LightVolumeManager manager, FieldInfo field) {
            return (T)field.GetValue(manager);
        }

        // Assigns a private LightVolumeManager field used by focused regression tests.
        private static void SetManagerField<T>(LightVolumeManager manager, FieldInfo field, T value) {
            field.SetValue(manager, value);
        }

        // Injects an area cookie average readback result into the manager's private callback path.
        private static void UploadAreaCookieAverageColor(LightVolumeManager manager, int customId, Color color) {
            Assert.That(_uploadAreaCookieAverageColorMethod, Is.Not.Null);
            AsyncGPUReadback.WaitAllRequests();
            _uploadAreaCookieAverageColorMethod.Invoke(manager, new object[] { customId, color });
        }

        // Returns the cached area-cookie fallback average for a point light registry index.
        private static Color GetAreaCookieAverageColor(LightVolumeManager manager, int registryIndex) {
            Color[] colors = GetManagerField<Color[]>(manager, _pointLightAreaCookieAverageColorsField);
            Assert.That(colors, Is.Not.Null);
            Assert.That(registryIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(registryIndex, Is.LessThan(colors.Length));
            return colors[registryIndex];
        }

        // Reads the runtime blur shader from either a Unity project root or this package directory.
        private static string ReadRuntimeShadowBlurShaderSource() {
            string projectPackagePath = Path.Combine("Packages", "red.sim.lightvolumes", RuntimeShadowBlurShaderPath);
            string packagePath = RuntimeShadowBlurShaderPath;
            string shaderPath = File.Exists(projectPackagePath) ? projectPackagePath : packagePath;
            Assert.That(File.Exists(shaderPath), Is.True, shaderPath + " was not found");
            return File.ReadAllText(shaderPath);
        }

        // Reads the public lighting include from either a Unity project root or this package directory.
        private static string ReadLightVolumesIncludeSource() {
            string projectPackagePath = Path.Combine("Packages", "red.sim.lightvolumes", LightVolumesIncludePath);
            string packagePath = LightVolumesIncludePath;
            string shaderPath = File.Exists(projectPackagePath) ? projectPackagePath : packagePath;
            Assert.That(File.Exists(shaderPath), Is.True, shaderPath + " was not found");
            return File.ReadAllText(shaderPath);
        }

        // Depth slice count must scale only Z; it must never silently trade away camera angular resolution.
        [Test]
        public void FroxelSlicesDoNotChangeAngularResolution() {
            LightVolumeManager manager = CreateManager("Independent Froxel Axes Manager", true);

            MethodInfo buildMethod = typeof(LightVolumeManager).GetMethod("BuildClustering", _lifecycleMethodFlags);
            FieldInfo unsupportedField = typeof(LightVolumeManager).GetField("_clusteringUnsupported", _lifecycleMethodFlags);
            FieldInfo fineGridField = typeof(LightVolumeManager).GetField("_fineGridParams", _lifecycleMethodFlags);
            Assert.That(buildMethod, Is.Not.Null);
            Assert.That(unsupportedField, Is.Not.Null);
            Assert.That(fineGridField, Is.Not.Null);

            unsupportedField.SetValue(manager, true); // Exercise layout math without allocating GPU textures.
            manager.FroxelDensity = 1f;
            object[] arguments = {
                Vector3.zero,
                Vector3.right,
                Vector3.up,
                Vector3.forward,
                60f,
                16f / 9f,
                0.3f,
                100f,
                0f,
                0f,
                null
            };

            manager.FroxelSlices = 8;
            buildMethod.Invoke(manager, arguments);
            Vector4 shallowGrid = (Vector4)fineGridField.GetValue(manager);

            manager.FroxelSlices = 200;
            buildMethod.Invoke(manager, arguments);
            Vector4 deepGrid = (Vector4)fineGridField.GetValue(manager);

            float horizontalFov = Mathf.Rad2Deg * 2f * Mathf.Atan(Mathf.Tan(30f * Mathf.Deg2Rad) * (16f / 9f));
            int expectedColumns = Mathf.CeilToInt(horizontalFov);
            int expectedRows = 60;

            Assert.That(shallowGrid.x, Is.EqualTo(expectedColumns));
            Assert.That(shallowGrid.z, Is.EqualTo(expectedRows));
            Assert.That(shallowGrid.y, Is.EqualTo(8));
            Assert.That(deepGrid.x, Is.EqualTo(expectedColumns));
            Assert.That(deepGrid.z, Is.EqualTo(expectedRows));
            Assert.That(deepGrid.y, Is.EqualTo(200));
        }

        // Both Scene View clustering previews must resolve to supported shaders on the active editor graphics API.
        [Test]
        public void FroxelPreviewShadersAreSupported() {
            Shader fine = Shader.Find("Hidden/LV_DebugDisplayFineClustering");
            Shader coarse = Shader.Find("Hidden/LV_DebugDisplayCoarseClustering");

            Assert.That(fine, Is.Not.Null);
            Assert.That(fine.isSupported, Is.True);
            Assert.That(coarse, Is.Not.Null);
            Assert.That(coarse.isSupported, Is.True);
        }

        // Native editor texture loss must invalidate the C# cache even when camera and layout values stay unchanged.
        [Test]
        public void FroxelClusteringRecreatesReleasedNativeTexture() {
            if (SystemInfo.graphicsShaderLevel < 35 || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBInt)) {
                Assert.Ignore("The active graphics API does not support the packed froxel mask format.");
            }
            Shader clusteringShader = Shader.Find("Hidden/VRCLV/FroxelClusteringBuild");
            if (clusteringShader == null || !clusteringShader.isSupported) {
                Assert.Ignore("The froxel clustering build shader is unavailable on the active graphics API.");
            }

            LightVolumeManager manager = CreateManager("Released Froxel Texture Manager", false);
            manager.Clustering = true;
            manager.ClusteringMinLights = 1;
            manager.FroxelDensity = 0.1f;
            manager.FroxelSlices = 8;
            manager.FroxelCoarse = 2;
            PointLightVolumeInstance point = CreatePointLight(manager, "Released Froxel Texture Light", true);
            manager.PointLightVolumeInstances = new[] { point };
            manager.UpdateVolumes();

            GameObject cameraObject = CreateGameObject("Released Froxel Texture Camera", false);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 60f;
            camera.aspect = 16f / 9f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;

            FieldInfo clusterMaskField = typeof(LightVolumeManager).GetField("_clusterMask", _lifecycleMethodFlags);
            Assert.That(clusterMaskField, Is.Not.Null);
            manager.UpdateClusteringFromCamera(camera);
            RenderTexture clusterMask = (RenderTexture)clusterMaskField.GetValue(manager);
            Assert.That(clusterMask, Is.Not.Null);
            Assert.That(clusterMask.IsCreated(), Is.True);
            Assert.That(manager.ClusteringMaterial, Is.Null, "Scene View preview must not serialize its generated material on the manager");
            PropertyInfo generatedMaterialProperty = typeof(LightVolumeManager).GetProperty("_generatedClusteringMaterial", _lifecycleMethodFlags);
            Assert.That(generatedMaterialProperty, Is.Not.Null);
            object generatedMaterial = generatedMaterialProperty.GetValue(manager);
            Assert.That(generatedMaterial, Is.Not.Null);
            Assert.That(manager.ClusteringMaterialPreview, Is.SameAs(generatedMaterial), "The editor Debug view must expose the generated preview material");

            clusterMask.Release();
            Assert.That(clusterMask.IsCreated(), Is.False);
            manager.UpdateClusteringFromCamera(camera);

            RenderTexture recoveredMask = (RenderTexture)clusterMaskField.GetValue(manager);
            Assert.That(recoveredMask, Is.Not.Null);
            Assert.That(recoveredMask.IsCreated(), Is.True);

            int clusteringEnabledID = Shader.PropertyToID("_UdonClusteringEnabled");
            int fineMaskID = Shader.PropertyToID("_UdonClusterMask");
            int coarseMaskID = Shader.PropertyToID("_UdonCoarseClusterMask");
            Shader.SetGlobalFloat(clusteringEnabledID, 1f);
            manager.ReleaseClusteringPreview();
            Assert.That(Shader.GetGlobalFloat(clusteringEnabledID), Is.Zero);
            Assert.That(Shader.GetGlobalTexture(fineMaskID), Is.Null);
            Assert.That(Shader.GetGlobalTexture(coarseMaskID), Is.Null);
        }

        // Editor caches excluded from COMPILER_UDONSHARP must not appear in the reflected proxy layout.
        [Test]
        public void EditorOnlyManagerStateDoesNotAddUdonProxyInstanceFields() {
            string[] removedFieldNames = {
                "_editorCustomSourceOwners",
                "_editorCustomSourceTextures",
                "_editorCustomSourceMaterials",
                "_editorCustomSourceStates",
                "_editorCustomTextureWidth",
                "_editorCustomTextureHeight",
                "_editorShadowSourceOwners",
                "_editorShadowSourceTextures",
                "_editorShadowSourceMaterials",
                "_editorShadowSourceStates",
                "_editorShadowTextureWidth",
                "_editorShadowTextureHeight",
                "_editorShadowTextureFormat",
                "_prevAutoUpdateVolumes",
                "_prevAutoUpdateTextures",
                "_generatedClusteringMaterial",
                "_editorFroxelDepthParams"
            };

            for (int i = 0; i < removedFieldNames.Length; i++) {
                Assert.That(typeof(LightVolumeManager).GetField(removedFieldNames[i], _lifecycleMethodFlags), Is.Null, removedFieldNames[i]);
            }
            Assert.That(typeof(LightVolumeManager).GetMethod("Update", _lifecycleMethodFlags), Is.Null,
                "LightVolumeManager must not use per-object editor polling");
            Assert.That(typeof(PointLightVolumeInstance).GetMethod("Update", _lifecycleMethodFlags), Is.Null,
                "PointLightVolumeInstance must not use per-object editor polling");
        }

        // Any AssetDatabase batch can restore serialized proxy state, including package metadata imports.
        [Test]
        public void PackageMetadataImportQueuesClusteringPreviewRefresh() {
            Type editorAssemblyType = typeof(LightVolumeManagerTools);
            Type previewType = editorAssemblyType.Assembly.GetType("VRCLightVolumes.LightVolumeClusteringPreview");
            Type postprocessorType = editorAssemblyType.Assembly.GetType(
                "VRCLightVolumes.LightVolumeClusteringImportPostprocessor");
            Assert.That(previewType, Is.Not.Null);
            Assert.That(postprocessorType, Is.Not.Null);

            FieldInfo refreshPending = previewType.GetField("_refreshPending", _staticMigrationMethodFlags);
            MethodInfo postprocess = postprocessorType.GetMethod("OnPostprocessAllAssets", _staticMigrationMethodFlags);
            Assert.That(refreshPending, Is.Not.Null);
            Assert.That(postprocess, Is.Not.Null);

            refreshPending.SetValue(null, false);
            try {
                postprocess.Invoke(null, new object[] {
                    new[] { "Packages/red.sim.lightvolumes/package.json" },
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    false
                });
                Assert.That((bool)refreshPending.GetValue(null), Is.True);
            } finally {
                refreshPending.SetValue(null, false);
            }
        }

        // Domain reload recovery must distrust every restored runtime gate, not only the depth cache.
        [Test]
        public void EditorPreviewRecoveryRebuildsRestoredRuntimeState() {
            LightVolumeManager manager = CreateManager("Domain Reload Recovery Manager", false);
            PointLightVolumeInstance point = CreatePointLight(manager, "Domain Reload Recovery Light", true);
            manager.PointLightVolumeInstances = new[] { point };
            manager.Clustering = true;
            manager.ClusteringMinLights = 1;
            manager.UpdateVolumes();

            string[] trueFields = {
                "_isUpdatingVolumes",
                "_clusterGeometryUploadPending",
                "_clusteringUnsupported",
                "_clusteringAllocationFailed",
                "_clusteringActive",
                "_froxelLayoutValid",
                "_froxelDepthValid",
                "_froxelProjectionValid",
                "_clusterMaskValid"
            };
            for (int i = 0; i < trueFields.Length; i++) {
                FieldInfo field = typeof(LightVolumeManager).GetField(trueFields[i], _lifecycleMethodFlags);
                Assert.That(field, Is.Not.Null, trueFields[i]);
                field.SetValue(manager, true);
            }

            FieldInfo initializedField = typeof(LightVolumeManager).GetField("_isInitialized", _lifecycleMethodFlags);
            FieldInfo pointCountField = typeof(LightVolumeManager).GetField("_pointLightCount", _lifecycleMethodFlags);
            FieldInfo clusteringEnabledIdField = typeof(LightVolumeManager).GetField("_clusteringEnabledID", _lifecycleMethodFlags);
            FieldInfo clusteringLightsDirtyField = typeof(LightVolumeManager).GetField("_clusteringLightsDirty", _lifecycleMethodFlags);
            FieldInfo maskDirtyField = typeof(LightVolumeManager).GetField("_clusterMaskDirty", _lifecycleMethodFlags);
            MethodInfo recover = typeof(LightVolumeManager).GetMethod("RebuildClusteringPreviewState", _lifecycleMethodFlags);
            Assert.That(initializedField, Is.Not.Null);
            Assert.That(pointCountField, Is.Not.Null);
            Assert.That(clusteringEnabledIdField, Is.Not.Null);
            Assert.That(clusteringLightsDirtyField, Is.Not.Null);
            Assert.That(maskDirtyField, Is.Not.Null);
            Assert.That(recover, Is.Not.Null);

            initializedField.SetValue(manager, true);
            pointCountField.SetValue(manager, 77);
            clusteringEnabledIdField.SetValue(manager, 0);
            clusteringLightsDirtyField.SetValue(manager, false);
            maskDirtyField.SetValue(manager, false);
            int clusteringEnabledID = Shader.PropertyToID("_UdonClusteringEnabled");
            Shader.SetGlobalFloat(_pointLightCountID, 0f);
            Shader.SetGlobalFloat(clusteringEnabledID, 1f);

            recover.Invoke(manager, null);

            Assert.That((bool)initializedField.GetValue(manager), Is.True);
            Assert.That((int)pointCountField.GetValue(manager), Is.EqualTo(1));
            Assert.That((int)clusteringEnabledIdField.GetValue(manager), Is.EqualTo(clusteringEnabledID));
            Assert.That((bool)clusteringLightsDirtyField.GetValue(manager), Is.True);
            Assert.That((bool)maskDirtyField.GetValue(manager), Is.True);
            for (int i = 0; i < trueFields.Length; i++) {
                FieldInfo field = typeof(LightVolumeManager).GetField(trueFields[i], _lifecycleMethodFlags);
                Assert.That((bool)field.GetValue(manager), Is.False, trueFields[i]);
            }
            AssertGlobalFloat(_pointLightCountID, 1f);
            AssertGlobalFloat(clusteringEnabledID, 0f);

            recover.Invoke(manager, null);
            Assert.That((int)pointCountField.GetValue(manager), Is.EqualTo(1));
            AssertGlobalFloat(_pointLightCountID, 1f);
            AssertGlobalFloat(clusteringEnabledID, 0f);
        }

        // Edit-mode UdonSharp proxies do not dispatch delayed events, so a request must upload immediately.
        [Test]
        public void EditModeUpdateRequestPublishesPointLightsWithoutDelayedUdonEvent() {
            LightVolumeManager manager = CreateManager("Edit Mode Bootstrap Manager", false);
            PointLightVolumeInstance point = CreatePointLight(manager, "Edit Mode Bootstrap Light", true);
            manager.PointLightVolumeInstances = new[] { point };

            Shader.SetGlobalFloat(_pointLightCountID, 0f);
            manager.RequestUpdateVolumes();

            AssertGlobalFloat(_pointLightCountID, 1f);
        }

        // Unified Udon proxies rely on one editor change coordinator instead of per-object ExecuteAlways polling.
        [Test]
        public void EditorChangeCoordinatorSynchronizesTransformAndActiveLifecycle() {
            Type coordinatorType = typeof(LightVolumeManagerTools).Assembly.GetType(
                "VRCLightVolumes.LightVolumeEditorUpdater");
            Assert.That(coordinatorType, Is.Not.Null);
            MethodInfo queueObject = coordinatorType.GetMethod("QueueObject", _staticMigrationMethodFlags);
            MethodInfo flush = coordinatorType.GetMethod("FlushPendingSceneChanges", _staticMigrationMethodFlags);
            Assert.That(queueObject, Is.Not.Null);
            Assert.That(flush, Is.Not.Null);

            // Do not let a refresh queued by the previous test update unrelated scene managers
            // after this test manager and overwrite process-wide shader globals.
            flush.Invoke(null, null);
            ResetShaderGlobals();

            LightVolumeManager manager = CreateManager("Editor Change Coordinator Manager", false);
            PointLightVolumeInstance point = CreatePointLight(manager, "Editor Change Coordinator Point", true);
            manager.PointLightVolumeInstances = new[] { point };
            point.transform.position = new Vector3(3.5f, -2f, 7.25f);

            queueObject.Invoke(null, new object[] { point.transform });
            flush.Invoke(null, null);

            AssertVectorClose(new Vector4(3.5f, -2f, 7.25f, 0f), new Vector4(point.Position.x, point.Position.y, point.Position.z, 0f));
            AssertGlobalFloat(_pointLightCountID, 1f);
            AssertVectorClose(ExpectedPointLightPosition(point), Shader.GetGlobalVectorArray(_pointLightPositionID)[0]);

            point.gameObject.SetActive(false);
            queueObject.Invoke(null, new object[] { point.gameObject });
            flush.Invoke(null, null);
            Assert.That(point.IsActive, Is.False);
            AssertGlobalFloat(_pointLightCountID, 0f);

            point.gameObject.SetActive(true);
            queueObject.Invoke(null, new object[] { point.gameObject });
            flush.Invoke(null, null);
            Assert.That(point.IsActive, Is.True);
            AssertGlobalFloat(_pointLightCountID, 1f);

            manager.enabled = false;
            Shader.SetGlobalFloat(_lightVolumeEnabledID, 1f);
            Shader.SetGlobalFloat(_pointLightCountID, 1f);
            queueObject.Invoke(null, new object[] { manager });
            flush.Invoke(null, null);
            AssertGlobalFloat(_lightVolumeEnabledID, 0f);
            AssertGlobalFloat(_pointLightCountID, 0f);

            manager.enabled = true;
            queueObject.Invoke(null, new object[] { manager });
            flush.Invoke(null, null);
            AssertGlobalFloat(_lightVolumeEnabledID, 1f);
            AssertGlobalFloat(_pointLightCountID, 1f);
        }
        // Serialized and live Udon data must store the backing UdonBehaviour, never the managed U# proxy.
        [Test]
        public void RuntimeShadowDependenciesWriteManagerBackingToUdonHeap() {
            GameObject managerObject = CreateGameObject("Runtime Heap Manager", true);
            GameObject pointObject = CreateGameObject("Runtime Heap Point", true);
            // Tests run in the user's active scene. Avoid Undo-backed setup because a later Undo
            // replay can resurrect the containers after TearDown has destroyed them.
            LightVolumeManager manager = managerObject.AddUdonSharpComponent<LightVolumeManager>();
            PointLightVolumeInstance point = pointObject.AddUdonSharpComponent<PointLightVolumeInstance>();
            GameObject excludedObject = CreateGameObject("Runtime Heap Shadow Exclusion", true);
            point.LightVolumeManager = manager;
            point.Shadows = true;
            point.BakeInGame = true;
            point.ExclusionMask = new[] { excludedObject };
            UdonSharpEditor.UdonSharpEditorUtility.CopyProxyToUdon(manager);
            UdonSharpEditor.UdonSharpEditorUtility.CopyProxyToUdon(point);

            Type preprocessorType = typeof(LightVolumeManagerTools).Assembly.GetType("VRCLightVolumes.LightVolumePreprocessor");
            Assert.That(preprocessorType, Is.Not.Null);
            MethodInfo applyDependencies = preprocessorType.GetMethod("ApplyPointLightRuntimeShadowDependencies", _staticMigrationMethodFlags);
            Assert.That(applyDependencies, Is.Not.Null);
            applyDependencies.Invoke(null, new object[] { point });

            var managerBacking = UdonSharpEditor.UdonSharpEditorUtility.GetBackingUdonBehaviour(manager);
            var pointBacking = UdonSharpEditor.UdonSharpEditorUtility.GetBackingUdonBehaviour(point);
            Assert.That(managerBacking, Is.Not.Null);
            Assert.That(pointBacking, Is.Not.Null);
            Assert.That(pointBacking.publicVariables.TryGetVariableValue("LightVolumeManager", out object serializedManager), Is.True);
            Assert.That(serializedManager, Is.SameAs(managerBacking));
            Assert.That(serializedManager, Is.Not.SameAs(manager));
            Assert.That(pointBacking.publicVariables.TryGetVariableValue("ExclusionMask", out object serializedExclusionMask), Is.True);
            Assert.That(serializedExclusionMask, Is.EqualTo(new[] { excludedObject }));

            GameObject cameraObject = CreateGameObject("Runtime Heap Camera", true);
            Camera runtimeCamera = cameraObject.AddComponent<Camera>();
            manager.RuntimeShadowCamera = runtimeCamera;
            manager.RuntimeShadowBlurQualityPreset = 2;
            manager.RuntimeShadowBlurUniformKeyword = 1;
            manager.RuntimeShadowBlurDirectKeyword = 1;
            manager.RuntimeShadowBlurSphericalKeyword = 1;
            point.RuntimeShadowCamera = runtimeCamera;

            MethodInfo applyManagerDependencies = preprocessorType.GetMethod("ApplyManagerRuntimeDependencies", _staticMigrationMethodFlags);
            MethodInfo clearDependencies = preprocessorType.GetMethod("ClearRuntimeDependencies", _staticMigrationMethodFlags);
            Assert.That(applyManagerDependencies, Is.Not.Null);
            Assert.That(clearDependencies, Is.Not.Null);
            applyManagerDependencies.Invoke(null, new object[] { manager });
            applyDependencies.Invoke(null, new object[] { point });
            clearDependencies.Invoke(null, new object[] { new[] { managerObject, pointObject } });

            Assert.That(point.RuntimeShadowCamera, Is.Null);
            Assert.That(pointBacking.publicVariables.TryGetVariableValue("RuntimeShadowCamera", out object serializedPointCamera), Is.True);
            Assert.That(serializedPointCamera, Is.Null);
            Assert.That(manager.RuntimeShadowCamera, Is.SameAs(runtimeCamera));
            Assert.That(managerBacking.publicVariables.TryGetVariableValue("RuntimeShadowCamera", out object serializedManagerCamera), Is.True);
            Assert.That(serializedManagerCamera, Is.SameAs(runtimeCamera));
            Assert.That(manager.RuntimeShadowBlurQualityPreset, Is.EqualTo(-1));
            Assert.That(managerBacking.publicVariables.TryGetVariableValue("RuntimeShadowBlurQualityPreset", out object serializedBlurPreset), Is.True);
            Assert.That(serializedBlurPreset, Is.EqualTo(-1));
        }


        // Verifies that empty light-volume and point-light families do not block each other.
        [Test]
        public void EmptyVolumeFamiliesWriteIndependentCounts() {
            LightVolumeManager emptyManager = CreateManager("Empty Families Manager", true);

            emptyManager.UpdateVolumes();

            AssertGlobalFloat(_lightVolumeEnabledID, 0);
            AssertGlobalFloat(_lightVolumeCountID, 0);
            AssertGlobalFloat(_pointLightCountID, 0);

            LightVolumeManager pointOnlyManager = CreateManager("Point Only Manager", false);
            PointLightVolumeInstance point = CreatePointLight(pointOnlyManager, "Point Only Light", true);
            LightVolumeInstance volumeWithoutAtlas = CreateLightVolume(pointOnlyManager, "Ignored No Atlas Volume", true);
            point.transform.position = new Vector3(1, 2, 3);
            SetPointLightSquaredSize(point, 2);
            point.SetPointLight();
            pointOnlyManager.LightVolumeInstances = new[] { volumeWithoutAtlas };
            pointOnlyManager.PointLightVolumeInstances = new[] { point };

            pointOnlyManager.UpdateVolumes();

            AssertGlobalFloat(_lightVolumeEnabledID, 1);
            AssertGlobalFloat(_lightVolumeCountID, 0);
            AssertGlobalFloat(_lightVolumeAdditiveCountID, 0);
            AssertGlobalFloat(_pointLightCountID, 1);
            AssertVectorClose(ExpectedPointLightPosition(point), Shader.GetGlobalVectorArray(_pointLightPositionID)[0]);

            LightVolumeManager volumeOnlyManager = CreateManager("Volume Only Manager", true);
            LightVolumeInstance volume = CreateLightVolume(volumeOnlyManager, "Volume Only Light Volume", true);
            volumeOnlyManager.LightVolumeInstances = new[] { volume };
            volumeOnlyManager.PointLightVolumeInstances = new PointLightVolumeInstance[0];

            volumeOnlyManager.UpdateVolumes();

            AssertGlobalFloat(_lightVolumeEnabledID, 1);
            AssertGlobalFloat(_lightVolumeCountID, 1);
            AssertGlobalFloat(_pointLightCountID, 0);
            AssertVectorClose(ExpectedLightVolumeColor(volume), Shader.GetGlobalVectorArray(_lightVolumeColorID)[0]);
        }

        // Verifies a disabled startup option and regular volume uploads preserve another system's global override.
        [Test]
        public void DisabledForceSceneLightingDoesNotOverrideExistingGlobal() {
            LightVolumeManager manager = CreateManager("Disabled Force Scene Lighting Manager", true);
            manager.ForceSceneLighting = false;
            Shader.SetGlobalInteger(_forceSceneLightingID, 1);

            InvokeLifecycleMethod(manager, "Start");
            manager.UpdateVolumes();

            AssertGlobalInteger(_forceSceneLightingID, 1);
            Assert.That(manager.ForceSceneLighting, Is.False);
        }

        // Verifies the enabled inspector option explicitly enables the shader override on startup.
        [Test]
        public void EnabledForceSceneLightingSetsGlobalOnStart() {
            LightVolumeManager manager = CreateManager("Enabled Force Scene Lighting Manager", true);
            manager.ForceSceneLighting = true;
            Shader.SetGlobalInteger(_forceSceneLightingID, 0);

            InvokeLifecycleMethod(manager, "Start");

            AssertGlobalInteger(_forceSceneLightingID, 1);
        }

        // Verifies the public runtime API can set either state without UpdateVolumes continuously reasserting it.
        [Test]
        public void SetForceSceneLightingControlsGlobalManually() {
            LightVolumeManager manager = CreateManager("Manual Force Scene Lighting Manager", true);

            manager.SetForceSceneLighting(true);
            AssertGlobalInteger(_forceSceneLightingID, 1);
            Assert.That(manager.ForceSceneLighting, Is.True);

            Shader.SetGlobalInteger(_forceSceneLightingID, 0);
            manager.UpdateVolumes();
            AssertGlobalInteger(_forceSceneLightingID, 0);

            manager.SetForceSceneLighting(false);
            AssertGlobalInteger(_forceSceneLightingID, 0);
            Assert.That(manager.ForceSceneLighting, Is.False);
        }

        // Verifies editor update errors do not leave the manager permanently locked until domain reload.
        [Test]
        public void UpdateVolumesClearsEditorGuardAfterException() {
            LightVolumeManager manager = CreateManager("Update Guard Manager", true);
            PointLightVolumeInstance point = CreatePointLight(manager, "Update Guard Point", true);
            manager.PointLightVolumeInstances = new[] { point };
            int[] enabledPointIDs = GetManagerField<int[]>(manager, _enabledPointIDsField);
            SetManagerField<int[]>(manager, _enabledPointIDsField, null);

            Assert.Catch<System.Exception>(() => manager.UpdateVolumes());
            Assert.That(GetManagerField<bool>(manager, _isUpdatingVolumesField), Is.False);

            SetManagerField(manager, _enabledPointIDsField, enabledPointIDs);
            Assert.DoesNotThrow(() => manager.UpdateVolumes());
        }

        // Verifies null public registries are treated as empty legacy data instead of an update failure.
        [Test]
        public void UpdateVolumesSanitizesNullRegistries() {
            LightVolumeManager manager = CreateManager("Null Registry Manager", true);
            manager.LightVolumeInstances = null;
            manager.PointLightVolumeInstances = null;

            Assert.DoesNotThrow(() => manager.UpdateVolumes());
            Assert.That(manager.LightVolumeInstances, Is.Empty);
            Assert.That(manager.PointLightVolumeInstances, Is.Empty);
            Assert.That(GetManagerField<bool>(manager, _isUpdatingVolumesField), Is.False);
        }

        // Exercises real GameObject enable and disable callbacks for unregister and re-register behavior.
        [Test]
        public void LifecycleCallbacksRegisterUnregisterAndReinitializeVolumes() {
            LightVolumeManager manager = CreateManager("Lifecycle Manager", true);
            LightVolumeInstance volume = CreateLightVolume(manager, "Lifecycle Volume", true);

            manager.UpdateVolumes();
            Assert.That(ContainsLightVolume(manager.LightVolumeInstances, volume), Is.True);
            Assert.That(CountLightVolumeReferences(manager.LightVolumeInstances, volume), Is.EqualTo(1));
            AssertGlobalFloat(_lightVolumeEnabledID, 1);
            AssertGlobalFloat(_lightVolumeCountID, 1);

            volume.gameObject.SetActive(false);
            InvokeLifecycleMethod(volume, "OnDisable");

            manager.UpdateVolumes();
            Assert.That(ContainsLightVolume(manager.LightVolumeInstances, volume), Is.False);
            AssertGlobalFloat(_lightVolumeEnabledID, 0);
            AssertGlobalFloat(_lightVolumeCountID, 0);
            AssertGlobalFloat(_pointLightCountID, 0);

            volume.gameObject.SetActive(true);
            InvokeLifecycleMethod(volume, "OnEnable");

            manager.UpdateVolumes();
            Assert.That(ContainsLightVolume(manager.LightVolumeInstances, volume), Is.True);
            Assert.That(CountLightVolumeReferences(manager.LightVolumeInstances, volume), Is.EqualTo(1));
            AssertGlobalFloat(_lightVolumeEnabledID, 1);
            AssertGlobalFloat(_lightVolumeCountID, 1);

            manager.gameObject.SetActive(false);
            InvokeLifecycleMethod(manager, "OnDisable");

            AssertGlobalFloat(_lightVolumeEnabledID, 0);
            AssertGlobalFloat(_lightVolumeCountID, 0);
            AssertGlobalFloat(_pointLightCountID, 0);
        }

        // Verifies runtime-style initialization and deinitialization for both regular and point light volumes.
        [Test]
        public void RuntimeLifecycleInitializesAndDeinitializesBothVolumeTypes() {
            LightVolumeManager manager = CreateManager("Runtime Lifecycle Manager", true);
            LightVolumeInstance volume = CreateUnregisteredLightVolume(manager, "Runtime Light Volume");
            PointLightVolumeInstance point = CreateUnregisteredPointLight(manager, "Runtime Point Light Volume");

            InvokeLifecycleMethod(volume, "OnEnable");
            InvokeLifecycleMethod(point, "OnEnable");
            manager.UpdateVolumes();

            Assert.That(ContainsLightVolume(manager.LightVolumeInstances, volume), Is.True);
            Assert.That(ContainsPointLightVolume(manager.PointLightVolumeInstances, point), Is.True);
            Assert.That(CountLightVolumeReferences(manager.LightVolumeInstances, volume), Is.EqualTo(1));
            Assert.That(CountPointLightVolumeReferences(manager.PointLightVolumeInstances, point), Is.EqualTo(1));
            AssertGlobalFloat(_lightVolumeCountID, 1);
            AssertGlobalFloat(_pointLightCountID, 1);

            InvokeLifecycleMethod(volume, "OnDisable");
            InvokeLifecycleMethod(point, "OnDisable");
            manager.UpdateVolumes();

            Assert.That(ContainsLightVolume(manager.LightVolumeInstances, volume), Is.False);
            Assert.That(ContainsPointLightVolume(manager.PointLightVolumeInstances, point), Is.False);
            AssertGlobalFloat(_lightVolumeEnabledID, 0);
            AssertGlobalFloat(_lightVolumeCountID, 0);
            AssertGlobalFloat(_pointLightCountID, 0);
        }

        // Child volumes leave a disabled manager clean and register exactly once when it is enabled again.
        [Test]
        public void ManagerObjectDisableUnregistersAndReinitializesVolumes() {
            LightVolumeManager manager = CreateManager("Inactive Manager Registry Owner", true);
            LightVolumeInstance regular = CreateLightVolume(manager, "Manager Child Regular Volume", true);
            LightVolumeInstance additive = CreateLightVolume(manager, "Manager Child Additive Volume", true);
            PointLightVolumeInstance point = CreatePointLight(manager, "Manager Child Point Volume", true);
            additive.IsAdditive = true;
            regular.transform.SetParent(manager.transform);
            additive.transform.SetParent(manager.transform);
            point.transform.SetParent(manager.transform);

            manager.UpdateVolumes();
            Assert.That(ContainsLightVolume(manager.LightVolumeInstances, regular), Is.True);
            Assert.That(ContainsLightVolume(manager.LightVolumeInstances, additive), Is.True);
            Assert.That(ContainsPointLightVolume(manager.PointLightVolumeInstances, point), Is.True);

            manager.gameObject.SetActive(false);
            // Plain UdonSharp proxies do not receive child lifecycle callbacks automatically in
            // Edit Mode, so invoke the callbacks Unity dispatches when this happens at runtime.
            InvokeLifecycleMethod(regular, "OnDisable");
            InvokeLifecycleMethod(additive, "OnDisable");
            InvokeLifecycleMethod(point, "OnDisable");

            Assert.That(ContainsLightVolume(manager.LightVolumeInstances, regular), Is.False);
            Assert.That(ContainsLightVolume(manager.LightVolumeInstances, additive), Is.False);
            Assert.That(ContainsPointLightVolume(manager.PointLightVolumeInstances, point), Is.False);

            manager.gameObject.SetActive(true);
            InvokeLifecycleMethod(regular, "OnEnable");
            InvokeLifecycleMethod(additive, "OnEnable");
            InvokeLifecycleMethod(point, "OnEnable");
            manager.UpdateVolumes();

            Assert.That(CountLightVolumeReferences(manager.LightVolumeInstances, regular), Is.EqualTo(1));
            Assert.That(CountLightVolumeReferences(manager.LightVolumeInstances, additive), Is.EqualTo(1));
            Assert.That(CountPointLightVolumeReferences(manager.PointLightVolumeInstances, point), Is.EqualTo(1));
            AssertGlobalFloat(_lightVolumeCountID, 2);
            AssertGlobalFloat(_pointLightCountID, 1);
        }

        // Verifies runtime Light Volume registration preserves stable setup order instead of filling the first null slot.
        [Test]
        public void RuntimeLightVolumeRegistrationPreservesRegistryOrderAcrossDisableEnable() {
            LightVolumeManager manager = CreateManager("Stable Runtime Light Volume Order Manager", true);
            LightVolumeInstance first = CreateLightVolume(manager, "Stable First Volume", true);
            LightVolumeInstance second = CreateLightVolume(manager, "Stable Second Volume", true);
            LightVolumeInstance late = CreateUnregisteredLightVolume(manager, "Stable Late Volume");
            first.RegistryOrder = 0;
            second.RegistryOrder = 1;
            late.RegistryOrder = 2;

            manager.DeinitializeLightVolume(first);

            Assert.That(manager.LightVolumeInstances, Has.Length.EqualTo(1));
            Assert.That(manager.LightVolumeInstances[0], Is.SameAs(second));

            manager.InitializeLightVolume(late);

            Assert.That(manager.LightVolumeInstances, Has.Length.EqualTo(2));
            Assert.That(manager.LightVolumeInstances[0], Is.SameAs(second));
            Assert.That(manager.LightVolumeInstances[1], Is.SameAs(late));

            manager.InitializeLightVolume(first);

            Assert.That(manager.LightVolumeInstances, Has.Length.EqualTo(3));
            Assert.That(manager.LightVolumeInstances[0], Is.SameAs(first));
            Assert.That(manager.LightVolumeInstances[1], Is.SameAs(second));
            Assert.That(manager.LightVolumeInstances[2], Is.SameAs(late));
        }

        // Verifies runtime Point Light Volume registration preserves stable setup order instead of filling the first null slot.
        [Test]
        public void RuntimePointLightVolumeRegistrationPreservesRegistryOrderAcrossDisableEnable() {
            LightVolumeManager manager = CreateManager("Stable Runtime Point Light Volume Order Manager", false);
            PointLightVolumeInstance first = CreatePointLight(manager, "Stable First Point", true);
            PointLightVolumeInstance second = CreatePointLight(manager, "Stable Second Point", true);
            PointLightVolumeInstance late = CreateUnregisteredPointLight(manager, "Stable Late Point");
            first.RegistryOrder = 0;
            second.RegistryOrder = 1;
            late.RegistryOrder = 2;

            manager.DeinitializePointLightVolume(first, false, false);

            Assert.That(manager.PointLightVolumeInstances, Has.Length.EqualTo(1));
            Assert.That(manager.PointLightVolumeInstances[0], Is.SameAs(second));

            manager.InitializePointLightVolume(late);

            Assert.That(manager.PointLightVolumeInstances, Has.Length.EqualTo(2));
            Assert.That(manager.PointLightVolumeInstances[0], Is.SameAs(second));
            Assert.That(manager.PointLightVolumeInstances[1], Is.SameAs(late));

            manager.InitializePointLightVolume(first);

            Assert.That(manager.PointLightVolumeInstances, Has.Length.EqualTo(3));
            Assert.That(manager.PointLightVolumeInstances[0], Is.SameAs(first));
            Assert.That(manager.PointLightVolumeInstances[1], Is.SameAs(second));
            Assert.That(manager.PointLightVolumeInstances[2], Is.SameAs(late));
        }

        // Verifies legacy serialized null slots are compacted without changing relative registry order.
        [Test]
        public void SanitizeRegistriesRemovesLegacyNullSlots() {
            LightVolumeManager manager = CreateManager("Registry Sanitation Manager", false);
            LightVolumeInstance firstVolume = CreateLightVolume(manager, "First Sanitation Volume", false);
            LightVolumeInstance secondVolume = CreateLightVolume(manager, "Second Sanitation Volume", false);
            PointLightVolumeInstance firstPoint = CreatePointLight(manager, "First Sanitation Point", false);
            PointLightVolumeInstance secondPoint = CreatePointLight(manager, "Second Sanitation Point", false);
            manager.LightVolumeInstances = new[] { firstVolume, null, secondVolume, null };
            manager.PointLightVolumeInstances = new[] { null, firstPoint, null, secondPoint };

            bool changed = manager.SanitizeRegistries();

            Assert.That(changed, Is.True);
            Assert.That(manager.LightVolumeInstances, Is.EqualTo(new[] { firstVolume, secondVolume }));
            Assert.That(manager.PointLightVolumeInstances, Is.EqualTo(new[] { firstPoint, secondPoint }));
            Assert.That(manager.SanitizeRegistries(), Is.False);
        }

        // Verifies only the highest-weight active Light Volumes are uploaded when the registry exceeds shader capacity.
        [Test]
        public void UpdateVolumesUploadsHighestWeightedLightVolumesWithinShaderLimit() {
            LightVolumeManager manager = CreateManager("Weighted Light Volume Limit Manager", true);
            LightVolumeInstance[] volumes = new LightVolumeInstance[35];
            for (int i = 0; i < volumes.Length; i++) {
                LightVolumeInstance volume = CreateUnregisteredLightVolume(manager, "Weighted Light Volume " + i);
                volume.RegistryWeight = i;
                volume.RegistryOrder = i;
                ConfigureLightVolume(volume, new Color((i + 1) / 64f, 0.1f, 0.2f, 1), 1, false, 0.1f);
                manager.InitializeLightVolume(volume);
                volumes[i] = volume;
            }

            manager.UpdateVolumes();

            AssertGlobalFloat(_lightVolumeCountID, 32);
            Vector4[] colors = Shader.GetGlobalVectorArray(_lightVolumeColorID);
            AssertVectorClose(ExpectedLightVolumeColor(volumes[34]), colors[0]);
            AssertVectorClose(ExpectedLightVolumeColor(volumes[3]), colors[31]);

            manager.DeinitializeLightVolume(volumes[34]);
            manager.UpdateVolumes();

            colors = Shader.GetGlobalVectorArray(_lightVolumeColorID);
            AssertGlobalFloat(_lightVolumeCountID, 32);
            AssertVectorClose(ExpectedLightVolumeColor(volumes[33]), colors[0]);
            AssertVectorClose(ExpectedLightVolumeColor(volumes[2]), colors[31]);

            manager.InitializeLightVolume(volumes[34]);
            manager.UpdateVolumes();

            colors = Shader.GetGlobalVectorArray(_lightVolumeColorID);
            AssertGlobalFloat(_lightVolumeCountID, 32);
            AssertVectorClose(ExpectedLightVolumeColor(volumes[34]), colors[0]);
            AssertVectorClose(ExpectedLightVolumeColor(volumes[3]), colors[31]);
        }

        // Verifies the shader cap is applied by weight before additive volumes are compacted into the prefix.
        [Test]
        public void UpdateVolumesAppliesLightVolumeLimitBeforeAdditiveCompaction() {
            LightVolumeManager manager = CreateManager("Weighted Additive Limit Manager", true);
            LightVolumeInstance additive = CreateUnregisteredLightVolume(manager, "Low Weight Additive Volume");
            additive.RegistryWeight = 0f;
            additive.RegistryOrder = 0;
            ConfigureLightVolume(additive, new Color(1f, 0.2f, 0.05f, 1), 1, true, 0.1f);
            manager.InitializeLightVolume(additive);

            LightVolumeInstance[] regulars = new LightVolumeInstance[32];
            for (int i = 0; i < regulars.Length; i++) {
                LightVolumeInstance regular = CreateUnregisteredLightVolume(manager, "Higher Weight Regular Volume " + i);
                regular.RegistryWeight = i + 1;
                regular.RegistryOrder = i + 1;
                ConfigureLightVolume(regular, new Color((i + 1) / 64f, 0.3f, 0.15f, 1), 1, false, 0.2f);
                manager.InitializeLightVolume(regular);
                regulars[i] = regular;
            }

            manager.UpdateVolumes();

            AssertGlobalFloat(_lightVolumeCountID, 32);
            AssertGlobalFloat(_lightVolumeAdditiveCountID, 0);
            Vector4[] colors = Shader.GetGlobalVectorArray(_lightVolumeColorID);
            AssertVectorClose(ExpectedLightVolumeColor(regulars[31]), colors[0]);
            AssertVectorClose(ExpectedLightVolumeColor(regulars[0]), colors[31]);
        }

        // Authoring metadata synchronization never changes the Manager list when weights differ.
        [Test]
        public void ManagerAuthoringPreservesLightVolumeRegistryOrderAcrossWeights() {
            LightVolumeManager manager = CreateManager("Stable Authoring Registry Manager", true);
            LightVolumeInstance equalFirst = CreateUnregisteredLightVolume(manager, "Equal Weight First");
            LightVolumeInstance additiveHighest = CreateUnregisteredLightVolume(manager, "Highest Additive");
            LightVolumeInstance equalSecond = CreateUnregisteredLightVolume(manager, "Equal Weight Second");
            LightVolumeInstance lowest = CreateUnregisteredLightVolume(manager, "Lowest Regular");
            equalFirst.RegistryWeight = 5f;
            additiveHighest.RegistryWeight = 20f;
            additiveHighest.IsAdditive = true;
            equalSecond.RegistryWeight = 5f;
            lowest.RegistryWeight = -1f;
            manager.LightVolumeInstances = new[] {
                equalFirst,
                lowest,
                additiveHighest,
                equalSecond
            };

            LightVolumeManagerTools.SynchronizeRegistryMetadata(manager);

            Assert.That(manager.LightVolumeInstances, Is.EqualTo(new[] {
                equalFirst,
                lowest,
                additiveHighest,
                equalSecond
            }));
            for (int i = 0; i < 4; i++) {
                Assert.That(manager.LightVolumeInstances[i].RegistryOrder, Is.EqualTo(i));
                Assert.That(manager.LightVolumeInstances[i].LightVolumeManager, Is.SameAs(manager));
            }
        }

        // The Manager menu sort keeps weights authoritative and only applies resolution ordering
        // within equal-weight groups, preserving equal density authoring order.
        [Test]
        public void ManagerAuthoringSortsLightVolumesByVoxelsPerUnit() {
            LightVolumeManager manager = CreateManager("Voxel Density Sort Manager", true);
            LightVolumeInstance adaptiveLow = CreateUnregisteredLightVolume(manager, "Adaptive Low");
            LightVolumeInstance manualFirst = CreateUnregisteredLightVolume(manager, "Manual First");
            LightVolumeInstance adaptiveHighFirst = CreateUnregisteredLightVolume(manager, "Adaptive High First");
            LightVolumeInstance manualSecond = CreateUnregisteredLightVolume(manager, "Manual Second");
            LightVolumeInstance adaptiveHighSecond = CreateUnregisteredLightVolume(manager, "Adaptive High Second");
            LightVolumeInstance adaptiveMiddle = CreateUnregisteredLightVolume(manager, "Adaptive Middle");

            adaptiveLow.VoxelsPerUnit = 1f;
            manualFirst.AdaptiveResolution = false;
            adaptiveHighFirst.VoxelsPerUnit = 8f;
            manualSecond.AdaptiveResolution = false;
            adaptiveHighSecond.VoxelsPerUnit = 8f;
            adaptiveMiddle.VoxelsPerUnit = 4f;

            adaptiveLow.RegistryWeight = 30f;
            manualFirst.RegistryWeight = 10f;
            adaptiveHighFirst.RegistryWeight = 10f;
            manualSecond.RegistryWeight = 10f;
            adaptiveHighSecond.RegistryWeight = 10f;
            adaptiveMiddle.RegistryWeight = 10f;
            manager.LightVolumeInstances = new[] {
                adaptiveLow,
                manualFirst,
                adaptiveHighFirst,
                manualSecond,
                adaptiveHighSecond,
                adaptiveMiddle
            };

            LightVolumeManagerTools.SortLightVolumesByVoxelsPerUnit(manager);

            LightVolumeInstance[] expected = {
                adaptiveLow,
                manualFirst,
                manualSecond,
                adaptiveHighFirst,
                adaptiveHighSecond,
                adaptiveMiddle
            };
            Assert.That(manager.LightVolumeInstances, Is.EqualTo(expected));
            Assert.That(manager.LightVolumeInstances[0].RegistryWeight, Is.EqualTo(30f).Within(Epsilon));
            Assert.That(manager.LightVolumeInstances[1].RegistryWeight, Is.EqualTo(10f).Within(Epsilon));
            Assert.That(manager.LightVolumeInstances[2].RegistryWeight, Is.EqualTo(10f).Within(Epsilon));
            Assert.That(manager.LightVolumeInstances[3].RegistryWeight, Is.EqualTo(10f).Within(Epsilon));
            Assert.That(manager.LightVolumeInstances[4].RegistryWeight, Is.EqualTo(10f).Within(Epsilon));
            Assert.That(manager.LightVolumeInstances[5].RegistryWeight, Is.EqualTo(10f).Within(Epsilon));
            for (int i = 0; i < expected.Length; i++) {
                Assert.That(expected[i].RegistryOrder, Is.EqualTo(i));
                Assert.That(expected[i].LightVolumeManager, Is.SameAs(manager));
            }

            LightVolumeManagerTools.SynchronizeRegistryMetadata(manager);
            Assert.That(manager.LightVolumeInstances, Is.EqualTo(expected));
        }

        // Verifies only the highest-weight active Point Light Volumes are uploaded when the registry exceeds shader capacity.
        [Test]
        public void UpdateVolumesUploadsHighestWeightedPointLightVolumesWithinShaderLimit() {
            LightVolumeManager manager = CreateManager("Weighted Point Light Volume Limit Manager", false);
            PointLightVolumeInstance[] points = new PointLightVolumeInstance[130];
            for (int i = 0; i < points.Length; i++) {
                PointLightVolumeInstance point = CreateUnregisteredPointLight(manager, "Weighted Point Light Volume " + i);
                point.RegistryWeight = i;
                point.RegistryOrder = i;
                point.Color = new Color((i + 1) / 160f, 0.2f, 0.1f, 1);
                manager.InitializePointLightVolume(point);
                points[i] = point;
            }

            manager.UpdateVolumes();

            AssertGlobalFloat(_pointLightCountID, 128);
            Vector4[] colors = Shader.GetGlobalVectorArray(_pointLightColorID);
            AssertVectorClose(ExpectedPointLightColor(points[129]), colors[0]);
            AssertVectorClose(ExpectedPointLightColor(points[2]), colors[127]);

            manager.DeinitializePointLightVolume(points[129], false, false);
            manager.UpdateVolumes();

            colors = Shader.GetGlobalVectorArray(_pointLightColorID);
            AssertGlobalFloat(_pointLightCountID, 128);
            AssertVectorClose(ExpectedPointLightColor(points[128]), colors[0]);
            AssertVectorClose(ExpectedPointLightColor(points[1]), colors[127]);

            manager.InitializePointLightVolume(points[129]);
            manager.UpdateVolumes();

            colors = Shader.GetGlobalVectorArray(_pointLightColorID);
            AssertGlobalFloat(_pointLightCountID, 128);
            AssertVectorClose(ExpectedPointLightColor(points[129]), colors[0]);
            AssertVectorClose(ExpectedPointLightColor(points[2]), colors[127]);
        }

        // Verifies SetWeight changes shader priority without moving the Manager's authoring registry.
        [Test]
        public void SetWeightPreservesRegistryOrderAndReordersShaderUpload() {
            LightVolumeManager manager = CreateManager("Set Weight Light Volume Manager", true);
            LightVolumeInstance first = CreateLightVolume(manager, "Set Weight First Volume", true);
            LightVolumeInstance second = CreateLightVolume(manager, "Set Weight Second Volume", true);
            ConfigureLightVolume(first, new Color(0.2f, 0.4f, 0.8f, 1), 1, false, 0.1f);
            ConfigureLightVolume(second, new Color(1f, 0.25f, 0.1f, 1), 1, false, 0.2f);

            second.SetWeight(10f);

            Assert.That(second.RegistryWeight, Is.EqualTo(10f).Within(Epsilon));
            Assert.That(manager.LightVolumeInstances[0], Is.SameAs(first));
            Assert.That(manager.LightVolumeInstances[1], Is.SameAs(second));
            manager.UpdateVolumes();

            Vector4[] colors = Shader.GetGlobalVectorArray(_lightVolumeColorID);
            AssertVectorClose(ExpectedLightVolumeColor(second), colors[0]);
            AssertVectorClose(ExpectedLightVolumeColor(first), colors[1]);

            first.SetWeight(20f);
            manager.UpdateVolumes();

            Assert.That(first.RegistryWeight, Is.EqualTo(20f).Within(Epsilon));
            Assert.That(manager.LightVolumeInstances[0], Is.SameAs(first));
            Assert.That(manager.LightVolumeInstances[1], Is.SameAs(second));
            colors = Shader.GetGlobalVectorArray(_lightVolumeColorID);
            AssertVectorClose(ExpectedLightVolumeColor(first), colors[0]);
            AssertVectorClose(ExpectedLightVolumeColor(second), colors[1]);
        }

        // Verifies SetWeight reorders registered Point Light Volumes and refreshes registry-indexed texture IDs.
        [Test]
        public void SetWeightReordersRegisteredPointLightVolumesAndRefreshesTextureIds() {
            LightVolumeManager manager = CreateManager("Set Weight Point Light Volume Manager", false);
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;
            PointLightVolumeInstance cookie = CreatePointLight(manager, "Set Weight Cookie Point", true);
            PointLightVolumeInstance plain = CreatePointLight(manager, "Set Weight Plain Point", true);
            cookie.Color = new Color(0.2f, 0.4f, 0.8f, 1);
            plain.Color = new Color(1f, 0.25f, 0.1f, 1);
            cookie.CustomTexture = CreateTexture2D("Set Weight Cookie Source");
            cookie.ProjectionType = 1; // 1: texture
            cookie.SetLut();
            manager.ReinitializeCustomTextures();

            plain.SetWeight(10f);
            manager.UpdateVolumes();

            Assert.That(plain.RegistryWeight, Is.EqualTo(10f).Within(Epsilon));
            Assert.That(manager.PointLightVolumeInstances[0], Is.SameAs(plain));
            Assert.That(manager.PointLightVolumeInstances[1], Is.SameAs(cookie));
            Vector4[] colors = Shader.GetGlobalVectorArray(_pointLightColorID);
            AssertVectorClose(ExpectedPointLightColor(plain), colors[0]);
            AssertVectorClose(ExpectedPointLightColor(cookie), colors[1]);
            AssertPointCustomData(0, plain, 0, 0);
            AssertPointCustomData(1, cookie, 1, 0);
        }

        // Verifies active runtime-spawned regular volumes self-register when their manager reference is assigned later.
        [Test]
        public void LateManagerAssignmentInitializesActiveLightVolumeWithoutUpdatePolling() {
            LightVolumeManager manager = CreateManager("Late Assigned Volume Manager", true);
            LightVolumeInstance volume = CreateManagerlessLightVolume("Late Assigned Runtime Volume");

            Assert.That(volume.LightVolumeManager, Is.Null);
            Assert.That(ContainsLightVolume(manager.LightVolumeInstances, volume), Is.False);

            volume.LightVolumeManager = manager;
            volume._onVarChange_LightVolumeManager();
            manager.UpdateVolumes();

            Assert.That(volume.LightVolumeManager, Is.SameAs(manager));
            Assert.That(ContainsLightVolume(manager.LightVolumeInstances, volume), Is.True);
            Assert.That(CountLightVolumeReferences(manager.LightVolumeInstances, volume), Is.EqualTo(1));
            AssertGlobalFloat(_lightVolumeEnabledID, 1);
            AssertGlobalFloat(_lightVolumeCountID, 1);
            AssertVectorClose(ExpectedLightVolumeColor(volume), Shader.GetGlobalVectorArray(_lightVolumeColorID)[0]);

            InvokeLifecycleMethod(volume, "OnEnable");
            manager.UpdateVolumes();

            Assert.That(CountLightVolumeReferences(manager.LightVolumeInstances, volume), Is.EqualTo(1));
            AssertGlobalFloat(_lightVolumeCountID, 1);
        }

        // Verifies active runtime-spawned point lights self-register when their manager reference is assigned later.
        [Test]
        public void LateManagerAssignmentInitializesActivePointLightWithoutUpdatePolling() {
            LightVolumeManager manager = CreateManager("Late Assigned Point Manager", false);
            PointLightVolumeInstance point = CreateManagerlessPointLight("Late Assigned Runtime Point");

            Assert.That(point.LightVolumeManager, Is.Null);
            Assert.That(ContainsPointLightVolume(manager.PointLightVolumeInstances, point), Is.False);

            point.LightVolumeManager = manager;
            point._onVarChange_LightVolumeManager();
            manager.UpdateVolumes();

            Assert.That(point.LightVolumeManager, Is.SameAs(manager));
            Assert.That(ContainsPointLightVolume(manager.PointLightVolumeInstances, point), Is.True);
            Assert.That(CountPointLightVolumeReferences(manager.PointLightVolumeInstances, point), Is.EqualTo(1));
            AssertGlobalFloat(_lightVolumeEnabledID, 1);
            AssertGlobalFloat(_pointLightCountID, 1);
            AssertVectorClose(ExpectedPointLightColor(point), Shader.GetGlobalVectorArray(_pointLightColorID)[0]);

            InvokeLifecycleMethod(point, "OnEnable");
            manager.UpdateVolumes();

            Assert.That(CountPointLightVolumeReferences(manager.PointLightVolumeInstances, point), Is.EqualTo(1));
            AssertGlobalFloat(_pointLightCountID, 1);
        }

        // Editor synchronization and external program-variable callbacks must not reactivate a disabled component.
        [Test]
        public void DisabledComponentsRemainInactiveWhenRuntimeCallbacksRun() {
            LightVolumeManager manager = CreateManager("Disabled Callback Manager", true);
            LightVolumeInstance volume = CreateLightVolume(manager, "Disabled Callback Volume", true);
            PointLightVolumeInstance point = CreatePointLight(manager, "Disabled Callback Point", true);

            volume.enabled = false;
            point.enabled = false;
            volume.IsActive = true;
            point.IsActive = true;

            LightVolumeTools.ApplyRuntimeState(volume, false);
            Assert.That(volume.IsActive, Is.False);
            volume.IsActive = true;

            volume._onVarChange_IsDynamic();
            volume._onVarChange_IsAdditive();
            point._onVarChange_IsDynamic();

            Assert.That(volume.IsActive, Is.False);
            Assert.That(point.IsActive, Is.False);
        }

        // Verifies inactive, black, and zero-intensity entries are removed from the final shader-visible arrays.
        [Test]
        public void DisabledAndZeroBrightnessInstancesAreExcludedFromFinalGlobals() {
            LightVolumeManager manager = CreateManager("Filtering Manager", true);
            LightVolumeInstance validVolume = CreateLightVolume(manager, "Valid Volume", true);
            LightVolumeInstance inactiveVolume = CreateLightVolume(manager, "Inactive Volume", false);
            LightVolumeInstance blackVolume = CreateLightVolume(manager, "Black Volume", true);
            LightVolumeInstance zeroVolume = CreateLightVolume(manager, "Zero Volume", true);
            ConfigureLightVolume(validVolume, new Color(0.2f, 0.4f, 0.8f, 1), 2, false, 0.25f);
            ConfigureLightVolume(blackVolume, Color.black, 1, false, 0.5f);
            ConfigureLightVolume(zeroVolume, Color.white, 0, false, 0.75f);

            PointLightVolumeInstance validPoint = CreatePointLight(manager, "Valid Point", true);
            PointLightVolumeInstance inactivePoint = CreatePointLight(manager, "Inactive Point", false);
            PointLightVolumeInstance blackPoint = CreatePointLight(manager, "Black Point", true);
            PointLightVolumeInstance zeroPoint = CreatePointLight(manager, "Zero Point", true);
            validPoint.Color = new Color(1, 0.5f, 0.25f, 1);
            validPoint.Intensity = 3;
            SetPointLightSquaredSize(validPoint, 4);
            validPoint.SetPointLight();
            blackPoint.Color = Color.black;
            zeroPoint.Intensity = 0;
            manager.LightVolumeInstances = new[] { blackVolume, inactiveVolume, validVolume, zeroVolume };
            manager.PointLightVolumeInstances = new[] { zeroPoint, validPoint, inactivePoint, blackPoint };

            manager.UpdateVolumes();

            AssertGlobalFloat(_lightVolumeEnabledID, 1);
            AssertGlobalFloat(_lightVolumeCountID, 1);
            AssertGlobalFloat(_pointLightCountID, 1);
            AssertVectorClose(ExpectedLightVolumeColor(validVolume), Shader.GetGlobalVectorArray(_lightVolumeColorID)[0]);
            AssertVectorClose(ExpectedPointLightColor(validPoint), Shader.GetGlobalVectorArray(_pointLightColorID)[0]);

            validVolume.Intensity = 0;
            validPoint.Intensity = 0;

            manager.UpdateVolumes();

            AssertGlobalFloat(_lightVolumeEnabledID, 0);
            AssertGlobalFloat(_lightVolumeCountID, 0);
            AssertGlobalFloat(_pointLightCountID, 0);
        }

        // Checks shader globals for volume order, color/intensity changes, movement, UVW data, and additive counters.
        [Test]
        public void LightVolumeGlobalsFollowOrderMovementAndParameterChanges() {
            LightVolumeManager manager = CreateManager("Volume Globals Manager", true);
            manager.LightProbesBlending = false;
            manager.SharpBounds = false;
            manager.AdditiveMaxOverdraw = 2;

            LightVolumeInstance first = CreateLightVolume(manager, "First Volume", true);
            LightVolumeInstance second = CreateLightVolume(manager, "Second Volume", true);
            ConfigureLightVolume(first, new Color(0.25f, 0.5f, 0.75f, 1), 1.5f, false, 0.1f);
            ConfigureLightVolume(second, new Color(1, 0.2f, 0.05f, 1), 0.75f, true, 0.5f);
            first.transform.position = new Vector3(-1, 0.5f, 2);
            first.transform.localScale = new Vector3(1, 2, 3);
            second.transform.position = new Vector3(2, 3, 4);
            second.transform.rotation = Quaternion.Euler(0, 45, 0);
            second.transform.localScale = new Vector3(2, 3, 4);
            second.InvBakedRotation = Quaternion.Euler(0, 15, 0);
            manager.LightVolumeInstances = new[] { second, first };

            manager.UpdateVolumes();

            AssertGlobalFloat(_lightVolumeEnabledID, 1);
            AssertGlobalFloat(_lightVolumeCountID, 2);
            AssertGlobalFloat(_lightVolumeAdditiveCountID, 1);
            AssertGlobalFloat(_lightVolumeOcclusionCountID, 0);
            AssertGlobalFloat(_lightVolumeProbesBlendID, 0);
            AssertGlobalFloat(_lightVolumeSharpBoundsID, 0);
            AssertGlobalFloat(_lightVolumeAdditiveMaxOverdrawID, 2);
            AssertVectorClose(ExpectedLightVolumeColor(second), Shader.GetGlobalVectorArray(_lightVolumeColorID)[0]);
            AssertVectorClose(ExpectedLightVolumeColor(first), Shader.GetGlobalVectorArray(_lightVolumeColorID)[1]);
            AssertVectorClose(second.BoundsUvwMin0, Shader.GetGlobalVectorArray(_lightVolumeUvwScaleID)[0]);
            AssertVectorClose(second.BoundsUvwMin1, Shader.GetGlobalVectorArray(_lightVolumeUvwScaleID)[1]);
            AssertVectorClose(second.BoundsUvwMin2, Shader.GetGlobalVectorArray(_lightVolumeUvwScaleID)[2]);
            Vector4[] expandedUvw = Shader.GetGlobalVectorArray(_lightVolumeUvwID);
            AssertVectorClose(ExpectedExpandedLightVolumeUvw(second, 0, false), expandedUvw[0]);
            AssertVectorClose(ExpectedExpandedLightVolumeUvw(second, 0, true), expandedUvw[1]);
            AssertVectorClose(ExpectedExpandedLightVolumeUvw(second, 1, false), expandedUvw[2]);
            AssertVectorClose(ExpectedExpandedLightVolumeUvw(second, 1, true), expandedUvw[3]);
            AssertVectorClose(ExpectedExpandedLightVolumeUvw(second, 2, false), expandedUvw[4]);
            AssertVectorClose(ExpectedExpandedLightVolumeUvw(second, 2, true), expandedUvw[5]);
            AssertVectorClose(second.RelativeRotationRow0, Shader.GetGlobalVectorArray(_lightVolumeRotationID)[0]);
            AssertVectorClose(second.RelativeRotationRow1, Shader.GetGlobalVectorArray(_lightVolumeRotationID)[1]);
            AssertMatrixClose(Matrix4x4.TRS(second.transform.position, second.transform.rotation, second.transform.lossyScale).inverse, Shader.GetGlobalMatrixArray(_lightVolumeInvWorldMatrixID)[0]);

            second.Intensity = 0;
            manager.UpdateVolumes();

            AssertGlobalFloat(_lightVolumeCountID, 1);
            AssertGlobalFloat(_lightVolumeAdditiveCountID, 0);
            AssertVectorClose(ExpectedLightVolumeColor(first), Shader.GetGlobalVectorArray(_lightVolumeColorID)[0]);

            second.Intensity = 0.75f;
            first.SetSmoothBlending(0.5f);
            first.Color = Color.green;
            first.Intensity = 2;
            first.IsAdditive = true;
            first.transform.position = new Vector3(3, 4, 5);
            manager.LightVolumeInstances = new[] { first, second };

            manager.UpdateVolumes();

            Vector3 expectedSmooth = first.transform.lossyScale / 0.5f;
            AssertGlobalFloat(_lightVolumeCountID, 2);
            AssertGlobalFloat(_lightVolumeAdditiveCountID, 2);
            AssertVectorClose(ExpectedLightVolumeColor(first), Shader.GetGlobalVectorArray(_lightVolumeColorID)[0]);
            AssertVectorClose(new Vector4(expectedSmooth.x, expectedSmooth.y, expectedSmooth.z, 0), Shader.GetGlobalVectorArray(_lightVolumeInvLocalEdgeSmoothID)[0]);
            AssertMatrixClose(Matrix4x4.TRS(first.transform.position, first.transform.rotation, first.transform.lossyScale).inverse, Shader.GetGlobalMatrixArray(_lightVolumeInvWorldMatrixID)[0]);
        }

        // Verifies additive volumes are compacted into leading shader slots even when serialized registry order is stale.
        [Test]
        public void AdditiveVolumesOccupyLeadingShaderSlotsWhenRegistryOrderIsStale() {
            LightVolumeManager manager = CreateManager("Stale Additive Order Manager", true);
            LightVolumeInstance regular = CreateLightVolume(manager, "Regular Ordered First", true);
            LightVolumeInstance additive = CreateLightVolume(manager, "Additive Ordered Second", true);
            ConfigureLightVolume(regular, new Color(0.1f, 0.4f, 0.8f, 1), 1, false, 0.15f);
            ConfigureLightVolume(additive, new Color(1, 0.2f, 0.05f, 1), 2, true, 0.55f);
            regular.transform.position = new Vector3(-2, 1, 3);
            regular.transform.localScale = new Vector3(1, 2, 3);
            additive.transform.position = new Vector3(4, 5, 6);
            additive.transform.rotation = Quaternion.Euler(0, 45, 0);
            additive.transform.localScale = new Vector3(2, 3, 4);
            manager.LightVolumeInstances = new[] { regular, additive };

            manager.UpdateVolumes();

            AssertGlobalFloat(_lightVolumeCountID, 2);
            AssertGlobalFloat(_lightVolumeAdditiveCountID, 1);
            AssertVectorClose(ExpectedLightVolumeColor(additive), Shader.GetGlobalVectorArray(_lightVolumeColorID)[0]);
            AssertVectorClose(ExpectedLightVolumeColor(regular), Shader.GetGlobalVectorArray(_lightVolumeColorID)[1]);
            AssertVectorClose(additive.BoundsUvwMin0, Shader.GetGlobalVectorArray(_lightVolumeUvwScaleID)[0]);
            AssertMatrixClose(Matrix4x4.TRS(additive.transform.position, additive.transform.rotation, additive.transform.lossyScale).inverse, Shader.GetGlobalMatrixArray(_lightVolumeInvWorldMatrixID)[0]);
        }

        // Verifies dynamic regular and point light transforms are pushed into shader globals after movement.
        [Test]
        public void DynamicInstancesWriteMovedTransformsToShaderGlobals() {
            LightVolumeManager manager = CreateManager("Dynamic Transform Manager", true);
            LightVolumeInstance volume = CreateLightVolume(manager, "Dynamic Volume", true);
            PointLightVolumeInstance point = CreatePointLight(manager, "Dynamic Point", true);

            volume.IsDynamic = true;
            volume.transform.position = new Vector3(10, 20, 30);
            volume.transform.rotation = Quaternion.Euler(15, 25, 35);
            volume.transform.localScale = new Vector3(2, 3, 4);
            point.IsDynamic = true;
            point.transform.position = new Vector3(-2, -3, -4);
            point.transform.rotation = Quaternion.Euler(0, 90, 0);
            point.transform.localScale = new Vector3(1.5f, 1.5f, 1.5f);
            SetPointLightSquaredSize(point, 3);
            point.SetCustomTexture();
            manager.LightVolumeInstances = new[] { volume };
            manager.PointLightVolumeInstances = new[] { point };

            manager.UpdateVolumes();

            Quaternion expectedPointRotation = Quaternion.Inverse(point.transform.rotation);
            AssertMatrixClose(Matrix4x4.TRS(volume.transform.position, volume.transform.rotation, volume.transform.lossyScale).inverse, Shader.GetGlobalMatrixArray(_lightVolumeInvWorldMatrixID)[0]);
            AssertVectorClose(volume.RelativeRotationRow0, Shader.GetGlobalVectorArray(_lightVolumeRotationID)[0]);
            AssertVectorClose(volume.RelativeRotationRow1, Shader.GetGlobalVectorArray(_lightVolumeRotationID)[1]);
            AssertVectorClose(ExpectedPointLightPosition(point), Shader.GetGlobalVectorArray(_pointLightPositionID)[0]);
            AssertVectorClose(new Vector4(expectedPointRotation.x, expectedPointRotation.y, expectedPointRotation.z, expectedPointRotation.w), Shader.GetGlobalVectorArray(_pointLightDirectionID)[0]);
        }

        // Verifies cached transform data changes only when the instance update methods run.
        [Test]
        public void StaticInstancesKeepCachedTransformDataUntilManualUpdateMethodsRun() {
            LightVolumeInstance volume = CreateLightVolume(null, "Static Cached Volume", true);
            PointLightVolumeInstance point = CreatePointLight(null, "Static Cached Point", true);

            volume.IsDynamic = false;
            volume.transform.position = Vector3.zero;
            volume.UpdateTransform();
            Matrix4x4 cachedVolumeMatrix = volume.InvWorldMatrix;
            point.IsDynamic = false;
            point.transform.position = Vector3.zero;
            SetPointLightSquaredSize(point, 2);
            point.UpdateTransform();
            Vector4 cachedPointPosition = new Vector4(point.Position.x, point.Position.y, point.Position.z, 0);

            volume.transform.position = new Vector3(5, 6, 7);
            point.transform.position = new Vector3(8, 9, 10);

            AssertMatrixClose(cachedVolumeMatrix, volume.InvWorldMatrix);
            AssertVectorClose(cachedPointPosition, new Vector4(point.Position.x, point.Position.y, point.Position.z, 0));

            volume.UpdateTransform();
            point.UpdateTransform();

            AssertMatrixClose(Matrix4x4.TRS(volume.transform.position, volume.transform.rotation, volume.transform.lossyScale).inverse, volume.InvWorldMatrix);
            AssertVectorClose(new Vector4(8, 9, 10, 0), new Vector4(point.Position.x, point.Position.y, point.Position.z, 0));
        }

        // Checks point light shader globals through point, LUT, cookie spot, area, and Shadow modes.
        [Test]
        public void PointLightGlobalsWorldSpaceShadowAndCutoffChanges() {
            LightVolumeManager manager = CreateManager("Point Globals Manager", false);
            manager.ShadowTexturesWidth = 256;
            manager.ShadowTexturesHeight = 256;
            manager.LightsBrightnessCutoff = 0.2f;

            PointLightVolumeInstance point = CreatePointLight(manager, "Point Light", true);
            point.transform.position = new Vector3(2, 3, 4);
            point.transform.rotation = Quaternion.Euler(10, 20, 30);
            point.transform.localScale = Vector3.one;
            point.Color = new Color(1, 0.5f, 0.25f, 1);
            point.Intensity = 2;
            SetPointLightSquaredSize(point, 4);
            point.Angle = 30 * Mathf.Deg2Rad;
            point.OuterAngleCos = Mathf.Cos(point.Angle);
            point.SetPointLight();

            manager.UpdateVolumes();

            AssertGlobalFloat(_lightVolumeEnabledID, 1);
            AssertGlobalFloat(_pointLightCountID, 1);
            AssertGlobalFloat(_pointLightCubeCountID, 0);
            AssertGlobalFloat(_lightBrightnessCutoffID, 0.2f);
            AssertVectorClose(ExpectedPointLightPosition(point), Shader.GetGlobalVectorArray(_pointLightPositionID)[0]);
            AssertVectorClose(ExpectedPointLightColor(point), Shader.GetGlobalVectorArray(_pointLightColorID)[0]);
            AssertPointCustomData(point, 0, 0);

            point.ShadingStrength = 0.25f;
            manager.UpdateVolumes();
            AssertPointCustomData(point, 0, 0.75f);
            point.ShadingStrength = 1;

            point.SetColor(Color.black);
            manager.UpdateVolumes();

            AssertGlobalFloat(_lightVolumeEnabledID, 0);
            AssertGlobalFloat(_pointLightCountID, 0);

            point.SetColor(new Color(1, 0.5f, 0.25f, 1));
            ConfigureShadowTexture(point, CreateCubemap("Point Globals Shadow Source"), false, true, false);
            point.WorldSpaceShadows = true;
            point.Bias = -1;
            point.ShadowBakePosition = new Vector3(5, 6, 7);
            manager.PointLightVolumeInstances = new[] { point };
            manager.ReinitializeShadowTextures();

            manager.UpdateVolumes();

            AssertGlobalFloat(_pointLightShadowCountID, 1);
            AssertPointCustomData(point, 0, 1);
            AssertVectorClose(new Vector4(5, 6, 7, ExpectedCubemapShadowInvDepthRange(point)), Shader.GetGlobalVectorArray(_pointLightShadowReprojectionDataID)[0]);
            AssertVectorClose(new Vector4(0, 0, 0, 1), Shader.GetGlobalVectorArray(_pointLightShadowRotationDataID)[0]);

            point.ShadingStrength = 0.5f;
            manager.UpdateVolumes();
            AssertPointCustomData(point, 0, 1.5f);

            point.ShadingStrength = 0;
            manager.UpdateVolumes();
            Vector4 disabledShadingData = Shader.GetGlobalVectorArray(_pointLightCustomIdID)[0];
            AssertGlobalFloat(_pointLightShadowCountID, 0);
            Assert.That(disabledShadingData.y, Is.EqualTo(10000).Within(Epsilon));
            Assert.That(disabledShadingData.w, Is.EqualTo(0).Within(Epsilon));
            point.ShadingStrength = 1;

            point.WorldSpaceShadows = false;

            manager.UpdateVolumes();

            Quaternion expectedLocalSpaceRotation = Quaternion.Inverse(point.transform.rotation);
            AssertPointCustomData(point, 0, -1);
            AssertVectorClose(new Vector4(5, 6, 7, ExpectedCubemapShadowInvDepthRange(point)), Shader.GetGlobalVectorArray(_pointLightShadowReprojectionDataID)[0]);
            AssertVectorClose(new Vector4(expectedLocalSpaceRotation.x, expectedLocalSpaceRotation.y, expectedLocalSpaceRotation.z, expectedLocalSpaceRotation.w), Shader.GetGlobalVectorArray(_pointLightShadowRotationDataID)[0]);

            point.CustomTexture = CreateTexture2D("Point Globals LUT");
            point.ProjectionType = 1; // 1: texture
            point.SetLut();
            point.SetLightSourceSize(5);

            manager.ReinitializeCustomTextures();
            manager.UpdateVolumes();

            Assert.That(point.ProjectionMode, Is.EqualTo(1)); // 1: LUT
            AssertPointCustomData(point, 1, -1);
            AssertVectorClose(ExpectedPointLightPosition(point), Shader.GetGlobalVectorArray(_pointLightPositionID)[0]);

            point.SetCustomTexture();
            point.SetSpotLight(60, 0.25f);

            manager.ReinitializeCustomTextures();
            manager.UpdateVolumes();

            Quaternion expectedCookieRotation = Quaternion.Inverse(point.transform.rotation);
            Assert.That(point.LightType, Is.EqualTo(1)); // 1: spot
            AssertPointCustomData(point, -1, -1);
            AssertVectorClose(ExpectedPointLightColor(point), Shader.GetGlobalVectorArray(_pointLightColorID)[0]);
            AssertVectorClose(new Vector4(expectedCookieRotation.x, expectedCookieRotation.y, expectedCookieRotation.z, expectedCookieRotation.w), Shader.GetGlobalVectorArray(_pointLightDirectionID)[0]);

            point.SetParametric();
            point.transform.localScale = new Vector3(2, 3, 1);
            point.SetAreaLight();

            manager.UpdateVolumes();

            Quaternion expectedAreaRotation = point.transform.rotation;
            Assert.That(point.LightType, Is.EqualTo(2)); // 2: area
            AssertVectorClose(ExpectedPointLightPosition(point), Shader.GetGlobalVectorArray(_pointLightPositionID)[0]);
            AssertVectorClose(ExpectedPointLightColor(point), Shader.GetGlobalVectorArray(_pointLightColorID)[0]);
            AssertVectorClose(new Vector4(expectedAreaRotation.x, expectedAreaRotation.y, expectedAreaRotation.z, expectedAreaRotation.w), Shader.GetGlobalVectorArray(_pointLightDirectionID)[0]);
            AssertPointCustomData(point, 0, -1);

            manager.LightsBrightnessCutoff = 0.5f;

            manager.UpdateVolumes();

            AssertGlobalFloat(_lightBrightnessCutoffID, 0.5f);
            Assert.That(point.IsRangeDirty, Is.False);
            Assert.That(Shader.GetGlobalVectorArray(_pointLightCustomIdID)[0].z, Is.EqualTo(point.SquaredRange).Within(Epsilon));
        }

        // Verifies all active point lights can use cubemap projection data at the same time.
        [Test]
        public void AllPointLightsWithCubemapProjectionWriteProjectionGlobals() {
            LightVolumeManager manager = CreateManager("All Cubemap Projection Manager", false);
            const int pointCount = 8;
            PointLightVolumeInstance[] points = new PointLightVolumeInstance[pointCount];

            for (int i = 0; i < pointCount; i++) {
                PointLightVolumeInstance point = CreatePointLight(manager, "Cubemap Point " + i, true);
                point.transform.position = new Vector3(i, i + 1, i + 2);
                point.transform.rotation = Quaternion.Euler(i * 5, i * 7, i * 11);
                SetPointLightSquaredSize(point, i + 1);
                point.CustomTexture = CreateCubemap("Cubemap Projection Source " + i);
                point.CustomTextureIsCubemap = true;
                point.ProjectionType = 1; // 1: texture
                point.SetCustomTexture();
                points[i] = point;
            }
            manager.PointLightVolumeInstances = points;

            manager.UpdateVolumes();

            Vector4[] positions = Shader.GetGlobalVectorArray(_pointLightPositionID);
            Vector4[] directions = Shader.GetGlobalVectorArray(_pointLightDirectionID);
            AssertGlobalFloat(_lightVolumeEnabledID, 1);
            AssertGlobalFloat(_pointLightCountID, pointCount);
            AssertGlobalFloat(_pointLightCubeCountID, pointCount);
            for (int i = 0; i < pointCount; i++) {
                Quaternion expectedRotation = Quaternion.Inverse(points[i].transform.rotation);
                AssertVectorClose(ExpectedPointLightPosition(points[i]), positions[i]);
                AssertVectorClose(new Vector4(expectedRotation.x, expectedRotation.y, expectedRotation.z, expectedRotation.w), directions[i]);
                AssertPointCustomData(i, points[i], -i - 1, 0);
            }
        }

        // Verifies native spot cookies create a manager-owned runtime texture array and shader ID
        [Test]
        public void SpotCookieCreatesRuntimeArrayAndShaderId() {
            LightVolumeManager manager = CreateManager("Spot Cookie Runtime Manager", false);
            RenderTexture source = CreateRenderTexture("Animated Spot Cookie Source", 4, 4, 1, TextureDimension.Tex2D);
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;

            PointLightVolumeInstance point = CreatePointLight(manager, "Animated Spot Cookie Light", true);
            point.SetCustomTexture();
            point.SetSpotLight(60, 0.5f);
            point.SpotCookieAspect = 1.75f;
            point.CustomTexture = source;
            point.ProjectionType = 1; // 1: texture
            point.AutoUpdateCustomTexture = true;
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeCustomTextures();
            manager.UpdateVolumes();

            Assert.That(manager.CustomTextures, Is.Not.Null);
            Assert.That(manager.CustomTextures.dimension, Is.EqualTo(TextureDimension.Tex2DArray));
            Assert.That(manager.CustomTextures.width, Is.EqualTo(4));
            Assert.That(manager.CustomTextures.height, Is.EqualTo(4));
            Assert.That(manager.CustomTextures.volumeDepth, Is.EqualTo(1));
            Assert.That(manager.CustomTextures.format, Is.EqualTo(RenderTextureFormat.ARGBHalf));
            Assert.That(manager.CustomTextures.useMipMap, Is.False);
            Assert.That(manager.CustomTextures.autoGenerateMips, Is.False);
            AssertPointCustomData(point, -1, 0);
            Assert.That(Shader.GetGlobalVectorArray(_pointLightExtraDataID)[0].x, Is.EqualTo(1.75f).Within(Epsilon));
        }

        // Verifies area lights without a cookie keep the default fast area light shader path.
        [Test]
        public void AreaLightWithoutCookieKeepsDefaultProjectionId() {
            LightVolumeManager manager = CreateManager("Area No Cookie Runtime Manager", false);

            PointLightVolumeInstance point = CreatePointLight(manager, "Area No Cookie Light", true);
            point.transform.localScale = new Vector3(2, 3, 1);
            point.SetAreaLight();
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeCustomTextures();
            manager.UpdateVolumes();

            Assert.That(point.LightType, Is.EqualTo(2)); // 2: area
            Assert.That(point.ProjectionMode, Is.EqualTo(0)); // 0: parametric, no cookie
            Assert.That(manager.CustomTextures, Is.Null);
            AssertPointCustomData(point, 0, 0);
        }

        // Verifies area light cookies use the single-slice texture cache and write a negative shader ID.
        [Test]
        public void AreaCookieCreatesRuntimeArrayAndShaderId() {
            LightVolumeManager manager = CreateManager("Area Cookie Runtime Manager", false);
            Texture2D source = CreateTexture2D("Area Cookie Source");
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;

            PointLightVolumeInstance point = CreatePointLight(manager, "Area Cookie Light", true);
            point.transform.localScale = new Vector3(2, 3, 1);
            point.SetCustomTexture();
            point.SetAreaLight();
            point.CustomTexture = source;
            point.ProjectionType = 1; // 1: texture
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeCustomTextures();
            manager.UpdateVolumes();

            Assert.That(point.LightType, Is.EqualTo(2)); // 2: area
            Assert.That(point.ProjectionMode, Is.EqualTo(2)); // 2: custom cookie or cubemap
            Assert.That(manager.CubemapsCount, Is.EqualTo(0));
            Assert.That(GetManagerField<int>(manager, _customSingleTextureCountField), Is.EqualTo(1));
            Assert.That(manager.CustomTextures, Is.Not.Null);
            Assert.That(manager.CustomTextures.dimension, Is.EqualTo(TextureDimension.Tex2DArray));
            Assert.That(manager.CustomTextures.volumeDepth, Is.EqualTo(1));
            Assert.That(manager.CustomTextures.useMipMap, Is.True);
            Assert.That(manager.CustomTextures.autoGenerateMips, Is.False);
            Assert.That(Shader.GetGlobalFloat(_pointLightTextureTexelCountID), Is.EqualTo(16));
            AssertVectorClose(ExpectedPointLightPosition(point), Shader.GetGlobalVectorArray(_pointLightPositionID)[0]);
            AssertVectorClose(ExpectedPointLightColor(point), Shader.GetGlobalVectorArray(_pointLightColorID)[0]);
            AssertPointCustomData(point, -1, 0);
        }

        // Verifies the runtime manager respects a manual auto-update override on an area RenderTexture cookie.
        [Test]
        public void AreaRenderTextureCookieRespectsManualAutoUpdateOverride() {
            LightVolumeManager manager = CreateManager("Area Render Texture Manual Auto Update Manager", false);
            RenderTexture source = CreateRenderTexture("Area Render Texture Cookie Source", 4, 4, 1, TextureDimension.Tex2D);
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;

            PointLightVolumeInstance point = CreatePointLight(manager, "Area Render Texture Manual Cookie Light", true);
            point.transform.localScale = new Vector3(2, 3, 1);
            point.SetCustomTexture();
            point.SetAreaLight();
            point.CustomTexture = source;
            point.ProjectionType = 1; // 1: texture
            point.AutoUpdateCustomTexture = false;
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeCustomTextures();

            Assert.That(manager.HasAutoCustomTextureUpdates, Is.False);
        }

        // Verifies edit-mode auto updates rebuild stale auto-mip arrays before manual mip generation.
        [Test]
        public void AreaCookieAutoUpdateRebuildsStaleAutoMipArray() {
            LightVolumeManager manager = CreateManager("Area Cookie Stale Auto Mip Manager", false);
            RenderTexture source = CreateRenderTexture("Area Cookie Auto Update Source", 4, 4, 1, TextureDimension.Tex2D);
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;

            PointLightVolumeInstance point = CreatePointLight(manager, "Area Cookie Auto Update Light", true);
            point.transform.localScale = new Vector3(2, 3, 1);
            point.SetCustomTexture();
            point.SetAreaLight();
            point.CustomTexture = source;
            point.ProjectionType = 1; // 1: texture
            point.AutoUpdateCustomTexture = true;
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeCustomTextures();

            RenderTexture stale = new RenderTexture(4, 4, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            stale.name = "Stale Auto Mip Custom Texture Array";
            stale.dimension = TextureDimension.Tex2DArray;
            stale.volumeDepth = 1;
            stale.useMipMap = true;
            stale.autoGenerateMips = true;
            stale.filterMode = FilterMode.Trilinear;
            stale.Create();
            _createdObjects.Add(stale);
            manager.CustomTextures = stale;

            manager.UpdateAutoCustomTextures();

            Assert.That(ReferenceEquals(manager.CustomTextures, stale), Is.False);
            Assert.That(manager.CustomTextures.useMipMap, Is.True);
            Assert.That(manager.CustomTextures.autoGenerateMips, Is.False);
        }

        // Verifies area cookie fallback averages cache before shader buffers exist, patch live buffers, and keep the cached color until replacement readback.
        [Test]
        public void AreaCookieFallbackAveragePreservesCachedColorUntilReplacementReadback() {
            LightVolumeManager manager = CreateManager("Area Cookie Fallback Average Manager", false, false);
            Texture2D source = CreateTexture2D("Area Average Cookie Source");
            Texture2D replacementSource = CreateTexture2D("Area Average Cookie Replacement Source");
            Color averageColor = new Color(0.25f, 0.5f, 0.75f, 1f);
            Color liveAverageColor = new Color(0.5f, 0.25f, 0.125f, 1f);
            Color replacementAverageColor = new Color(0.125f, 0.75f, 0.375f, 1f);
            source.SetPixel(0, 0, liveAverageColor);
            source.Apply(false);
            replacementSource.SetPixel(0, 0, replacementAverageColor);
            replacementSource.Apply(false);
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;

            PointLightVolumeInstance point = CreatePointLight(manager, "Area Average Cookie Light", true);
            point.transform.localScale = new Vector3(2, 3, 1);
            point.Color = new Color(0.25f, 0.5f, 1f, 1f);
            point.Intensity = 2f;
            point.SetCustomTexture();
            point.SetAreaLight();
            point.CustomTexture = source;
            point.ProjectionType = 1; // 1: texture
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeCustomTextures();

            Assert.That(manager.HasAutoCustomTextureUpdates, Is.False);

            point.AreaCookieAverageReadbackPending = true;
            point.AreaCookieAverageReadbackDirty = false;
            point.AreaCookieAverageCustomId = 0;
            manager.ReinitializeCustomTextures();

            Assert.That(point.AreaCookieAverageReadbackPending, Is.True);
            Assert.That(point.AreaCookieAverageReadbackDirty, Is.True);
            Assert.That(point.AreaCookieAverageCustomId, Is.EqualTo(0));

            point.AreaCookieAverageReadbackPending = false;
            point.AreaCookieAverageCustomId = -1;

            UploadAreaCookieAverageColor(manager, 0, averageColor);
            manager.gameObject.SetActive(true);
            manager.UpdateVolumes();

            AssertVectorClose(ExpectedAreaCookieFallbackColor(point, averageColor), Shader.GetGlobalVectorArray(_pointLightColorID)[0]);

            UploadAreaCookieAverageColor(manager, 0, liveAverageColor);
            AssertVectorClose(ExpectedAreaCookieFallbackColor(point, liveAverageColor), Shader.GetGlobalVectorArray(_pointLightColorID)[0]);

            manager.ReinitializeCustomTextures();
            point.Color = new Color(1f, 0.5f, 0.25f, 1f);
            manager.UpdateVolumes();

            Color liveReadbackColor = GetAreaCookieAverageColor(manager, 0);
            AssertVectorClose(ExpectedAreaCookieFallbackColor(point, liveReadbackColor), Shader.GetGlobalVectorArray(_pointLightColorID)[0]);

            point.CustomTexture = replacementSource;
            manager.ReinitializeCustomTextures();
            point.Color = new Color(0.5f, 1f, 0.25f, 1f);
            manager.UpdateVolumes();

            Color replacementReadbackColor = GetAreaCookieAverageColor(manager, 0);
            AssertVectorClose(ExpectedAreaCookieFallbackColor(point, replacementReadbackColor), Shader.GetGlobalVectorArray(_pointLightColorID)[0]);

            UploadAreaCookieAverageColor(manager, 0, replacementAverageColor);
            AssertVectorClose(ExpectedAreaCookieFallbackColor(point, replacementAverageColor), Shader.GetGlobalVectorArray(_pointLightColorID)[0]);

            point.IsActive = false;
            manager.ReinitializeCustomTextures();

            point.IsActive = true;
            manager.ReinitializeCustomTextures();

            manager.UpdateVolumes();
            Color restoredReadbackColor = GetAreaCookieAverageColor(manager, 0);
            AssertVectorClose(ExpectedAreaCookieFallbackColor(point, restoredReadbackColor), Shader.GetGlobalVectorArray(_pointLightColorID)[0]);
        }

        // Verifies an existing area-cookie fallback average is shared with later area lights deduped to the same cookie source.
        [Test]
        public void SharedAreaCookieFallbackAveragePropagatesAfterDedupe() {
            LightVolumeManager manager = CreateManager("Shared Area Cookie Fallback Manager", false);
            Texture2D source = CreateTexture2D("Shared Area Cookie Source");
            Color averageColor = new Color(0.25f, 0.5f, 0.75f, 1f);
            source.SetPixel(0, 0, averageColor);
            source.Apply(false);
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;

            PointLightVolumeInstance firstPoint = CreatePointLight(manager, "Shared Area Cookie A", true);
            firstPoint.transform.localScale = new Vector3(2, 3, 1);
            firstPoint.Color = new Color(0.25f, 0.5f, 1f, 1f);
            firstPoint.Intensity = 2f;
            firstPoint.SetCustomTexture();
            firstPoint.SetAreaLight();
            firstPoint.CustomTexture = source;
            firstPoint.ProjectionType = 1; // 1: texture
            manager.PointLightVolumeInstances = new[] { firstPoint };

            manager.ReinitializeCustomTextures();
            UploadAreaCookieAverageColor(manager, 0, averageColor);

            PointLightVolumeInstance secondPoint = CreatePointLight(manager, "Shared Area Cookie B", true);
            secondPoint.transform.localScale = new Vector3(2, 3, 1);
            secondPoint.Color = new Color(1f, 0.5f, 0.25f, 1f);
            secondPoint.Intensity = 2f;
            secondPoint.SetCustomTexture();
            secondPoint.SetAreaLight();
            secondPoint.CustomTexture = source;
            secondPoint.ProjectionType = 1; // 1: texture
            manager.PointLightVolumeInstances = new[] { firstPoint, secondPoint };

            manager.ReinitializeCustomTextures();
            manager.UpdateVolumes();

            Assert.That(GetManagerField<int>(manager, _customSingleTextureCountField), Is.EqualTo(1));
            Assert.That(GetManagerField<int[]>(manager, _pointLightCustomIDsField), Is.EqualTo(new[] { 0, 0 }));
            Vector4[] colors = Shader.GetGlobalVectorArray(_pointLightColorID);
            Color firstReadbackColor = GetAreaCookieAverageColor(manager, 0);
            Color secondReadbackColor = GetAreaCookieAverageColor(manager, 1);
            AssertVectorClose(new Vector4(firstReadbackColor.r, firstReadbackColor.g, firstReadbackColor.b, firstReadbackColor.a), new Vector4(secondReadbackColor.r, secondReadbackColor.g, secondReadbackColor.b, secondReadbackColor.a));
            AssertVectorClose(ExpectedAreaCookieFallbackColor(firstPoint, firstReadbackColor), colors[0]);
            AssertVectorClose(ExpectedAreaCookieFallbackColor(secondPoint, secondReadbackColor), colors[1]);
        }

        // Verifies deinitializing an area cookie light stores its fallback average on the instance for the next initialization.
        [Test]
        public void AreaCookieFallbackAverageCachesOnDeinitializeAndRestoresOnInitialize() {
            LightVolumeManager manager = CreateManager("Area Cookie Deinitialize Fallback Manager", false);
            Texture2D source = CreateTexture2D("Area Deinitialize Cookie Source");
            Color averageColor = new Color(0.25f, 0.5f, 0.75f, 1f);
            source.SetPixel(0, 0, averageColor);
            source.Apply(false);
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;

            PointLightVolumeInstance point = CreatePointLight(manager, "Area Deinitialize Cookie Light", true);
            point.transform.localScale = new Vector3(2, 3, 1);
            point.Color = new Color(0.25f, 0.5f, 1f, 1f);
            point.Intensity = 2f;
            point.SetCustomTexture();
            point.SetAreaLight();
            point.CustomTexture = source;
            point.ProjectionType = 1; // 1: texture
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeCustomTextures();
            UploadAreaCookieAverageColor(manager, 0, averageColor);

            manager.DeinitializePointLightVolume(point, true, false);
            AssertVectorClose(new Vector4(averageColor.r, averageColor.g, averageColor.b, averageColor.a), point.AreaLightFallbackColor);
            manager.ReinitializeCustomTextures();
            Assert.That(GetAreaCookieAverageColor(manager, 0).a, Is.EqualTo(0f).Within(Epsilon));

            point.AreaCookieAverageReadbackPending = true;
            point.AreaCookieAverageReadbackDirty = false;
            point.AreaCookieAverageCustomId = 0;
            manager.InitializePointLightVolume(point);
            manager.ReinitializeCustomTextures();
            manager.UpdateVolumes();

            Assert.That(point.AreaCookieAverageReadbackPending, Is.True);
            AssertVectorClose(ExpectedAreaCookieFallbackColor(point, averageColor), Shader.GetGlobalVectorArray(_pointLightColorID)[0]);
            point.AreaCookieAverageReadbackPending = false;
            point.AreaCookieAverageReadbackDirty = false;
            point.AreaCookieAverageCustomId = -1;
        }

        // Verifies an invalidated async area cookie readback cannot patch the current fallback color through a reused custom ID.
        [Test]
        public void InvalidatedAreaCookieReadbackDoesNotPatchReusedCustomId() {
            LightVolumeManager manager = CreateManager("Area Cookie Invalidated Readback Manager", false);
            Texture2D source = CreateTexture2D("Area Invalidated Cookie Source");
            Color staleAverageColor = new Color(0.875f, 0.125f, 0.25f, 1f);
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;

            PointLightVolumeInstance point = CreatePointLight(manager, "Area Invalidated Cookie Light", true);
            point.transform.localScale = new Vector3(2, 3, 1);
            point.Color = new Color(0.25f, 0.5f, 1f, 1f);
            point.Intensity = 2f;
            point.SetCustomTexture();
            point.SetAreaLight();
            point.CustomTexture = source;
            point.ProjectionType = 1; // 1: texture
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeCustomTextures();
            manager.UpdateVolumes();

            point.AreaCookieAverageReadbackPending = true;
            point.AreaCookieAverageReadbackDirty = false;
            point.AreaCookieAverageCustomId = -1;
            manager.CompleteAreaCookieAverageReadback(point, true, staleAverageColor);

            AssertVectorClose(ExpectedAreaCookieFallbackColor(point, Color.white), Shader.GetGlobalVectorArray(_pointLightColorID)[0]);
            Assert.That(point.AreaCookieAverageReadbackPending, Is.False);
            Assert.That(point.AreaCookieAverageCustomId, Is.EqualTo(-1));
        }

        // Verifies a dirty stale area-cookie readback completes cleanly and lets the retry path rebuild the source cache.
        [Test]
        public void DirtyAreaCookieReadbackCompletesAndQueuesRetry() {
            LightVolumeManager manager = CreateManager("Area Cookie Source Swap Readback Manager", false);
            Texture2D source = CreateTexture2D("Area Source Swap Cookie Source");
            Texture2D replacementSource = CreateTexture2D("Area Source Swap Replacement Cookie Source");
            Color staleAverageColor = new Color(0.875f, 0.125f, 0.25f, 1f);
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;

            PointLightVolumeInstance point = CreatePointLight(manager, "Area Source Swap Cookie Light", true);
            point.transform.localScale = new Vector3(2, 3, 1);
            point.Color = new Color(0.25f, 0.5f, 1f, 1f);
            point.Intensity = 2f;
            point.SetCustomTexture();
            point.SetAreaLight();
            point.CustomTexture = source;
            point.ProjectionType = 1; // 1: texture
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeCustomTextures();
            manager.UpdateVolumes();

            point.AreaCookieAverageReadbackPending = true;
            point.AreaCookieAverageReadbackDirty = true;
            point.AreaCookieAverageCustomId = 0;
            point.CustomTexture = replacementSource;
            replacementSource.SetPixel(0, 0, Color.black);
            replacementSource.Apply(false);

            manager.CompleteAreaCookieAverageReadback(point, true, staleAverageColor);

            Assert.That(point.AreaCookieAverageReadbackPending, Is.False);
            Assert.That(point.AreaCookieAverageCustomId, Is.EqualTo(-1));
            Color retryReadbackColor = GetAreaCookieAverageColor(manager, 0);
            Assert.That(retryReadbackColor.r, Is.LessThan(0.1f));
            AssertVectorClose(ExpectedAreaCookieFallbackColor(point, retryReadbackColor), Shader.GetGlobalVectorArray(_pointLightColorID)[0]);
        }

        // Verifies source-cache rebuild marks a pending area-cookie readback dirty so completion retries once.
        [Test]
        public void AreaCookieSourceRebuildMarksPendingReadbackDirty() {
            LightVolumeManager manager = CreateManager("Area Cookie Pending Source Change Manager", false);
            Texture2D source = CreateTexture2D("Area Pending Cookie Source");
            Texture2D replacementSource = CreateTexture2D("Area Pending Cookie Replacement Source");
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;

            PointLightVolumeInstance point = CreatePointLight(manager, "Area Pending Cookie Light", true);
            point.transform.localScale = new Vector3(2, 3, 1);
            point.SetCustomTexture();
            point.SetAreaLight();
            point.CustomTexture = source;
            point.ProjectionType = 1; // 1: texture
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeCustomTextures();

            point.AreaCookieAverageReadbackPending = true;
            point.AreaCookieAverageReadbackDirty = false;
            point.AreaCookieAverageCustomId = 0;
            point.CustomTexture = replacementSource;

            manager.ReinitializeCustomTextures();

            Assert.That(point.AreaCookieAverageReadbackPending, Is.True);
            Assert.That(point.AreaCookieAverageReadbackDirty, Is.True);
            Assert.That(point.AreaCookieAverageCustomId, Is.EqualTo(0));
        }

        // Verifies area cookie readback does not live-patch spot lights that share the same single-slice source ID.
        [Test]
        public void AreaCookieFallbackReadbackDoesNotPatchSharedSpotCookie() {
            LightVolumeManager manager = CreateManager("Area Cookie Shared Spot Manager", false);
            Texture2D source = CreateTexture2D("Area Cookie Shared Spot Source");
            Color averageColor = new Color(0.25f, 0.5f, 0.75f, 1f);
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;

            PointLightVolumeInstance area = CreatePointLight(manager, "Area Cookie Shared Source Light", true);
            area.transform.localScale = new Vector3(2, 3, 1);
            area.SetCustomTexture();
            area.SetAreaLight();
            area.CustomTexture = source;
            area.ProjectionType = 1; // 1: texture

            PointLightVolumeInstance spot = CreatePointLight(manager, "Wide Spot Shared Cookie Light", true);
            spot.SetCustomTexture();
            spot.SetSpotLight(150, 0.25f);
            spot.CustomTexture = source;
            spot.ProjectionType = 1; // 1: texture

            manager.PointLightVolumeInstances = new[] { area, spot };

            manager.ReinitializeCustomTextures();
            manager.UpdateVolumes();
            UploadAreaCookieAverageColor(manager, 0, averageColor);

            Vector4[] colors = Shader.GetGlobalVectorArray(_pointLightColorID);
            AssertVectorClose(ExpectedAreaCookieFallbackColor(area, averageColor), colors[0]);
            AssertVectorClose(ExpectedPointLightColor(spot), colors[1]);
        }

        // Verifies area light material cookies reuse matching material sources with mipmapped projection data.
        [Test]
        public void AreaMaterialCookieDeduplicatesRuntimeArrayAndShaderIds() {
            LightVolumeManager manager = CreateManager("Area Material Cookie Runtime Manager", false);
            Material material = CreateMaterial("Hidden/CubeFace");
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;

            PointLightVolumeInstance firstPoint = CreatePointLight(manager, "Area Material Cookie A", true);
            firstPoint.transform.localScale = new Vector3(2, 3, 1);
            firstPoint.SetCustomMaterial(material, true);
            firstPoint.SetAreaLight();

            PointLightVolumeInstance secondPoint = CreatePointLight(manager, "Area Material Cookie B", true);
            secondPoint.transform.localScale = new Vector3(2, 3, 1);
            secondPoint.Color = Color.red;
            secondPoint.SetCustomMaterial(material, true);
            secondPoint.SetAreaLight();

            manager.PointLightVolumeInstances = new[] { firstPoint, secondPoint };

            manager.ReinitializeCustomTextures();
            manager.UpdateVolumes();

            Assert.That(manager.CubemapsCount, Is.EqualTo(0));
            Assert.That(GetManagerField<int>(manager, _customSingleTextureCountField), Is.EqualTo(0));
            Assert.That(GetManagerField<int>(manager, _customSingleMaterialCountField), Is.EqualTo(1));
            Assert.That(manager.CustomTextures, Is.Not.Null);
            Assert.That(manager.CustomTextures.dimension, Is.EqualTo(TextureDimension.Tex2DArray));
            Assert.That(manager.CustomTextures.volumeDepth, Is.EqualTo(1));
            Assert.That(manager.CustomTextures.useMipMap, Is.True);
            Assert.That(manager.CustomTextures.autoGenerateMips, Is.False);
            Assert.That(GetManagerField<int[]>(manager, _pointLightCustomIDsField), Is.EqualTo(new[] { 0, 0 }));
            AssertPointCustomData(0, firstPoint, -1, 0);
            AssertPointCustomData(1, secondPoint, -1, 0);
        }

        // Verifies deinitializing one material-cookie area light does not clear another material-cookie area's fallback average.
        [Test]
        public void AreaMaterialCookieFallbackSurvivesOtherAreaDeinitialize() {
            LightVolumeManager manager = CreateManager("Area Material Cookie Deinitialize Manager", false);
            Material firstMaterial = CreateMaterial("Hidden/CubeFace");
            Material secondMaterial = CreateMaterial("Hidden/CubeFace");
            Color firstAverageColor = new Color(0.125f, 0.25f, 0.5f, 1f);
            Color secondAverageColor = new Color(0.5f, 0.25f, 0.125f, 1f);
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;

            PointLightVolumeInstance firstPoint = CreatePointLight(manager, "Area Material Cookie Deinit A", true);
            firstPoint.transform.localScale = new Vector3(2, 3, 1);
            firstPoint.SetCustomMaterial(firstMaterial, true);
            firstPoint.SetAreaLight();

            PointLightVolumeInstance secondPoint = CreatePointLight(manager, "Area Material Cookie Deinit B", true);
            secondPoint.transform.localScale = new Vector3(2, 3, 1);
            secondPoint.Color = new Color(1f, 0.5f, 0.25f, 1f);
            secondPoint.SetCustomMaterial(secondMaterial, true);
            secondPoint.SetAreaLight();

            manager.PointLightVolumeInstances = new[] { firstPoint, secondPoint };
            manager.ReinitializeCustomTextures();
            UploadAreaCookieAverageColor(manager, 0, firstAverageColor);
            UploadAreaCookieAverageColor(manager, 1, secondAverageColor);

            secondPoint.AreaCookieAverageReadbackPending = true;
            secondPoint.AreaCookieAverageReadbackDirty = false;
            secondPoint.AreaCookieAverageCustomId = 0;
            manager.DeinitializePointLightVolume(firstPoint, true, false);
            manager.ReinitializeCustomTextures();
            manager.UpdateVolumes();

            Assert.That(secondPoint.AreaCookieAverageReadbackPending, Is.True);
            AssertVectorClose(ExpectedAreaCookieFallbackColor(secondPoint, secondAverageColor), Shader.GetGlobalVectorArray(_pointLightColorID)[0]);
            secondPoint.AreaCookieAverageReadbackPending = false;
            secondPoint.AreaCookieAverageReadbackDirty = false;
            secondPoint.AreaCookieAverageCustomId = -1;
        }

        // Verifies a shared material source is split when lights need different runtime auto-update behavior.
        [Test]
        public void AreaMaterialCookieAutoUpdateMismatchUsesSeparateRuntimeSlices() {
            LightVolumeManager manager = CreateManager("Area Material Cookie Auto Update Split Manager", false);
            Material material = CreateMaterial("Hidden/CubeFace");
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;

            PointLightVolumeInstance livePoint = CreatePointLight(manager, "Area Material Cookie Live", true);
            livePoint.transform.localScale = new Vector3(2, 3, 1);
            livePoint.SetCustomMaterial(material, true);
            livePoint.SetAreaLight();

            PointLightVolumeInstance snapshotPoint = CreatePointLight(manager, "Area Material Cookie Snapshot", true);
            snapshotPoint.transform.localScale = new Vector3(2, 3, 1);
            snapshotPoint.SetCustomMaterial(material, false);
            snapshotPoint.SetAreaLight();

            manager.PointLightVolumeInstances = new[] { livePoint, snapshotPoint };

            manager.ReinitializeCustomTextures();
            manager.UpdateVolumes();

            Assert.That(GetManagerField<int>(manager, _customSingleMaterialCountField), Is.EqualTo(2));
            Assert.That(manager.CustomTextures, Is.Not.Null);
            Assert.That(manager.CustomTextures.volumeDepth, Is.EqualTo(2));
            Assert.That(manager.HasAutoCustomTextureUpdates, Is.True);
            Assert.That(GetManagerField<bool[]>(manager, _customSingleMaterialAutoUpdatesField), Is.EqualTo(new[] { true, false }));
            Assert.That(GetManagerField<int[]>(manager, _pointLightCustomIDsField), Is.EqualTo(new[] { 0, 1 }));
            AssertPointCustomData(0, livePoint, -1, 0);
            AssertPointCustomData(1, snapshotPoint, -2, 0);
        }

        // Verifies the runtime API assigns a texture source and refreshes manager-owned projection arrays
        [Test]
        public void CustomTextureApiAssignsTextureAndRefreshesRuntimeArray() {
            LightVolumeManager manager = CreateManager("Custom Texture API Manager", false);
            RenderTexture source = CreateRenderTexture("Custom Texture API Source", 4, 4, 1, TextureDimension.Tex2D);
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;

            PointLightVolumeInstance point = CreatePointLight(manager, "Custom Texture API Spot", true);
            point.SetSpotLight(60, 0.5f);
            point.SetCustomTexture(source, false, true);
            manager.UpdateVolumes();

            Assert.That(point.CustomTexture, Is.SameAs(source));
            Assert.That(point.CustomTextureMaterial, Is.Null);
            Assert.That(point.ProjectionType, Is.EqualTo(1)); // 1: texture
            Assert.That(point.ProjectionMode, Is.EqualTo(2)); // 2: custom cookie or cubemap
            Assert.That(point.AutoUpdateCustomTexture, Is.True);
            Assert.That(point.CustomTextureIsCubemap, Is.False);
            Assert.That(point.CustomTextureHasDepthSlices, Is.False);
            Assert.That(manager.CustomTextures, Is.Not.Null);
            Assert.That(Shader.GetGlobalTexture(_pointLightTextureID), Is.SameAs(manager.CustomTextures));
            Assert.That(manager.CustomTextures.volumeDepth, Is.EqualTo(1));
            AssertPointCustomData(point, -1, 0);

            point.SetCustomTexture(null, false, false);
            manager.UpdateVolumes();

            Assert.That(point.CustomTexture, Is.Null);
            Assert.That(point.ProjectionType, Is.EqualTo(0)); // 0: none
            Assert.That(point.ProjectionMode, Is.EqualTo(0)); // 0: parametric
            Assert.That(manager.CustomTextures, Is.Null);
        }

        // Verifies the runtime API uses isCubemap to mark cubemap texture sources
        [Test]
        public void CustomTextureApiMarksCubemapSources() {
            LightVolumeManager manager = CreateManager("Custom Texture API Cubemap Manager", false);
            Cubemap source = CreateCubemap("Custom Texture API Cubemap Source");
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;

            PointLightVolumeInstance point = CreatePointLight(manager, "Custom Texture API Point", true);
            point.SetCustomTexture(source, true, true);
            manager.UpdateVolumes();

            Assert.That(point.CustomTexture, Is.SameAs(source));
            Assert.That(point.CustomTextureIsCubemap, Is.True);
            Assert.That(point.CustomTextureHasDepthSlices, Is.False);
            Assert.That(manager.CustomTextures, Is.Not.Null);
            Assert.That(manager.CubemapsCount, Is.EqualTo(1));
            Assert.That(manager.CustomTextures.volumeDepth, Is.EqualTo(6));
            AssertPointCustomData(point, -1, 0);
        }

        // Verifies the runtime API assigns a material source and refreshes manager-owned projection arrays
        [Test]
        public void CustomMaterialApiAssignsMaterialAndRefreshesRuntimeArray() {
            LightVolumeManager manager = CreateManager("Custom Material API Manager", false);
            Material material = CreateMaterial("Hidden/CubeFace");
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;

            PointLightVolumeInstance firstPoint = CreatePointLight(manager, "Material API Point A", true);
            firstPoint.SetCustomMaterial(material, true);

            PointLightVolumeInstance duplicatePoint = CreatePointLight(manager, "Material API Point B", true);
            duplicatePoint.SetCustomMaterial(material, true);

            Assert.That(firstPoint.CustomTexture, Is.Null);
            Assert.That(firstPoint.CustomTextureMaterial, Is.SameAs(material));
            Assert.That(firstPoint.ProjectionType, Is.EqualTo(2)); // 2: material
            Assert.That(firstPoint.ProjectionMode, Is.EqualTo(2)); // 2: custom cookie or cubemap
            Assert.That(firstPoint.AutoUpdateCustomTexture, Is.True);

            manager.UpdateVolumes();

            Assert.That(manager.CubemapsCount, Is.EqualTo(1));
            Assert.That(manager.CustomTextures, Is.Not.Null);
            Assert.That(manager.CustomTextures.volumeDepth, Is.EqualTo(6));
            Assert.That(GetManagerField<int[]>(manager, _pointLightCustomIDsField), Is.EqualTo(new[] { 0, 0 }));
            AssertPointCustomData(0, firstPoint, -1, 0);
            AssertPointCustomData(1, duplicatePoint, -1, 0);
        }

        // Verifies runtime cookie size comes from the manager setting, not from the source texture
        [Test]
        public void SpotCookieRuntimeArrayUsesConfiguredSize() {
            LightVolumeManager manager = CreateManager("Spot Cookie Configured Size Manager", false);
            RenderTexture source = CreateRenderTexture("Animated Spot Cookie No Fallback Source", 8, 4, 1, TextureDimension.Tex2D);
            manager.CustomTexturesWidth = 16;
            manager.CustomTexturesHeight = 8;

            PointLightVolumeInstance point = CreatePointLight(manager, "Animated Spot Cookie No Fallback Light", true);
            point.SetCustomTexture();
            point.SetSpotLight(60, 0.5f);
            point.CustomTexture = source;
            point.ProjectionType = 1; // 1: texture
            point.AutoUpdateCustomTexture = true;
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeCustomTextures();
            manager.UpdateVolumes();

            Assert.That(manager.CustomTextures, Is.Not.Null);
            Assert.That(Shader.GetGlobalTexture(_pointLightTextureID), Is.SameAs(manager.CustomTextures));
            Assert.That(manager.CustomTextures.width, Is.EqualTo(16));
            Assert.That(manager.CustomTextures.height, Is.EqualTo(8));
            Assert.That(manager.CustomTextures.volumeDepth, Is.EqualTo(1));
            Assert.That(manager.CustomTextures.format, Is.EqualTo(RenderTextureFormat.ARGBHalf));
            AssertPointCustomData(point, -1, 0);
        }

        // Verifies animated point cubemap cookies target a reserved six-slice cubemap range.
        [Test]
        public void AnimatedPointCubemapUsesReservedCubemapSliceRange() {
            LightVolumeManager manager = CreateManager("Animated Point Cubemap Manager", false);
            RenderTexture source = CreateRenderTexture("Animated Point Cubemap Source", 4, 4, 6, TextureDimension.Tex2DArray);
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;

            PointLightVolumeInstance point = CreatePointLight(manager, "Animated Point Cubemap Light", true);
            point.SetCustomTexture();
            point.SetPointLight();
            point.CustomTexture = source;
            point.ProjectionType = 1; // 1: texture
            point.CustomTextureHasDepthSlices = true;
            point.AutoUpdateCustomTexture = true;
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeCustomTextures();
            manager.UpdateVolumes();

            Assert.That(manager.CustomTextures, Is.Not.Null);
            Assert.That(manager.CustomTextures.volumeDepth, Is.EqualTo(6));
            Assert.That(manager.CustomTextures.format, Is.EqualTo(RenderTextureFormat.ARGBHalf));
            AssertGlobalFloat(_pointLightCubeCountID, 1);
            AssertPointCustomData(point, -1, 0);
        }

        // Verifies static shadow cubemaps build the same manager-owned runtime array as animated sources.
        [Test]
        public void ShadowCubemapCreatesRuntimeArray() {
            LightVolumeManager manager = CreateManager("Shadow Cubemap Runtime Manager", false);
            Cubemap source = CreateCubemap("Shadow Cubemap Source");
            manager.ShadowTexturesWidth = 8;
            manager.ShadowTexturesHeight = 8;

            PointLightVolumeInstance point = CreatePointLight(manager, "Shadow Cubemap Light", true);
            ConfigureShadowTexture(point, source, false, true, false);
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();

            Assert.That(manager.ShadowTextures, Is.Not.Null);
            Assert.That(manager.ShadowTextures.dimension, Is.EqualTo(TextureDimension.Tex2DArray));
            Assert.That(manager.ShadowTextures.width, Is.EqualTo(8));
            Assert.That(manager.ShadowTextures.height, Is.EqualTo(8));
            Assert.That(manager.ShadowTextures.volumeDepth, Is.EqualTo(6));
            Assert.That(manager.ShadowTextures.format, Is.EqualTo(RenderTextureFormat.ARGBFloat));
            Assert.That(manager.ShadowTextures.useMipMap, Is.False);
            Assert.That(manager.ShadowTextures.autoGenerateMips, Is.False);
            Assert.That(manager.ShadowTextures.filterMode, Is.EqualTo(FilterMode.Bilinear));
            Assert.That(Shader.GetGlobalTexture(_pointLightShadowTextureID), Is.SameAs(manager.ShadowTextures));
            AssertGlobalFloat(_pointLightShadowCountID, 1);
        }

        // Verifies serialized shadow outputs cannot keep an old resolution after setup metadata changes.
        [Test]
        public void ShadowRuntimeArrayUsesConfiguredSize() {
            LightVolumeManager manager = CreateManager("Shadow Configured Size Manager", false);
            RenderTexture source = CreateRenderTexture("Shadow Configured Size Source", 8, 4, 6, TextureDimension.Tex2DArray);
            manager.ShadowTexturesWidth = 16;
            manager.ShadowTexturesHeight = 8;
            manager.ShadowTextures = CreateRenderTexture("Shadow Stale Runtime", 64, 64, 6, TextureDimension.Tex2DArray);

            PointLightVolumeInstance point = CreatePointLight(manager, "Shadow Configured Size Light", true);
            ConfigureShadowTexture(point, source, true, false, true);
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();

            Assert.That(manager.ShadowTextures, Is.Not.Null);
            Assert.That(manager.ShadowTextures.width, Is.EqualTo(16));
            Assert.That(manager.ShadowTextures.height, Is.EqualTo(8));
            Assert.That(manager.ShadowTextures.format, Is.EqualTo(RenderTextureFormat.ARGBFloat));
            Assert.That(manager.ShadowTextures.useMipMap, Is.False);
            Assert.That(manager.ShadowTextures.autoGenerateMips, Is.False);
        }

        // Verifies destruction cleanup releases manager-owned shadow output arrays.
        [Test]
        public void RuntimeShadowOutputDestroyClearsManagerOwnedRuntimeTexture() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Output Reset Manager", false);
            RenderTexture staleOutput = CreateRenderTexture("LightVolumeManager_ShadowTextures", 64, 64, 6, TextureDimension.Tex2DArray);
            staleOutput.hideFlags = HideFlags.HideAndDontSave;
            manager.ShadowTextures = staleOutput;
            manager.ShadowMapsCount = 1;
            SetManagerField(manager, _shadowTexturesDepthField, staleOutput.volumeDepth);
            manager.ShadowTexturesWidth = 256;
            manager.ShadowTexturesHeight = 256;

            InvokeLifecycleMethod(manager, "OnDestroy");

            Assert.That(manager.ShadowTextures, Is.Null);
        }

        // Verifies shadow runtime arrays use the default EVSM float format.
        [Test]
        public void ShadowRuntimeArrayUsesDefaultEVSMFloatFormat() {
            LightVolumeManager manager = CreateManager("Shadow Default Format Manager", false);
            Cubemap source = CreateCubemap("Shadow Default Format Source");
            manager.ShadowTexturesWidth = 4;
            manager.ShadowTexturesHeight = 4;

            PointLightVolumeInstance point = CreatePointLight(manager, "Shadow Fixed Format Light", true);
            ConfigureShadowTexture(point, source, false, true, false);
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeShadowTextures();

            Assert.That(manager.ShadowTextures, Is.Not.Null);
            Assert.That(manager.ShadowTextures.format, Is.EqualTo(RenderTextureFormat.ARGBFloat));
            Assert.That(manager.ShadowTextures.useMipMap, Is.False);
            Assert.That(manager.ShadowTextures.autoGenerateMips, Is.False);
        }

        // Verifies EVSM Half shadows use an ARGBHalf texture array.
        [Test]
        public void ShadowRuntimeArrayUsesConfiguredHalfFormat() {
            LightVolumeManager manager = CreateManager("Shadow EVSM Half Type Manager", false);
            Cubemap source = CreateCubemap("Shadow EVSM Half Type Source");
            manager.ShadowTextureFormat = 0;
            manager.ShadowBleedReduction = 0.35f;
            manager.ShadowMinVariance = 0.01f;
            manager.ShadowTexturesWidth = 4;
            manager.ShadowTexturesHeight = 4;

            PointLightVolumeInstance point = CreatePointLight(manager, "Shadow EVSM Half Type Light", true);
            point.WorldSpaceShadows = true;
            point.Bias = 0;
            point.NearClip = 0.25f;
            ConfigureShadowTexture(point, source, false, true, false);
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();

            Assert.That(manager.ShadowTextures, Is.Not.Null);
            Assert.That(manager.ShadowTextures.format, Is.EqualTo(RenderTextureFormat.ARGBHalf));
            Assert.That(manager.ShadowTextures.useMipMap, Is.False);
            Assert.That(manager.ShadowTextures.autoGenerateMips, Is.False);
            AssertVectorClose(new Vector4(0.0001f * 5.54f, -0.35f / 0.65f, 1f / 0.65f, 0.0001f * 5f), Shader.GetGlobalVector(_pointLightShadowReceiverParamsID));
            AssertPointCustomData(point, 0, 1);
            Assert.That(Shader.GetGlobalVectorArray(_pointLightExtraDataID)[0].w, Is.EqualTo(0.25f).Within(Epsilon));
        }

        // Keeps the precomputed EVSM constants finite even when authoring values are outside their supported range.
        [Test]
        public void DisabledManagerPublishesClampedShadowReceiverParams() {
            LightVolumeManager manager = CreateManager("Clamped Shadow Receiver Params Manager", false);
            manager.ShadowBleedReduction = 2f;
            manager.ShadowMinVariance = -4f;

            manager.UpdateVolumes();

            AssertVectorClose(new Vector4(0f, -0.999f / (1f - 0.999f), 1f / (1f - 0.999f), 0f), Shader.GetGlobalVector(_pointLightShadowReceiverParamsID));
        }

        // Verifies EVSM Float shadows use an ARGBFloat texture array.
        [Test]
        public void ShadowRuntimeArrayUsesConfiguredFloatFormat() {
            LightVolumeManager manager = CreateManager("Shadow EVSM Float Type Manager", false);
            Cubemap source = CreateCubemap("Shadow EVSM Float Type Source");
            manager.ShadowTextureFormat = 1;
            manager.ShadowTexturesWidth = 4;
            manager.ShadowTexturesHeight = 4;

            PointLightVolumeInstance point = CreatePointLight(manager, "Shadow EVSM Float Type Light", true);
            point.WorldSpaceShadows = true;
            ConfigureShadowTexture(point, source, false, true, false);
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();

            Assert.That(manager.ShadowTextures, Is.Not.Null);
            Assert.That(manager.ShadowTextures.format, Is.EqualTo(RenderTextureFormat.ARGBFloat));
            Assert.That(manager.ShadowTextures.useMipMap, Is.False);
            Assert.That(manager.ShadowTextures.autoGenerateMips, Is.False);
            AssertPointCustomData(point, 0, 1);
        }

        // Verifies manager shader uploads sanitize shadow clip values without rewriting the light instance.
        [Test]
        public void PointLightShadowClipUploadClampsWithoutMutatingInstance() {
            LightVolumeManager manager = CreateManager("Shadow Clip Upload Clamp Manager", false);
            Cubemap source = CreateCubemap("Shadow Clip Upload Clamp Source");
            manager.ShadowTexturesWidth = 4;
            manager.ShadowTexturesHeight = 4;

            PointLightVolumeInstance point = CreatePointLight(manager, "Shadow Clip Upload Clamp Light", true);
            point.SquaredRange = 64f;
            point.IsRangeDirty = false;
            point.NearClip = 4f;
            point.FarClip = 1f;
            point.WorldSpaceShadows = true;
            ConfigureShadowTexture(point, source, false, true, false);
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();

            AssertPointCustomData(point, 0, 1);
            Assert.That(Shader.GetGlobalVectorArray(_pointLightExtraDataID)[0].w, Is.EqualTo(4f).Within(Epsilon));
            Assert.That(Shader.GetGlobalVectorArray(_pointLightCustomIdID)[0].w,
                Is.EqualTo(ExpectedCustomShadowInvDepthRange(point)).Within(Epsilon));
            Assert.That(Shader.GetGlobalVectorArray(_pointLightShadowReprojectionDataID)[0].w,
                Is.EqualTo(ExpectedCubemapShadowInvDepthRange(point)).Within(Epsilon));
            Assert.That(point.NearClip, Is.EqualTo(4f).Within(Epsilon));
            Assert.That(point.FarClip, Is.EqualTo(1f).Within(Epsilon));
        }

        // Verifies a repeated runtime bake republishes the receiver depth range when only its clip planes changed.
        [Test]
        public void RuntimeShadowBakeClipChangeRefreshesReceiverMetadata() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Clip Refresh Manager", false);
            manager.ShadowTexturesWidth = 16;
            manager.ShadowTexturesHeight = 16;
            PointLightVolumeInstance point = CreatePointLight(manager, "Runtime Shadow Clip Refresh Light", true);
            point.SquaredRange = 64f;
            point.IsRangeDirty = false;
            point.NearClip = 0.25f;
            point.FarClip = 4f;
            point.RuntimeShadowResolution = 16;
            point.RuntimeShadowDepthEncodeMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowDepthEncode");
            AddRuntimeShadowCamera(point);

            point.BakeShadows();
            manager.UpdateVolumes();
            Assert.That(Shader.GetGlobalVectorArray(_pointLightExtraDataID)[0].w, Is.EqualTo(0.25f).Within(Epsilon));
            Assert.That(Shader.GetGlobalVectorArray(_pointLightShadowReprojectionDataID)[0].w,
                Is.EqualTo(-1f / 3.75f).Within(Epsilon));

            point.IsRangeDirty = false;
            point.NearClip = 0.5f;
            point.FarClip = 8f;
            point.BakeShadows();

            // The edit-mode C# proxy applies RequestUpdateVolumes synchronously.
            Assert.That(Shader.GetGlobalVectorArray(_pointLightExtraDataID)[0].w, Is.EqualTo(0.5f).Within(Epsilon));
            Assert.That(Shader.GetGlobalVectorArray(_pointLightShadowReprojectionDataID)[0].w,
                Is.EqualTo(-1f / 7.5f).Within(Epsilon));
        }

        // Verifies runtime shadow baking uses the target light far clip data.
        [Test]
        public void RuntimeShadowBakerUsesTargetRangeForShadowFarClip() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Far Clip Manager", false);
            PointLightVolumeInstance point = CreatePointLight(manager, "Runtime Shadow Far Clip Light", true);
            point.FarClip = 0f;
            point.Intensity = 100f;

            PointLightVolumeInstance baker = point;
            baker.RuntimeShadowResolution = 16;
            baker.RuntimeShadowDepthEncodeMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowDepthEncode");
            Camera shadowCamera = AddRuntimeShadowCamera(baker);

            float firstAutomaticFarClip = point.GetShadowFarClip();
            point.IsRangeDirty = true;
            baker.BakeShadows();
            Assert.That(shadowCamera.farClipPlane, Is.EqualTo(firstAutomaticFarClip).Within(Epsilon));
            Assert.That(point.BakedFarClip, Is.EqualTo(firstAutomaticFarClip).Within(Epsilon));

            point.Intensity = 25f;
            float secondAutomaticFarClip = point.GetShadowFarClip();
            point.IsRangeDirty = true;

            baker.BakeShadows();
            Assert.That(shadowCamera.farClipPlane, Is.EqualTo(secondAutomaticFarClip).Within(Epsilon));
            Assert.That(point.BakedFarClip, Is.EqualTo(secondAutomaticFarClip).Within(Epsilon));

            point.FarClip = 3;

            baker.BakeShadows();
            Assert.That(shadowCamera.farClipPlane, Is.EqualTo(3).Within(Epsilon));
            Assert.That(point.BakedFarClip, Is.EqualTo(3).Within(Epsilon));
        }

        // Verifies a dirty automatic range is resolved before runtime shadow depth is encoded and uploaded.
        [Test]
        public void RuntimeShadowBakerRefreshesDirtyAutomaticRangeBeforeEncoding() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Dirty Range Manager", false);
            manager.ShadowTexturesWidth = 16;
            manager.ShadowTexturesHeight = 16;
            PointLightVolumeInstance point = CreatePointLight(manager, "Runtime Shadow Dirty Range Light", true);
            point.SetLut();
            point.SetLightSourceSize(8f);
            point.SquaredRange = 1f; // Deliberately stale value from before the LUT range change.
            point.IsRangeDirty = true;
            point.NearClip = 0.25f;
            point.FarClip = 0f;
            point.RuntimeShadowResolution = 16;
            point.RuntimeShadowDepthEncodeMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowDepthEncode");
            Camera shadowCamera = AddRuntimeShadowCamera(point);
            float expectedFarClip = point.GetShadowFarClip();

            point.BakeShadows();

            Assert.That(point.IsRangeDirty, Is.False);
            Assert.That(point.SquaredRange, Is.EqualTo(64f).Within(Epsilon));
            Assert.That(shadowCamera.farClipPlane, Is.EqualTo(expectedFarClip).Within(Epsilon));
            Assert.That(point.BakedFarClip, Is.EqualTo(expectedFarClip).Within(Epsilon));
            Assert.That(point.RuntimeShadowDepthEncodeMaterial.GetFloat("_ShadowFarClip"), Is.EqualTo(expectedFarClip).Within(Epsilon));

            manager.UpdateVolumes();
            Assert.That(Shader.GetGlobalVectorArray(_pointLightShadowReprojectionDataID)[0].w,
                Is.EqualTo(-1f / (expectedFarClip - 0.25f)).Within(Epsilon));
        }

        // Verifies runtime shadow baking treats FarClip as an input setting and does not publish calculated range back to the target.
        [Test]
        public void RuntimeShadowBakerDoesNotOverwriteTargetFarClip() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Published Far Clip Manager", false);
            PointLightVolumeInstance point = CreatePointLight(manager, "Runtime Shadow Published Far Clip Light", true);
            point.FarClip = 0;
            point.Intensity = 100f;

            PointLightVolumeInstance baker = point;
            baker.RuntimeShadowResolution = 16;
            baker.RuntimeShadowDepthEncodeMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowDepthEncode");
            Camera shadowCamera = AddRuntimeShadowCamera(baker);

            float firstAutomaticFarClip = point.GetShadowFarClip();
            point.IsRangeDirty = true;
            baker.BakeShadows();
            Assert.That(shadowCamera.farClipPlane, Is.EqualTo(firstAutomaticFarClip).Within(Epsilon));
            Assert.That(point.FarClip, Is.EqualTo(0).Within(Epsilon));
            Assert.That(point.BakedFarClip, Is.EqualTo(firstAutomaticFarClip).Within(Epsilon));

            point.Intensity = 25f;
            float secondAutomaticFarClip = point.GetShadowFarClip();
            point.IsRangeDirty = true;
            baker.BakeShadows();
            Assert.That(shadowCamera.farClipPlane, Is.EqualTo(secondAutomaticFarClip).Within(Epsilon));
            Assert.That(point.FarClip, Is.EqualTo(0).Within(Epsilon));
            Assert.That(point.BakedFarClip, Is.EqualTo(secondAutomaticFarClip).Within(Epsilon));

            point.FarClip = 5;
            baker.BakeShadows();
            Assert.That(shadowCamera.farClipPlane, Is.EqualTo(5).Within(Epsilon));
            Assert.That(point.FarClip, Is.EqualTo(5).Within(Epsilon));
            Assert.That(point.BakedFarClip, Is.EqualTo(5).Within(Epsilon));
        }

        // Verifies automatic baking publishes its resolved far clip without replacing the 0 authoring mode.
        [Test]
        public void AutomaticShadowBakeKeepsFarClipSettingAtZero() {
            LightVolumeManager manager = CreateManager("Editor Shadow Far Clip Manager", false);
            PointLightVolumeInstance point = CreatePointLight(manager, "Editor Shadow Far Clip Light", true);
            point.NearClip = 0.25f;
            point.FarClip = 0f;
            point.RuntimeShadowResolution = 16;
            point.RuntimeShadowDepthEncodeMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowDepthEncode");
            Camera shadowCamera = AddRuntimeShadowCamera(point);
            float expectedFarClip = point.GetShadowFarClip();

            point.BakeShadows();

            Assert.That(shadowCamera.farClipPlane, Is.EqualTo(expectedFarClip).Within(Epsilon));
            Assert.That(point.FarClip, Is.EqualTo(0f).Within(Epsilon));
            Assert.That(point.BakedFarClip, Is.EqualTo(expectedFarClip).Within(Epsilon));
            manager.UpdateVolumes();
            Assert.That(Shader.GetGlobalVectorArray(_pointLightShadowReprojectionDataID)[0].w,
                Is.EqualTo(-1f / (expectedFarClip - 0.25f)).Within(Epsilon));
        }

        // Verifies runtime shadow baking clamps unsafe bake inputs locally without normalizing public fields.
        [Test]
        public void RuntimeShadowBakerClampsBakeInputsWithoutMutatingTargetFields() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Clamp Inputs Manager", false);
            PointLightVolumeInstance point = CreatePointLight(manager, "Runtime Shadow Clamp Inputs Light", true);
            point.SquaredRange = 4f;
            point.IsRangeDirty = false;
            point.NearClip = -2f;
            point.FarClip = -1f;
            point.Bias = -0.25f;
            point.Blur = -1f;
            point.ContactHardening = 2f;

            PointLightVolumeInstance baker = point;
            baker.RuntimeShadowResolution = 16;
            baker.RuntimeShadowDepthEncodeMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowDepthEncode");
            baker.RuntimeShadowBlurMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowRuntimeBlur");
            Camera shadowCamera = AddRuntimeShadowCamera(baker);

            baker.BakeShadows();

            Assert.That(shadowCamera.nearClipPlane, Is.EqualTo(0.0001f).Within(Epsilon));
            Assert.That(shadowCamera.farClipPlane, Is.EqualTo(2f).Within(Epsilon));
            Assert.That(baker.RuntimeShadowDepthEncodeMaterial.GetFloat("_ShadowNearClip"), Is.EqualTo(0.0001f).Within(Epsilon));
            Assert.That(baker.RuntimeShadowDepthEncodeMaterial.GetFloat("_ShadowFarClip"), Is.EqualTo(2f).Within(Epsilon));
            Assert.That(baker.RuntimeShadowDepthEncodeMaterial.GetFloat("_ShadowBakeBias"), Is.EqualTo(0f).Within(Epsilon));
            Assert.That(point.NearClip, Is.EqualTo(-2f).Within(Epsilon));
            Assert.That(point.FarClip, Is.EqualTo(-1f).Within(Epsilon));
            Assert.That(point.Bias, Is.EqualTo(-0.25f).Within(Epsilon));
            Assert.That(point.Blur, Is.EqualTo(-1f).Within(Epsilon));
            Assert.That(point.ContactHardening, Is.EqualTo(2f).Within(Epsilon));

            point.Blur = 2f;
            baker.BakeShadows();

            Assert.That(baker.RuntimeShadowBlurMaterial.GetFloat("_BlurRadius"), Is.EqualTo(0.25f).Within(Epsilon));
            Assert.That(baker.RuntimeShadowBlurMaterial.GetFloat("_BlurDepth"), Is.EqualTo(1f).Within(Epsilon));
            Assert.That(point.Blur, Is.EqualTo(2f).Within(Epsilon));
            Assert.That(point.ContactHardening, Is.EqualTo(2f).Within(Epsilon));
        }

        // Verifies runtime shadow baking uses the target light bias so it matches editor shadow bakes.
        [Test]
        public void RuntimeShadowBakerUsesTargetBakeBias() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Bias Manager", false);
            PointLightVolumeInstance point = CreatePointLight(manager, "Runtime Shadow Bias Light", true);
            point.Bias = 0;

            PointLightVolumeInstance baker = point;
            baker.RuntimeShadowResolution = 16;
            baker.RuntimeShadowDepthEncodeMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowDepthEncode");
            AddRuntimeShadowCamera(baker);

            baker.BakeShadows();
            Assert.That(baker.RuntimeShadowDepthEncodeMaterial.GetFloat("_ShadowBakeBias"), Is.EqualTo(0).Within(Epsilon));

            point.Bias = 0.125f;

            baker.BakeShadows();
            Assert.That(baker.RuntimeShadowDepthEncodeMaterial.GetFloat("_ShadowBakeBias"), Is.EqualTo(0.125f).Within(Epsilon));
        }

        // Verifies runtime shadow baking reads camera and blur settings from the target light instance.
        [Test]
        public void RuntimeShadowBakerUsesTargetBakeSettings() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Settings Manager", false);
            PointLightVolumeInstance point = CreatePointLight(manager, "Runtime Shadow Settings Light", true);
            point.NearClip = 0.25f;
            point.LayerMask = 1 << 7;
            point.Blur = 6.5f;
            point.ContactHardening = 0.35f;

            PointLightVolumeInstance baker = point;
            baker.RuntimeShadowSphericalBlur = true;
            baker.RuntimeShadowDepthEncodeMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowDepthEncode");
            baker.RuntimeShadowBlurMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowRuntimeBlur");
            Camera shadowCamera = AddRuntimeShadowCamera(baker);

            baker.BakeShadows();

            Assert.That(shadowCamera.nearClipPlane, Is.EqualTo(0.25f).Within(Epsilon));
            Assert.That(shadowCamera.cullingMask, Is.EqualTo(1 << 7));
            Assert.That(baker.RuntimeShadowBlurMaterial.GetFloat("_BlurRadius"), Is.EqualTo(6.5f).Within(Epsilon));
            Assert.That(baker.RuntimeShadowBlurMaterial.GetFloat("_BlurDepth"), Is.EqualTo((Mathf.Pow(10f, 0.35f) - 1f) * 0.1111111111f).Within(Epsilon));
            Assert.That(baker.RuntimeShadowBlurMaterial.IsKeywordEnabled("VRCLV_RUNTIME_SHADOW_BLUR_SPHERICAL"), Is.True);
        }

        // Verifies every runtime bake reconfigures the shared camera from the current light settings.
        [Test]
        public void RuntimeShadowBakerReconfiguresSharedCameraBetweenLights() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Shared Camera Config Manager", false);
            manager.ShadowTexturesWidth = 16;
            manager.ShadowTexturesHeight = 16;
            manager.RuntimeShadowDepthEncodeMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowDepthEncode");
            manager.EnsureRuntimeShadowCamera();
            Camera sharedCamera = manager.RuntimeShadowCamera;

            PointLightVolumeInstance first = CreatePointLight(manager, "Runtime Shadow Shared Camera First", true);
            PointLightVolumeInstance second = CreatePointLight(manager, "Runtime Shadow Shared Camera Second", true);
            manager.PointLightVolumeInstances = new[] { first, second };

            first.RuntimeShadowResolution = 16;
            first.LayerMask = 1 << 5;
            first.NearClip = 0.25f;
            first.FarClip = 4f;
            first.Blur = 0f;

            second.RuntimeShadowResolution = 16;
            second.LayerMask = 1 << 7;
            second.NearClip = 0.5f;
            second.FarClip = 8f;
            second.Blur = 0f;

            first.BakeShadows();
            Assert.That(sharedCamera.cullingMask, Is.EqualTo(1 << 5));
            Assert.That(sharedCamera.nearClipPlane, Is.EqualTo(0.25f).Within(Epsilon));
            Assert.That(sharedCamera.farClipPlane, Is.EqualTo(4f).Within(Epsilon));

            second.BakeShadows();
            Assert.That(sharedCamera.cullingMask, Is.EqualTo(1 << 7));
            Assert.That(sharedCamera.nearClipPlane, Is.EqualTo(0.5f).Within(Epsilon));
            Assert.That(sharedCamera.farClipPlane, Is.EqualTo(8f).Within(Epsilon));

            first.BakeShadows();
            Assert.That(sharedCamera.cullingMask, Is.EqualTo(1 << 5));
            Assert.That(sharedCamera.nearClipPlane, Is.EqualTo(0.25f).Within(Epsilon));
            Assert.That(sharedCamera.farClipPlane, Is.EqualTo(4f).Within(Epsilon));
        }

        // Verifies realtime baking keeps the baker resolution separate from the manager-owned final array size.
        [Test]
        public void RuntimeShadowBakerResolutionDoesNotOverrideManagerArraySize() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Resolution Manager", false);
            manager.ShadowTexturesWidth = 32;
            manager.ShadowTexturesHeight = 32;
            PointLightVolumeInstance point = CreatePointLight(manager, "Runtime Shadow Resolution Light", true);

            PointLightVolumeInstance baker = point;
            baker.RuntimeShadowResolution = 96;
            baker.RuntimeShadowDepthEncodeMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowDepthEncode");
            AddRuntimeShadowCamera(baker);

            FieldInfo shadowTextureField = typeof(PointLightVolumeInstance).GetField("_runtimeShadowTexture", _lifecycleMethodFlags);
            Assert.That(shadowTextureField, Is.Not.Null);

            baker.BakeShadows();

            RenderTexture shadowTexture = (RenderTexture)shadowTextureField.GetValue(baker);
            Assert.That(shadowTexture, Is.Not.Null);
            Assert.That(shadowTexture.width, Is.EqualTo(96));
            Assert.That(shadowTexture.height, Is.EqualTo(96));
            Assert.That(manager.ShadowTexturesWidth, Is.EqualTo(32));
            Assert.That(manager.ShadowTexturesHeight, Is.EqualTo(32));
        }

        // Verifies texture recreation restarts an unfinished per-face bake without cached resolution fields.
        [Test]
        public void RuntimeShadowBakerResolutionChangeRestartsPartialBake() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Resolution Restart Manager", false);
            PointLightVolumeInstance point = CreatePointLight(manager, "Runtime Shadow Resolution Restart Light", true);

            PointLightVolumeInstance baker = point;
            baker.RuntimeShadowResolution = 16;
            baker.RuntimeShadowFacesPerFrame = 1;
            baker.RuntimeShadowDepthEncodeMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowDepthEncode");
            AddRuntimeShadowCamera(baker);

            FieldInfo faceIndexField = typeof(PointLightVolumeInstance).GetField("_runtimeShadowFaceIndex", _lifecycleMethodFlags);
            FieldInfo shadowTextureField = typeof(PointLightVolumeInstance).GetField("_runtimeShadowTexture", _lifecycleMethodFlags);
            Assert.That(faceIndexField, Is.Not.Null);
            Assert.That(shadowTextureField, Is.Not.Null);

            baker.BakeShadows();
            Assert.That((int)faceIndexField.GetValue(baker), Is.EqualTo(1));

            baker.RuntimeShadowResolution = 32;
            baker.BakeShadows();

            RenderTexture shadowTexture = (RenderTexture)shadowTextureField.GetValue(baker);
            Assert.That(shadowTexture, Is.Not.Null);
            Assert.That(shadowTexture.width, Is.EqualTo(32));
            Assert.That((int)faceIndexField.GetValue(baker), Is.EqualTo(1));
        }

        // Verifies changing receiver clip planes restarts an unfinished cubemap cycle at face zero.
        [Test]
        public void RuntimeShadowBakerClipChangeRestartsPartialBake() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Clip Restart Manager", false);
            PointLightVolumeInstance point = CreatePointLight(manager, "Runtime Shadow Clip Restart Light", true);
            point.RuntimeShadowResolution = 16;
            point.RuntimeShadowFacesPerFrame = 1;
            point.RuntimeShadowDepthEncodeMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowDepthEncode");
            Camera shadowCamera = AddRuntimeShadowCamera(point);
            point.NearClip = 0.25f;
            point.FarClip = 4f;

            FieldInfo faceIndexField = typeof(PointLightVolumeInstance).GetField("_runtimeShadowFaceIndex", _lifecycleMethodFlags);
            Assert.That(faceIndexField, Is.Not.Null);

            point.BakeShadows();
            Assert.That((int)faceIndexField.GetValue(point), Is.EqualTo(1));

            point.NearClip = 0.5f;
            point.FarClip = 8f;
            point.BakeShadows();

            Assert.That((int)faceIndexField.GetValue(point), Is.EqualTo(1));
            Assert.That(shadowCamera.nearClipPlane, Is.EqualTo(0.5f).Within(Epsilon));
            Assert.That(shadowCamera.farClipPlane, Is.EqualTo(8f).Within(Epsilon));
            Assert.That(point.RuntimeShadowDepthEncodeMaterial.GetFloat("_ShadowNearClip"), Is.EqualTo(0.5f).Within(Epsilon));
            Assert.That(point.RuntimeShadowDepthEncodeMaterial.GetFloat("_ShadowFarClip"), Is.EqualTo(8f).Within(Epsilon));
        }

        // Verifies Bake In Game keeps a full-size generated shadow source instead of publishing a 1x1 direct-output registration texture.
        [Test]
        public void BakeInGameRuntimeShadowBakeUsesFullResolutionSourceTexture() {
            LightVolumeManager manager = CreateManager("Bake In Game Source Texture Manager", false);
            manager.ShadowTexturesWidth = 16;
            manager.ShadowTexturesHeight = 16;
            PointLightVolumeInstance point = CreatePointLight(manager, "Bake In Game Source Texture Light", true);
            point.BakeInGame = true;
            point.RuntimeShadowResolution = 16;
            point.RuntimeShadowDepthEncodeMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowDepthEncode");
            manager.PointLightVolumeInstances = new[] { point };
            AddRuntimeShadowCamera(point);

            FieldInfo shadowTextureField = typeof(PointLightVolumeInstance).GetField("_runtimeShadowTexture", _lifecycleMethodFlags);
            FieldInfo registrationTextureField = typeof(PointLightVolumeInstance).GetField("_runtimeShadowRegistrationTexture", _lifecycleMethodFlags);
            Assert.That(shadowTextureField, Is.Not.Null);
            Assert.That(registrationTextureField, Is.Not.Null);

            point.BakeShadows();

            RenderTexture shadowTexture = (RenderTexture)shadowTextureField.GetValue(point);
            RenderTexture registrationTexture = (RenderTexture)registrationTextureField.GetValue(point);
            Assert.That(registrationTexture, Is.Null);
            Assert.That(shadowTexture, Is.Not.Null);
            Assert.That(shadowTexture.width, Is.EqualTo(16));
            Assert.That(shadowTexture.height, Is.EqualTo(16));
            Assert.That(shadowTexture.dimension, Is.EqualTo(TextureDimension.Tex2DArray));
            Assert.That(shadowTexture.volumeDepth, Is.EqualTo(6));
        }

        // Verifies runtime spot shadow baking uses one texture slice when the target is in single-shadow mode.
        [Test]
        public void RuntimeShadowBakerPreparesSingleTextureSpotShadowMode() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Single Spot Manager", false);
            PointLightVolumeInstance spot = CreatePointLight(manager, "Runtime Shadow Single Spot", true);
            spot.SetSpotLight(60, 0.5f);
            spot.ShadowMapUsesCubemap = false;
            manager.PointLightVolumeInstances = new[] { spot };

            PointLightVolumeInstance baker = spot;
            baker.RuntimeShadowResolution = 16;
            baker.RuntimeShadowSphericalBlur = true;
            baker.RuntimeShadowDepthEncodeMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowDepthEncode");
            AddRuntimeShadowCamera(baker);

            FieldInfo shadowTextureField = typeof(PointLightVolumeInstance).GetField("_runtimeShadowTexture", _lifecycleMethodFlags);
            Assert.That(shadowTextureField, Is.Not.Null);

            baker.BakeShadows();

            RenderTexture shadowTexture = (RenderTexture)shadowTextureField.GetValue(baker);
            Assert.That(shadowTexture, Is.Not.Null);
            Assert.That(shadowTexture.dimension, Is.EqualTo(TextureDimension.Tex2DArray));
            Assert.That(shadowTexture.volumeDepth, Is.EqualTo(1));

            Assert.That(spot.ShadowMapTexture, Is.SameAs(shadowTexture));
            Assert.That(spot.ShadowMapUsesCubemap, Is.False);
            Assert.That(spot.ShadowMapTextureHasDepthSlices, Is.False);
        }

        // Verifies runtime-selected blur variants are kept in player builds instead of relying on editor-only shader_feature fallback.
        [Test]
        public void RuntimeShadowBlurShaderKeepsRuntimeSpotVariantsInBuild() {
            string shaderSource = ReadRuntimeShadowBlurShaderSource();

            Assert.That(shaderSource, Does.Contain("#pragma multi_compile_local_fragment __ VRCLV_RUNTIME_SHADOW_BLUR_DIRECT"));
            Assert.That(shaderSource, Does.Contain("#pragma multi_compile_local_fragment __ VRCLV_RUNTIME_SHADOW_BLUR_SPHERICAL"));
        }

        // MaxOverdraw is a performance budget: completed shadow/cookie/LUT work consumes a slot even when RGB is zero.
        [Test]
        public void PointLightOverdrawCountsCompletedEvaluations() {
            string shaderSource = ReadLightVolumesIncludeSource();
            int shadowMaskStart = shaderSource.IndexOf("inline bool LV_PointLightVolumeShadowMask", StringComparison.Ordinal);
            int contributionStart = shaderSource.IndexOf("bool LV_PointLightVolumeContribution", StringComparison.Ordinal);
            int specularAccumulatorStart = shaderSource.IndexOf("inline bool LV_AccumulatePointLightVolumeSHSpecular", StringComparison.Ordinal);
            int volumeSamplingStart = shaderSource.IndexOf("void LV_SampleLightVolumeTex", StringComparison.Ordinal);
            int diffuseAccumulatorStart = shaderSource.IndexOf("inline bool LV_AccumulatePointLightVolumeSH(", StringComparison.Ordinal);
            int pointLoopStart = shaderSource.IndexOf("void LV_PointLightVolumeSHSpecular", StringComparison.Ordinal);

            Assert.That(shadowMaskStart, Is.GreaterThanOrEqualTo(0));
            Assert.That(contributionStart, Is.GreaterThan(shadowMaskStart));
            Assert.That(specularAccumulatorStart, Is.GreaterThan(contributionStart));
            Assert.That(diffuseAccumulatorStart, Is.GreaterThan(specularAccumulatorStart));
            Assert.That(volumeSamplingStart, Is.GreaterThan(contributionStart));
            Assert.That(pointLoopStart, Is.GreaterThan(diffuseAccumulatorStart));

            string shadowMaskSource = shaderSource.Substring(shadowMaskStart, contributionStart - shadowMaskStart);
            string contributionSource = shaderSource.Substring(contributionStart, volumeSamplingStart - contributionStart);
            string specularAccumulatorSource = shaderSource.Substring(specularAccumulatorStart, diffuseAccumulatorStart - specularAccumulatorStart);
            string diffuseAccumulatorSource = shaderSource.Substring(diffuseAccumulatorStart, pointLoopStart - diffuseAccumulatorStart);
            int areaProjectionStart = contributionSource.IndexOf("LV_ProjectFastQuadLightIrradianceSH(", StringComparison.Ordinal);
            int areaBudgetStart = contributionSource.IndexOf("counted = true;", areaProjectionStart, StringComparison.Ordinal);
            int areaCookieStart = contributionSource.IndexOf("LV_AreaLightCookie(", areaProjectionStart, StringComparison.Ordinal);

            Assert.That(shadowMaskSource, Does.Contain("return shadowVisible;"));
            Assert.That(areaBudgetStart, Is.GreaterThan(areaProjectionStart));
            Assert.That(areaCookieStart, Is.GreaterThan(areaBudgetStart));
            Assert.That(specularAccumulatorSource, Does.Contain("if (any(l0))"));
            Assert.That(specularAccumulatorSource, Does.Not.Contain("if (!any(l0)) return false;"));
            Assert.That(diffuseAccumulatorSource, Does.Not.Contain("if (!any(l0)) return false;"));
            Assert.That(specularAccumulatorSource, Does.Contain("return true;"));
            Assert.That(diffuseAccumulatorSource, Does.Contain("return true;"));
        }

        // The SM4/GLES3.0 fallback is selected at compile time; higher targets use native bit scan without a keyword variant.
        [Test]
        public void ClusteredBitScanUsesShaderTargetInsteadOfApiVariant() {
            string shaderSource = ReadLightVolumesIncludeSource();
            int bitScanStart = shaderSource.IndexOf("inline bool LV_NextClusteredLight", StringComparison.Ordinal);
            int bitScanEnd = shaderSource.IndexOf("// Rotates vector by Quaternion", bitScanStart, StringComparison.Ordinal);

            Assert.That(shaderSource, Does.Contain("#define VRCLV_CLUSTERING_SUPPORTED 1"));
            Assert.That(shaderSource, Does.Contain("uniform Texture2D<int4> _UdonClusterMask;"));
            Assert.That(shaderSource, Does.Contain("inline void LV_LoadClusterMask"));
            Assert.That(shaderSource, Does.Contain("mask = asuint(_UdonClusterMask.Load"));
            Assert.That(shaderSource, Does.Contain("uint sequentialEnd = useClustering ? 0u : pointCount;"));
            Assert.That(shaderSource, Does.Contain("[branch] if (traversalIndex < sequentialEnd)"));
            Assert.That(bitScanStart, Is.GreaterThanOrEqualTo(0));
            Assert.That(bitScanEnd, Is.GreaterThan(bitScanStart));
            string bitScanSource = shaderSource.Substring(bitScanStart, bitScanEnd - bitScanStart);
            Assert.That(bitScanSource, Does.Contain("#if SHADER_TARGET >= 45"));
            Assert.That(bitScanSource, Does.Not.Contain("SHADER_API_GLES3"));
            Assert.That(bitScanSource, Does.Not.Contain("#pragma multi_compile"));
        }

        // Verifies material-source blits keep _MainTex as the generator source and use a dummy texture only for Udon destination binding.
        [Test]
        public void MaterialSourceBlitPreservesMainTexInput() {
            LightVolumeManager manager = CreateManager("Material Source MainTex Manager", false);
            manager.UpdateVolumes();
            manager.CustomTextures = CreateRenderTexture("Material Source MainTex Runtime", 4, 4, 1, TextureDimension.Tex2DArray);
            Material material = CreateMaterial("Unlit/Texture");
            Texture2D mainTexture = CreateTexture2D("Material Source MainTex");
            material.SetTexture("_MainTex", mainTexture);
            MethodInfo method = typeof(LightVolumeManager).GetMethod("BlitMaterialSlice", _lifecycleMethodFlags);
            Assert.That(method, Is.Not.Null);
            Assert.That(_dummyRTField, Is.Not.Null);

            method.Invoke(manager, new object[] { material, 0, 0, false, manager.CustomTextures });

            Assert.That(material.GetTexture("_MainTex"), Is.SameAs(mainTexture));
            AssertVectorClose(new Vector4(4, 4, 1, 0), material.GetVector(CustomRenderTextureInfoProperty));
            Assert.That(GetManagerField<RenderTexture>(manager, _dummyRTField), Is.Not.Null);
        }

        // Verifies runtime blur radius is normalized by resolution before shader sampling.
        [Test]
        public void RuntimeShadowBlurMaterialKeepsEffectiveRadiusStableAcrossResolution() {
            PointLightVolumeInstance baker = CreatePointLight(null, "Runtime Shadow Blur Resolution Light", false);
            Material blurMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowRuntimeBlur");

            MethodInfo initializeShaderPropertiesMethod = typeof(PointLightVolumeInstance).GetMethod("InitializeRuntimeShadowShaderProperties", _lifecycleMethodFlags);
            MethodInfo prepareShadowBlurMaterialMethod = typeof(PointLightVolumeInstance).GetMethod("PrepareRuntimeShadowBlurMaterial", _lifecycleMethodFlags);
            Assert.That(initializeShaderPropertiesMethod, Is.Not.Null);
            Assert.That(prepareShadowBlurMaterialMethod, Is.Not.Null);

            initializeShaderPropertiesMethod.Invoke(baker, null);
            baker.RuntimeShadowBlurMaterial = blurMaterial;
            baker.Blur = 1f;
            baker.ContactHardening = 0f;

            Assert.That((bool)prepareShadowBlurMaterialMethod.Invoke(baker, new object[] { true, 0.25f, 64, false, false }), Is.True);
            float lowResolutionEffectiveRadius = blurMaterial.GetFloat("_BlurRadius") * blurMaterial.GetFloat("_InvResolution");
            float narrowTanHalfFov = blurMaterial.GetFloat("_ShadowTanHalfFov");
            float narrowAngleProjectedRadius = lowResolutionEffectiveRadius / narrowTanHalfFov;
            float narrowAnglePhysicalRadius = narrowAngleProjectedRadius * narrowTanHalfFov;

            Assert.That((bool)prepareShadowBlurMaterialMethod.Invoke(baker, new object[] { true, 1f, 256, false, false }), Is.True);
            float highResolutionEffectiveRadius = blurMaterial.GetFloat("_BlurRadius") * blurMaterial.GetFloat("_InvResolution");
            float wideTanHalfFov = blurMaterial.GetFloat("_ShadowTanHalfFov");
            float wideAngleProjectedRadius = highResolutionEffectiveRadius / wideTanHalfFov;
            float wideAnglePhysicalRadius = wideAngleProjectedRadius * wideTanHalfFov;

            Assert.That(blurMaterial.IsKeywordEnabled("VRCLV_RUNTIME_SHADOW_BLUR_DIRECT"), Is.True);
            Assert.That(blurMaterial.GetFloat("_ShadowTanHalfFov"), Is.EqualTo(1f).Within(Epsilon));
            Assert.That(lowResolutionEffectiveRadius, Is.EqualTo(highResolutionEffectiveRadius).Within(Epsilon));
            Assert.That(narrowAngleProjectedRadius, Is.GreaterThan(wideAngleProjectedRadius));
            Assert.That(narrowAnglePhysicalRadius, Is.EqualTo(wideAnglePhysicalRadius).Within(Epsilon));
        }

        // Verifies shared manager-owned blur materials use manager keyword state instead of stale per-light state.
        [Test]
        public void RuntimeShadowBlurSharedMaterialReappliesKeywordsBetweenLights() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Shared Blur Manager", false);
            Material blurMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowRuntimeBlur");
            manager.RuntimeShadowBlurMaterial = blurMaterial;
            manager.RuntimeShadowBlurQualityPreset = -1;
            manager.RuntimeShadowBlurUniformKeyword = -1;
            manager.RuntimeShadowBlurDirectKeyword = -1;
            manager.RuntimeShadowBlurSphericalKeyword = -1;

            PointLightVolumeInstance highQuality = CreatePointLight(manager, "Runtime Shadow Shared Blur High", true);
            PointLightVolumeInstance lowQuality = CreatePointLight(manager, "Runtime Shadow Shared Blur Low", true);
            highQuality.RuntimeShadowBlurMaterial = blurMaterial;
            lowQuality.RuntimeShadowBlurMaterial = blurMaterial;
            highQuality.RuntimeShadowBlurSamplePreset = 2;
            lowQuality.RuntimeShadowBlurSamplePreset = 0;

            MethodInfo initializeShaderPropertiesMethod = typeof(PointLightVolumeInstance).GetMethod("InitializeRuntimeShadowShaderProperties", _lifecycleMethodFlags);
            MethodInfo prepareShadowBlurMaterialMethod = typeof(PointLightVolumeInstance).GetMethod("PrepareRuntimeShadowBlurMaterial", _lifecycleMethodFlags);
            Assert.That(initializeShaderPropertiesMethod, Is.Not.Null);
            Assert.That(prepareShadowBlurMaterialMethod, Is.Not.Null);

            ConfigureRuntimeShadowBlurReflectionState(highQuality, initializeShaderPropertiesMethod);
            ConfigureRuntimeShadowBlurReflectionState(lowQuality, initializeShaderPropertiesMethod);

            Assert.That((bool)prepareShadowBlurMaterialMethod.Invoke(highQuality, new object[] { true, 1f, 128, true, false }), Is.True);
            Assert.That(blurMaterial.IsKeywordEnabled("VRCLV_RUNTIME_SHADOW_QUALITY_HIGH"), Is.True);
            Assert.That(manager.RuntimeShadowBlurQualityPreset, Is.EqualTo(2));

            Assert.That((bool)prepareShadowBlurMaterialMethod.Invoke(lowQuality, new object[] { true, 1f, 128, true, false }), Is.True);
            Assert.That(blurMaterial.IsKeywordEnabled("VRCLV_RUNTIME_SHADOW_QUALITY_LOW"), Is.True);
            Assert.That(manager.RuntimeShadowBlurQualityPreset, Is.EqualTo(0));

            Assert.That((bool)prepareShadowBlurMaterialMethod.Invoke(highQuality, new object[] { true, 1f, 128, true, false }), Is.True);
            Assert.That(blurMaterial.IsKeywordEnabled("VRCLV_RUNTIME_SHADOW_QUALITY_HIGH"), Is.True);
            Assert.That(manager.RuntimeShadowBlurQualityPreset, Is.EqualTo(2));
        }

        // Verifies planar runtime Spot Light blur compensates projection scale so changing the cone angle keeps blur width stable.
        [Test]
        public void RuntimePlanarSpotBlurCompensatesSpotAngle() {
            string shaderSource = ReadRuntimeShadowBlurShaderSource();
            int radiusMethodStart = shaderSource.IndexOf("float RuntimeBlurRadius", System.StringComparison.Ordinal);
            int stepMethodStart = shaderSource.IndexOf("float2 RuntimeBlurStep", System.StringComparison.Ordinal);
            int stepMethodEnd = shaderSource.IndexOf("float4 BlurArrayDirect", System.StringComparison.Ordinal);
            Assert.That(radiusMethodStart, Is.GreaterThanOrEqualTo(0));
            Assert.That(stepMethodStart, Is.GreaterThan(radiusMethodStart));
            Assert.That(stepMethodEnd, Is.GreaterThan(stepMethodStart));

            string planarRuntimeBlurSource = shaderSource.Substring(radiusMethodStart, stepMethodEnd - radiusMethodStart);
            Assert.That(planarRuntimeBlurSource, Does.Contain("rcp(max(_ShadowTanHalfFov"));
            Assert.That(planarRuntimeBlurSource, Does.Contain("spotScale"));
        }

        // Editor and runtime shadow baking share one exclusion implementation that touches only listed hierarchies.
        [Test]
        public void ExclusionMaskDisablesListedRenderersAndRestoresExactState() {
            PointLightVolumeInstance point = CreateManagerlessPointLight("Shadow Exclusion Light");
            GameObject excludedRoot = CreateGameObject("Shadow Exclusion Root", true);
            MeshRenderer excludedRootRenderer = excludedRoot.AddComponent<MeshRenderer>();
            GameObject excludedChild = CreateGameObject("Shadow Exclusion Child", true);
            excludedChild.transform.SetParent(excludedRoot.transform, false);
            MeshRenderer excludedChildRenderer = excludedChild.AddComponent<MeshRenderer>();
            GameObject includedObject = CreateGameObject("Shadow Included Object", true);
            MeshRenderer includedRenderer = includedObject.AddComponent<MeshRenderer>();

            excludedRootRenderer.forceRenderingOff = true;
            excludedChildRenderer.forceRenderingOff = false;
            includedRenderer.forceRenderingOff = false;
            point.ExclusionMask = new[] { excludedRoot, excludedChild };

            MethodInfo applyMask = typeof(PointLightVolumeInstance).GetMethod("ApplyExclusionMask", _lifecycleMethodFlags);
            MethodInfo restoreMask = typeof(PointLightVolumeInstance).GetMethod("RestoreExclusionMask", _lifecycleMethodFlags);
            Assert.That(applyMask, Is.Not.Null);
            Assert.That(restoreMask, Is.Not.Null);

            try {
                applyMask.Invoke(point, null);
                Assert.That(excludedRootRenderer.forceRenderingOff, Is.True);
                Assert.That(excludedChildRenderer.forceRenderingOff, Is.True);
                Assert.That(includedRenderer.forceRenderingOff, Is.False);
            } finally {
                restoreMask.Invoke(point, null);
            }

            Assert.That(excludedRootRenderer.forceRenderingOff, Is.True);
            Assert.That(excludedChildRenderer.forceRenderingOff, Is.False);
            Assert.That(includedRenderer.forceRenderingOff, Is.False);

            // Hierarchy changes under the same root are picked up by the next bake.
            GameObject addedChild = CreateGameObject("Added Shadow Exclusion Child", true);
            addedChild.transform.SetParent(excludedRoot.transform, false);
            MeshRenderer addedRenderer = addedChild.AddComponent<MeshRenderer>();
            try {
                applyMask.Invoke(point, null);
                Assert.That(excludedRootRenderer.forceRenderingOff, Is.True);
                Assert.That(excludedChildRenderer.forceRenderingOff, Is.True);
                Assert.That(addedRenderer.forceRenderingOff, Is.True);
                Assert.That(includedRenderer.forceRenderingOff, Is.False);
            } finally {
                restoreMask.Invoke(point, null);
            }

            Assert.That(excludedRootRenderer.forceRenderingOff, Is.True);
            Assert.That(excludedChildRenderer.forceRenderingOff, Is.False);
            Assert.That(addedRenderer.forceRenderingOff, Is.False);
        }

        // Verifies runtime shadow baking reports metadata changes so manager globals can refresh after the first bake.
        [Test]
        public void RuntimeShadowBakerDetectsRealtimeShadowMetadataChanges() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Metadata Manager", false);
            PointLightVolumeInstance point = CreatePointLight(manager, "Runtime Shadow Metadata Light", true);
            point.WorldSpaceShadows = true;
            RenderTexture source = CreateRenderTexture("Runtime Shadow Metadata Source", 4, 4, 1, TextureDimension.Cube);

            PointLightVolumeInstance baker = point;
            AddRuntimeShadowCamera(baker);
            point.FarClip = 12f;
            point.NearClip = 0.35f;
            point.Bias = 0.25f;

            FieldInfo shadowMapTextureField = typeof(PointLightVolumeInstance).GetField("_runtimeShadowTexture", _lifecycleMethodFlags);
            MethodInfo applyMethod = typeof(PointLightVolumeInstance).GetMethod("ApplyRuntimeShadowSourceInternal", _lifecycleMethodFlags);
            Assert.That(shadowMapTextureField, Is.Not.Null);
            Assert.That(applyMethod, Is.Not.Null);
            shadowMapTextureField.SetValue(baker, source);

            Vector3 bakePosition = new Vector3(1, 2, 3);
            Quaternion bakeRotation = Quaternion.Euler(0f, 45f, 0f);
            Assert.That((bool)applyMethod.Invoke(baker, new object[] { bakePosition, bakeRotation, false, false, true }), Is.True);
            Assert.That(point.FarClip, Is.EqualTo(12f).Within(Epsilon));
            Assert.That(point.NearClip, Is.EqualTo(0.35f).Within(Epsilon));
            Assert.That(point.Bias, Is.EqualTo(0.25f).Within(Epsilon));
            Assert.That(point.AutoUpdateShadowMap, Is.False);
            AssertVectorClose(new Vector4(bakePosition.x, bakePosition.y, bakePosition.z, 0), new Vector4(point.ShadowBakePosition.x, point.ShadowBakePosition.y, point.ShadowBakePosition.z, 0));
            Assert.That(Quaternion.Dot(point.ShadowBakeRotation, bakeRotation), Is.EqualTo(1f).Within(Epsilon));

            Assert.That((bool)applyMethod.Invoke(baker, new object[] { bakePosition, bakeRotation, false, false, true }), Is.False);

            // Unity's Vector3/Quaternion operators consider these distinct bake transforms equal.
            // Receiver metadata must still match the exact transform used to render every world-space rebake.
            Vector3 preciseBakePosition = new Vector3(bakePosition.x + 0.000001f, bakePosition.y, bakePosition.z);
            Assert.That((bool)applyMethod.Invoke(baker, new object[] { preciseBakePosition, bakeRotation, false, false, true }), Is.True);
            Assert.That(point.ShadowBakePosition.x, Is.EqualTo(preciseBakePosition.x));
            Assert.That(point.ShadowBakePosition.y, Is.EqualTo(preciseBakePosition.y));
            Assert.That(point.ShadowBakePosition.z, Is.EqualTo(preciseBakePosition.z));

            Quaternion preciseBakeRotation = Quaternion.Euler(0f, 45.01f, 0f);
            Assert.That((bool)applyMethod.Invoke(baker, new object[] { preciseBakePosition, preciseBakeRotation, false, false, true }), Is.True);
            Assert.That(point.ShadowBakeRotation.x, Is.EqualTo(preciseBakeRotation.x));
            Assert.That(point.ShadowBakeRotation.y, Is.EqualTo(preciseBakeRotation.y));
            Assert.That(point.ShadowBakeRotation.z, Is.EqualTo(preciseBakeRotation.z));
            Assert.That(point.ShadowBakeRotation.w, Is.EqualTo(preciseBakeRotation.w));
            Assert.That((bool)applyMethod.Invoke(baker, new object[] { preciseBakePosition, preciseBakeRotation, false, false, true }), Is.False);

            Assert.That((bool)applyMethod.Invoke(baker, new object[] { bakePosition + Vector3.right, bakeRotation, false, false, true }), Is.True);
        }

        // Verifies direct runtime baker output reserves a manager shadow slot without entering the auto shadow update cache.
        [Test]
        public void RuntimeShadowBakerDirectOutputDoesNotEnterAutoShadowUpdateCache() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Direct Auto Cache Manager", false);
            manager.ShadowTexturesWidth = 16;
            manager.ShadowTexturesHeight = 16;
            PointLightVolumeInstance point = CreatePointLight(manager, "Runtime Shadow Direct Auto Cache Light", true);
            point.AutoUpdateShadowMap = true;
            manager.PointLightVolumeInstances = new[] { point };

            PointLightVolumeInstance baker = point;
            baker.RuntimeShadowResolution = 16;
            AddRuntimeShadowCamera(baker);

            RenderTexture registrationTexture = CreateRenderTexture("Runtime Shadow Direct Registration", 1, 1, 6, TextureDimension.Tex2DArray);
            FieldInfo registrationTextureField = typeof(PointLightVolumeInstance).GetField("_runtimeShadowRegistrationTexture", _lifecycleMethodFlags);
            MethodInfo applyMethod = typeof(PointLightVolumeInstance).GetMethod("ApplyRuntimeShadowSourceInternal", _lifecycleMethodFlags);
            Assert.That(registrationTextureField, Is.Not.Null);
            Assert.That(applyMethod, Is.Not.Null);

            baker.RuntimeShadowResolution = 16;
            baker.RuntimeShadowBlurSamplePreset = 1;
            baker.RuntimeShadowSphericalBlur = false;
            baker.RuntimeShadowFacesPerFrame = 1;
            baker.RuntimeShadowDirectOutput = true;
            registrationTextureField.SetValue(baker, registrationTexture);

            Assert.That((bool)applyMethod.Invoke(baker, new object[] { Vector3.zero, Quaternion.identity, false, true, true }), Is.True);
            manager.ReinitializeShadowTextures();

            Assert.That(point.AutoUpdateShadowMap, Is.False);
            Assert.That(manager.HasAutoShadowTextureUpdates, Is.False);
        }

        // Verifies local-space shadows do not report metadata changes for a bake position that the shader does not read.
        [Test]
        public void RuntimeShadowBakerIgnoresLocalSpaceBakePositionMetadataChanges() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Local Metadata Manager", false);
            PointLightVolumeInstance point = CreatePointLight(manager, "Runtime Shadow Local Metadata Light", true);
            point.WorldSpaceShadows = false;
            RenderTexture source = CreateRenderTexture("Runtime Shadow Local Metadata Source", 4, 4, 6, TextureDimension.Tex2DArray);

            PointLightVolumeInstance baker = point;
            AddRuntimeShadowCamera(baker);

            FieldInfo shadowMapTextureField = typeof(PointLightVolumeInstance).GetField("_runtimeShadowTexture", _lifecycleMethodFlags);
            MethodInfo applyMethod = typeof(PointLightVolumeInstance).GetMethod("ApplyRuntimeShadowSourceInternal", _lifecycleMethodFlags);
            Assert.That(shadowMapTextureField, Is.Not.Null);
            Assert.That(applyMethod, Is.Not.Null);
            shadowMapTextureField.SetValue(baker, source);

            Vector3 bakePosition = new Vector3(1, 2, 3);
            Assert.That((bool)applyMethod.Invoke(baker, new object[] { bakePosition, Quaternion.identity, false, false, true }), Is.True);
            Assert.That((bool)applyMethod.Invoke(baker, new object[] { bakePosition + Vector3.right, Quaternion.identity, false, false, true }), Is.False);
        }

        // Verifies runtime shadow baking does not dirty manager metadata for local-space transform-only movement.
        [Test]
        public void RuntimeShadowBakerDoesNotDirtyManagerOnLocalSpaceMove() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Local Move Manager", false);
            PointLightVolumeInstance point = CreatePointLight(manager, "Runtime Shadow Local Move Light", true);
            point.WorldSpaceShadows = false;
            manager.PointLightVolumeInstances = new[] { point };
            manager.UpdateVolumes();

            PointLightVolumeInstance baker = point;
            baker.RuntimeShadowResolution = 16;
            baker.RuntimeShadowDepthEncodeMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowDepthEncode");
            AddRuntimeShadowCamera(baker);

            Assert.That(_pointLightArraysDirtyField, Is.Not.Null);

            baker.BakeShadows();
            point.IsRangeDirty = false;
            SetManagerField(manager, _pointLightArraysDirtyField, false);

            point.transform.position = new Vector3(3, 4, 5);
            point.transform.rotation = Quaternion.Euler(0, 45, 0);

            baker.BakeShadows();

            Assert.That(GetManagerField<bool>(manager, _pointLightArraysDirtyField), Is.False);
        }

        // Verifies runtime blur publishes the local shadow texture array used for final blurred output.
        [Test]
        public void RuntimeShadowBakerRegistersBlurredArrayWhenApplied() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Blur Metadata Manager", false);
            PointLightVolumeInstance point = CreatePointLight(manager, "Runtime Shadow Blur Metadata Light", true);
            RenderTexture shadowSource = CreateRenderTexture("Runtime Shadow Blur Source", 4, 4, 6, TextureDimension.Tex2DArray);

            PointLightVolumeInstance baker = point;
            point.Blur = 1;
            baker.RuntimeShadowBlurMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowRuntimeBlur");
            AddRuntimeShadowCamera(baker);

            FieldInfo shadowMapTextureField = typeof(PointLightVolumeInstance).GetField("_runtimeShadowTexture", _lifecycleMethodFlags);
            MethodInfo applyMethod = typeof(PointLightVolumeInstance).GetMethod("ApplyRuntimeShadowSourceInternal", _lifecycleMethodFlags);
            Assert.That(shadowMapTextureField, Is.Not.Null);
            Assert.That(applyMethod, Is.Not.Null);

            shadowMapTextureField.SetValue(baker, shadowSource);

            Assert.That((bool)applyMethod.Invoke(baker, new object[] { Vector3.zero, Quaternion.identity, false, false, true }), Is.True);
            Assert.That(point.ShadowMapTexture, Is.SameAs(shadowSource));
            Assert.That(point.ShadowMapTextureIsCubemap, Is.False);
            Assert.That(point.ShadowMapTextureHasDepthSlices, Is.True);
        }

        // Verifies per-frame animated shadow updates recover shadow IDs after setup metadata resets the count.
        [Test]
        public void AnimatedShadowUpdateRestoresRuntimeReservedShadowCount() {
            LightVolumeManager manager = CreateManager("Animated Shadow Count Reset Manager", false);
            RenderTexture source = CreateRenderTexture("Animated Shadow Count Reset Source", 8, 8, 6, TextureDimension.Tex2DArray);
            manager.ShadowTexturesWidth = 8;
            manager.ShadowTexturesHeight = 8;

            PointLightVolumeInstance point = CreatePointLight(manager, "Animated Shadow Count Reset Light", true);
            ConfigureShadowTexture(point, source, true, false, true);
            manager.PointLightVolumeInstances = new[] { point };
            manager.ShadowMapsCount = 0;

            manager.UpdateAutoShadowTextures();

            Assert.That(manager.ShadowMapsCount, Is.EqualTo(1));
            Assert.That(manager.ShadowTextures, Is.Not.Null);
            Assert.That(manager.ShadowTextures.volumeDepth, Is.EqualTo(6));
            Assert.That(manager.ShadowTextures.useMipMap, Is.False);
            Assert.That(manager.ShadowTextures.autoGenerateMips, Is.False);
        }

        // Verifies multiple unique shadow cubemaps reserve independent six-slice ranges.
        [Test]
        public void ShadowRuntimeArrayReservesSixSlicesPerUniqueSource() {
            LightVolumeManager manager = CreateManager("Shadow Unique Sources Manager", false);
            Cubemap firstSource = CreateCubemap("Shadow First Source");
            Cubemap secondSource = CreateCubemap("Shadow Second Source");
            manager.ShadowTexturesWidth = 4;
            manager.ShadowTexturesHeight = 4;

            PointLightVolumeInstance firstPoint = CreatePointLight(manager, "Shadow First Light", true);
            ConfigureShadowTexture(firstPoint, firstSource, false, true, false);
            PointLightVolumeInstance secondPoint = CreatePointLight(manager, "Shadow Second Light", true);
            ConfigureShadowTexture(secondPoint, secondSource, false, true, false);
            manager.PointLightVolumeInstances = new[] { firstPoint, secondPoint };

            manager.ReinitializeShadowTextures();
            Assert.DoesNotThrow(() => manager.UpdateVolumes());

            Assert.That(manager.ShadowTextures, Is.Not.Null);
            Assert.That(manager.ShadowTextures.volumeDepth, Is.EqualTo(12));
            Assert.That(manager.ShadowMapsCount, Is.EqualTo(2));
            AssertGlobalFloat(_pointLightShadowCountID, 2);
        }

        // Verifies duplicate shadow sources resolve to one manager-owned shadow ID and one reserved cubemap range.
        [Test]
        public void DuplicateShadowSourcesReuseOneShadowID() {
            LightVolumeManager manager = CreateManager("Duplicate Shadow IDs Manager", false);
            Cubemap source = CreateCubemap("Duplicate Shadow Source");
            manager.ShadowTexturesWidth = 4;
            manager.ShadowTexturesHeight = 4;

            PointLightVolumeInstance firstPoint = CreatePointLight(manager, "Duplicate Shadow A", true);
            ConfigureShadowTexture(firstPoint, source, false, true, false);
            PointLightVolumeInstance secondPoint = CreatePointLight(manager, "Duplicate Shadow B", true);
            ConfigureShadowTexture(secondPoint, source, false, true, false);
            manager.PointLightVolumeInstances = new[] { firstPoint, secondPoint };

            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();

            Assert.That(manager.ShadowMapsCount, Is.EqualTo(1));
            Assert.That(GetManagerField<int>(manager, _shadowTexturesDepthField), Is.EqualTo(6));
            Assert.That(firstPoint.ShadowMapID, Is.EqualTo(0).Within(Epsilon));
            Assert.That(secondPoint.ShadowMapID, Is.EqualTo(0).Within(Epsilon));
            AssertGlobalFloat(_pointLightShadowCountID, 1);
        }

        // Verifies single spotlight shadows are stored after the cubemap shadow prefix.
        [Test]
        public void SpotSingleShadowUsesSingleSliceAfterCubemapPrefix() {
            LightVolumeManager manager = CreateManager("Spot Single Shadow Layout Manager", false);
            Cubemap cubemapSource = CreateCubemap("Spot Single Layout Cubemap Source");
            Texture2D singleSource = CreateTexture2D("Spot Single Layout Texture Source");
            manager.ShadowTexturesWidth = 4;
            manager.ShadowTexturesHeight = 4;

            PointLightVolumeInstance point = CreatePointLight(manager, "Cubemap Shadow Point", true);
            point.NearClip = 0.25f;
            point.FarClip = 4f;
            ConfigureShadowTexture(point, cubemapSource, false, true, false);
            PointLightVolumeInstance spot = CreatePointLight(manager, "Single Shadow Spot", true);
            spot.SetSpotLight(60, 0.5f);
            spot.NearClip = 0.5f;
            spot.FarClip = 8f;
            ConfigureShadowTexture(spot, singleSource, false, false, false);
            spot.ShadowMapUsesCubemap = false;
            manager.PointLightVolumeInstances = new[] { point, spot };

            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();

            Assert.That(manager.ShadowTextures, Is.Not.Null);
            Assert.That(manager.ShadowTextures.volumeDepth, Is.EqualTo(7));
            Assert.That(manager.ShadowCubemapsCount, Is.EqualTo(1));
            Assert.That(manager.ShadowMapsCount, Is.EqualTo(2));
            Assert.That(GetManagerField<int>(manager, _shadowCubemapTextureCountField), Is.EqualTo(1));
            Assert.That(GetManagerField<int>(manager, _shadowSingleTextureCountField), Is.EqualTo(1));
            Assert.That(GetManagerField<int[]>(manager, _pointLightShadowIDsField), Is.EqualTo(new[] { 0, 1 }));
            AssertGlobalFloat(_pointLightShadowCubeCountID, 1);
            AssertGlobalFloat(_pointLightShadowCountID, 2);
            AssertPointCustomData(0, point, 0, -1);
            AssertPointCustomData(1, spot, 0, -2);
            Vector4[] customData = Shader.GetGlobalVectorArray(_pointLightCustomIdID);
            Assert.That(customData[0].w, Is.EqualTo(ExpectedCustomShadowInvDepthRange(point)).Within(Epsilon));
            Assert.That(customData[1].w, Is.EqualTo(ExpectedCustomShadowInvDepthRange(spot)).Within(Epsilon));
            Vector4[] reprojectionData = Shader.GetGlobalVectorArray(_pointLightShadowReprojectionDataID);
            AssertVectorClose(new Vector4(point.ShadowBakePosition.x, point.ShadowBakePosition.y, point.ShadowBakePosition.z, ExpectedCubemapShadowInvDepthRange(point)), reprojectionData[0]);
            AssertVectorClose(new Vector4(spot.ShadowBakePosition.x, spot.ShadowBakePosition.y, spot.ShadowBakePosition.z, spot.OuterAngleTan), reprojectionData[1]);
        }

        // Keeps registry priority stable across shadow backends; final slots drive incremental dynamic updates.
        [Test]
        public void PointLightShaderRecordsPreserveRegistryOrderAndDynamicIndices() {
            LightVolumeManager manager = CreateManager("Stable Point Registry Manager", false);
            manager.ShadowTexturesWidth = 4;
            manager.ShadowTexturesHeight = 4;

            PointLightVolumeInstance unshadowed = CreatePointLight(manager, "Registry Unshadowed Point", true);
            unshadowed.transform.position = new Vector3(30, 0, 0);

            PointLightVolumeInstance single = CreatePointLight(manager, "Registry Single Shadow Spot", true);
            single.SetSpotLight(60, 0.5f);
            single.transform.position = new Vector3(20, 0, 0);
            ConfigureShadowTexture(single, CreateTexture2D("Registry Single Shadow Source"), false, false, false);
            single.ShadowMapUsesCubemap = false;

            PointLightVolumeInstance cube = CreatePointLight(manager, "Registry Cubemap Shadow Point", true);
            cube.transform.position = new Vector3(10, 0, 0);
            ConfigureShadowTexture(cube, CreateCubemap("Registry Cubemap Shadow Source"), false, true, false);

            // Mixed shadow backends must not reorder the user-visible overdraw priority.
            manager.PointLightVolumeInstances = new[] { unshadowed, single, cube };
            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();

            AssertGlobalFloat(_pointLightCountID, 3);
            int[] enabledPointIDs = GetManagerField<int[]>(manager, _enabledPointIDsField);
            Assert.That(enabledPointIDs[0], Is.EqualTo(0));
            Assert.That(enabledPointIDs[1], Is.EqualTo(1));
            Assert.That(enabledPointIDs[2], Is.EqualTo(2));
            AssertPointCustomData(0, unshadowed, 0, 0);
            AssertPointCustomData(1, single, 0, -2);
            AssertPointCustomData(2, cube, 0, -1);
            Vector4[] positions = Shader.GetGlobalVectorArray(_pointLightPositionID);
            Assert.That(positions[0].x, Is.EqualTo(30).Within(Epsilon));
            Assert.That(positions[1].x, Is.EqualTo(20).Within(Epsilon));
            Assert.That(positions[2].x, Is.EqualTo(10).Within(Epsilon));

            cube.transform.position = new Vector3(11, 0, 0);
            single.transform.position = new Vector3(21, 0, 0);
            unshadowed.transform.position = new Vector3(31, 0, 0);
            Assert.That(_updateAutoUpdatedVolumeChangesMethod, Is.Not.Null);
            Assert.That(_uploadAutoUpdatedVolumeChangesMethod, Is.Not.Null);
            _updateAutoUpdatedVolumeChangesMethod.Invoke(manager, null);
            _uploadAutoUpdatedVolumeChangesMethod.Invoke(manager, null);

            positions = Shader.GetGlobalVectorArray(_pointLightPositionID);
            Assert.That(positions[0].x, Is.EqualTo(31).Within(Epsilon));
            Assert.That(positions[1].x, Is.EqualTo(21).Within(Epsilon));
            Assert.That(positions[2].x, Is.EqualTo(11).Within(Epsilon));

            // The per-pixel overdraw cap must not mutate upload order either.
            manager.AdditiveMaxOverdraw = 2;
            manager.UpdateVolumes();
            enabledPointIDs = GetManagerField<int[]>(manager, _enabledPointIDsField);
            Assert.That(enabledPointIDs[0], Is.EqualTo(0));
            Assert.That(enabledPointIDs[1], Is.EqualTo(1));
            Assert.That(enabledPointIDs[2], Is.EqualTo(2));
            positions = Shader.GetGlobalVectorArray(_pointLightPositionID);
            Assert.That(positions[0].x, Is.EqualTo(31).Within(Epsilon));
            Assert.That(positions[1].x, Is.EqualTo(21).Within(Epsilon));
            Assert.That(positions[2].x, Is.EqualTo(11).Within(Epsilon));
        }

        // Keeps the common unshadowed translation path to one shader-array upload.
        [Test]
        public void TranslationOnlyUnshadowedPointUploadsOnlyPositionBuffer() {
            LightVolumeManager manager = CreateManager("Position-Only Point Upload Manager", false);
            PointLightVolumeInstance point = CreatePointLight(manager, "Position-Only Point", true);
            manager.PointLightVolumeInstances = new[] { point };
            manager.UpdateVolumes();

            point.transform.position = new Vector3(4, 5, 6);
            Assert.That(_updateAutoUpdatedVolumeChangesMethod, Is.Not.Null);
            Assert.That(_uploadAutoUpdatedVolumeChangesMethod, Is.Not.Null);
            Assert.That(_updatePointLightBuffersField, Is.Not.Null);
            Assert.That(_updatePointLightPositionBufferField, Is.Not.Null);
            _updateAutoUpdatedVolumeChangesMethod.Invoke(manager, null);

            Assert.That(GetManagerField<bool>(manager, _updatePointLightBuffersField), Is.False);
            Assert.That(GetManagerField<bool>(manager, _updatePointLightPositionBufferField), Is.True);
            _uploadAutoUpdatedVolumeChangesMethod.Invoke(manager, null);

            Assert.That(GetManagerField<bool>(manager, _updatePointLightPositionBufferField), Is.False);
            Vector4[] positions = Shader.GetGlobalVectorArray(_pointLightPositionID);
            AssertVectorClose(ExpectedPointLightPosition(point), positions[0]);
        }

        // Verifies spot lights can still force a six-slice cubemap shadow layout.
        [Test]
        public void SpotForceCubemapShadowReservesSixSlices() {
            LightVolumeManager manager = CreateManager("Spot Force Cubemap Shadow Manager", false);
            Texture2D source = CreateTexture2D("Spot Force Cubemap Texture Source");
            manager.ShadowTexturesWidth = 4;
            manager.ShadowTexturesHeight = 4;

            PointLightVolumeInstance spot = CreatePointLight(manager, "Forced Cubemap Shadow Spot", true);
            spot.SetSpotLight(60, 0.5f);
            spot.NearClip = 0.5f;
            spot.FarClip = 8f;
            ConfigureShadowTexture(spot, source, false, false, false);
            spot.ShadowMapUsesCubemap = true;
            manager.PointLightVolumeInstances = new[] { spot };

            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();

            Assert.That(manager.ShadowTextures, Is.Not.Null);
            Assert.That(manager.ShadowTextures.volumeDepth, Is.EqualTo(6));
            Assert.That(manager.ShadowCubemapsCount, Is.EqualTo(1));
            Assert.That(manager.ShadowMapsCount, Is.EqualTo(1));
            Assert.That(GetManagerField<int>(manager, _shadowCubemapTextureCountField), Is.EqualTo(1));
            Assert.That(GetManagerField<int>(manager, _shadowSingleTextureCountField), Is.EqualTo(0));
            AssertGlobalFloat(_pointLightShadowCubeCountID, 1);
            AssertGlobalFloat(_pointLightShadowCountID, 1);
            AssertPointCustomData(spot, 0, -1);
            Assert.That(Shader.GetGlobalVectorArray(_pointLightShadowReprojectionDataID)[0].w,
                Is.EqualTo(ExpectedCubemapShadowInvDepthRange(spot)).Within(Epsilon));
        }

        // Verifies CustomID.W keeps the Spot reciprocal range while reprojection W preserves the backend payload.
        [Test]
        public void SpotShadowBackendSwitchKeepsInvDepthRangeInCustomData() {
            LightVolumeManager manager = CreateManager("Spot Shadow Backend Switch Manager", false);
            manager.ShadowTexturesWidth = 4;
            manager.ShadowTexturesHeight = 4;

            PointLightVolumeInstance spot = CreatePointLight(manager, "Switchable Shadow Spot", true);
            spot.SetSpotLight(60, 0.5f);
            spot.NearClip = 0.5f;
            spot.FarClip = 8f;
            ConfigureShadowTexture(spot, CreateTexture2D("Switchable Spot Shadow Source"), false, false, false);
            spot.ShadowMapUsesCubemap = false;
            manager.PointLightVolumeInstances = new[] { spot };

            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();

            Assert.That(manager.ShadowCubemapsCount, Is.EqualTo(0));
            AssertPointCustomData(spot, 0, -1);
            Assert.That(Shader.GetGlobalVectorArray(_pointLightCustomIdID)[0].w,
                Is.EqualTo(ExpectedCustomShadowInvDepthRange(spot)).Within(Epsilon));
            Assert.That(Shader.GetGlobalVectorArray(_pointLightShadowReprojectionDataID)[0].w,
                Is.EqualTo(spot.OuterAngleTan).Within(Epsilon));

            spot.ShadowMapUsesCubemap = true;
            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();

            Assert.That(manager.ShadowCubemapsCount, Is.EqualTo(1));
            AssertPointCustomData(spot, 0, -1);
            Assert.That(Shader.GetGlobalVectorArray(_pointLightCustomIdID)[0].w,
                Is.EqualTo(ExpectedCustomShadowInvDepthRange(spot)).Within(Epsilon));
            Assert.That(Shader.GetGlobalVectorArray(_pointLightShadowReprojectionDataID)[0].w,
                Is.EqualTo(ExpectedCubemapShadowInvDepthRange(spot)).Within(Epsilon));

            spot.ShadowMapUsesCubemap = false;
            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();

            Assert.That(manager.ShadowCubemapsCount, Is.EqualTo(0));
            AssertPointCustomData(spot, 0, -1);
            Assert.That(Shader.GetGlobalVectorArray(_pointLightCustomIdID)[0].w,
                Is.EqualTo(ExpectedCustomShadowInvDepthRange(spot)).Within(Epsilon));
            Assert.That(Shader.GetGlobalVectorArray(_pointLightShadowReprojectionDataID)[0].w,
                Is.EqualTo(spot.OuterAngleTan).Within(Epsilon));
        }

        // The v3 fast-path marker must require exact component equality and follow incremental dynamic moves.
        [Test]
        public void WorldShadowSameOriginMarkerTracksExactDynamicPosition() {
            LightVolumeManager manager = CreateManager("Exact Same Origin Shadow Manager", false);
            manager.ShadowTexturesWidth = 4;
            manager.ShadowTexturesHeight = 4;

            PointLightVolumeInstance point = CreatePointLight(manager, "Exact Same Origin Shadow Point", true);
            Vector3 bakePosition = new Vector3(2, 3, 4);
            point.transform.position = bakePosition;
            point.WorldSpaceShadows = true;
            point.ShadowBakePosition = bakePosition;
            point.ShadowBakeRotation = Quaternion.Euler(17, 29, 43); // Rotation may differ; the receiver keeps baked rotation.
            ConfigureShadowTexture(point, CreateCubemap("Exact Same Origin Shadow Source"), false, true, false);
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();

            Vector4 customData = Shader.GetGlobalVectorArray(_pointLightCustomIdID)[0];
            Assert.That(customData.y, Is.EqualTo(1f).Within(Epsilon)); // World-space shadow ID semantics stay positive.
            Assert.That(customData.w, Is.EqualTo(-ExpectedShadowInvDepthRange(point)).Within(Epsilon));
            Quaternion expectedBakeRotation = Quaternion.Inverse(point.ShadowBakeRotation);
            AssertVectorClose(new Vector4(expectedBakeRotation.x, expectedBakeRotation.y, expectedBakeRotation.z, expectedBakeRotation.w),
                Shader.GetGlobalVectorArray(_pointLightShadowRotationDataID)[0]);

            // Moving only the live light makes the origins distinct; the incremental writer must clear the marker.
            point.transform.position = new Vector3(2.25f, 3, 4);
            Assert.That(_updateAutoUpdatedVolumeChangesMethod, Is.Not.Null);
            Assert.That(_uploadAutoUpdatedVolumeChangesMethod, Is.Not.Null);
            _updateAutoUpdatedVolumeChangesMethod.Invoke(manager, null);
            _uploadAutoUpdatedVolumeChangesMethod.Invoke(manager, null);
            customData = Shader.GetGlobalVectorArray(_pointLightCustomIdID)[0];
            Assert.That(customData.y, Is.EqualTo(1f).Within(Epsilon));
            Assert.That(customData.w, Is.EqualTo(ExpectedShadowInvDepthRange(point)).Within(Epsilon));

            // Publishing a bake at the new exact origin restores the marker.
            point.ShadowBakePosition = point.transform.position;
            manager.UpdateVolumes();
            customData = Shader.GetGlobalVectorArray(_pointLightCustomIdID)[0];
            Assert.That(customData.w, Is.EqualTo(-ExpectedShadowInvDepthRange(point)).Within(Epsilon));

            // Even a sub-Epsilon but representable component difference must not take the exact path.
            point.ShadowBakePosition = new Vector3(point.Position.x + 0.000001f, point.Position.y, point.Position.z);
            manager.UpdateVolumes();
            customData = Shader.GetGlobalVectorArray(_pointLightCustomIdID)[0];
            Assert.That(customData.w, Is.EqualTo(ExpectedShadowInvDepthRange(point)).Within(Epsilon));
        }

        // Spot single/cubemap backends share the sign marker while retaining their reprojection W payloads.
        [Test]
        public void SpotSameOriginMarkerPreservesBackendReprojectionPayloads() {
            LightVolumeManager manager = CreateManager("Same Origin Spot Backend Manager", false);
            manager.ShadowTexturesWidth = 4;
            manager.ShadowTexturesHeight = 4;

            PointLightVolumeInstance spot = CreatePointLight(manager, "Same Origin Shadow Spot", true);
            spot.SetSpotLight(60, 0.5f);
            spot.transform.position = new Vector3(6, 7, 8);
            spot.WorldSpaceShadows = true;
            spot.ShadowBakePosition = spot.transform.position;
            spot.ShadowBakeRotation = Quaternion.Euler(11, 23, 37);
            spot.NearClip = 0.5f;
            spot.FarClip = 8f;
            ConfigureShadowTexture(spot, CreateTexture2D("Same Origin Spot Shadow Source"), false, false, false);
            spot.ShadowMapUsesCubemap = false;
            manager.PointLightVolumeInstances = new[] { spot };

            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();

            Vector4 customData = Shader.GetGlobalVectorArray(_pointLightCustomIdID)[0];
            Vector4 extraData = Shader.GetGlobalVectorArray(_pointLightExtraDataID)[0];
            Vector4 reprojectionData = Shader.GetGlobalVectorArray(_pointLightShadowReprojectionDataID)[0];
            Assert.That(customData.y, Is.EqualTo(1f).Within(Epsilon));
            Assert.That(customData.w, Is.EqualTo(-ExpectedShadowInvDepthRange(spot)).Within(Epsilon));
            Assert.That(extraData.y, Is.EqualTo(spot.OuterAngleTan).Within(Epsilon)); // Packed tangent used by the no-fetch arm.
            Assert.That(reprojectionData.w, Is.EqualTo(spot.OuterAngleTan).Within(Epsilon)); // Manager/shader ABI remains populated.

            spot.ShadowMapUsesCubemap = true;
            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();

            customData = Shader.GetGlobalVectorArray(_pointLightCustomIdID)[0];
            reprojectionData = Shader.GetGlobalVectorArray(_pointLightShadowReprojectionDataID)[0];
            Assert.That(customData.y, Is.EqualTo(1f).Within(Epsilon));
            Assert.That(customData.w, Is.EqualTo(-ExpectedShadowInvDepthRange(spot)).Within(Epsilon));
            Assert.That(reprojectionData.w, Is.EqualTo(ExpectedCubemapShadowInvDepthRange(spot)).Within(Epsilon));

            spot.ShadowBakePosition = new Vector3(spot.Position.x, spot.Position.y, spot.Position.z + 0.000001f);
            manager.UpdateVolumes();
            Assert.That(Shader.GetGlobalVectorArray(_pointLightCustomIdID)[0].w,
                Is.EqualTo(ExpectedShadowInvDepthRange(spot)).Within(Epsilon));
        }

        // Verifies malformed low-level Point/Area metadata cannot enter the single-slice shadow block used only by spots.
        [Test]
        public void PointAndAreaShadowMetadataIsCanonicalizedToCubemaps() {
            LightVolumeManager manager = CreateManager("Non Spot Shadow Canonicalization Manager", false);
            manager.ShadowTexturesWidth = 4;
            manager.ShadowTexturesHeight = 4;

            PointLightVolumeInstance point = CreatePointLight(manager, "Malformed Point Shadow", true);
            ConfigureShadowTexture(point, CreateTexture2D("Malformed Point Shadow Source"), false, false, false);
            point.ShadowMapUsesCubemap = false;

            PointLightVolumeInstance area = CreatePointLight(manager, "Malformed Area Shadow", true);
            area.SetCustomTexture();
            area.SetAreaLight();
            area.CustomTexture = CreateTexture2D("Area Shadow Cookie");
            area.ProjectionType = 1; // 1: texture
            area.transform.localScale = new Vector3(-2, 3, 1);
            area.UpdateScale();
            ConfigureShadowTexture(area, CreateTexture2D("Malformed Area Shadow Source"), false, false, false);
            area.ShadowMapUsesCubemap = false;
            manager.PointLightVolumeInstances = new[] { point, area };

            manager.ReinitializeCustomTextures();
            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();

            Assert.That(point.ShadowMapUsesCubemap, Is.True);
            Assert.That(area.ShadowMapUsesCubemap, Is.True);
            Assert.That(manager.ShadowCubemapsCount, Is.EqualTo(2));
            Assert.That(manager.ShadowMapsCount, Is.EqualTo(2));
            Assert.That(manager.ShadowTextures, Is.Not.Null);
            Assert.That(manager.ShadowTextures.volumeDepth, Is.EqualTo(12));
            Assert.That(GetManagerField<int>(manager, _shadowSingleTextureCountField), Is.EqualTo(0));
            Assert.That(GetManagerField<int[]>(manager, _pointLightShadowIDsField), Is.EqualTo(new[] { 0, 1 }));
            Vector4 areaCustomData = Shader.GetGlobalVectorArray(_pointLightCustomIdID)[1];
            Assert.That(areaCustomData.x, Is.LessThan(0));
            Assert.That(areaCustomData.w, Is.EqualTo(-1f).Within(Epsilon)); // X mirror tag replaces unused Area far clip.
            Assert.That(Shader.GetGlobalVectorArray(_pointLightShadowReprojectionDataID)[1].w,
                Is.EqualTo(ExpectedCubemapShadowInvDepthRange(area)).Within(Epsilon));
        }

        // Verifies a runtime Spot(single) -> Point transition rebuilds the atlas before the typed cubemap shader path is used.
        [Test]
        public void SpotSingleShadowSwitchingToPointRebuildsAsCubemap() {
            LightVolumeManager manager = CreateManager("Spot To Point Shadow Transition Manager", false);
            manager.ShadowTexturesWidth = 4;
            manager.ShadowTexturesHeight = 4;

            PointLightVolumeInstance light = CreatePointLight(manager, "Spot To Point Shadow", true);
            light.SetSpotLight(60, 0.5f);
            light.NearClip = 0.5f;
            light.FarClip = 8f;
            ConfigureShadowTexture(light, CreateTexture2D("Spot To Point Shadow Source"), false, false, false);
            light.ShadowMapUsesCubemap = false;
            manager.PointLightVolumeInstances = new[] { light };

            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();
            Assert.That(manager.ShadowCubemapsCount, Is.EqualTo(0));
            Assert.That(manager.ShadowTextures.volumeDepth, Is.EqualTo(1));
            Assert.That(Shader.GetGlobalVectorArray(_pointLightCustomIdID)[0].w,
                Is.EqualTo(ExpectedCustomShadowInvDepthRange(light)).Within(Epsilon));

            light.SetPointLight();
            manager.UpdateVolumes();

            Assert.That(light.ShadowMapUsesCubemap, Is.True);
            Assert.That(manager.ShadowCubemapsCount, Is.EqualTo(1));
            Assert.That(manager.ShadowMapsCount, Is.EqualTo(1));
            Assert.That(manager.ShadowTextures, Is.Not.Null);
            Assert.That(manager.ShadowTextures.volumeDepth, Is.EqualTo(6));
            Assert.That(GetManagerField<int>(manager, _shadowSingleTextureCountField), Is.EqualTo(0));
            AssertPointCustomData(light, 0, -1);
            Assert.That(Shader.GetGlobalVectorArray(_pointLightShadowReprojectionDataID)[0].w,
                Is.EqualTo(ExpectedCubemapShadowInvDepthRange(light)).Within(Epsilon));
        }

        // Verifies enabling an already-registered shadowed light invalidates stale empty shadow caches.
        [Test]
        public void ReenabledRegisteredShadowLightRebuildsShadowTextures() {
            LightVolumeManager manager = CreateManager("Reenabled Shadow Manager", false);
            manager.ShadowTexturesWidth = 4;
            manager.ShadowTexturesHeight = 4;

            PointLightVolumeInstance point = CreatePointLight(manager, "Reenabled Shadow Light", true);
            ConfigureShadowTexture(point, CreateCubemap("Reenabled Shadow Source"), false, true, false);
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();

            Assert.That(manager.ShadowTextures, Is.Not.Null);
            AssertGlobalFloat(_pointLightShadowCountID, 1);
            AssertPointCustomData(point, 0, -1);

            point.gameObject.SetActive(false);
            point.IsActive = false;
            manager.PointLightVolumeInstances = new[] { point };
            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();

            Assert.That(manager.ShadowTextures, Is.Null);
            AssertGlobalFloat(_pointLightShadowCountID, 0);

            point.gameObject.SetActive(true);
            point.IsActive = true;
            manager.InitializePointLightVolume(point);
            manager.UpdateVolumes();

            Assert.That(manager.ShadowTextures, Is.Not.Null);
            Assert.That(manager.ShadowTextures.volumeDepth, Is.EqualTo(6));
            AssertGlobalFloat(_pointLightShadowCountID, 1);
            AssertPointCustomData(point, 0, -1);
            Assert.That(GetManagerField<int[]>(manager, _pointLightShadowIDsField)[0], Is.EqualTo(0));
        }

        // Verifies material-only cubemap updates receive Light Volumes per-face target info.
        [Test]
        public void AnimatedPointCubemapMaterialReceivesPerFaceBlitInfo() {
            LightVolumeManager manager = CreateManager("Animated Point Cubemap Material Manager", false);
            manager.CustomTextures = CreateRenderTexture("Animated Point Cubemap Material Runtime", 16, 8, 12, TextureDimension.Tex2DArray);
            SetManagerField(manager, _customTexturesDepthField, 12);
            Material material = CreateMaterial("Hidden/CubeFace");
            MethodInfo method = typeof(LightVolumeManager).GetMethod("BlitMaterialSlice", _lifecycleMethodFlags);
            Assert.That(method, Is.Not.Null);

            method.Invoke(manager, new object[] { material, 4, 10, true, manager.CustomTextures });

            AssertVectorClose(new Vector4(16, 8, 1, 4), material.GetVector(CustomRenderTextureInfoProperty));
        }

        // Verifies manager-owned custom IDs stay compatible with shader slice formulas when cubemap inputs are deduplicated.
        [Test]
        public void RuntimeCustomTextureIdsStayCompatibleAfterDuplicateCubemaps() {
            LightVolumeManager manager = CreateManager("Duplicate Cookie IDs Manager", false);
            Cubemap cubemap = CreateCubemap("Duplicate Cubemap Cookie");
            Texture2D cookieA = CreateTexture2D("Cookie A");
            Texture2D cookieB = CreateTexture2D("Cookie B");
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;

            PointLightVolumeInstance cubemapPointA = CreatePointLight(manager, "Duplicate Cubemap Point A", true);
            cubemapPointA.SetPointLight();
            cubemapPointA.SetCustomTexture();
            cubemapPointA.CustomTexture = cubemap;
            cubemapPointA.CustomTextureIsCubemap = true;
            cubemapPointA.ProjectionType = 1; // 1: texture

            PointLightVolumeInstance cubemapPointB = CreatePointLight(manager, "Duplicate Cubemap Point B", true);
            cubemapPointB.SetPointLight();
            cubemapPointB.SetCustomTexture();
            cubemapPointB.CustomTexture = cubemap;
            cubemapPointB.CustomTextureIsCubemap = true;
            cubemapPointB.ProjectionType = 1; // 1: texture

            PointLightVolumeInstance cookieSpotA = CreatePointLight(manager, "Duplicate Cookie Spot A", true);
            cookieSpotA.SetCustomTexture();
            cookieSpotA.SetSpotLight(60, 0.5f);
            cookieSpotA.CustomTexture = cookieA;
            cookieSpotA.ProjectionType = 1; // 1: texture

            PointLightVolumeInstance cookieSpotB = CreatePointLight(manager, "Duplicate Cookie Spot B", true);
            cookieSpotB.SetCustomTexture();
            cookieSpotB.SetSpotLight(60, 0.5f);
            cookieSpotB.CustomTexture = cookieB;
            cookieSpotB.ProjectionType = 1; // 1: texture

            PointLightVolumeInstance cookieSpotADuplicate = CreatePointLight(manager, "Duplicate Cookie Spot A Duplicate", true);
            cookieSpotADuplicate.SetCustomTexture();
            cookieSpotADuplicate.SetSpotLight(60, 0.5f);
            cookieSpotADuplicate.CustomTexture = cookieA;
            cookieSpotADuplicate.ProjectionType = 1; // 1: texture

            manager.PointLightVolumeInstances = new[] { cubemapPointA, cubemapPointB, cookieSpotA, cookieSpotB, cookieSpotADuplicate };
            manager.ReinitializeCustomTextures();
            manager.UpdateVolumes();

            Assert.That(manager.CustomTextures, Is.Not.Null);
            Assert.That(manager.CustomTextures.volumeDepth, Is.EqualTo(8));
            Assert.That(manager.CubemapsCount, Is.EqualTo(1));
            Assert.That(GetManagerField<int>(manager, _customCubemapTextureCountField), Is.EqualTo(1));
            Assert.That(GetManagerField<int>(manager, _customSingleTextureCountField), Is.EqualTo(2));
            Assert.That(GetManagerField<int[]>(manager, _pointLightCustomIDsField), Is.EqualTo(new[] { 0, 0, 1, 2, 1 }));
            AssertPointCustomData(0, cubemapPointA, -1, 0);
            AssertPointCustomData(1, cubemapPointB, -1, 0);
            AssertPointCustomData(2, cookieSpotA, -2, 0);
            AssertPointCustomData(3, cookieSpotB, -3, 0);
            AssertPointCustomData(4, cookieSpotADuplicate, -2, 0);
        }

        // Verifies point LUTs use the 2.x-compatible positive ID convention without overlapping later cookie slices.
        [Test]
        public void PointLutSourceUsesV2IdBeforeLaterCookies() {
            LightVolumeManager manager = CreateManager("Point LUT V2 Compatibility Manager", false);
            Texture2D lut = CreateTexture2D("Shared Point LUT");
            Material areaCookie = CreateMaterial("Hidden/CubeFace");
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;

            PointLightVolumeInstance area = CreatePointLight(manager, "Area Material Cookie", true);
            area.transform.localScale = new Vector3(2, 3, 1);
            area.SetCustomTexture();
            area.SetAreaLight();
            area.CustomTextureMaterial = areaCookie;
            area.ProjectionType = 2; // 2: material
            area.AutoUpdateCustomTexture = true;

            PointLightVolumeInstance spot = CreatePointLight(manager, "Spot LUT", true);
            spot.SetLut();
            spot.SetSpotLight(60, 0.5f);
            spot.CustomTexture = lut;
            spot.ProjectionType = 1; // 1: texture

            PointLightVolumeInstance point = CreatePointLight(manager, "Point LUT", true);
            point.SetLut();
            point.CustomTexture = lut;
            point.ProjectionType = 1; // 1: texture

            manager.PointLightVolumeInstances = new[] { area, spot, point };
            manager.ReinitializeCustomTextures();
            manager.UpdateVolumes();

            Assert.That(manager.CustomTextures, Is.Not.Null);
            Assert.That(manager.CustomTextures.volumeDepth, Is.EqualTo(3));
            Assert.That(GetManagerField<int>(manager, _customSingleTextureCountField), Is.EqualTo(1));
            Assert.That(GetManagerField<int>(manager, _customSingleMaterialCountField), Is.EqualTo(1));
            Assert.That(GetManagerField<int[]>(manager, _pointLightCustomIDsField), Is.EqualTo(new[] { 2, 1, 1 }));
            AssertPointCustomData(0, area, -3, 0);
            AssertPointCustomData(1, spot, 2, 0);
            AssertPointCustomData(2, point, 1, 0);
        }

        // Verifies Area Cookie mirror flags live only in the cookie descriptor while size and rotation stay canonical.
        [Test]
        public void AreaCookieNegativeScaleKeepsCanonicalDataPositiveAndPacksCookieFlags() {
            LightVolumeManager manager = CreateManager("Area Cookie Mirror Manager", false);
            Texture2D cookie = CreateTexture2D("Mirrored Area Cookie");
            Vector3[] scales = {
                new Vector3(2, 3, 1),
                new Vector3(-2, 3, 1),
                new Vector3(2, -3, 1),
                new Vector3(-2, -3, -1)
            };
            PointLightVolumeInstance[] areas = new PointLightVolumeInstance[scales.Length];

            for (int i = 0; i < areas.Length; i++) {
                PointLightVolumeInstance area = CreatePointLight(manager, "Mirrored Area " + i, true);
                area.transform.rotation = Quaternion.Euler(17, 31, 43);
                area.transform.localScale = scales[i];
                area.SetCustomTexture();
                area.SetAreaLight();
                area.CustomTexture = cookie;
                area.ProjectionType = 1; // 1: texture
                area.UpdateScale();
                areas[i] = area;
            }

            manager.PointLightVolumeInstances = areas;
            manager.ReinitializeCustomTextures();
            manager.UpdateVolumes();

            Assert.That(GetManagerField<int[]>(manager, _pointLightCustomIDsField), Is.EqualTo(new[] { 0, 0, 0, 0 }));
            Vector4[] directions = Shader.GetGlobalVectorArray(_pointLightDirectionID);
            Vector4[] colors = Shader.GetGlobalVectorArray(_pointLightColorID);
            float expectedSquaredScale = 4f; // Average abs scale is 2 for every sign combination.
            float expectedRange = areas[0].SquaredRange;
            for (int i = 0; i < areas.Length; i++) {
                AssertPointCustomData(i, areas[i], -1f, 0);
                Assert.That(areas[i].Width, Is.EqualTo(2).Within(Epsilon));
                Assert.That(areas[i].Height, Is.EqualTo(3).Within(Epsilon));
                Assert.That(areas[i].SquaredScale, Is.EqualTo(expectedSquaredScale).Within(Epsilon));
                Assert.That(areas[i].SquaredRange, Is.EqualTo(expectedRange).Within(Epsilon));
                Vector4 encoded = directions[i];
                Assert.That(colors[i].w, Is.EqualTo(5f).Within(Epsilon));
                Quaternion expectedRotation = areas[i].transform.rotation;
                Quaternion encodedRotation = new Quaternion(encoded.x, encoded.y, encoded.z, encoded.w);
                Assert.That(Mathf.Abs(Quaternion.Dot(encodedRotation, expectedRotation)), Is.EqualTo(1f).Within(Epsilon));
            }
        }

        // Verifies Area Cookie mirrors also follow aligned negative scale inherited from a parent transform.
        [Test]
        public void AreaCookieParentNegativeScalePacksCookieFlags() {
            LightVolumeManager manager = CreateManager("Parent Area Cookie Mirror Manager", false);
            Texture2D cookie = CreateTexture2D("Parent Mirrored Area Cookie");
            Vector3[] parentScales = {
                new Vector3(-1, 1, 1),
                new Vector3(1, -1, 1),
                new Vector3(-1, -1, 1)
            };
            float[] expectedMirrorTags = { -1f, 2f, -2f };
            PointLightVolumeInstance[] areas = new PointLightVolumeInstance[parentScales.Length];

            for (int i = 0; i < areas.Length; i++) {
                GameObject parent = CreateGameObject("Mirrored Area Parent " + i, true);
                parent.transform.rotation = Quaternion.Euler(13, 29, 41);
                parent.transform.localScale = parentScales[i];
                PointLightVolumeInstance area = CreatePointLight(manager, "Parent Mirrored Area " + i, true);
                area.transform.SetParent(parent.transform, false);
                area.transform.localRotation = Quaternion.identity;
                area.transform.localScale = new Vector3(2, 3, 1);
                area.SetCustomTexture();
                area.SetAreaLight();
                area.CustomTexture = cookie;
                area.ProjectionType = 1; // 1: texture
                areas[i] = area;
            }

            manager.PointLightVolumeInstances = areas;
            manager.ReinitializeCustomTextures();
            manager.UpdateVolumes();

            Vector4[] customData = Shader.GetGlobalVectorArray(_pointLightCustomIdID);
            for (int i = 0; i < areas.Length; i++) {
                Assert.That(customData[i].w, Is.EqualTo(expectedMirrorTags[i]).Within(Epsilon));
                Assert.That(areas[i].Width, Is.EqualTo(2f).Within(Epsilon));
                Assert.That(areas[i].Height, Is.EqualTo(3f).Within(Epsilon));
            }
        }

        // Verifies the explicit transform refresh API updates Area Cookie mirrors without a dynamic-manager pass.
        [Test]
        public void AreaCookieManualUpdateScaleRefreshesParentAndLocalMirrorTag() {
            GameObject parent = CreateGameObject("Manual Area Cookie Mirror Parent", true);
            parent.transform.rotation = Quaternion.Euler(13, 29, 41);
            parent.transform.localScale = new Vector3(-1, 1, 1);
            PointLightVolumeInstance area = CreatePointLight(null, "Manual Area Cookie Mirror", true);
            area.IsDynamic = false;
            area.transform.SetParent(parent.transform, false);
            area.transform.localRotation = Quaternion.identity;
            area.transform.localScale = new Vector3(2, 3, 1);
            area.SetCustomTexture();
            area.SetAreaLight();

            Assert.That(area.AreaCookieMirror, Is.EqualTo(-1f).Within(Epsilon));

            parent.transform.localScale = new Vector3(1, -1, 1);
            area.UpdateScale();
            Assert.That(area.AreaCookieMirror, Is.EqualTo(2f).Within(Epsilon));

            area.transform.localScale = new Vector3(-2, 3, 1);
            area.UpdateScale();
            Assert.That(area.AreaCookieMirror, Is.EqualTo(-2f).Within(Epsilon));
        }

        // Verifies inactive point lights do not keep custom projection sources allocated.
        [Test]
        public void InactiveCustomTextureUsersReleaseRuntimeArrayUntilReactivated() {
            LightVolumeManager manager = CreateManager("Inactive Cookie Users Manager", false);
            Cubemap cubemap = CreateCubemap("Inactive Cookie Cubemap");
            PointLightVolumeInstance firstPoint = ConfigurePointCubemapSource(CreatePointLight(manager, "Inactive Cookie Point A", true), cubemap, true);
            PointLightVolumeInstance secondPoint = ConfigurePointCubemapSource(CreatePointLight(manager, "Inactive Cookie Point B", true), cubemap, true);
            manager.PointLightVolumeInstances = new[] { firstPoint, secondPoint };

            manager.UpdateVolumes();
            Assert.That(GetManagerField<int[]>(manager, _pointLightCustomIDsField), Is.EqualTo(new[] { 0, 0 }));

            firstPoint.Intensity = 0;
            manager.UpdateVolumes();
            Assert.That(manager.CustomTextures, Is.Not.Null);
            Assert.That(GetManagerField<int[]>(manager, _pointLightCustomIDsField), Is.EqualTo(new[] { -1, 0 }));

            secondPoint.Intensity = 0;
            manager.UpdateVolumes();
            Assert.That(manager.CustomTextures, Is.Null);
            Assert.That(manager.CubemapsCount, Is.EqualTo(0));

            firstPoint.Intensity = 1;
            manager.UpdateVolumes();
            Assert.That(manager.CustomTextures, Is.Not.Null);
            Assert.That(GetManagerField<int[]>(manager, _pointLightCustomIDsField), Is.EqualTo(new[] { 0, -1 }));
        }

        // Verifies inactive point lights do not keep shadow sources allocated.
        [Test]
        public void InactiveShadowUsersReleaseRuntimeArrayUntilReactivated() {
            LightVolumeManager manager = CreateManager("Inactive Shadow Users Manager", false);
            Cubemap cubemap = CreateCubemap("Inactive Shadow Cubemap");
            PointLightVolumeInstance firstPoint = CreatePointLight(manager, "Inactive Shadow Point A", true);
            ConfigureShadowTexture(firstPoint, cubemap, true, true, false);
            PointLightVolumeInstance secondPoint = CreatePointLight(manager, "Inactive Shadow Point B", true);
            ConfigureShadowTexture(secondPoint, cubemap, true, true, false);
            manager.PointLightVolumeInstances = new[] { firstPoint, secondPoint };

            manager.UpdateVolumes();
            Assert.That(GetManagerField<int[]>(manager, _pointLightShadowIDsField), Is.EqualTo(new[] { 0, 0 }));
            AssertGlobalFloat(_pointLightCountID, 2);
            AssertGlobalFloat(_pointLightShadowCountID, 1);

            firstPoint.ShadingStrength = 0;
            manager.UpdateVolumes();
            AssertGlobalFloat(_pointLightCountID, 2);
            AssertGlobalFloat(_pointLightShadowCountID, 1);

            firstPoint.ShadingStrength = 1;
            manager.UpdateVolumes();

            firstPoint.Intensity = 0;
            manager.UpdateVolumes();
            Assert.That(manager.ShadowTextures, Is.Not.Null);
            Assert.That(GetManagerField<int[]>(manager, _pointLightShadowIDsField), Is.EqualTo(new[] { -1, 0 }));

            secondPoint.Intensity = 0;
            manager.UpdateVolumes();
            Assert.That(manager.ShadowTextures, Is.Null);
            Assert.That(manager.ShadowMapsCount, Is.EqualTo(0));

            firstPoint.Intensity = 1;
            manager.UpdateVolumes();
            Assert.That(manager.ShadowTextures, Is.Not.Null);
            Assert.That(GetManagerField<int[]>(manager, _pointLightShadowIDsField), Is.EqualTo(new[] { 0, -1 }));
        }

        // Verifies all active point lights can write Shadow data together.
        [Test]
        public void AllPointLightsWithShadowWriteShadowGlobals() {
            LightVolumeManager manager = CreateManager("All Shadow Manager", false);
            const int pointCount = 6;
            manager.ShadowTexturesWidth = 4;
            manager.ShadowTexturesHeight = 4;
            PointLightVolumeInstance[] points = new PointLightVolumeInstance[pointCount];

            for (int i = 0; i < pointCount; i++) {
                PointLightVolumeInstance point = CreatePointLight(manager, "Shadow Point " + i, true);
                ConfigureShadowTexture(point, CreateCubemap("Shadow Source " + i), false, true, false);
                point.Bias = i == 0 ? 0 : 0.01f * (i + 1);
                point.WorldSpaceShadows = i % 2 == 0;
                point.ShadowBakePosition = new Vector3(i + 3, i + 4, i + 5);
                point.ShadowBakeRotation = Quaternion.Euler(i * 3, i * 5, i * 7);
                point.transform.rotation = Quaternion.Euler(i * 11, i * 13, i * 17);
                points[i] = point;
            }
            manager.PointLightVolumeInstances = points;

            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();

            Vector4[] reprojectionData = Shader.GetGlobalVectorArray(_pointLightShadowReprojectionDataID);
            Vector4[] rotationData = Shader.GetGlobalVectorArray(_pointLightShadowRotationDataID);
            AssertGlobalFloat(_lightVolumeEnabledID, 1);
            AssertGlobalFloat(_pointLightCountID, pointCount);
            AssertGlobalFloat(_pointLightShadowCountID, pointCount);
            for (int i = 0; i < pointCount; i++) {
                float expectedShadowState = points[i].WorldSpaceShadows ? i + 1 : -i - 1;
                AssertPointCustomData(i, points[i], 0, expectedShadowState);
                AssertVectorClose(new Vector4(points[i].ShadowBakePosition.x, points[i].ShadowBakePosition.y, points[i].ShadowBakePosition.z, ExpectedCubemapShadowInvDepthRange(points[i])), reprojectionData[i]);
                if (points[i].WorldSpaceShadows) {
                    Quaternion expectedRotation = Quaternion.Inverse(points[i].ShadowBakeRotation);
                    AssertVectorClose(new Vector4(expectedRotation.x, expectedRotation.y, expectedRotation.z, expectedRotation.w), rotationData[i]);
                } else {
                    Quaternion expectedRotation = Quaternion.Inverse(points[i].transform.rotation);
                    AssertVectorClose(new Vector4(expectedRotation.x, expectedRotation.y, expectedRotation.z, expectedRotation.w), rotationData[i]);
                }
            }
        }

        // Ensures shader array caps are enforced for oversized runtime registries.
        [Test]
        public void ShaderCountsClampToSupportedUdonArraySizes() {
            LightVolumeManager manager = CreateManager("Caps Manager", true);
            LightVolumeInstance[] volumes = new LightVolumeInstance[35];
            PointLightVolumeInstance[] points = new PointLightVolumeInstance[130];

            for (int i = 0; i < volumes.Length; i++) {
                LightVolumeInstance volume = CreateLightVolume(manager, "Clamped Volume " + i, true);
                ConfigureLightVolume(volume, Color.white, 1, false, i * 0.01f);
                volumes[i] = volume;
            }
            for (int i = 0; i < points.Length; i++) {
                PointLightVolumeInstance point = CreatePointLight(manager, "Clamped Point " + i, true);
                SetPointLightSquaredSize(point, 1);
                point.SetPointLight();
                points[i] = point;
            }

            manager.LightVolumeInstances = volumes;
            manager.PointLightVolumeInstances = points;
            manager.AdditiveMaxOverdraw = 128;
            manager.ShadowTexturesWidth = 4;
            manager.ShadowTexturesHeight = 4;
            // A shadowed record outside the selected prefix must not be promoted into the first 128.
            ConfigureShadowTexture(points[129], CreateCubemap("Excluded Clamped Shadow"), false, true, false);
            manager.ReinitializeShadowTextures();

            manager.UpdateVolumes();

            AssertGlobalFloat(_lightVolumeEnabledID, 1);
            AssertGlobalFloat(_lightVolumeCountID, 32);
            AssertGlobalFloat(_pointLightCountID, 128);
            AssertVectorClose(ExpectedLightVolumeColor(volumes[0]), Shader.GetGlobalVectorArray(_lightVolumeColorID)[0]);
            AssertVectorClose(ExpectedLightVolumeColor(volumes[31]), Shader.GetGlobalVectorArray(_lightVolumeColorID)[31]);
            AssertVectorClose(ExpectedPointLightColor(points[0]), Shader.GetGlobalVectorArray(_pointLightColorID)[0]);
            AssertVectorClose(ExpectedPointLightColor(points[127]), Shader.GetGlobalVectorArray(_pointLightColorID)[127]);
        }

        // Creates a manager with deterministic defaults.
        private LightVolumeManager CreateManager(string name, bool withAtlas) {
            return CreateManager(name, withAtlas, true);
        }

        private LightVolumeManager CreateManager(string name, bool withAtlas, bool active) {
            GameObject gameObject = CreateGameObject(name, false);
            LightVolumeManager manager = gameObject.AddComponent<LightVolumeManager>();
            manager.LightVolumeAtlas = withAtlas ? CreateAtlas() : null;
            manager.LightVolumeInstances = new LightVolumeInstance[0];
            manager.PointLightVolumeInstances = new PointLightVolumeInstance[0];
            manager.LightProbesBlending = true;
            manager.SharpBounds = true;
            manager.AutoUpdateVolumes = false;
            manager.AdditiveMaxOverdraw = 4;
            manager.LightsBrightnessCutoff = 0.35f;
            gameObject.SetActive(active);
            return manager;
        }

        // Creates a scene light volume instance and optionally lets Unity call OnEnable.
        private LightVolumeInstance CreateLightVolume(LightVolumeManager manager, string name, bool active) {
            GameObject gameObject = CreateGameObject(name, false);
            LightVolumeInstance volume = gameObject.AddComponent<LightVolumeInstance>();
            volume.LightVolumeManager = manager;
            volume.IsDynamic = true;
            ConfigureLightVolume(volume, Color.white, 1, false, 0);
            gameObject.SetActive(active);
            if (active && manager != null) manager.InitializeLightVolume(volume);
            return volume;
        }

        // Creates a scene point light volume instance and optionally lets Unity call OnEnable.
        private PointLightVolumeInstance CreatePointLight(LightVolumeManager manager, string name, bool active) {
            GameObject gameObject = CreateGameObject(name, false);
            PointLightVolumeInstance point = gameObject.AddComponent<PointLightVolumeInstance>();
            point.LightVolumeManager = manager;
            point.Color = Color.white;
            point.Intensity = 1;
            point.IsDynamic = true;
            SetPointLightSquaredSize(point, 1);
            point.Direction = Vector3.forward;
            point.ConeFalloff = 1;
            point.Angle = 30 * Mathf.Deg2Rad;
            point.OuterAngleCos = Mathf.Cos(point.Angle);
            point.OuterAngleTan = Mathf.Tan(point.Angle);
            gameObject.SetActive(active);
            if (active && manager != null) manager.InitializePointLightVolume(point);
            return point;
        }

        // Creates an active light volume that has a manager reference but is not registered yet.
        private LightVolumeInstance CreateUnregisteredLightVolume(LightVolumeManager manager, string name) {
            GameObject gameObject = CreateGameObject(name, true);
            LightVolumeInstance volume = gameObject.AddComponent<LightVolumeInstance>();
            volume.LightVolumeManager = manager;
            volume.IsDynamic = true;
            ConfigureLightVolume(volume, Color.white, 1, false, 0);
            return volume;
        }

        // Creates an active point light volume that has a manager reference but is not registered yet.
        private PointLightVolumeInstance CreateUnregisteredPointLight(LightVolumeManager manager, string name) {
            GameObject gameObject = CreateGameObject(name, true);
            PointLightVolumeInstance point = gameObject.AddComponent<PointLightVolumeInstance>();
            point.LightVolumeManager = manager;
            point.Color = Color.white;
            point.Intensity = 1;
            point.IsDynamic = true;
            SetPointLightSquaredSize(point, 1);
            point.Direction = Vector3.forward;
            point.ConeFalloff = 1;
            point.Angle = 30 * Mathf.Deg2Rad;
            point.OuterAngleCos = Mathf.Cos(point.Angle);
            return point;
        }

        // Creates an active light volume that has no manager reference after its initial enable pass.
        private LightVolumeInstance CreateManagerlessLightVolume(string name) {
            GameObject gameObject = CreateGameObject(name, true);
            LightVolumeInstance volume = gameObject.AddComponent<LightVolumeInstance>();
            volume.IsDynamic = true;
            ConfigureLightVolume(volume, Color.white, 1, false, 0);
            return volume;
        }

        // Creates an active point light volume that has no manager reference after its initial enable pass.
        private PointLightVolumeInstance CreateManagerlessPointLight(string name) {
            GameObject gameObject = CreateGameObject(name, true);
            PointLightVolumeInstance point = gameObject.AddComponent<PointLightVolumeInstance>();
            point.Color = Color.white;
            point.Intensity = 1;
            point.IsDynamic = true;
            SetPointLightSquaredSize(point, 1);
            point.Direction = Vector3.forward;
            point.ConeFalloff = 1;
            point.Angle = 30 * Mathf.Deg2Rad;
            point.OuterAngleCos = Mathf.Cos(point.Angle);
            point.OuterAngleTan = Mathf.Tan(point.Angle);
            return point;
        }

        // Creates a temporary GameObject tracked by teardown.
        private GameObject CreateGameObject(string name, bool active) {
            GameObject gameObject = new GameObject(name);
            _createdObjects.Add(gameObject);
            gameObject.SetActive(active);
            return gameObject;
        }

        // Configures the private runtime blur state needed by reflection-based blur material tests.
        private static void ConfigureRuntimeShadowBlurReflectionState(PointLightVolumeInstance point, MethodInfo initializeShaderPropertiesMethod) {
            initializeShaderPropertiesMethod.Invoke(point, null);
            point.Blur = 1f;
            point.ContactHardening = 0f;
        }

        // Adds the hidden camera that the editor preprocessor normally injects before Play Mode or build
        private Camera AddRuntimeShadowCamera(PointLightVolumeInstance point) {
            GameObject cameraObject = CreateGameObject(point.name + " Runtime Shadow Camera", true);
            cameraObject.transform.SetParent(point.transform, false);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            point.RuntimeShadowCamera = camera;
            return camera;
        }

        // Creates a temporary 3D atlas texture tracked by teardown.
        private Texture3D CreateAtlas() {
            Texture3D texture = new Texture3D(1, 1, 1, TextureFormat.RGBA32, false);
            texture.name = "Runtime Test Light Volume Atlas";
            _createdObjects.Add(texture);
            return texture;
        }

        // Creates a temporary 2D texture for texture global assignment checks.
        private Texture2D CreateTexture2D(string name) {
            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.name = name;
            texture.SetPixel(0, 0, Color.white);
            texture.Apply(false);
            _createdObjects.Add(texture);
            return texture;
        }

        // Creates a temporary cubemap tracked by teardown.
        private Cubemap CreateCubemap(string name) {
            Cubemap cubemap = new Cubemap(1, TextureFormat.RGBA32, false);
            cubemap.name = name;
            cubemap.SetPixel(CubemapFace.PositiveX, 0, 0, Color.white);
            cubemap.SetPixel(CubemapFace.NegativeX, 0, 0, Color.white);
            cubemap.SetPixel(CubemapFace.PositiveY, 0, 0, Color.white);
            cubemap.SetPixel(CubemapFace.NegativeY, 0, 0, Color.white);
            cubemap.SetPixel(CubemapFace.PositiveZ, 0, 0, Color.white);
            cubemap.SetPixel(CubemapFace.NegativeZ, 0, 0, Color.white);
            cubemap.Apply(false);
            _createdObjects.Add(cubemap);
            return cubemap;
        }

        // Creates a temporary render texture source tracked by teardown.
        private RenderTexture CreateRenderTexture(string name, int width, int height, int depth, TextureDimension dimension) {
            RenderTexture texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            texture.name = name;
            texture.dimension = dimension;
            texture.volumeDepth = Mathf.Max(depth, 1);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Point;
            texture.Create();
            _createdObjects.Add(texture);
            return texture;
        }

        // Assigns a shadow texture source in the same shape as point-light authoring sync.
        private static void ConfigureShadowTexture(PointLightVolumeInstance point, Texture source, bool autoUpdate, bool isCubemap, bool hasDepthSlices) {
            point.ShadowMapID = 0;
            point.ShadowMapTexture = source;
            point.ShadowMapMaterial = null;
            point.AutoUpdateShadowMap = autoUpdate;
            point.ShadowMapTextureIsCubemap = isCubemap;
            point.ShadowMapTextureHasDepthSlices = hasDepthSlices;
        }

        // Assigns a point cubemap projection source in the same shape as point-light authoring sync.
        private static PointLightVolumeInstance ConfigurePointCubemapSource(PointLightVolumeInstance point, Texture source, bool autoUpdate) {
            point.SetPointLight();
            point.SetCustomTexture();
            point.CustomTexture = source;
            point.CustomTextureMaterial = null;
            point.ProjectionType = 1; // 1: texture
            point.CustomTextureIsCubemap = true;
            point.CustomTextureHasDepthSlices = false;
            point.AutoUpdateCustomTexture = autoUpdate;
            return point;
        }

        // Creates a temporary material tracked by teardown.
        private Material CreateMaterial(string shaderName) {
            Shader shader = Shader.Find(shaderName);
            Assert.That(shader, Is.Not.Null, shaderName + " shader was not found");
            Material material = new Material(shader);
            material.name = "Runtime Test Material";
            _createdObjects.Add(material);
            return material;
        }

        // Assigns deterministic volume data used by shader global assertions.
        private static void ConfigureLightVolume(LightVolumeInstance volume, Color color, float intensity, bool isAdditive, float offset) {
            volume.Color = color;
            volume.Intensity = intensity;
            volume.IsAdditive = isAdditive;
            volume.InvBakedRotation = Quaternion.identity;
            volume.BoundsUvwMin0 = new Vector4(offset + 0.01f, offset + 0.02f, offset + 0.03f, offset + 0.04f);
            volume.BoundsUvwMin1 = new Vector4(offset + 0.05f, offset + 0.06f, offset + 0.07f, offset + 0.08f);
            volume.BoundsUvwMin2 = new Vector4(offset + 0.09f, offset + 0.1f, offset + 0.11f, offset + 0.12f);
            volume.InvLocalEdgeSmoothing = new Vector4(offset + 0.29f, offset + 0.3f, offset + 0.31f, offset + 0.32f);
        }

        // Converts a light volume color exactly like LightVolumeManager does.
        private static Vector4 ExpectedLightVolumeColor(LightVolumeInstance instance) {
            Color color = instance.Color.linear * instance.Intensity;
            return new Vector4(color.r, color.g, color.b, instance.IsRotated ? 1 : 0);
        }

        // Expands the compact UVW layout into the min/max records consumed by shaders.
        private static Vector4 ExpectedExpandedLightVolumeUvw(LightVolumeInstance instance, int textureIndex, bool max) {
            Vector4 uvwMin = textureIndex == 0 ? instance.BoundsUvwMin0 : textureIndex == 1 ? instance.BoundsUvwMin1 : instance.BoundsUvwMin2;
            if (!max) return new Vector4(uvwMin.x, uvwMin.y, uvwMin.z, 0);
            return new Vector4(uvwMin.x + instance.BoundsUvwMin0.w, uvwMin.y + instance.BoundsUvwMin1.w, uvwMin.z + instance.BoundsUvwMin2.w, 0);
        }

        // Converts a point light color exactly like LightVolumeManager does.
        private static Vector4 ExpectedPointLightColor(PointLightVolumeInstance instance) {
            Color color = instance.Color.linear * instance.Intensity;
            return new Vector4(color.r, color.g, color.b, ExpectedPointLightAngleData(instance));
        }

        // Converts an area light color with a cookie fallback average exactly like LightVolumeManager does.
        private static Vector4 ExpectedAreaCookieFallbackColor(PointLightVolumeInstance instance, Color averageColor) {
            Color color = instance.Color.linear * instance.Intensity;
            float alpha = averageColor.a;
            return new Vector4(color.r * averageColor.r * alpha, color.g * averageColor.g * alpha, color.b * averageColor.b * alpha, ExpectedPointLightAngleData(instance));
        }

        // Assigns the readable point light size field using the old packed squared-size value used by previous tests.
        private static void SetPointLightSquaredSize(PointLightVolumeInstance point, float squaredSize) {
            point.LightSourceSize = Mathf.Sqrt(Mathf.Max(Mathf.Abs(squaredSize), 0.0001f));
            point.InverseSquaredRange = 1f / Mathf.Max(point.LightSourceSize * point.LightSourceSize, 0.0001f);
        }

        // Converts readable point light position data exactly like LightVolumeManager does.
        private static Vector4 ExpectedPointLightPosition(PointLightVolumeInstance instance) {
            if (instance.LightType == 2) return new Vector4(instance.Position.x, instance.Position.y, instance.Position.z, Mathf.Max(Mathf.Abs(instance.Width), 0.001f)); // 2: area
            float typeSign = instance.LightType == 1 ? -1f : 1f; // 1: spot
            float w = instance.ProjectionMode == 1 ? typeSign * instance.InverseSquaredRange / Mathf.Max(instance.SquaredScale, 0.000001f) : typeSign * instance.LightSourceSize * instance.LightSourceSize * instance.SquaredScale; // 1: LUT
            return new Vector4(instance.Position.x, instance.Position.y, instance.Position.z, w);
        }

        // Computes the positive reciprocal shadow depth range precomputed by current managers.
        private static float ExpectedShadowInvDepthRange(PointLightVolumeInstance instance) {
            float nearClip = Mathf.Max(instance.NearClip, 0.0001f);
            float requestedFarClip = instance.BakedFarClip > 0f ? instance.BakedFarClip : instance.FarClip;
            float farClip = requestedFarClip > 0f ? Mathf.Max(requestedFarClip, nearClip + 0.0001f) : Mathf.Sqrt(Mathf.Max(instance.SquaredRange, 0.000001f));
            if (nearClip >= farClip) farClip = nearClip + 0.0001f;
            return 1f / Mathf.Max(farClip - nearClip, 0.0001f);
        }

        // CustomID.W stores the reciprocal magnitude for every Point/Spot shadow. Its sign marks
        // only exact same-origin world shadows; local and shifted-world shadows remain positive.
        private static float ExpectedCustomShadowInvDepthRange(PointLightVolumeInstance instance) {
            float invDepthRange = ExpectedShadowInvDepthRange(instance);
            Vector3 bakePosition = instance.ShadowBakePosition;
            Vector4 lightPosition = instance.Position;
            bool reuseWorldShadowOrigin = instance.WorldSpaceShadows
                && bakePosition.x == lightPosition.x
                && bakePosition.y == lightPosition.y
                && bakePosition.z == lightPosition.z;
            return reuseWorldShadowOrigin ? -invDepthRange : invDepthRange;
        }

        // Cubemap reprojection stores the reciprocal depth range with a negative sign.
        private static float ExpectedCubemapShadowInvDepthRange(PointLightVolumeInstance instance) {
            return -ExpectedShadowInvDepthRange(instance);
        }

        // Converts readable point light angle data exactly like LightVolumeManager does.
        private static float ExpectedPointLightAngleData(PointLightVolumeInstance instance) {
            if (instance.LightType == 2) return 2f + Mathf.Max(Mathf.Abs(instance.Height), 0.001f); // 2: area
            if (instance.LightType == 1 && instance.ProjectionMode == 2) return instance.OuterAngleTan; // 1: spot, 2: custom cookie
            return instance.OuterAngleCos;
        }

        // Asserts the packed point custom data vector written to the shader.
        private static void AssertPointCustomData(PointLightVolumeInstance point, float customId, float shadowId) {
            AssertPointCustomData(0, point, customId, shadowId);
        }

        // Asserts the packed point custom data vector at a specific shader array index.
        private static void AssertPointCustomData(int index, PointLightVolumeInstance point, float customId, float shadowId) {
            Vector4 data = Shader.GetGlobalVectorArray(_pointLightCustomIdID)[index];
            Assert.That(data.x, Is.EqualTo(customId).Within(Epsilon));
            Assert.That(data.y, Is.EqualTo(shadowId).Within(Epsilon));
            Assert.That(data.z, Is.EqualTo(point.SquaredRange).Within(Epsilon));
            float expectedCustomDataW = 0f;
            if (point.LightType == 2) { // Area uses the v2-invisible W padding only as an optional Cookie mirror tag.
                if (customId < 0) expectedCustomDataW = point.AreaCookieMirror;
            } else {
                float shadowIdAbs = Mathf.Abs(shadowId);
                bool hasShadow = shadowIdAbs >= 1f && shadowIdAbs < 10000f;
                if (hasShadow) expectedCustomDataW = ExpectedCustomShadowInvDepthRange(point);
            }
            Assert.That(data.w, Is.EqualTo(expectedCustomDataW).Within(Epsilon));
        }

        // Asserts a global float with the shared tolerance.
        private static void AssertGlobalFloat(int propertyId, float expected) {
            Assert.That(Shader.GetGlobalFloat(propertyId), Is.EqualTo(expected).Within(Epsilon));
        }

        // Asserts an integer shader global without relying on float/int global coercion.
        private static void AssertGlobalInteger(int propertyId, int expected) {
            Assert.That(Shader.GetGlobalInteger(propertyId), Is.EqualTo(expected));
        }

        // Asserts a Vector4 with the shared tolerance.
        private static void AssertVectorClose(Vector4 expected, Vector4 actual) {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(Epsilon));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(Epsilon));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(Epsilon));
            Assert.That(actual.w, Is.EqualTo(expected.w).Within(Epsilon));
        }

        // Asserts a Matrix4x4 with the shared tolerance.
        private static void AssertMatrixClose(Matrix4x4 expected, Matrix4x4 actual) {
            for (int i = 0; i < 16; i++) {
                Assert.That(actual[i], Is.EqualTo(expected[i]).Within(Epsilon), "Matrix index " + i);
            }
        }

        // Finds a light volume reference without relying on LINQ.
        private static bool ContainsLightVolume(LightVolumeInstance[] instances, LightVolumeInstance target) {
            if (instances == null) return false;
            for (int i = 0; i < instances.Length; i++) {
                if (instances[i] == target) return true;
            }
            return false;
        }

        // Finds a point light volume reference without relying on LINQ.
        private static bool ContainsPointLightVolume(PointLightVolumeInstance[] instances, PointLightVolumeInstance target) {
            if (instances == null) return false;
            for (int i = 0; i < instances.Length; i++) {
                if (instances[i] == target) return true;
            }
            return false;
        }

        // Counts light volume references without relying on LINQ.
        private static int CountLightVolumeReferences(LightVolumeInstance[] instances, LightVolumeInstance target) {
            if (instances == null) return 0;
            int count = 0;
            for (int i = 0; i < instances.Length; i++) {
                if (instances[i] == target) count++;
            }
            return count;
        }

        // Counts point light volume references without relying on LINQ.
        private static int CountPointLightVolumeReferences(PointLightVolumeInstance[] instances, PointLightVolumeInstance target) {
            if (instances == null) return 0;
            int count = 0;
            for (int i = 0; i < instances.Length; i++) {
                if (instances[i] == target) count++;
            }
            return count;
        }

        // Invokes private Unity lifecycle methods because EditMode tests do not run normal MonoBehaviour lifecycle for these scripts.
        private static void InvokeLifecycleMethod(MonoBehaviour behaviour, string methodName) {
            MethodInfo method = behaviour.GetType().GetMethod(methodName, _lifecycleMethodFlags);
            Assert.That(method, Is.Not.Null, methodName + " method was not found on " + behaviour.GetType().Name);
            method.Invoke(behaviour, null);
        }

        // Resets scalar shader globals that can affect later tests.
        private static void ResetShaderGlobals() {
            Shader.SetGlobalFloat(_lightVolumeEnabledID, 0);
            Shader.SetGlobalFloat(_lightVolumeCountID, 0);
            Shader.SetGlobalFloat(_lightVolumeAdditiveCountID, 0);
            Shader.SetGlobalFloat(_lightVolumeOcclusionCountID, 0);
            Shader.SetGlobalFloat(_pointLightCountID, 0);
            Shader.SetGlobalFloat(_pointLightCubeCountID, 0);
            Shader.SetGlobalFloat(_pointLightShadowCubeCountID, 0);
            Shader.SetGlobalFloat(_pointLightShadowCountID, 0);
            Shader.SetGlobalVector(_pointLightShadowReceiverParamsID, Vector4.zero);
            Shader.SetGlobalFloat(_lightBrightnessCutoffID, 0);
            Shader.SetGlobalInteger(_forceSceneLightingID, 0);
        }

        // Destroys a temporary Unity object immediately when the editor runtime allows it.
        private static void DestroyTestObject(UnityEngine.Object target) {
            if (target == null) return;
            if (Application.isEditor) UnityEngine.Object.DestroyImmediate(target);
            else UnityEngine.Object.Destroy(target);
        }
    }

}
