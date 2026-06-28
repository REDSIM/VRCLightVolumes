using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VRCLightVolumes.Tests {
    [Category("Editor")]
    public class LightVolumeEditorPipelineTests {
        private const float Epsilon = 0.0001f;
        private static readonly BindingFlags _nonPublicInstanceFlags = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo _customTexturesDepthField = typeof(LightVolumeManager).GetField("_customTextureArrayDepth", _nonPublicInstanceFlags);

        private readonly List<UnityEngine.Object> _createdObjects = new List<UnityEngine.Object>();

        // Destroys all temporary scene and texture objects created by a test case.
        [TearDown]
        public void TearDown() {
            for (int i = _createdObjects.Count - 1; i >= 0; i--) {
                DestroyTestObject(_createdObjects[i]);
            }
            _createdObjects.Clear();
        }

        // Verifies PointLightVolume infers auto-update on projection source assignment and preserves manual overrides until the next assignment.
        [Test]
        public void PointLightVolumeInfersAutoUpdateFromProjectionSourceType() {
            GameObject setupObject = CreateGameObject("Projection Auto Update Setup", true);
            LightVolumeSetup setup = setupObject.AddComponent<LightVolumeSetup>();
            setup.SetupDependencies();
            LightVolumeManager manager = setup.LightVolumeManager;
            if (manager == null) manager = setupObject.GetComponent<LightVolumeManager>();
            Assert.That(manager, Is.Not.Null);

            GameObject lightObject = CreateGameObject("Projection Auto Update Light", true);
            PointLightVolumeInstance instance = lightObject.AddComponent<PointLightVolumeInstance>();
            PointLightVolume pointLight = lightObject.AddComponent<PointLightVolume>();
            pointLight.LightVolumeSetup = setup;
            pointLight.PointLightVolumeInstance = instance;
            pointLight.Type = PointLightVolume.LightType.SpotLight;
            pointLight.Projection = PointLightVolume.LightProjection.Custom;
            instance.LightVolumeManager = manager;

            pointLight.Cookie = CreateTexture2D("Static Cookie Source");
            pointLight.SyncUdonScript();

            Assert.That(instance.AutoUpdateCustomTexture, Is.False);

            pointLight.Cookie = CreateRenderTexture("Render Cookie Source", 4, 4, 1, TextureDimension.Tex2D);
            pointLight.SyncUdonScript();

            Assert.That(instance.AutoUpdateCustomTexture, Is.True);

            instance.AutoUpdateCustomTexture = false;
            pointLight.Intensity = 2f;
            pointLight.SyncUdonScript();

            Assert.That(instance.AutoUpdateCustomTexture, Is.False);

            pointLight.Cookie = CreateMaterial("Hidden/CubeFace");
            pointLight.SyncUdonScript();

            Assert.That(instance.AutoUpdateCustomTexture, Is.True);
            Assert.That(instance.ProjectionType, Is.EqualTo(2)); // 2: material

            pointLight.Cookie = CreateTexture2D("Static Cookie Source After Material");
            pointLight.SyncUdonScript();

            Assert.That(instance.AutoUpdateCustomTexture, Is.False);

            instance.AutoUpdateCustomTexture = true;
            pointLight.ShadingStrength = 0.5f;
            pointLight.SyncUdonScript();

            Assert.That(instance.AutoUpdateCustomTexture, Is.True);

            pointLight.Cookie = CreateTexture2D("Static Cookie Source After Manual Override");
            pointLight.SyncUdonScript();

            Assert.That(instance.AutoUpdateCustomTexture, Is.False);
        }

        // Verifies editor sync copies changed projection source references before texture array rebuilds.
        [Test]
        public void PointLightVolumeEditorSyncTargetsCopiesChangedProjectionSources() {
            GameObject setupObject = CreateGameObject("Editor Projection Sync Setup", true);
            LightVolumeSetup setup = setupObject.AddComponent<LightVolumeSetup>();
            setup.SetupDependencies();

            GameObject lightObject = CreateGameObject("Editor Projection Sync Light", true);
            PointLightVolumeInstance instance = lightObject.AddComponent<PointLightVolumeInstance>();
            PointLightVolume pointLight = lightObject.AddComponent<PointLightVolume>();
            pointLight.LightVolumeSetup = setup;
            pointLight.PointLightVolumeInstance = instance;
            pointLight.Type = PointLightVolume.LightType.SpotLight;
            pointLight.Projection = PointLightVolume.LightProjection.Custom;
            pointLight.Cookie = CreateTexture2D("Editor Cookie Source");

            Editor editor = Editor.CreateEditor(pointLight);
            _createdObjects.Add(editor);
            MethodInfo syncTargets = typeof(PointLightVolumeEditor).GetMethod("SyncTargets", _nonPublicInstanceFlags);
            Assert.That(syncTargets, Is.Not.Null);

            syncTargets.Invoke(editor, new object[] { true, false });

            Assert.That(instance.CustomTexture, Is.SameAs(pointLight.Cookie));
            Assert.That(instance.ProjectionType, Is.EqualTo(1)); // 1: texture
            Assert.That(instance.ProjectionMode, Is.EqualTo(2)); // 2: cookie/cubemap
        }

        // Verifies area light cookies are copied even though area lights hide the projection mode enum.
        [Test]
        public void AreaLightVolumeEditorSyncTargetsCopiesCookieProjectionSource() {
            GameObject setupObject = CreateGameObject("Editor Area Cookie Sync Setup", true);
            LightVolumeSetup setup = setupObject.AddComponent<LightVolumeSetup>();
            setup.SetupDependencies();

            GameObject lightObject = CreateGameObject("Editor Area Cookie Sync Light", true);
            PointLightVolumeInstance instance = lightObject.AddComponent<PointLightVolumeInstance>();
            PointLightVolume pointLight = lightObject.AddComponent<PointLightVolume>();
            pointLight.LightVolumeSetup = setup;
            pointLight.PointLightVolumeInstance = instance;
            pointLight.Type = PointLightVolume.LightType.AreaLight;
            pointLight.Cookie = CreateTexture2D("Editor Area Cookie Source");

            Editor editor = Editor.CreateEditor(pointLight);
            _createdObjects.Add(editor);
            MethodInfo syncTargets = typeof(PointLightVolumeEditor).GetMethod("SyncTargets", _nonPublicInstanceFlags);
            Assert.That(syncTargets, Is.Not.Null);

            syncTargets.Invoke(editor, new object[] { true, false });

            Assert.That(pointLight.GetProjectionSource(), Is.SameAs(pointLight.Cookie));
            Assert.That(instance.CustomTexture, Is.SameAs(pointLight.Cookie));
            Assert.That(instance.ProjectionType, Is.EqualTo(1)); // 1: texture
            Assert.That(instance.ProjectionMode, Is.EqualTo(2)); // 2: cookie/cubemap
            Assert.That(instance.LightType, Is.EqualTo(2)); // 2: area
        }

        // Verifies migration re-sync rebuilds custom texture arrays from authoring PointLightVolume sources.
        [Test]
        public void MigrationAuthoringSyncRebuildsCustomTexturesFromPointSources() {
            GameObject setupObject = CreateGameObject("Migration Texture Sync Setup", true);
            LightVolumeSetup setup = setupObject.AddComponent<LightVolumeSetup>();
            setup.SetupDependencies();
            setup.CookieResolution = LightVolumeSetup.TextureArrayResolution._16x16;
            LightVolumeManager manager = setup.LightVolumeManager;
            Assert.That(manager, Is.Not.Null);

            GameObject lightObject = CreateGameObject("Migration Texture Sync Light", true);
            PointLightVolumeInstance instance = lightObject.AddComponent<PointLightVolumeInstance>();
            PointLightVolume pointLight = lightObject.AddComponent<PointLightVolume>();
            pointLight.LightVolumeSetup = setup;
            pointLight.PointLightVolumeInstance = instance;
            pointLight.Type = PointLightVolume.LightType.SpotLight;
            pointLight.Projection = PointLightVolume.LightProjection.Custom;
            pointLight.Cookie = CreateTexture2D("Migration Cookie Source");
            setup.PointLightVolumes.Clear();
            setup.PointLightVolumes.Add(pointLight);

            instance.CustomTexture = null;
            instance.LightVolumeManager = null;
            manager.CustomTextures = null;
            setup.LightVolumeManager = null;

            MethodInfo syncAuthoring = typeof(LightVolumeUdonComponentSanitizer).GetMethod("SyncAuthoringComponentsToMigratedRuntime", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(syncAuthoring, Is.Not.Null);

            syncAuthoring.Invoke(null, null);

            Assert.That(setup.LightVolumeManager, Is.SameAs(manager));
            Assert.That(instance.LightVolumeManager, Is.SameAs(manager));
            Assert.That(instance.CustomTexture, Is.SameAs(pointLight.Cookie));
            Assert.That(manager.CustomTextures, Is.Not.Null);
            Assert.That(manager.CustomTextures.dimension, Is.EqualTo(TextureDimension.Tex2DArray));
            Assert.That(manager.CustomTextures.width, Is.EqualTo(16));
            Assert.That(manager.CustomTextures.height, Is.EqualTo(16));
            Assert.That(GetManagerField<int>(manager, _customTexturesDepthField), Is.EqualTo(1));
        }

        // Verifies cubemap RenderTextures are unfolded as cubemaps instead of copied as a single 2D slice.
        [Test]
        public void PointLightVolumeDetectsCubemapRenderTextureSources() {
            GameObject setupObject = CreateGameObject("Cubemap RenderTexture Setup", true);
            LightVolumeSetup setup = setupObject.AddComponent<LightVolumeSetup>();
            setup.SetupDependencies();
            LightVolumeManager manager = setup.LightVolumeManager;
            if (manager == null) manager = setupObject.GetComponent<LightVolumeManager>();

            GameObject lightObject = CreateGameObject("Cubemap RenderTexture Light", true);
            PointLightVolumeInstance instance = lightObject.AddComponent<PointLightVolumeInstance>();
            PointLightVolume pointLight = lightObject.AddComponent<PointLightVolume>();
            pointLight.LightVolumeSetup = setup;
            pointLight.PointLightVolumeInstance = instance;
            pointLight.Type = PointLightVolume.LightType.PointLight;
            pointLight.Projection = PointLightVolume.LightProjection.Custom;
            pointLight.Cubemap = CreateRenderTexture("Animated Cubemap Source", 4, 4, 1, TextureDimension.Cube);
            pointLight.Shadows = true;
            pointLight.Bias = 0.42f;
            pointLight.LayerMask = 1 << 6;
            pointLight.NearPlane = 0.15f;
            pointLight.Blur = 2.5f;
            pointLight.ContactHardening = 0.08f;
            pointLight.ShadingStrength = 0.42f;
            pointLight.ShadowMap = CreateRenderTexture("Animated Shadow Cubemap Source", 4, 4, 1, TextureDimension.Cube);
            instance.LightVolumeManager = manager;

            pointLight.SyncUdonScript();

            Assert.That(instance.CustomTextureIsCubemap, Is.True);
            Assert.That(instance.AutoUpdateCustomTexture, Is.True);
            Assert.That(instance.ShadowMapTextureIsCubemap, Is.True);
            Assert.That(instance.AutoUpdateShadowMap, Is.True);
            Assert.That(instance.Bias, Is.EqualTo(0.42f).Within(0.0001f));
            Assert.That(instance.LayerMask, Is.EqualTo(1 << 6));
            Assert.That(instance.NearClip, Is.EqualTo(0.15f).Within(0.0001f));
            Assert.That(instance.Blur, Is.EqualTo(2.5f).Within(0.0001f));
            Assert.That(instance.ContactHardening, Is.EqualTo(0.08f).Within(0.0001f));
            Assert.That(instance.ShadingStrength, Is.EqualTo(0.42f).Within(0.0001f));

            Texture shadowSource = instance.ShadowMapTexture;
            pointLight.ShadowMap = CreateCubemap("Static Shadow Cubemap After Data Sync");
            pointLight.Intensity = 2.25f;
            pointLight.Bias = 0.5f;
            pointLight.SyncUdonScript(false);

            Assert.That(instance.Intensity, Is.EqualTo(2.25f).Within(0.0001f));
            Assert.That(instance.Bias, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(instance.ShadowMapTexture, Is.SameAs(shadowSource));
            Assert.That(instance.AutoUpdateShadowMap, Is.True);
        }

        // Verifies spot shadow authoring selects single texture layout unless the source or force flag requires a cubemap.
        [Test]
        public void PointLightVolumeSyncsSpotSingleShadowMetadata() {
            GameObject setupObject = CreateGameObject("Spot Single Shadow Metadata Setup", true);
            LightVolumeSetup setup = setupObject.AddComponent<LightVolumeSetup>();
            setup.SetupDependencies();

            GameObject lightObject = CreateGameObject("Spot Single Shadow Metadata Light", true);
            PointLightVolumeInstance instance = lightObject.AddComponent<PointLightVolumeInstance>();
            PointLightVolume pointLight = lightObject.AddComponent<PointLightVolume>();
            pointLight.LightVolumeSetup = setup;
            pointLight.PointLightVolumeInstance = instance;
            pointLight.Type = PointLightVolume.LightType.SpotLight;
            pointLight.Angle = 60f;
            pointLight.Shadows = true;
            pointLight.ShadowMap = CreateTexture2D("Spot Single Shadow Texture");

            pointLight.SyncUdonScript();

            Assert.That(pointLight.ShouldBakeCubemapShadows(), Is.False);
            Assert.That(pointLight.UsesCubemapShadows(), Is.False);
            Assert.That(instance.ShadowMapUsesCubemap, Is.False);

            pointLight.ForceCubemapShadows = true;
            pointLight.SyncUdonScript();

            Assert.That(pointLight.ShouldBakeCubemapShadows(), Is.True);
            Assert.That(pointLight.UsesCubemapShadows(), Is.True);
            Assert.That(instance.ShadowMapUsesCubemap, Is.True);

            pointLight.ForceCubemapShadows = false;
            pointLight.ShadowMap = CreateCubemap("Spot Existing Cubemap Shadow");
            pointLight.SyncUdonScript();

            Assert.That(pointLight.ShouldBakeCubemapShadows(), Is.False);
            Assert.That(pointLight.UsesCubemapShadows(), Is.True);
            Assert.That(instance.ShadowMapUsesCubemap, Is.True);
        }

        // Verifies data-only sync updates Shading Strength without refreshing projection or shadow texture metadata.
        [Test]
        public void PointLightVolumeDataOnlySyncUpdatesShadingStrength() {
            GameObject setupObject = CreateGameObject("Shading Strength Setup", true);
            LightVolumeSetup setup = setupObject.AddComponent<LightVolumeSetup>();
            setup.SetupDependencies();

            GameObject lightObject = CreateGameObject("Shading Strength Light", true);
            PointLightVolumeInstance instance = lightObject.AddComponent<PointLightVolumeInstance>();
            PointLightVolume pointLight = lightObject.AddComponent<PointLightVolume>();
            pointLight.LightVolumeSetup = setup;
            pointLight.PointLightVolumeInstance = instance;
            pointLight.Type = PointLightVolume.LightType.SpotLight;
            pointLight.Projection = PointLightVolume.LightProjection.Custom;
            pointLight.Cookie = CreateRenderTexture("Animated Cookie Before Shading Sync", 4, 4, 1, TextureDimension.Tex2D);

            pointLight.SyncUdonScript();
            Assert.That(instance.AutoUpdateCustomTexture, Is.True);

            pointLight.Cookie = CreateTexture2D("Static Cookie After Shading Sync");
            pointLight.ShadingStrength = 0.25f;
            pointLight.SyncUdonScript(false);

            Assert.That(instance.ShadingStrength, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(instance.AutoUpdateCustomTexture, Is.True);
        }

        // Verifies the authoring Shadows toggle controls runtime shadow usage even when a shadow map asset exists.
        [Test]
        public void PointLightVolumeShadowsToggleControlsRuntimeShadowId() {
            GameObject gameObject = CreateGameObject("Shadow Toggle Point Light Volume", false);
            PointLightVolume pointLightVolume = gameObject.AddComponent<PointLightVolume>();
            Cubemap shadowMap = CreateCubemap("Shadow Toggle Cubemap");
            MethodInfo method = typeof(PointLightVolume).GetMethod("GetShadowRuntimeID", _nonPublicInstanceFlags);
            Assert.That(method, Is.Not.Null);

            pointLightVolume.ShadowMap = shadowMap;
            pointLightVolume.Shadows = false;

            Assert.That((int)method.Invoke(pointLightVolume, null), Is.EqualTo(-1));

            pointLightVolume.Shadows = true;

            Assert.That((int)method.Invoke(pointLightVolume, null), Is.EqualTo(0));
        }

        // Verifies manager-created runtime texture arrays are hidden from scene and asset serialization.
        [Test]
        public void RuntimeTextureArraysUseHideAndDontSave() {
            LightVolumeManager manager = CreateManager("Runtime Hide Flags Manager", false);
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;
            manager.ShadowTexturesWidth = 4;
            manager.ShadowTexturesHeight = 4;

            RenderTexture customSource = CreateRenderTexture("Runtime Hide Flags Cookie Source", 4, 4, 1, TextureDimension.Tex2D);
            RenderTexture shadowSource = CreateRenderTexture("Runtime Hide Flags Shadow Source", 4, 4, 6, TextureDimension.Tex2DArray);
            PointLightVolumeInstance point = CreatePointLight(manager, "Runtime Hide Flags Point", true);
            point.SetCustomTexture();
            point.CustomTexture = customSource;
            point.ProjectionType = 1; // 1: texture
            point.AutoUpdateCustomTexture = true;
            point.ShadowMapID = 0;
            point.ShadowMapTexture = shadowSource;
            point.AutoUpdateShadowMap = true;
            point.ShadowMapTextureHasDepthSlices = true;
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeCustomTextures();
            manager.ReinitializeShadowTextures();

            Assert.That(manager.CustomTextures, Is.TypeOf<RenderTexture>());
            Assert.That(manager.ShadowTextures, Is.TypeOf<RenderTexture>());
            Assert.That(manager.CustomTextures.hideFlags, Is.EqualTo(HideFlags.HideAndDontSave));
            Assert.That(manager.ShadowTextures.hideFlags, Is.EqualTo(HideFlags.HideAndDontSave));
        }

        // Verifies setup sync can restore manager volume instances even when serialized LightVolumeDataList is missing.
        [Test]
        public void SetupSyncUsesAuthoringVolumesWhenDataListIsEmpty() {
            GameObject setupObject = CreateGameObject("Empty Data List Setup", false);
            LightVolumeSetup setup = setupObject.AddComponent<LightVolumeSetup>();
            setup.SetupDependencies();
            LightVolumeManager manager = setup.LightVolumeManager;
            if (manager == null) manager = setupObject.GetComponent<LightVolumeManager>();
            Texture3D atlas = CreateAtlas("Empty Data List Atlas");
            manager.LightVolumeAtlas = atlas;
            manager.LightVolumeAtlasBase = atlas;

            LightVolumeInstance regularInstance = CreateLightVolume(setup, manager, "Regular Volume", false);
            LightVolumeInstance additiveInstance = CreateLightVolume(setup, manager, "Additive Volume", true);
            setup.LightVolumesWeights.Add(10);
            setup.LightVolumesWeights.Add(0);
            setup.LightVolumeDataList.Clear();

            setup.SyncUdonScript();

            Assert.That(manager.LightVolumeInstances, Has.Length.EqualTo(2));
            Assert.That(manager.LightVolumeInstances[0], Is.SameAs(additiveInstance));
            Assert.That(manager.LightVolumeInstances[1], Is.SameAs(regularInstance));
        }

        // Verifies setup sync restores stale runtime additive flags before sorting manager instances.
        [Test]
        public void SetupSyncCopiesAuthoringAdditiveBeforeSortingInstances() {
            GameObject setupObject = CreateGameObject("Stale Additive Setup", false);
            LightVolumeSetup setup = setupObject.AddComponent<LightVolumeSetup>();
            setup.SetupDependencies();
            LightVolumeManager manager = setup.LightVolumeManager;
            if (manager == null) manager = setupObject.GetComponent<LightVolumeManager>();
            Texture3D atlas = CreateAtlas("Stale Additive Atlas");
            manager.LightVolumeAtlas = atlas;
            manager.LightVolumeAtlasBase = atlas;

            LightVolumeInstance regularInstance = CreateLightVolume(setup, manager, "Regular Volume", false);
            LightVolumeInstance additiveInstance = CreateLightVolume(setup, manager, "Additive Volume", true);
            additiveInstance.IsAdditive = false;
            setup.LightVolumesWeights.Add(10);
            setup.LightVolumesWeights.Add(0);
            setup.LightVolumeDataList.Clear();

            setup.SyncUdonScript();

            Assert.That(additiveInstance.IsAdditive, Is.True);
            Assert.That(manager.LightVolumeInstances, Has.Length.EqualTo(2));
            Assert.That(manager.LightVolumeInstances[0], Is.SameAs(additiveInstance));
            Assert.That(manager.LightVolumeInstances[1], Is.SameAs(regularInstance));
        }

        // Verifies reserved UV space creates unique atlas islands filled with neutral SH data.
        [Test]
        public void ReservedUVSpaceCreatesUniqueWhiteAtlasIslands() {
            LightVolume first = CreateReservedLightVolume("Reserved UV Space A");
            LightVolume second = CreateReservedLightVolume("Reserved UV Space B");
            Atlas3D result = new Atlas3D();

            IEnumerator routine = Texture3DAtlasGenerator.CreateAtlas(new[] { first, second }, atlas => {
                result = atlas;
                _createdObjects.Add(atlas.Texture);
            });
            RunEnumerator(routine);

            Assert.That(result.Texture, Is.Not.Null);
            Assert.That(result.BoundsUvwMin, Has.Length.EqualTo(6));
            Assert.That(BoundsDiffer(result.BoundsUvwMin[0], result.BoundsUvwMin[1]), Is.True);
            Assert.That(BoundsDiffer(result.BoundsUvwMin[1], result.BoundsUvwMin[2]), Is.True);
            Assert.That(BoundsDiffer(result.BoundsUvwMin[0], result.BoundsUvwMin[3]), Is.True);
            AssertColorClose(new Color(1, 1, 1, 0), SampleAtlasPixel(result.Texture, result.BoundsUvwMin[0]));
            AssertColorClose(Color.clear, SampleAtlasPixel(result.Texture, result.BoundsUvwMin[1]));
            AssertColorClose(Color.clear, SampleAtlasPixel(result.Texture, result.BoundsUvwMin[2]));
        }

        // Creates a manager with deterministic defaults.
        private LightVolumeManager CreateManager(string name, bool withAtlas) {
            GameObject gameObject = CreateGameObject(name, false);
            LightVolumeManager manager = gameObject.AddComponent<LightVolumeManager>();
            manager.LightVolumeAtlas = withAtlas ? CreateAtlas("Editor Test Light Volume Atlas") : null;
            manager.LightVolumeInstances = new LightVolumeInstance[0];
            manager.PointLightVolumeInstances = new PointLightVolumeInstance[0];
            gameObject.SetActive(true);
            return manager;
        }

        // Creates a scene point light volume instance and optionally lets Unity call OnEnable.
        private PointLightVolumeInstance CreatePointLight(LightVolumeManager manager, string name, bool active) {
            GameObject gameObject = CreateGameObject(name, false);
            PointLightVolumeInstance point = gameObject.AddComponent<PointLightVolumeInstance>();
            point.LightVolumeManager = manager;
            point.Color = Color.white;
            point.Intensity = 1;
            point.IsDynamic = true;
            point.LightSourceSize = 1;
            point.InverseSquaredRange = 1;
            point.Direction = Vector3.forward;
            point.ConeFalloff = 1;
            point.Angle = 30 * Mathf.Deg2Rad;
            point.OuterAngleCos = Mathf.Cos(point.Angle);
            gameObject.SetActive(active);
            if (active && manager != null) manager.InitializePointLightVolume(point);
            return point;
        }

        // Creates a scene Light Volume authoring/runtime pair.
        private LightVolumeInstance CreateLightVolume(LightVolumeSetup setup, LightVolumeManager manager, string name, bool additive) {
            GameObject gameObject = CreateGameObject(name, false);
            LightVolumeInstance instance = gameObject.AddComponent<LightVolumeInstance>();
            LightVolume volume = gameObject.AddComponent<LightVolume>();
            volume.LightVolumeSetup = setup;
            volume.LightVolumeInstance = instance;
            volume.Intensity = 1;
            volume.Color = Color.white;
            volume.Additive = additive;
            instance.LightVolumeManager = manager;
            instance.Intensity = 1;
            instance.Color = Color.white;
            instance.IsAdditive = additive;
            setup.LightVolumes.Add(volume);
            return instance;
        }

        // Creates a Light Volume configured to reserve atlas space instead of using baked textures.
        private LightVolume CreateReservedLightVolume(string name) {
            GameObject gameObject = CreateGameObject(name, false);
            LightVolume volume = gameObject.AddComponent<LightVolume>();
            volume.Bake = false;
            volume.ReserveUVSpace = true;
            volume.Resolution = new Vector3Int(2, 2, 2);
            volume.Exposure = 2;
            volume.Highlights = 1;
            volume.Shadows = -1;
            return volume;
        }

        // Runs a simple iterator-based editor coroutine to completion in a synchronous test.
        private static void RunEnumerator(IEnumerator routine) {
            int guard = 10000;
            while (routine.MoveNext()) {
                guard--;
                if (guard < 0) Assert.Fail("Atlas generation coroutine did not finish.");
            }
        }

        // Samples the first voxel inside a packed atlas island.
        private static Color SampleAtlasPixel(Texture3D atlas, Vector3 boundsMin) {
            int x = Mathf.Clamp(Mathf.RoundToInt(boundsMin.x * atlas.width), 0, atlas.width - 1);
            int y = Mathf.Clamp(Mathf.RoundToInt(boundsMin.y * atlas.height), 0, atlas.height - 1);
            int z = Mathf.Clamp(Mathf.RoundToInt(boundsMin.z * atlas.depth), 0, atlas.depth - 1);
            Color[] pixels = atlas.GetPixels();
            return pixels[x + y * atlas.width + z * atlas.width * atlas.height];
        }

        // Checks whether two atlas bounds point to different islands.
        private static bool BoundsDiffer(Vector3 a, Vector3 b) {
            return Mathf.Abs(a.x - b.x) > Epsilon || Mathf.Abs(a.y - b.y) > Epsilon || Mathf.Abs(a.z - b.z) > Epsilon;
        }

        // Asserts colors with the shared editor-test tolerance.
        private static void AssertColorClose(Color expected, Color actual) {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(Epsilon));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(Epsilon));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(Epsilon));
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(Epsilon));
        }

        // Creates a temporary GameObject tracked by teardown.
        private GameObject CreateGameObject(string name, bool active) {
            GameObject gameObject = new GameObject(name);
            _createdObjects.Add(gameObject);
            gameObject.SetActive(active);
            return gameObject;
        }

        // Creates a temporary 3D atlas texture tracked by teardown.
        private Texture3D CreateAtlas(string name) {
            Texture3D texture = new Texture3D(1, 1, 1, TextureFormat.RGBA32, false);
            texture.name = name;
            _createdObjects.Add(texture);
            return texture;
        }

        // Creates a temporary 2D texture for authoring sync checks.
        private Texture2D CreateTexture2D(string name) {
            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.name = name;
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

        // Creates a temporary material tracked by teardown.
        private Material CreateMaterial(string shaderName) {
            Shader shader = Shader.Find(shaderName);
            Assert.That(shader, Is.Not.Null, shaderName + " shader was not found");
            Material material = new Material(shader);
            material.name = "Editor Test Material";
            _createdObjects.Add(material);
            return material;
        }

        // Destroys test objects immediately and releases render textures first.
        private static void DestroyTestObject(UnityEngine.Object target) {
            if (target == null) return;
            RenderTexture renderTexture = target as RenderTexture;
            if (renderTexture != null) renderTexture.Release();
            Object.DestroyImmediate(target);
        }

        // Returns a private LightVolumeManager field used by focused regression tests.
        private static T GetManagerField<T>(LightVolumeManager manager, FieldInfo field) {
            return (T)field.GetValue(manager);
        }
    }
}
