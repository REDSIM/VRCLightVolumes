using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace VRCLightVolumes.Tests {
    [Category("Udon")]
    public class LightVolumeUdonEditorTests {
        private const float Epsilon = 0.0001f;
        private const string CustomRenderTextureInfoProperty = "_CustomRenderTextureInfo";

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
        private static readonly int _pointLightCustomIdID = Shader.PropertyToID("_UdonPointLightVolumeCustomID");
        private static readonly int _pointLightCountID = Shader.PropertyToID("_UdonPointLightVolumeCount");
        private static readonly int _pointLightCubeCountID = Shader.PropertyToID("_UdonPointLightVolumeCubeCount");
        private static readonly int _pointLightTextureID = Shader.PropertyToID("_UdonPointLightVolumeTexture");
        private static readonly int _pointLightShadowReprojectionDataID = Shader.PropertyToID("_UdonPointLightVolumeShadowReprojectionData");
        private static readonly int _pointLightShadowRotationDataID = Shader.PropertyToID("_UdonPointLightVolumeShadowRotationData");
        private static readonly int _pointLightShadowCubeCountID = Shader.PropertyToID("_UdonPointLightVolumeShadowCubeCount");
        private static readonly int _pointLightShadowCountID = Shader.PropertyToID("_UdonPointLightVolumeShadowCount");
        private static readonly int _pointLightShadowTextureID = Shader.PropertyToID("_UdonPointLightVolumeShadowTexture");
        private static readonly int _lightBrightnessCutoffID = Shader.PropertyToID("_UdonLightBrightnessCutoff");
        private static readonly BindingFlags _lifecycleMethodFlags = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo _customTexturesDepthField = typeof(LightVolumeManager).GetField("_customTextureArrayDepth", _lifecycleMethodFlags);
        private static readonly FieldInfo _shadowTexturesDepthField = typeof(LightVolumeManager).GetField("_shadowTextureArrayDepth", _lifecycleMethodFlags);
        private static readonly FieldInfo _customCubemapTextureCountField = typeof(LightVolumeManager).GetField("_customCubemapTextureCount", _lifecycleMethodFlags);
        private static readonly FieldInfo _customSingleTextureCountField = typeof(LightVolumeManager).GetField("_customSingleTextureCount", _lifecycleMethodFlags);
        private static readonly FieldInfo _shadowCubemapTextureCountField = typeof(LightVolumeManager).GetField("_shadowCubemapTextureCount", _lifecycleMethodFlags);
        private static readonly FieldInfo _shadowSingleTextureCountField = typeof(LightVolumeManager).GetField("_shadowSingleTextureCount", _lifecycleMethodFlags);
        private static readonly FieldInfo _pointLightCustomIDsField = typeof(LightVolumeManager).GetField("_pointLightCustomIDs", _lifecycleMethodFlags);
        private static readonly FieldInfo _pointLightShadowIDsField = typeof(LightVolumeManager).GetField("_pointLightShadowIDs", _lifecycleMethodFlags);
        private static readonly FieldInfo _pointLightArraysDirtyField = typeof(LightVolumeManager).GetField("_pointLightArraysDirty", _lifecycleMethodFlags);

        private readonly List<UnityEngine.Object> _createdObjects = new List<UnityEngine.Object>();

        // Resets process-wide shader globals before every test case.
        [SetUp]
        public void SetUp() {
            ResetShaderGlobals();
        }

        // Verifies 2.x packed point light runtime fields migrate into the explicit 3.x fields.
        [Test]
        public void LegacyPackedPointLightDataMigratesToExplicitFields() {
            GameObject gameObject = CreateGameObject("Legacy Point Migration", false);
            PointLightVolumeInstance point = gameObject.AddComponent<PointLightVolumeInstance>();
            point.Angle = 0.5f;
            SetPrivateField(point, "_legacyPositionData", new Vector4(1, 2, 3, -0.25f));
            SetPrivateField(point, "_legacyDirectionData", new Vector4(0, 0, 1, 2));
            SetPrivateField(point, "_legacyCustomID", 0f);
            SetPrivateField(point, "_legacyAngleData", Mathf.Cos(0.5f));
            SetPrivateField(point, "_legacyPackedDataMigrated", false);

            Assert.That(point.MigrateLegacyPackedData(), Is.True);
            Assert.That(point.LightType, Is.EqualTo(1));
            Assert.That(point.ProjectionMode, Is.EqualTo(0));
            AssertVectorClose(new Vector4(1, 2, 3, 0), point.Position);
            Assert.That(point.LightSourceSize, Is.EqualTo(0.5f).Within(Epsilon));
            AssertVectorClose(new Vector4(0, 0, 1, 0), point.Direction);
            Assert.That(point.ConeFalloff, Is.EqualTo(2).Within(Epsilon));
            Assert.That(point.OuterAngleCos, Is.EqualTo(Mathf.Cos(0.5f)).Within(Epsilon));
            Assert.That(point.ShadowMapID, Is.EqualTo(-1).Within(Epsilon));
            Assert.That(point.IsRangeDirty, Is.True);
            Assert.That(GetPrivateField<Vector4>(point, "_legacyPositionData"), Is.EqualTo(new Vector4(1, 2, 3, -0.25f)));
            Assert.That(GetPrivateField<float>(point, "_legacyCustomID"), Is.EqualTo(0f));
            Assert.That(IsPrivateFieldSerialized<PointLightVolumeInstance>("_legacyPackedDataMigrated"), Is.True);
            Assert.That(point.ConsumeLegacyPackedDataMigrationDirty(), Is.True);
            Assert.That(point.ConsumeLegacyPackedDataMigrationDirty(), Is.False);
            point.Position = new Vector3(9, 9, 9);
            Assert.That(point.MigrateLegacyPackedData(), Is.False);
            AssertVectorClose(new Vector4(9, 9, 9, 0), point.Position);
        }

        // Verifies 2.x regular volume fallback fields restore rotation rows and compact bounds scale.
        [Test]
        public void LegacyLightVolumeDataMigratesRotationAndBoundsScale() {
            GameObject gameObject = CreateGameObject("Legacy Volume Migration", false);
            LightVolumeInstance volume = gameObject.AddComponent<LightVolumeInstance>();
            Quaternion rotation = Quaternion.Euler(0, 90, 0);
            SetPrivateField(volume, "_legacyRelativeRotation", new Vector4(rotation.x, rotation.y, rotation.z, rotation.w));
            volume.BoundsUvwMin0 = new Vector4(0.1f, 0.2f, 0.3f, 0);
            volume.BoundsUvwMin1 = new Vector4(0.2f, 0.3f, 0.4f, 0);
            volume.BoundsUvwMin2 = new Vector4(0.3f, 0.4f, 0.5f, 0);
            SetPrivateField(volume, "_legacyBoundsUvwMax0", new Vector4(0.6f, 0.2f, 0.3f, 0));
            SetPrivateField(volume, "_legacyBoundsUvwMax1", new Vector4(0.2f, 0.8f, 0.4f, 0));
            SetPrivateField(volume, "_legacyBoundsUvwMax2", new Vector4(0.3f, 0.4f, 1.2f, 0));
            SetPrivateField(volume, "_legacyVolumeDataMigrated", false);

            Assert.That(volume.MigrateLegacyVolumeData(), Is.True);
            Matrix4x4 expectedRotation = Matrix4x4.Rotate(rotation);
            AssertVectorClose(expectedRotation.GetRow(0), volume.RelativeRotationRow0);
            AssertVectorClose(expectedRotation.GetRow(1), volume.RelativeRotationRow1);
            Assert.That(volume.IsRotated, Is.True);
            Assert.That(volume.BoundsUvwMin0.w, Is.EqualTo(0.5f).Within(Epsilon));
            Assert.That(volume.BoundsUvwMin1.w, Is.EqualTo(0.5f).Within(Epsilon));
            Assert.That(volume.BoundsUvwMin2.w, Is.EqualTo(0.7f).Within(Epsilon));
            Assert.That(GetPrivateField<Vector4>(volume, "_legacyBoundsUvwMax0"), Is.EqualTo(new Vector4(0.6f, 0.2f, 0.3f, 0)));
            Assert.That(IsPrivateFieldSerialized<LightVolumeInstance>("_legacyVolumeDataMigrated"), Is.True);
            Assert.That(volume.ConsumeLegacyVolumeDataMigrationDirty(), Is.True);
            Assert.That(volume.ConsumeLegacyVolumeDataMigrationDirty(), Is.False);
            volume.BoundsUvwMin0.w = 0.25f;
            Assert.That(volume.MigrateLegacyVolumeData(), Is.False);
            Assert.That(volume.BoundsUvwMin0.w, Is.EqualTo(0.25f).Within(Epsilon));
        }

        // Destroys all temporary scene and texture objects created by a test case.
        [TearDown]
        public void TearDown() {
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

        // Assigns a private serialized migration field used by focused regression tests.
        private static void SetPrivateField<T>(object target, string fieldName, T value) {
            FieldInfo field = target.GetType().GetField(fieldName, _lifecycleMethodFlags);
            Assert.That(field, Is.Not.Null, fieldName + " field was not found on " + target.GetType().Name);
            field.SetValue(target, value);
        }

        // Reads a private serialized migration field used by focused regression tests.
        private static T GetPrivateField<T>(object target, string fieldName) {
            FieldInfo field = target.GetType().GetField(fieldName, _lifecycleMethodFlags);
            Assert.That(field, Is.Not.Null, fieldName + " field was not found on " + target.GetType().Name);
            return (T)field.GetValue(target);
        }

        // Returns true when a private field is persisted by Unity serialization.
        private static bool IsPrivateFieldSerialized<T>(string fieldName) {
            FieldInfo field = typeof(T).GetField(fieldName, _lifecycleMethodFlags);
            Assert.That(field, Is.Not.Null, fieldName + " field was not found on " + typeof(T).Name);
            return field.GetCustomAttribute<SerializeField>() != null;
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
            Vector4[] legacyUvw = Shader.GetGlobalVectorArray(_lightVolumeUvwID);
            AssertVectorClose(ExpectedLegacyLightVolumeUvw(second, 0, false), legacyUvw[0]);
            AssertVectorClose(ExpectedLegacyLightVolumeUvw(second, 0, true), legacyUvw[1]);
            AssertVectorClose(ExpectedLegacyLightVolumeUvw(second, 1, false), legacyUvw[2]);
            AssertVectorClose(ExpectedLegacyLightVolumeUvw(second, 1, true), legacyUvw[3]);
            AssertVectorClose(ExpectedLegacyLightVolumeUvw(second, 2, false), legacyUvw[4]);
            AssertVectorClose(ExpectedLegacyLightVolumeUvw(second, 2, true), legacyUvw[5]);
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
            AssertVectorClose(new Vector4(5, 6, 7, 0), Shader.GetGlobalVectorArray(_pointLightShadowReprojectionDataID)[0]);
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
            AssertVectorClose(new Vector4(5, 6, 7, 0), Shader.GetGlobalVectorArray(_pointLightShadowReprojectionDataID)[0]);
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
            AssertPointCustomData(point, -1, 0);
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
        public void ShadowRuntimeArrayUsesDefaultEVSMFloatPrecision() {
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

        // Verifies half precision shadows use a half texture array with the shared EVSM shader path.
        [Test]
        public void ShadowRuntimeArrayUsesConfiguredEVSMHalfFormat() {
            LightVolumeManager manager = CreateManager("Shadow Half Format Manager", false);
            Cubemap source = CreateCubemap("Shadow Half Format Source");
            manager.ShadowTextureFormat = 0;
            manager.ShadowTexturesWidth = 4;
            manager.ShadowTexturesHeight = 4;

            PointLightVolumeInstance point = CreatePointLight(manager, "Shadow Half Format Light", true);
            point.WorldSpaceShadows = true;
            point.Bias = 0;
            ConfigureShadowTexture(point, source, false, true, false);
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();

            Assert.That(manager.ShadowTextures, Is.Not.Null);
            Assert.That(manager.ShadowTextures.format, Is.EqualTo(RenderTextureFormat.ARGBHalf));
            Assert.That(manager.ShadowTextures.useMipMap, Is.False);
            Assert.That(manager.ShadowTextures.autoGenerateMips, Is.False);
            AssertPointCustomData(point, 0, 1);
        }

        // Verifies realtime EVSM baking uses the target light far clip data.
        [Test]
        public void RuntimeShadowBakerUsesTargetRangeForEVSMFarClip() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Far Clip Manager", false);
            PointLightVolumeInstance point = CreatePointLight(manager, "Runtime Shadow Far Clip Light", true);
            point.SquaredRange = 64;

            GameObject bakerObject = CreateGameObject("Runtime Shadow Far Clip Baker", true);
            PointLightShadowRuntimeBaker baker = bakerObject.AddComponent<PointLightShadowRuntimeBaker>();
            baker.TargetPointLightVolume = point;
            AddRuntimeShadowCamera(baker);

            MethodInfo cacheMethod = typeof(PointLightShadowRuntimeBaker).GetMethod("CacheRuntimeReferences", _lifecycleMethodFlags);
            MethodInfo refreshSettingsMethod = typeof(PointLightShadowRuntimeBaker).GetMethod("RefreshBakeSettings", _lifecycleMethodFlags);
            FieldInfo bakeFarClipField = typeof(PointLightShadowRuntimeBaker).GetField("_bakeFarClip", _lifecycleMethodFlags);
            Assert.That(cacheMethod, Is.Not.Null);
            Assert.That(refreshSettingsMethod, Is.Not.Null);
            Assert.That(bakeFarClipField, Is.Not.Null);

            cacheMethod.Invoke(baker, null);
            point.SquaredRange = 64;
            point.IsRangeDirty = false;
            refreshSettingsMethod.Invoke(baker, null);
            Assert.That((float)bakeFarClipField.GetValue(baker), Is.EqualTo(8).Within(Epsilon));

            point.SquaredRange = 4;
            point.IsRangeDirty = false;

            refreshSettingsMethod.Invoke(baker, null);
            Assert.That((float)bakeFarClipField.GetValue(baker), Is.EqualTo(2).Within(Epsilon));

            point.FarClip = 3;

            refreshSettingsMethod.Invoke(baker, null);
            Assert.That((float)bakeFarClipField.GetValue(baker), Is.EqualTo(3).Within(Epsilon));
        }

        // Verifies runtime-published FarClip remains metadata and does not become a stale manual override.
        [Test]
        public void RuntimeShadowBakerRefreshesPublishedFarClipAfterRangeChanges() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Published Far Clip Manager", false);
            PointLightVolumeInstance point = CreatePointLight(manager, "Runtime Shadow Published Far Clip Light", true);
            point.SquaredRange = 64;
            point.FarClip = 0;

            GameObject bakerObject = CreateGameObject("Runtime Shadow Published Far Clip Baker", true);
            PointLightShadowRuntimeBaker baker = bakerObject.AddComponent<PointLightShadowRuntimeBaker>();
            baker.TargetPointLightVolume = point;
            AddRuntimeShadowCamera(baker);

            MethodInfo cacheMethod = typeof(PointLightShadowRuntimeBaker).GetMethod("CacheRuntimeReferences", _lifecycleMethodFlags);
            MethodInfo refreshSettingsMethod = typeof(PointLightShadowRuntimeBaker).GetMethod("RefreshBakeSettings", _lifecycleMethodFlags);
            MethodInfo applyMethod = typeof(PointLightShadowRuntimeBaker).GetMethod("ApplyTargetShadowSourceInternal", _lifecycleMethodFlags);
            FieldInfo bakeFarClipField = typeof(PointLightShadowRuntimeBaker).GetField("_bakeFarClip", _lifecycleMethodFlags);
            Assert.That(cacheMethod, Is.Not.Null);
            Assert.That(refreshSettingsMethod, Is.Not.Null);
            Assert.That(applyMethod, Is.Not.Null);
            Assert.That(bakeFarClipField, Is.Not.Null);

            cacheMethod.Invoke(baker, null);
            point.SquaredRange = 64;
            point.IsRangeDirty = false;
            refreshSettingsMethod.Invoke(baker, null);
            float firstFarClip = (float)bakeFarClipField.GetValue(baker);
            Assert.That(firstFarClip, Is.EqualTo(8).Within(Epsilon));
            Assert.That((bool)applyMethod.Invoke(baker, new object[] { Vector3.zero, firstFarClip, 0.1f, false }), Is.True);
            Assert.That(point.FarClip, Is.EqualTo(8).Within(Epsilon));

            point.SquaredRange = 4;
            point.IsRangeDirty = false;
            refreshSettingsMethod.Invoke(baker, null);
            float secondFarClip = (float)bakeFarClipField.GetValue(baker);
            Assert.That(secondFarClip, Is.EqualTo(2).Within(Epsilon));
            Assert.That((bool)applyMethod.Invoke(baker, new object[] { Vector3.zero, secondFarClip, 0.1f, false }), Is.True);
            Assert.That(point.FarClip, Is.EqualTo(2).Within(Epsilon));
        }

        // Verifies realtime EVSM baking uses the target light bias so it matches editor shadow bakes.
        [Test]
        public void RuntimeShadowBakerUsesTargetBakeBias() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Bias Manager", false);
            PointLightVolumeInstance point = CreatePointLight(manager, "Runtime Shadow Bias Light", true);
            point.Bias = 0;

            GameObject bakerObject = CreateGameObject("Runtime Shadow Bias Baker", true);
            PointLightShadowRuntimeBaker baker = bakerObject.AddComponent<PointLightShadowRuntimeBaker>();
            baker.TargetPointLightVolume = point;
            AddRuntimeShadowCamera(baker);

            MethodInfo cacheMethod = typeof(PointLightShadowRuntimeBaker).GetMethod("CacheRuntimeReferences", _lifecycleMethodFlags);
            MethodInfo refreshSettingsMethod = typeof(PointLightShadowRuntimeBaker).GetMethod("RefreshBakeSettings", _lifecycleMethodFlags);
            FieldInfo bakeBiasField = typeof(PointLightShadowRuntimeBaker).GetField("_bakeBias", _lifecycleMethodFlags);
            Assert.That(cacheMethod, Is.Not.Null);
            Assert.That(refreshSettingsMethod, Is.Not.Null);
            Assert.That(bakeBiasField, Is.Not.Null);

            cacheMethod.Invoke(baker, null);
            refreshSettingsMethod.Invoke(baker, null);
            Assert.That((float)bakeBiasField.GetValue(baker), Is.EqualTo(0).Within(Epsilon));

            point.Bias = 0.125f;

            refreshSettingsMethod.Invoke(baker, null);
            Assert.That((float)bakeBiasField.GetValue(baker), Is.EqualTo(0.125f).Within(Epsilon));
        }

        // Verifies realtime EVSM baking reads camera and blur settings from the target light instance.
        [Test]
        public void RuntimeShadowBakerUsesTargetBakeSettings() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Settings Manager", false);
            PointLightVolumeInstance point = CreatePointLight(manager, "Runtime Shadow Settings Light", true);
            point.NearClip = 0.25f;
            point.LayerMask = 1 << 7;
            point.Blur = 6.5f;
            point.ContactHardening = 0.35f;

            GameObject bakerObject = CreateGameObject("Runtime Shadow Settings Baker", true);
            PointLightShadowRuntimeBaker baker = bakerObject.AddComponent<PointLightShadowRuntimeBaker>();
            baker.TargetPointLightVolume = point;
            AddRuntimeShadowCamera(baker);

            MethodInfo cacheMethod = typeof(PointLightShadowRuntimeBaker).GetMethod("CacheRuntimeReferences", _lifecycleMethodFlags);
            MethodInfo refreshSettingsMethod = typeof(PointLightShadowRuntimeBaker).GetMethod("RefreshBakeSettings", _lifecycleMethodFlags);
            FieldInfo bakeNearClipField = typeof(PointLightShadowRuntimeBaker).GetField("_bakeNearClip", _lifecycleMethodFlags);
            FieldInfo bakeCullingMaskField = typeof(PointLightShadowRuntimeBaker).GetField("_bakeCullingMask", _lifecycleMethodFlags);
            FieldInfo bakeBlurField = typeof(PointLightShadowRuntimeBaker).GetField("_bakeBlur", _lifecycleMethodFlags);
            FieldInfo bakeBlurDepthField = typeof(PointLightShadowRuntimeBaker).GetField("_bakeBlurDepth", _lifecycleMethodFlags);
            Assert.That(cacheMethod, Is.Not.Null);
            Assert.That(refreshSettingsMethod, Is.Not.Null);
            Assert.That(bakeNearClipField, Is.Not.Null);
            Assert.That(bakeCullingMaskField, Is.Not.Null);
            Assert.That(bakeBlurField, Is.Not.Null);
            Assert.That(bakeBlurDepthField, Is.Not.Null);

            cacheMethod.Invoke(baker, null);
            refreshSettingsMethod.Invoke(baker, null);

            Assert.That((float)bakeNearClipField.GetValue(baker), Is.EqualTo(0.25f).Within(Epsilon));
            Assert.That((int)bakeCullingMaskField.GetValue(baker), Is.EqualTo(1 << 7));
            Assert.That((float)bakeBlurField.GetValue(baker), Is.EqualTo(6.5f).Within(Epsilon));
            Assert.That((float)bakeBlurDepthField.GetValue(baker), Is.EqualTo(0.35f).Within(Epsilon));
        }

        // Verifies realtime baking keeps the baker resolution separate from the manager-owned final array size.
        [Test]
        public void RuntimeShadowBakerResolutionDoesNotOverrideManagerArraySize() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Resolution Manager", false);
            manager.ShadowTexturesWidth = 32;
            manager.ShadowTexturesHeight = 32;
            PointLightVolumeInstance point = CreatePointLight(manager, "Runtime Shadow Resolution Light", true);

            GameObject bakerObject = CreateGameObject("Runtime Shadow Resolution Baker", true);
            PointLightShadowRuntimeBaker baker = bakerObject.AddComponent<PointLightShadowRuntimeBaker>();
            baker.TargetPointLightVolume = point;
            baker.Realtime = true;
            baker.Resolution = 96;
            AddRuntimeShadowCamera(baker);

            MethodInfo cacheMethod = typeof(PointLightShadowRuntimeBaker).GetMethod("CacheRuntimeReferences", _lifecycleMethodFlags);
            MethodInfo refreshSettingsMethod = typeof(PointLightShadowRuntimeBaker).GetMethod("RefreshBakeSettings", _lifecycleMethodFlags);
            FieldInfo bakeResolutionField = typeof(PointLightShadowRuntimeBaker).GetField("_bakeResolution", _lifecycleMethodFlags);
            FieldInfo useDirectOutputField = typeof(PointLightShadowRuntimeBaker).GetField("_useDirectOutput", _lifecycleMethodFlags);
            Assert.That(cacheMethod, Is.Not.Null);
            Assert.That(refreshSettingsMethod, Is.Not.Null);
            Assert.That(bakeResolutionField, Is.Not.Null);
            Assert.That(useDirectOutputField, Is.Not.Null);

            cacheMethod.Invoke(baker, null);
            refreshSettingsMethod.Invoke(baker, null);

            Assert.That((int)bakeResolutionField.GetValue(baker), Is.EqualTo(96));
            Assert.That((bool)useDirectOutputField.GetValue(baker), Is.False);
            Assert.That(manager.ShadowTexturesWidth, Is.EqualTo(32));
            Assert.That(manager.ShadowTexturesHeight, Is.EqualTo(32));
        }

        // Verifies runtime spot shadow baking uses one texture slice when the target is in single-shadow mode.
        [Test]
        public void RuntimeShadowBakerPreparesSingleTextureSpotShadowMode() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Single Spot Manager", false);
            PointLightVolumeInstance spot = CreatePointLight(manager, "Runtime Shadow Single Spot", true);
            spot.SetSpotLight(60, 0.5f);
            spot.ShadowMapUsesCubemap = false;
            manager.PointLightVolumeInstances = new[] { spot };

            GameObject bakerObject = CreateGameObject("Runtime Shadow Single Spot Baker", true);
            PointLightShadowRuntimeBaker baker = bakerObject.AddComponent<PointLightShadowRuntimeBaker>();
            baker.TargetPointLightVolume = spot;
            baker.Resolution = 16;
            baker.RuntimeShadowDepthEncodeMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowDepthEncode");
            AddRuntimeShadowCamera(baker);

            MethodInfo prepareBakeMethod = typeof(PointLightShadowRuntimeBaker).GetMethod("PrepareBake", _lifecycleMethodFlags);
            MethodInfo applyMethod = typeof(PointLightShadowRuntimeBaker).GetMethod("ApplyTargetShadowSourceInternal", _lifecycleMethodFlags);
            FieldInfo shadowTextureField = typeof(PointLightShadowRuntimeBaker).GetField("_shadowTexture", _lifecycleMethodFlags);
            FieldInfo useCubemapShadowField = typeof(PointLightShadowRuntimeBaker).GetField("_useCubemapShadow", _lifecycleMethodFlags);
            FieldInfo bakeSliceCountField = typeof(PointLightShadowRuntimeBaker).GetField("_bakeSliceCount", _lifecycleMethodFlags);
            FieldInfo bakeFieldOfViewField = typeof(PointLightShadowRuntimeBaker).GetField("_bakeFieldOfView", _lifecycleMethodFlags);
            FieldInfo bakeTanHalfFovField = typeof(PointLightShadowRuntimeBaker).GetField("_bakeTanHalfFov", _lifecycleMethodFlags);
            Assert.That(prepareBakeMethod, Is.Not.Null);
            Assert.That(applyMethod, Is.Not.Null);
            Assert.That(shadowTextureField, Is.Not.Null);
            Assert.That(useCubemapShadowField, Is.Not.Null);
            Assert.That(bakeSliceCountField, Is.Not.Null);
            Assert.That(bakeFieldOfViewField, Is.Not.Null);
            Assert.That(bakeTanHalfFovField, Is.Not.Null);

            Assert.That((bool)prepareBakeMethod.Invoke(baker, null), Is.True);

            RenderTexture shadowTexture = (RenderTexture)shadowTextureField.GetValue(baker);
            Assert.That(shadowTexture, Is.Not.Null);
            Assert.That(shadowTexture.dimension, Is.EqualTo(TextureDimension.Tex2DArray));
            Assert.That(shadowTexture.volumeDepth, Is.EqualTo(1));
            Assert.That((bool)useCubemapShadowField.GetValue(baker), Is.False);
            Assert.That((int)bakeSliceCountField.GetValue(baker), Is.EqualTo(1));
            Assert.That((float)bakeFieldOfViewField.GetValue(baker), Is.EqualTo(60).Within(Epsilon));
            Assert.That((float)bakeTanHalfFovField.GetValue(baker), Is.EqualTo(Mathf.Tan(30f * Mathf.Deg2Rad)).Within(Epsilon));

            Assert.That((bool)applyMethod.Invoke(baker, new object[] { Vector3.zero, 8f, 0.1f, false }), Is.True);
            Assert.That(spot.ShadowMapTexture, Is.SameAs(shadowTexture));
            Assert.That(spot.ShadowMapUsesCubemap, Is.False);
            Assert.That(spot.ShadowMapTextureHasDepthSlices, Is.False);
        }

        // Verifies realtime EVSM baking reports metadata changes so manager globals can refresh after the first bake.
        [Test]
        public void RuntimeShadowBakerDetectsRealtimeShadowMetadataChanges() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Metadata Manager", false);
            PointLightVolumeInstance point = CreatePointLight(manager, "Runtime Shadow Metadata Light", true);
            point.WorldSpaceShadows = true;
            RenderTexture source = CreateRenderTexture("Runtime Shadow Metadata Source", 4, 4, 1, TextureDimension.Cube);

            GameObject bakerObject = CreateGameObject("Runtime Shadow Metadata Baker", true);
            PointLightShadowRuntimeBaker baker = bakerObject.AddComponent<PointLightShadowRuntimeBaker>();
            baker.TargetPointLightVolume = point;
            baker.Realtime = true;
            AddRuntimeShadowCamera(baker);

            MethodInfo cacheMethod = typeof(PointLightShadowRuntimeBaker).GetMethod("CacheRuntimeReferences", _lifecycleMethodFlags);
            MethodInfo refreshSettingsMethod = typeof(PointLightShadowRuntimeBaker).GetMethod("RefreshBakeSettings", _lifecycleMethodFlags);
            FieldInfo shadowMapTextureField = typeof(PointLightShadowRuntimeBaker).GetField("_shadowTexture", _lifecycleMethodFlags);
            MethodInfo applyMethod = typeof(PointLightShadowRuntimeBaker).GetMethod("ApplyTargetShadowSourceInternal", _lifecycleMethodFlags);
            Assert.That(cacheMethod, Is.Not.Null);
            Assert.That(refreshSettingsMethod, Is.Not.Null);
            Assert.That(shadowMapTextureField, Is.Not.Null);
            Assert.That(applyMethod, Is.Not.Null);
            cacheMethod.Invoke(baker, null);
            refreshSettingsMethod.Invoke(baker, null);
            shadowMapTextureField.SetValue(baker, source);

            Vector3 bakePosition = new Vector3(1, 2, 3);
            Assert.That((bool)applyMethod.Invoke(baker, new object[] { bakePosition, 12f, 0.25f, false }), Is.True);
            Assert.That(point.FarClip, Is.EqualTo(12f).Within(Epsilon));
            Assert.That(point.Bias, Is.EqualTo(0.25f).Within(Epsilon));
            Assert.That(point.AutoUpdateShadowMap, Is.False);
            AssertVectorClose(new Vector4(bakePosition.x, bakePosition.y, bakePosition.z, 0), new Vector4(point.ShadowBakePosition.x, point.ShadowBakePosition.y, point.ShadowBakePosition.z, 0));

            Assert.That((bool)applyMethod.Invoke(baker, new object[] { bakePosition, 12f, 0.25f, false }), Is.False);
            Assert.That((bool)applyMethod.Invoke(baker, new object[] { bakePosition + Vector3.right, 12f, 0.25f, false }), Is.True);
        }

        // Verifies direct runtime baker output reserves a manager shadow slot without entering the auto shadow update cache.
        [Test]
        public void RuntimeShadowBakerDirectOutputDoesNotEnterAutoShadowUpdateCache() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Direct Auto Cache Manager", false);
            manager.ShadowTexturesWidth = 8;
            manager.ShadowTexturesHeight = 8;
            PointLightVolumeInstance point = CreatePointLight(manager, "Runtime Shadow Direct Auto Cache Light", true);
            point.AutoUpdateShadowMap = true;
            manager.PointLightVolumeInstances = new[] { point };

            GameObject bakerObject = CreateGameObject("Runtime Shadow Direct Auto Cache Baker", true);
            PointLightShadowRuntimeBaker baker = bakerObject.AddComponent<PointLightShadowRuntimeBaker>();
            baker.TargetPointLightVolume = point;
            baker.Realtime = true;
            baker.Resolution = 8;
            AddRuntimeShadowCamera(baker);

            RenderTexture registrationTexture = CreateRenderTexture("Runtime Shadow Direct Registration", 1, 1, 6, TextureDimension.Tex2DArray);
            FieldInfo registrationTextureField = typeof(PointLightShadowRuntimeBaker).GetField("_registrationTexture", _lifecycleMethodFlags);
            FieldInfo useDirectOutputField = typeof(PointLightShadowRuntimeBaker).GetField("_useDirectOutput", _lifecycleMethodFlags);
            MethodInfo cacheMethod = typeof(PointLightShadowRuntimeBaker).GetMethod("CacheRuntimeReferences", _lifecycleMethodFlags);
            MethodInfo refreshSettingsMethod = typeof(PointLightShadowRuntimeBaker).GetMethod("RefreshBakeSettings", _lifecycleMethodFlags);
            MethodInfo applyMethod = typeof(PointLightShadowRuntimeBaker).GetMethod("ApplyTargetShadowSourceInternal", _lifecycleMethodFlags);
            Assert.That(registrationTextureField, Is.Not.Null);
            Assert.That(useDirectOutputField, Is.Not.Null);
            Assert.That(cacheMethod, Is.Not.Null);
            Assert.That(refreshSettingsMethod, Is.Not.Null);
            Assert.That(applyMethod, Is.Not.Null);

            cacheMethod.Invoke(baker, null);
            refreshSettingsMethod.Invoke(baker, null);
            registrationTextureField.SetValue(baker, registrationTexture);
            Assert.That((bool)useDirectOutputField.GetValue(baker), Is.True);

            Assert.That((bool)applyMethod.Invoke(baker, new object[] { Vector3.zero, 8f, 0.1f, true }), Is.True);
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

            GameObject bakerObject = CreateGameObject("Runtime Shadow Local Metadata Baker", true);
            PointLightShadowRuntimeBaker baker = bakerObject.AddComponent<PointLightShadowRuntimeBaker>();
            baker.TargetPointLightVolume = point;
            AddRuntimeShadowCamera(baker);

            MethodInfo cacheMethod = typeof(PointLightShadowRuntimeBaker).GetMethod("CacheRuntimeReferences", _lifecycleMethodFlags);
            MethodInfo refreshSettingsMethod = typeof(PointLightShadowRuntimeBaker).GetMethod("RefreshBakeSettings", _lifecycleMethodFlags);
            FieldInfo shadowMapTextureField = typeof(PointLightShadowRuntimeBaker).GetField("_shadowTexture", _lifecycleMethodFlags);
            MethodInfo applyMethod = typeof(PointLightShadowRuntimeBaker).GetMethod("ApplyTargetShadowSourceInternal", _lifecycleMethodFlags);
            Assert.That(cacheMethod, Is.Not.Null);
            Assert.That(refreshSettingsMethod, Is.Not.Null);
            Assert.That(shadowMapTextureField, Is.Not.Null);
            Assert.That(applyMethod, Is.Not.Null);
            cacheMethod.Invoke(baker, null);
            refreshSettingsMethod.Invoke(baker, null);
            shadowMapTextureField.SetValue(baker, source);

            Vector3 bakePosition = new Vector3(1, 2, 3);
            Assert.That((bool)applyMethod.Invoke(baker, new object[] { bakePosition, 12f, 0.25f, false }), Is.True);
            Assert.That((bool)applyMethod.Invoke(baker, new object[] { bakePosition + Vector3.right, 12f, 0.25f, false }), Is.False);
        }

        // Verifies realtime baker settings refresh does not notify the manager for local-space transform-only movement.
        [Test]
        public void RuntimeShadowBakerRefreshSettingsDoesNotDirtyManagerOnLocalSpaceMove() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Local Move Manager", false);
            PointLightVolumeInstance point = CreatePointLight(manager, "Runtime Shadow Local Move Light", true);
            point.WorldSpaceShadows = false;
            manager.PointLightVolumeInstances = new[] { point };
            manager.UpdateVolumes();

            GameObject bakerObject = CreateGameObject("Runtime Shadow Local Move Baker", true);
            PointLightShadowRuntimeBaker baker = bakerObject.AddComponent<PointLightShadowRuntimeBaker>();
            baker.TargetPointLightVolume = point;
            AddRuntimeShadowCamera(baker);

            MethodInfo cacheMethod = typeof(PointLightShadowRuntimeBaker).GetMethod("CacheRuntimeReferences", _lifecycleMethodFlags);
            MethodInfo refreshSettingsMethod = typeof(PointLightShadowRuntimeBaker).GetMethod("RefreshBakeSettings", _lifecycleMethodFlags);
            Assert.That(cacheMethod, Is.Not.Null);
            Assert.That(refreshSettingsMethod, Is.Not.Null);
            Assert.That(_pointLightArraysDirtyField, Is.Not.Null);

            cacheMethod.Invoke(baker, null);
            point.IsRangeDirty = false;
            SetManagerField(manager, _pointLightArraysDirtyField, false);

            point.transform.position = new Vector3(3, 4, 5);
            point.transform.rotation = Quaternion.Euler(0, 45, 0);

            refreshSettingsMethod.Invoke(baker, null);

            Assert.That(GetManagerField<bool>(manager, _pointLightArraysDirtyField), Is.False);
        }

        // Verifies runtime blur publishes the local shadow texture array used for final blurred output.
        [Test]
        public void RuntimeShadowBakerRegistersBlurredArrayWhenApplied() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Blur Metadata Manager", false);
            PointLightVolumeInstance point = CreatePointLight(manager, "Runtime Shadow Blur Metadata Light", true);
            RenderTexture shadowSource = CreateRenderTexture("Runtime Shadow Blur Source", 4, 4, 6, TextureDimension.Tex2DArray);

            GameObject bakerObject = CreateGameObject("Runtime Shadow Blur Metadata Baker", true);
            PointLightShadowRuntimeBaker baker = bakerObject.AddComponent<PointLightShadowRuntimeBaker>();
            baker.TargetPointLightVolume = point;
            AddRuntimeShadowCamera(baker);

            MethodInfo cacheMethod = typeof(PointLightShadowRuntimeBaker).GetMethod("CacheRuntimeReferences", _lifecycleMethodFlags);
            MethodInfo refreshSettingsMethod = typeof(PointLightShadowRuntimeBaker).GetMethod("RefreshBakeSettings", _lifecycleMethodFlags);
            FieldInfo shadowMapTextureField = typeof(PointLightShadowRuntimeBaker).GetField("_shadowTexture", _lifecycleMethodFlags);
            MethodInfo applyMethod = typeof(PointLightShadowRuntimeBaker).GetMethod("ApplyTargetShadowSourceInternal", _lifecycleMethodFlags);
            Assert.That(cacheMethod, Is.Not.Null);
            Assert.That(refreshSettingsMethod, Is.Not.Null);
            Assert.That(shadowMapTextureField, Is.Not.Null);
            Assert.That(applyMethod, Is.Not.Null);
            cacheMethod.Invoke(baker, null);
            refreshSettingsMethod.Invoke(baker, null);

            point.Blur = 1;
            baker.RuntimeShadowBlurMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowRuntimeBlur");
            shadowMapTextureField.SetValue(baker, shadowSource);

            Assert.That((bool)applyMethod.Invoke(baker, new object[] { Vector3.zero, 8f, 0.1f, false }), Is.True);
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
            ConfigureShadowTexture(point, cubemapSource, false, true, false);
            PointLightVolumeInstance spot = CreatePointLight(manager, "Single Shadow Spot", true);
            spot.SetSpotLight(60, 0.5f);
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
            Vector4[] reprojectionData = Shader.GetGlobalVectorArray(_pointLightShadowReprojectionDataID);
            AssertVectorClose(new Vector4(point.ShadowBakePosition.x, point.ShadowBakePosition.y, point.ShadowBakePosition.z, 0), reprojectionData[0]);
            AssertVectorClose(new Vector4(spot.ShadowBakePosition.x, spot.ShadowBakePosition.y, spot.ShadowBakePosition.z, spot.OuterAngleTan), reprojectionData[1]);
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
            MethodInfo method = typeof(LightVolumeManager).GetMethod("SetMaterialBlitProperties", _lifecycleMethodFlags);
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
                AssertVectorClose(new Vector4(points[i].ShadowBakePosition.x, points[i].ShadowBakePosition.y, points[i].ShadowBakePosition.z, 0), reprojectionData[i]);
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

        // Creates a temporary GameObject tracked by teardown.
        private GameObject CreateGameObject(string name, bool active) {
            GameObject gameObject = new GameObject(name);
            _createdObjects.Add(gameObject);
            gameObject.SetActive(active);
            return gameObject;
        }

        // Adds the hidden camera that the editor preprocessor normally injects before Play Mode or build
        private static Camera AddRuntimeShadowCamera(PointLightShadowRuntimeBaker baker) {
            Camera camera = baker.gameObject.AddComponent<Camera>();
            camera.enabled = false;
            baker.ShadowCamera = camera;
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

        // Assigns a shadow texture source in the same shape as PointLightVolume authoring sync does.
        private static void ConfigureShadowTexture(PointLightVolumeInstance point, Texture source, bool autoUpdate, bool isCubemap, bool hasDepthSlices) {
            point.ShadowMapID = 0;
            point.ShadowMapTexture = source;
            point.ShadowMapMaterial = null;
            point.AutoUpdateShadowMap = autoUpdate;
            point.ShadowMapTextureIsCubemap = isCubemap;
            point.ShadowMapTextureHasDepthSlices = hasDepthSlices;
        }

        // Assigns a point cubemap projection source in the same shape as PointLightVolume authoring sync does.
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

        // Converts the current compact UVW layout into the legacy min/max layout consumed by older shaders.
        private static Vector4 ExpectedLegacyLightVolumeUvw(LightVolumeInstance instance, int textureIndex, bool max) {
            Vector4 uvwMin = textureIndex == 0 ? instance.BoundsUvwMin0 : textureIndex == 1 ? instance.BoundsUvwMin1 : instance.BoundsUvwMin2;
            if (!max) return new Vector4(uvwMin.x, uvwMin.y, uvwMin.z, 0);
            return new Vector4(uvwMin.x + instance.BoundsUvwMin0.w, uvwMin.y + instance.BoundsUvwMin1.w, uvwMin.z + instance.BoundsUvwMin2.w, 0);
        }

        // Converts a point light color exactly like LightVolumeManager does.
        private static Vector4 ExpectedPointLightColor(PointLightVolumeInstance instance) {
            Color color = instance.Color.linear * instance.Intensity;
            return new Vector4(color.r, color.g, color.b, ExpectedPointLightAngleData(instance));
        }

        // Assigns the readable point light size field using the old packed squared-size value used by previous tests.
        private static void SetPointLightSquaredSize(PointLightVolumeInstance point, float squaredSize) {
            point.LightSourceSize = Mathf.Sqrt(Mathf.Max(Mathf.Abs(squaredSize), 0.0001f));
            point.InverseSquaredRange = 1f / Mathf.Max(point.LightSourceSize * point.LightSourceSize, 0.0001f);
        }

        // Converts readable point light position data exactly like LightVolumeManager does.
        private static Vector4 ExpectedPointLightPosition(PointLightVolumeInstance instance) {
            if (instance.LightType == 2) return new Vector4(instance.Position.x, instance.Position.y, instance.Position.z, instance.Width); // 2: area
            float typeSign = instance.LightType == 1 ? -1f : 1f; // 1: spot
            float w = instance.ProjectionMode == 1 ? typeSign * instance.InverseSquaredRange / Mathf.Max(instance.SquaredScale, 0.000001f) : typeSign * instance.LightSourceSize * instance.LightSourceSize * instance.SquaredScale; // 1: LUT
            return new Vector4(instance.Position.x, instance.Position.y, instance.Position.z, w);
        }

        // Converts readable point light angle data exactly like LightVolumeManager does.
        private static float ExpectedPointLightAngleData(PointLightVolumeInstance instance) {
            if (instance.LightType == 2) return 2f + instance.Height; // 2: area
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
            float expectedShadowFarClip = point.ShadowMapID >= 0 ? (point.FarClip > 0 ? point.FarClip : Mathf.Sqrt(Mathf.Max(point.SquaredRange, 0.000001f))) : 0;
            Assert.That(data.w, Is.EqualTo(expectedShadowFarClip).Within(Epsilon));
        }

        // Asserts a global float with the shared tolerance.
        private static void AssertGlobalFloat(int propertyId, float expected) {
            Assert.That(Shader.GetGlobalFloat(propertyId), Is.EqualTo(expected).Within(Epsilon));
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
            Shader.SetGlobalFloat(_lightBrightnessCutoffID, 0);
        }

        // Destroys a temporary Unity object immediately when the editor runtime allows it.
        private static void DestroyTestObject(UnityEngine.Object target) {
            if (target == null) return;
            if (Application.isEditor) UnityEngine.Object.DestroyImmediate(target);
            else UnityEngine.Object.Destroy(target);
        }
    }
}
