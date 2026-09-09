using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRCLightVolumes.Tests {
    [Category("Editor")]
    public class LightVolumeShaderFeatureTests {
        private readonly List<Object> _texturesAndMaterials = new List<Object>();
        private Scene _scene;
        private Scene _otherScene;

        // Preview scenes keep detection fixtures outside scene onboarding and the user's saved scenes.
        [SetUp]
        public void SetUp() {
            _scene = EditorSceneManager.NewPreviewScene();
            _otherScene = EditorSceneManager.NewPreviewScene();
        }

        // Close only this fixture's scenes and release its transient sources without saving or refreshing assets.
        [TearDown]
        public void TearDown() {
            if (_otherScene.IsValid()) EditorSceneManager.ClosePreviewScene(_otherScene);
            if (_scene.IsValid()) EditorSceneManager.ClosePreviewScene(_scene);
            for (int i = _texturesAndMaterials.Count - 1; i >= 0; i--)
                if (_texturesAndMaterials[i] != null) Object.DestroyImmediate(_texturesAndMaterials[i]);
            _texturesAndMaterials.Clear();
        }

        // An empty supplied scene must not borrow the loaded world's manager or feature mask.
        [Test]
        public void EmptySceneHasNoFeatures() {
            Assert.That(LightVolumeShaderFeatureConfig.DetectSceneFeatures(_scene), Is.EqualTo(LightVolumeShaderFeatures.None));
        }

        // Eligibility follows the Avatars package rather than requiring the Worlds package.
        [Test]
        public void AvatarSdkEligibilityMatchesThePackageVersionDefine() {
#if VRCLV_AVATARS_SDK
            Assert.That(LightVolumeShaderFeatureConfig.HasAvatarSdk, Is.True);
#else
            Assert.That(LightVolumeShaderFeatureConfig.HasAvatarSdk, Is.False);
#endif
        }

        // Projects without a Manager cannot specialize the shared include for a scene.
        [Test]
        public void MissingManagerKeepsEveryFeatureEnabled() {
            Assert.That(LightVolumeShaderFeatureConfig.GetConfiguredFeatures(null), Is.EqualTo(LightVolumeShaderFeatures.All));
        }

        // Neither manual selections nor Auto can override an installed Avatars SDK, including in a mixed SDK project.
        [TestCase(false)]
        [TestCase(true)]
        public void ManagerSelectionRespectsAvatarSdkEligibility(bool automatic) {
            LightVolumeManager manager = CreateComponent<LightVolumeManager>(_scene, "SDK Eligibility Manager");
            manager.ShaderStripping = true;
            manager.AutoShaderFeatures = automatic;
            manager.ShaderFeatures = (int)LightVolumeShaderFeatures.None;
            manager.ShaderFeaturesSchema = 1;
            manager.Clustering = false;

            LightVolumeShaderFeatures expected = LightVolumeShaderFeatureConfig.HasAvatarSdk ? LightVolumeShaderFeatures.All : LightVolumeShaderFeatures.None;
            Assert.That(LightVolumeShaderFeatureConfig.GetConfiguredFeatures(manager), Is.EqualTo(expected));
            Assert.That(manager.ShaderStripping, Is.True);
            Assert.That(manager.AutoShaderFeatures, Is.EqualTo(automatic));
            Assert.That(manager.ShaderFeatures, Is.Zero);
        }

        // Disabled and black initial states still represent authoring features that can be enabled later.
        [TestCase(false, 0)]
        [TestCase(false, 1)]
        [TestCase(false, 2)]
        [TestCase(true, 0)]
        [TestCase(true, 1)]
        [TestCase(true, 2)]
        public void VolumeDetectionPreservesTypeWhenInitiallyInvisible(bool additive, int inactiveState) {
            LightVolumeInstance volume = CreateComponent<LightVolumeInstance>(_scene, "Initially Invisible Volume");
            volume.IsAdditive = additive;
            volume.gameObject.SetActive(inactiveState != 0);
            volume.enabled = inactiveState != 1;
            volume.Intensity = inactiveState == 2 ? 0f : 1f;

            LightVolumeShaderFeatures expected = additive ? LightVolumeShaderFeatures.AdditiveVolumes : LightVolumeShaderFeatures.RegularVolumes;
            if (!additive) expected |= LightVolumeShaderFeatures.LightProbesBlending | LightVolumeShaderFeatures.SmoothBounds;
            Assert.That(LightVolumeShaderFeatureConfig.DetectSceneFeatures(_scene), Is.EqualTo(expected));
        }

        // Each light implementation remains available even when its component does not currently emit light.
        [TestCase(0, 0)]
        [TestCase(0, 1)]
        [TestCase(0, 2)]
        [TestCase(1, 0)]
        [TestCase(1, 1)]
        [TestCase(1, 2)]
        [TestCase(2, 0)]
        [TestCase(2, 1)]
        [TestCase(2, 2)]
        public void LightDetectionPreservesTypeWhenInitiallyInvisible(int lightType, int inactiveState) {
            PointLightVolumeInstance light = CreateLight(_scene, lightType);
            light.gameObject.SetActive(inactiveState != 0);
            light.enabled = inactiveState != 1;
            light.Intensity = inactiveState == 2 ? 0f : 100f;

            Assert.That(LightVolumeShaderFeatureConfig.DetectSceneFeatures(_scene), Is.EqualTo(GetLightFeature(lightType)));
        }

        // Only the selected point/spot LUT source adds LUT code; a material is as valid as a texture.
        [TestCase(0, false)]
        [TestCase(0, true)]
        [TestCase(1, false)]
        [TestCase(1, true)]
        public void AuthoredLutSupportsTexturesAndMaterials(int lightType, bool materialSource) {
            PointLightVolumeInstance light = CreateLight(_scene, lightType);
            light.Projection = 1;
            light.FalloffLUT = CreateSource(materialSource);

            Assert.That(LightVolumeShaderFeatureConfig.DetectSceneFeatures(_scene), Is.EqualTo(GetLightFeature(lightType) | LightVolumeShaderFeatures.LightLuts));
        }

        // The selected projection mode owns the source; an unused inspector reference must not retain LUT code.
        [Test]
        public void UnselectedLutAndEmptyCustomProjectionDoNotRetainTextureFeatures() {
            PointLightVolumeInstance light = CreateLight(_scene, 0);
            light.FalloffLUT = CreateTexture();
            light.Projection = 0;
            Assert.That(LightVolumeShaderFeatureConfig.DetectSceneFeatures(_scene), Is.EqualTo(LightVolumeShaderFeatures.PointLights));

            light.Projection = 2;
            Assert.That(LightVolumeShaderFeatureConfig.DetectSceneFeatures(_scene), Is.EqualTo(LightVolumeShaderFeatures.PointLights));
        }

        // Cookie retention follows the light's projection geometry, including area emitters without custom mode.
        [TestCase(0, false)]
        [TestCase(0, true)]
        [TestCase(1, false)]
        [TestCase(1, true)]
        [TestCase(2, false)]
        [TestCase(2, true)]
        public void AuthoredCookiesRetainOnlyTheirLightType(int lightType, bool materialSource) {
            PointLightVolumeInstance light = CreateLight(_scene, lightType);
            light.Projection = lightType == 2 ? 0 : 2;
            if (lightType == 0) light.Cubemap = materialSource ? CreateMaterial() : (Object)CreateCubemap();
            else light.Cookie = CreateSource(materialSource);

            Assert.That(LightVolumeShaderFeatureConfig.DetectSceneFeatures(_scene), Is.EqualTo(GetLightFeature(lightType) | GetCookieFeature(lightType)));
        }

        // Imported/runtime source fields remain meaningful when the authoring references have been cleared.
        [TestCase(0, 1, false)]
        [TestCase(1, 1, true)]
        [TestCase(0, 2, false)]
        [TestCase(1, 2, true)]
        [TestCase(2, 2, false)]
        public void RuntimeProjectionSourcesConservativelyRetainTheirFeatures(int lightType, int projectionMode, bool materialSource) {
            PointLightVolumeInstance light = CreateLight(_scene, lightType);
            light.ProjectionMode = projectionMode;
            if (materialSource) light.CustomTextureMaterial = CreateMaterial();
            else light.CustomTexture = lightType == 0 && projectionMode == 2 ? (Texture)CreateCubemap() : CreateTexture();

            LightVolumeShaderFeatures projectionFeature = projectionMode == 1 ? LightVolumeShaderFeatures.LightLuts : GetCookieFeature(lightType);
            Assert.That(LightVolumeShaderFeatureConfig.DetectSceneFeatures(_scene), Is.EqualTo(GetLightFeature(lightType) | projectionFeature));
        }

        // A deleted Unity source is null to rendering and must not prevent stripping its unused cookie path.
        [Test]
        public void DestroyedCookieDoesNotRetainCookieFeature() {
            PointLightVolumeInstance light = CreateLight(_scene, 1);
            light.Projection = 2;
            Texture2D source = CreateTexture();
            light.Cookie = source;
            light.CustomTexture = source;
            light.ProjectionMode = 2;
            Object.DestroyImmediate(source);

            Assert.That(LightVolumeShaderFeatureConfig.DetectSceneFeatures(_scene), Is.EqualTo(LightVolumeShaderFeatures.SpotLights));
        }

        // Shadow authoring and in-game baking retain the receiver before a baked texture exists.
        [TestCase(false)]
        [TestCase(true)]
        public void ShadowSettingsRetainShadowsWithoutABakedMap(bool bakeInGame) {
            PointLightVolumeInstance light = CreateLight(_scene, 0);
            light.Shadows = !bakeInGame;
            light.BakeInGame = bakeInGame;

            Assert.That(LightVolumeShaderFeatureConfig.DetectSceneFeatures(_scene), Is.EqualTo(LightVolumeShaderFeatures.PointLights | LightVolumeShaderFeatures.Shadows | LightVolumeShaderFeatures.CubemapShadows));
        }

        // All persistent/runtime shadow source forms must preserve receiving code independently of initial toggles.
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void ShadowSourcesRetainShadowsWithoutAuthoringToggle(int sourceKind) {
            PointLightVolumeInstance light = CreateLight(_scene, 1);
            if (sourceKind == 0) light.ShadowMap = CreateTexture();
            else if (sourceKind == 1) light.ShadowMapTexture = CreateTexture();
            else light.ShadowMapMaterial = CreateMaterial();

            LightVolumeShaderFeatures layout = sourceKind == 0 ? LightVolumeShaderFeatures.SingleSliceShadows : LightVolumeShaderFeatures.CubemapShadows;
            Assert.That(LightVolumeShaderFeatureConfig.DetectSceneFeatures(_scene), Is.EqualTo(LightVolumeShaderFeatures.SpotLights | LightVolumeShaderFeatures.Shadows | layout));
        }

        // An inactive baker may be invoked manually later, even with both automatic triggers disabled.
        [Test]
        public void ExternalRuntimeBakerRetainsShadowsForItsSameSceneTarget() {
            PointLightVolumeInstance light = CreateLight(_scene, 0);
            PointLightShadowRuntimeBaker baker = CreateComponent<PointLightShadowRuntimeBaker>(_scene, "Manual Runtime Baker");
            baker.TargetPointLightVolume = light;
            baker.BakeOnEnable = false;
            baker.Realtime = false;
            baker.enabled = false;

            Assert.That(LightVolumeShaderFeatureConfig.DetectSceneFeatures(_scene), Is.EqualTo(LightVolumeShaderFeatures.PointLights | LightVolumeShaderFeatures.Shadows | LightVolumeShaderFeatures.CubemapShadows));
        }

        // A cross-scene target does not expand the supplied scene's feature profile.
        [Test]
        public void RuntimeBakerDoesNotFollowATargetIntoAnotherScene() {
            PointLightVolumeInstance localLight = CreateLight(_scene, 0);
            PointLightVolumeInstance otherLight = CreateLight(_otherScene, 2);
            PointLightShadowRuntimeBaker baker = CreateComponent<PointLightShadowRuntimeBaker>(_scene, "Foreign Target Baker");
            baker.TargetPointLightVolume = otherLight;

            Assert.That(localLight.Shadows, Is.False);
            Assert.That(LightVolumeShaderFeatureConfig.DetectSceneFeatures(_scene), Is.EqualTo(LightVolumeShaderFeatures.PointLights));
        }

        // Detection follows supplied scene roots, independent of manager registry contents or other loaded scenes.
        [Test]
        public void DetectionIgnoresOtherScenesAndStaleManagerRegistryReferences() {
            LightVolumeManager localManager = CreateComponent<LightVolumeManager>(_scene, "Local Manager");
            localManager.Clustering = false;
            localManager.LightProbesBlending = false;
            LightVolumeInstance localVolume = CreateComponent<LightVolumeInstance>(_scene, "Local Regular Volume");
            localManager.LightVolumeInstances = new LightVolumeInstance[0];
            LightVolumeManager otherManager = CreateComponent<LightVolumeManager>(_otherScene, "Other Manager");
            otherManager.Clustering = true;
            LightVolumeInstance otherVolume = CreateComponent<LightVolumeInstance>(_otherScene, "Other Additive Volume");
            otherVolume.IsAdditive = true;
            PointLightVolumeInstance otherLight = CreateLight(_otherScene, 2);
            otherLight.Cookie = CreateTexture();
            otherLight.Shadows = true;
            localManager.PointLightVolumeInstances = new[] { otherLight };

            Assert.That(localVolume.LightVolumeManager, Is.Null);
            Assert.That(LightVolumeShaderFeatureConfig.DetectSceneFeatures(_scene), Is.EqualTo(LightVolumeShaderFeatures.RegularVolumes));
            Assert.That(LightVolumeShaderFeatureConfig.GetConfiguredFeatures(localManager), Is.EqualTo(ExpectedConfiguredProfile(LightVolumeShaderFeatures.RegularVolumes)));
        }

        // The first hierarchy Manager owns both the clustering toggle and its minimum potential light count.
        [TestCase(false)]
        [TestCase(true)]
        public void ClusteringUsesTheFirstManagerInTheSuppliedScene(bool clustering) {
            LightVolumeManager firstManager = CreateComponent<LightVolumeManager>(_scene, "First Manager");
            firstManager.Clustering = clustering;
            firstManager.ClusteringMinLights = 1;
            LightVolumeManager secondManager = CreateComponent<LightVolumeManager>(_scene, "Second Manager");
            secondManager.Clustering = !clustering;
            secondManager.ClusteringMinLights = 128;
            CreateLight(_scene, 0);

            LightVolumeShaderFeatures expected = LightVolumeShaderFeatures.PointLights;
            if (clustering) expected |= LightVolumeShaderFeatures.Clustering;
            Assert.That(LightVolumeShaderFeatureConfig.DetectSceneFeatures(_scene), Is.EqualTo(expected));
        }

        // The authored count bounds runtime activity, and the minimum is clamped exactly as it is by the runtime Manager.
        [TestCase(0, 0, false)]
        [TestCase(0, 1, true)]
        [TestCase(8, 7, false)]
        [TestCase(8, 8, true)]
        [TestCase(129, 127, false)]
        [TestCase(129, 128, true)]
        public void ClusteringRequiresEnoughPotentialLightsForItsClampedThreshold(int threshold, int count, bool expected) {
            LightVolumeManager manager = CreateComponent<LightVolumeManager>(_scene, "Clustering Threshold Manager");
            manager.Clustering = true;
            manager.ClusteringMinLights = threshold;
            for (int i = 0; i < count; i++) {
                PointLightVolumeInstance light = CreateLight(_scene, i % 3);
                light.enabled = false;
                light.Intensity = 0f;
                Assert.That(light.gameObject.activeInHierarchy, Is.False);
            }

            LightVolumeShaderFeatures detected = LightVolumeShaderFeatureConfig.DetectSceneFeatures(_scene);
            Assert.That((detected & LightVolumeShaderFeatures.Clustering) != 0, Is.EqualTo(expected));
        }

        // Foreign scene objects and stale registry references cannot push the selected scene across the clustering threshold.
        [Test]
        public void ClusteringPotentialCountDoesNotIncludeOtherScenesOrRegistryReferences() {
            LightVolumeManager manager = CreateComponent<LightVolumeManager>(_scene, "Local Clustering Manager");
            manager.Clustering = true;
            manager.ClusteringMinLights = 2;
            CreateLight(_scene, 0);
            PointLightVolumeInstance foreignLight = CreateLight(_otherScene, 1);
            manager.PointLightVolumeInstances = new[] { foreignLight };
            LightVolumeManager foreignManager = CreateComponent<LightVolumeManager>(_otherScene, "Foreign Clustering Manager");
            foreignManager.Clustering = true;
            foreignManager.ClusteringMinLights = 1;

            LightVolumeShaderFeatures detected = LightVolumeShaderFeatureConfig.DetectSceneFeatures(_scene);
            Assert.That(detected, Is.EqualTo(LightVolumeShaderFeatures.PointLights));
        }

        // Manual selection replaces detection instead of adding scene features back behind the user's choice.
        [TestCase(0)]
        [TestCase(4)]
        [TestCase(2047)]
        [TestCase(131071)]
        public void ManualMaskIsHonoredWithoutAddingDetectedFeatures(int manualMask) {
            LightVolumeManager manager = CreateComponent<LightVolumeManager>(_scene, "Manual Manager");
            manager.AutoShaderFeatures = false;
            manager.ShaderFeatures = manualMask;
            manager.ShaderFeaturesSchema = 1;
            manager.Clustering = true;
            PointLightVolumeInstance light = CreateLight(_scene, 2);
            light.Cookie = CreateTexture();
            light.Shadows = true;

            Assert.That(LightVolumeShaderFeatureConfig.GetConfiguredFeatures(manager), Is.EqualTo(ExpectedConfiguredProfile((LightVolumeShaderFeatures)manualMask)));
        }

        // New Managers preserve the existing stripping behavior until the author explicitly disables the master switch.
        [Test]
        public void ManagerEnablesShaderStrippingByDefault() {
            LightVolumeManager manager = CreateComponent<LightVolumeManager>(_scene, "Default Stripping Manager");

            Assert.That(manager.ShaderStripping, Is.True);
        }

        // Disabling the master bypasses both selection modes without rewriting the choices restored on re-enabling it.
        [TestCase(false)]
        [TestCase(true)]
        public void StrippingMasterPreservesConfiguredSelections(bool automatic) {
            LightVolumeManager manager = CreateComponent<LightVolumeManager>(_scene, "Stripping Master Manager");
            manager.AutoShaderFeatures = automatic;
            LightVolumeShaderFeatures manual = LightVolumeShaderFeatures.AreaLights | LightVolumeShaderFeatures.AreaCookies;
            manager.ShaderFeatures = (int)manual;
            manager.ShaderFeaturesSchema = 1;
            manager.Clustering = false;
            CreateLight(_scene, 0);

            manager.ShaderStripping = false;
            Assert.That(LightVolumeShaderFeatureConfig.GetConfiguredFeatures(manager), Is.EqualTo(LightVolumeShaderFeatures.All));
            Assert.That(manager.AutoShaderFeatures, Is.EqualTo(automatic));
            Assert.That(manager.ShaderFeatures, Is.EqualTo((int)manual));
            Assert.That(manager.ShaderFeaturesSchema, Is.EqualTo(1));

            manager.ShaderStripping = true;
            Assert.That(LightVolumeShaderFeatureConfig.GetConfiguredFeatures(manager), Is.EqualTo(ExpectedConfiguredProfile(automatic ? LightVolumeShaderFeatures.PointLights : manual)));
        }

        // An older saved manual selection cannot silently opt out of capabilities that did not yet have checkboxes.
        [TestCase(0, 0)]
        [TestCase(4, 4)]
        [TestCase(2047, 131071)]
        public void LegacyManualMasksRememberNewFeaturesButApplyTheirDependencies(int legacyMask, int effectiveMask) {
            LightVolumeManager manager = CreateComponent<LightVolumeManager>(_scene, "Legacy Manual Manager");
            manager.AutoShaderFeatures = false;
            manager.ShaderFeatures = legacyMask;
            manager.ShaderFeaturesSchema = 0;
            LightVolumeShaderFeatures newFeatures = LightVolumeShaderFeatures.All & ~(LightVolumeShaderFeatures)2047;

            Assert.That(LightVolumeShaderFeatureConfig.GetManualFeatures(legacyMask, 0), Is.EqualTo((LightVolumeShaderFeatures)legacyMask | newFeatures));
            Assert.That(LightVolumeShaderFeatureConfig.GetConfiguredFeatures(manager), Is.EqualTo(ExpectedConfiguredProfile((LightVolumeShaderFeatures)effectiveMask)));
            Assert.That(manager.ShaderFeatures, Is.EqualTo(legacyMask));
            Assert.That(manager.ShaderFeaturesSchema, Is.Zero);
        }

        // Editor selection resolves legacy masks the same way as compilation, then allows every new feature to be explicitly disabled.
        [Test]
        public void CurrentManualSchemaCanDisableAllNewCapabilities() {
            LightVolumeShaderFeatures legacy = LightVolumeShaderFeatureConfig.GetManualFeatures(4, 0);
            LightVolumeShaderFeatures current = LightVolumeShaderFeatureConfig.GetManualFeatures(4, 1);

            Assert.That((legacy & LightVolumeShaderFeatures.VolumeRotation) != 0, Is.True);
            Assert.That(current, Is.EqualTo(LightVolumeShaderFeatures.PointLights));
        }

        // Stale child selections cannot introduce their own volume/light parents, but stay remembered for later re-enabling.
        [Test]
        public void ChildOnlyManualSelectionIsInactiveWithoutLosingItsStoredChoices() {
            LightVolumeShaderFeatures children = LightVolumeShaderFeatures.All & ~(LightVolumeShaderFeatures)31;
            LightVolumeManager manager = CreateComponent<LightVolumeManager>(_scene, "Remembered Child Features");
            manager.AutoShaderFeatures = false;
            manager.ShaderFeatures = (int)children;
            manager.ShaderFeaturesSchema = 1;

            Assert.That(LightVolumeShaderFeatureConfig.GetConfiguredFeatures(manager), Is.EqualTo(ExpectedConfiguredProfile(LightVolumeShaderFeatures.None)));
            Assert.That(LightVolumeShaderFeatureConfig.GetManualFeatures(manager.ShaderFeatures, manager.ShaderFeaturesSchema), Is.EqualTo(children));
            Assert.That(manager.ShaderFeatures, Is.EqualTo((int)children));
            Assert.That(manager.ShaderFeaturesSchema, Is.EqualTo(1));
            for (int i = 5; i < 17; i++) Assert.That(LightVolumeShaderFeatureConfig.IsFeatureAvailable((LightVolumeShaderFeatures)(1 << i), children), Is.False, "Child bit " + i);

            manager.ShaderFeatures |= (int)LightVolumeShaderFeatures.RegularVolumes;
            Assert.That(LightVolumeShaderFeatureConfig.GetConfiguredFeatures(manager), Is.EqualTo(ExpectedConfiguredProfile(LightVolumeShaderFeatures.RegularVolumes | LightVolumeShaderFeatures.VolumeRotation | LightVolumeShaderFeatures.LightProbesBlending | LightVolumeShaderFeatures.SmoothBounds)));
        }

        // A selected light implementation retains only the capabilities its renderer can actually use.
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void LightOnlyProfilesRemoveUnavailableVolumeAndProjectionChildren(int lightType) {
            LightVolumeShaderFeatures requested = (LightVolumeShaderFeatures.All & ~(LightVolumeShaderFeatures)31) | GetLightFeature(lightType);
            LightVolumeShaderFeatures expected = GetLightFeature(lightType) | GetCookieFeature(lightType) | LightVolumeShaderFeatures.Shadows | LightVolumeShaderFeatures.Clustering | LightVolumeShaderFeatures.CubemapShadows | LightVolumeShaderFeatures.WorldSpaceShadows;
            if (lightType != 2) expected |= LightVolumeShaderFeatures.LightLuts;
            if (lightType == 1) expected |= LightVolumeShaderFeatures.SingleSliceShadows;

            Assert.That(LightVolumeShaderFeatureConfig.NormalizeFeatures(requested), Is.EqualTo(expected));
        }

        // Rotation belongs to both volume kinds; probes and smooth missing-volume fallback belong only to regular volumes.
        [TestCase(false)]
        [TestCase(true)]
        public void VolumeOnlyProfilesKeepOnlyTheirRelevantChildren(bool additive) {
            LightVolumeShaderFeatures parent = additive ? LightVolumeShaderFeatures.AdditiveVolumes : LightVolumeShaderFeatures.RegularVolumes;
            LightVolumeShaderFeatures requested = (LightVolumeShaderFeatures.All & ~(LightVolumeShaderFeatures)31) | parent;
            LightVolumeShaderFeatures expected = parent | LightVolumeShaderFeatures.VolumeRotation;
            if (!additive) expected |= LightVolumeShaderFeatures.LightProbesBlending | LightVolumeShaderFeatures.SmoothBounds;

            Assert.That(LightVolumeShaderFeatureConfig.NormalizeFeatures(requested), Is.EqualTo(expected));
            Assert.That(LightVolumeShaderFeatureConfig.IsFeatureAvailable(LightVolumeShaderFeatures.VolumeRotation, parent), Is.True);
            Assert.That(LightVolumeShaderFeatureConfig.IsFeatureAvailable(LightVolumeShaderFeatures.LightProbesBlending, parent), Is.EqualTo(!additive));
            Assert.That(LightVolumeShaderFeatureConfig.IsFeatureAvailable(LightVolumeShaderFeatures.SmoothBounds, parent), Is.EqualTo(!additive));
        }

        // The master remains selected with no receiver geometry; enabling either valid child can restore world-space shadows later.
        [Test]
        public void ShadowMasterDoesNotRequireSelectedChildrenButWorldSpaceDoes() {
            LightVolumeShaderFeatures parents = LightVolumeShaderFeatures.SpotLights | LightVolumeShaderFeatures.Shadows;
            LightVolumeShaderFeatures requested = parents | LightVolumeShaderFeatures.WorldSpaceShadows;

            Assert.That(LightVolumeShaderFeatureConfig.NormalizeFeatures(requested), Is.EqualTo(parents));
            Assert.That(LightVolumeShaderFeatureConfig.IsFeatureAvailable(LightVolumeShaderFeatures.WorldSpaceShadows, requested), Is.False);
            Assert.That(LightVolumeShaderFeatureConfig.IsFeatureAvailable(LightVolumeShaderFeatures.CubemapShadows, parents), Is.True);
            Assert.That(LightVolumeShaderFeatureConfig.IsFeatureAvailable(LightVolumeShaderFeatures.SingleSliceShadows, parents), Is.True);
            Assert.That(LightVolumeShaderFeatureConfig.NormalizeFeatures(requested | LightVolumeShaderFeatures.CubemapShadows), Is.EqualTo(requested | LightVolumeShaderFeatures.CubemapShadows));
            Assert.That(LightVolumeShaderFeatureConfig.NormalizeFeatures(requested | LightVolumeShaderFeatures.SingleSliceShadows), Is.EqualTo(requested | LightVolumeShaderFeatures.SingleSliceShadows));
        }

        // Selected shadow children still require the complete parent chain, including a supported Single Slice light type.
        [Test]
        public void ShadowDependenciesCascadeThroughUnavailableSelectedParents() {
            LightVolumeShaderFeatures requested = LightVolumeShaderFeatures.PointLights | LightVolumeShaderFeatures.Shadows | LightVolumeShaderFeatures.SingleSliceShadows | LightVolumeShaderFeatures.WorldSpaceShadows;
            Assert.That(LightVolumeShaderFeatureConfig.NormalizeFeatures(requested), Is.EqualTo(LightVolumeShaderFeatures.PointLights | LightVolumeShaderFeatures.Shadows));
            Assert.That(LightVolumeShaderFeatureConfig.IsFeatureAvailable(LightVolumeShaderFeatures.WorldSpaceShadows, requested), Is.False);

            requested |= LightVolumeShaderFeatures.CubemapShadows;
            Assert.That(LightVolumeShaderFeatureConfig.IsFeatureAvailable(LightVolumeShaderFeatures.WorldSpaceShadows, requested), Is.True);
            Assert.That(LightVolumeShaderFeatureConfig.NormalizeFeatures(requested & ~LightVolumeShaderFeatures.Shadows), Is.EqualTo(LightVolumeShaderFeatures.PointLights));
            Assert.That(LightVolumeShaderFeatureConfig.NormalizeFeatures(requested & ~LightVolumeShaderFeatures.PointLights), Is.EqualTo(LightVolumeShaderFeatures.None));
        }

        // Availability enables a currently unchecked checkbox when its parents permit it, rather than requiring its own bit.
        [Test]
        public void UncheckedChildrenAreAvailableWhenTheirParentsAreSelected() {
            LightVolumeShaderFeatures parents = LightVolumeShaderFeatures.PointLights;
            Assert.That(LightVolumeShaderFeatureConfig.IsFeatureAvailable(LightVolumeShaderFeatures.PointCookies, parents), Is.True);
            Assert.That(LightVolumeShaderFeatureConfig.IsFeatureAvailable(LightVolumeShaderFeatures.LightLuts, parents), Is.True);
            Assert.That(LightVolumeShaderFeatureConfig.IsFeatureAvailable(LightVolumeShaderFeatures.Shadows, parents), Is.True);
            Assert.That(LightVolumeShaderFeatureConfig.IsFeatureAvailable(LightVolumeShaderFeatures.Clustering, parents), Is.True);
            Assert.That(LightVolumeShaderFeatureConfig.IsFeatureAvailable(LightVolumeShaderFeatures.CubemapShadows, parents), Is.False);
            Assert.That(LightVolumeShaderFeatureConfig.IsFeatureAvailable(LightVolumeShaderFeatures.RegularVolumes, LightVolumeShaderFeatures.None), Is.True);
            Assert.That(LightVolumeShaderFeatureConfig.IsFeatureAvailable(LightVolumeShaderFeatures.None, LightVolumeShaderFeatures.All), Is.False);
        }

        // Dependency closure must be stable for arbitrary persisted combinations and must never invent an enabled feature.
        [Test]
        public void NormalizationIsIdempotentAndOnlyRemovesBitsForEveryKnownMask() {
            for (int mask = 0; mask <= (int)LightVolumeShaderFeatures.All; mask++) {
                LightVolumeShaderFeatures original = (LightVolumeShaderFeatures)mask;
                LightVolumeShaderFeatures normalized = LightVolumeShaderFeatureConfig.NormalizeFeatures(original);
                if ((normalized & ~original) != 0 || LightVolumeShaderFeatureConfig.NormalizeFeatures(normalized) != normalized) Assert.Fail("Invalid dependency closure for mask " + mask);
            }
            Assert.That(LightVolumeShaderFeatureConfig.NormalizeFeatures((LightVolumeShaderFeatures)(-1)), Is.EqualTo(LightVolumeShaderFeatures.All));
            Assert.That(LightVolumeShaderFeatureConfig.GetManualFeatures(int.MinValue | (int)LightVolumeShaderFeatures.PointLights, 1), Is.EqualTo(LightVolumeShaderFeatures.PointLights));
        }

        // Renaming labels and defines must not reinterpret the bits serialized by the previous feature configuration.
        [Test]
        public void RenamedFeaturesPreserveTheirSerializedPositionsAndSchema() {
            Assert.That((int)LightVolumeShaderFeatures.WorldSpaceShadows, Is.EqualTo(1 << 12));
            Assert.That((int)LightVolumeShaderFeatures.SingleSliceShadows, Is.EqualTo(1 << 14));
            Assert.That((int)LightVolumeShaderFeatures.LightProbesBlending, Is.EqualTo(1 << 15));
            Assert.That((int)LightVolumeShaderFeatures.SmoothBounds, Is.EqualTo(1 << 16));
            Assert.That((int)LightVolumeShaderFeatures.All, Is.EqualTo(131071));
            LightVolumeShaderFeatures renamed = (LightVolumeShaderFeatures)((1 << 12) | (1 << 14) | (1 << 15) | (1 << 16));
            Assert.That(LightVolumeShaderFeatureConfig.GetManualFeatures((int)renamed, 1), Is.EqualTo(renamed));
        }

        // A volume's absolute orientation is irrelevant when it matches the bake; dynamic volumes may rotate later.
        [TestCase(false, false, false)]
        [TestCase(false, false, true)]
        [TestCase(false, true, false)]
        [TestCase(true, false, false)]
        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        public void VolumeRotationDependsOnDynamicStateAndRelativeBakePose(bool additive, bool dynamic, bool changedPose) {
            LightVolumeInstance volume = CreateComponent<LightVolumeInstance>(_scene, "Rotated Volume");
            volume.IsAdditive = additive;
            volume.IsDynamic = dynamic;
            volume.transform.rotation = Quaternion.Euler(0f, 40f, 0f);
            volume.InvBakedRotation = Quaternion.Inverse(Quaternion.Euler(0f, changedPose ? 0f : 40f, 0f));

            LightVolumeShaderFeatures detected = LightVolumeShaderFeatureConfig.DetectSceneFeatures(_scene);
            Assert.That((detected & LightVolumeShaderFeatures.VolumeRotation) != 0, Is.EqualTo(dynamic || changedPose));
        }

        // Unity reports ancestor rotations as Transform property edits, including when the descendant volume is inactive.
        [Test]
        public void VolumeAndInactiveAncestorTransformsInvalidateFeatureDetection() {
            Transform ancestor = CreateObject(_scene, "Volume Ancestor").transform;
            LightVolumeInstance volume = CreateComponent<LightVolumeInstance>(_scene, "Nested Inactive Volume");
            volume.transform.SetParent(ancestor, false);

            Assert.That(volume.gameObject.activeInHierarchy, Is.False);
            Assert.That(LightVolumeShaderFeatureConfig.AffectsDetectedFeatures(volume.transform), Is.True);
            Assert.That(LightVolumeShaderFeatureConfig.AffectsDetectedFeatures(ancestor), Is.True);
        }

        // Ordinary geometry and light movement do not change the volume-rotation profile or require another scene scan.
        [Test]
        public void TransformsWithoutDescendantVolumesDoNotInvalidateFeatureDetection() {
            PointLightVolumeInstance light = CreateLight(_scene, 0);
            Transform unrelated = CreateObject(_scene, "Unrelated Transform").transform;

            Assert.That(LightVolumeShaderFeatureConfig.AffectsDetectedFeatures(light.transform), Is.False);
            Assert.That(LightVolumeShaderFeatureConfig.AffectsDetectedFeatures(unrelated), Is.False);
            Assert.That(LightVolumeShaderFeatureConfig.AffectsDetectedFeatures(null), Is.False);
        }

        // The first scene Manager owns both regular-volume boundary policies; secondary Managers cannot retain those paths.
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void RegularVolumeBlendingUsesFirstManagerSettings(bool probes, bool sharpBounds) {
            LightVolumeManager manager = CreateComponent<LightVolumeManager>(_scene, "Primary Volume Manager");
            manager.LightProbesBlending = probes;
            manager.SharpBounds = sharpBounds;
            LightVolumeManager secondary = CreateComponent<LightVolumeManager>(_scene, "Secondary Volume Manager");
            secondary.LightProbesBlending = !probes;
            secondary.SharpBounds = !sharpBounds;
            CreateComponent<LightVolumeInstance>(_scene, "Regular Volume");
            LightVolumeShaderFeatures expected = LightVolumeShaderFeatures.RegularVolumes;
            if (probes) expected |= LightVolumeShaderFeatures.LightProbesBlending;
            if (!sharpBounds) expected |= LightVolumeShaderFeatures.SmoothBounds;

            Assert.That(LightVolumeShaderFeatureConfig.DetectSceneFeatures(_scene), Is.EqualTo(expected));
        }

        // An additive-only scene never enters the regular-volume blending paths regardless of Manager settings.
        [Test]
        public void AdditiveOnlySceneDoesNotRetainRegularBlending() {
            LightVolumeManager manager = CreateComponent<LightVolumeManager>(_scene, "Additive Manager");
            manager.LightProbesBlending = true;
            manager.SharpBounds = false;
            LightVolumeInstance volume = CreateComponent<LightVolumeInstance>(_scene, "Additive Volume");
            volume.IsAdditive = true;

            Assert.That(LightVolumeShaderFeatureConfig.DetectSceneFeatures(_scene), Is.EqualTo(LightVolumeShaderFeatures.AdditiveVolumes));
        }

        // Reprojection matters only for shadow-capable lights; an unused World Space Shadows checkbox alone adds no shader work.
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void WorldSpaceShadowsRequiresWorldSpaceAndShadowCapability(bool shadows, bool worldSpace) {
            PointLightVolumeInstance light = CreateLight(_scene, 0);
            light.Shadows = shadows;
            light.WorldSpaceShadows = worldSpace;

            LightVolumeShaderFeatures detected = LightVolumeShaderFeatureConfig.DetectSceneFeatures(_scene);
            Assert.That((detected & LightVolumeShaderFeatures.WorldSpaceShadows) != 0, Is.EqualTo(shadows && worldSpace));
        }

        // Point/Area maps always use six faces; a Spot requires cubemap projection only when its bake settings demand it.
        [TestCase(0, false, false, true)]
        [TestCase(2, false, false, true)]
        [TestCase(1, false, false, false)]
        [TestCase(1, true, false, true)]
        [TestCase(1, false, true, true)]
        public void AuthoredShadowGeometrySelectsItsReceiver(int lightType, bool forceCube, bool wideAngle, bool expectedCube) {
            PointLightVolumeInstance light = CreateLight(_scene, lightType);
            light.Shadows = true;
            light.ForceCubemapShadows = forceCube;
            light.Angle = wideAngle ? Mathf.PI : Mathf.PI / 4f;
            LightVolumeShaderFeatures geometry = expectedCube ? LightVolumeShaderFeatures.CubemapShadows : LightVolumeShaderFeatures.SingleSliceShadows;

            Assert.That(LightVolumeShaderFeatureConfig.DetectSceneFeatures(_scene), Is.EqualTo(GetLightFeature(lightType) | LightVolumeShaderFeatures.Shadows | geometry));
        }

        // Keep both layouts until pending authoring-to-runtime synchronization resolves conflicting metadata.
        [TestCase(false)]
        [TestCase(true)]
        public void ConflictingSpotShadowLayoutsKeepBothReceivers(bool authoringCube) {
            PointLightVolumeInstance light = CreateLight(_scene, 1);
            light.Shadows = true;
            light.ForceCubemapShadows = authoringCube;
            light.ShadowMapTexture = CreateTexture();
            light.ShadowMapUsesCubemap = !authoringCube;

            LightVolumeShaderFeatures expected = LightVolumeShaderFeatures.SpotLights | LightVolumeShaderFeatures.Shadows | LightVolumeShaderFeatures.CubemapShadows | LightVolumeShaderFeatures.SingleSliceShadows;
            Assert.That(LightVolumeShaderFeatureConfig.DetectSceneFeatures(_scene), Is.EqualTo(expected));
        }

        // Runtime baker dependencies retain projected world-space receiving even before their target has a shadow texture.
        [Test]
        public void ExternalSpotBakerRetainsProjectedWorldShadows() {
            PointLightVolumeInstance light = CreateLight(_scene, 1);
            light.WorldSpaceShadows = true;
            light.ShadowMapUsesCubemap = false;
            PointLightShadowRuntimeBaker baker = CreateComponent<PointLightShadowRuntimeBaker>(_scene, "Projected Runtime Baker");
            baker.TargetPointLightVolume = light;

            LightVolumeShaderFeatures expected = LightVolumeShaderFeatures.SpotLights | LightVolumeShaderFeatures.Shadows | LightVolumeShaderFeatures.SingleSliceShadows | LightVolumeShaderFeatures.WorldSpaceShadows;
            Assert.That(LightVolumeShaderFeatureConfig.DetectSceneFeatures(_scene), Is.EqualTo(expected));
        }

        // Automatic mode derives the scene profile even if an earlier manual profile enabled every feature.
        [Test]
        public void AutomaticMaskIgnoresTheStoredManualMask() {
            LightVolumeManager manager = CreateComponent<LightVolumeManager>(_scene, "Automatic Manager");
            manager.AutoShaderFeatures = true;
            manager.ShaderFeatures = (int)LightVolumeShaderFeatures.All;
            manager.Clustering = false;
            CreateLight(_scene, 1);

            Assert.That(LightVolumeShaderFeatureConfig.GetConfiguredFeatures(manager), Is.EqualTo(ExpectedConfiguredProfile(LightVolumeShaderFeatures.SpotLights)));
        }

        // Removing a parent emits its dependent opt-outs too, while leaf features still produce exactly one definition.
        [TestCase(0, "VRCLV_DISABLE_REGULAR_VOLUMES", "VRCLV_DISABLE_LIGHT_PROBES_BLENDING,VRCLV_DISABLE_SMOOTH_BOUNDS")]
        [TestCase(1, "VRCLV_DISABLE_ADDITIVE_VOLUMES", "")]
        [TestCase(2, "VRCLV_DISABLE_POINT_LIGHTS", "VRCLV_DISABLE_POINT_COOKIES")]
        [TestCase(3, "VRCLV_DISABLE_SPOT_LIGHTS", "VRCLV_DISABLE_SPOT_COOKIES,VRCLV_DISABLE_SINGLE_SLICE_SHADOWS")]
        [TestCase(4, "VRCLV_DISABLE_AREA_LIGHTS", "VRCLV_DISABLE_AREA_COOKIES")]
        [TestCase(5, "VRCLV_DISABLE_LIGHT_LUTS", "")]
        [TestCase(6, "VRCLV_DISABLE_POINT_COOKIES", "")]
        [TestCase(7, "VRCLV_DISABLE_SPOT_COOKIES", "")]
        [TestCase(8, "VRCLV_DISABLE_AREA_COOKIES", "")]
        [TestCase(9, "VRCLV_DISABLE_SHADOWS", "VRCLV_DISABLE_WORLD_SPACE_SHADOWS,VRCLV_DISABLE_CUBEMAP_SHADOWS,VRCLV_DISABLE_SINGLE_SLICE_SHADOWS")]
        [TestCase(10, "VRCLV_DISABLE_CLUSTERING", "")]
        [TestCase(11, "VRCLV_DISABLE_VOLUME_ROTATION", "")]
        [TestCase(12, "VRCLV_DISABLE_WORLD_SPACE_SHADOWS", "")]
        [TestCase(13, "VRCLV_DISABLE_CUBEMAP_SHADOWS", "")]
        [TestCase(14, "VRCLV_DISABLE_SINGLE_SLICE_SHADOWS", "")]
        [TestCase(15, "VRCLV_DISABLE_LIGHT_PROBES_BLENDING", "")]
        [TestCase(16, "VRCLV_DISABLE_SMOOTH_BOUNDS", "")]
        public void GeneratedSourceDisablesAbsentFeaturesAndTheirDependents(int absentBit, string expectedDefine, string dependentDefines) {
            LightVolumeShaderFeatures enabled = LightVolumeShaderFeatures.All & ~(LightVolumeShaderFeatures)(1 << absentBit);
            string source = LightVolumeShaderFeatureConfig.BuildConfigSource(enabled);
            List<string> expected = new List<string> { expectedDefine };
            if (dependentDefines.Length != 0) expected.AddRange(dependentDefines.Split(','));

            CollectionAssert.AreEquivalent(expected, ReadDisableDefines(source));
            Assert.That(source, Does.Not.Contain("#pragma"));
        }

        // Default/full functionality is represented by no disabling definitions; no keyword selection is required.
        [Test]
        public void AllFeaturesGenerateAnEmptyOptOutProfileWithoutKeywords() {
            string source = LightVolumeShaderFeatureConfig.BuildConfigSource(LightVolumeShaderFeatures.All);

            Assert.That(ReadDisableDefines(source), Is.Empty);
            Assert.That(source, Does.Not.Contain("#pragma"));
            Assert.That(source, Does.Not.Contain("multi_compile"));
            Assert.That(source, Does.Not.Contain("shader_feature"));
        }

        // A scene with no components removes every optional feature while keeping the ordinary include contract.
        [Test]
        public void NoFeaturesGenerateEveryOptOutDefineOnce() {
            string source = LightVolumeShaderFeatureConfig.BuildConfigSource(LightVolumeShaderFeatures.None);
            string[] expected = {
                "VRCLV_DISABLE_REGULAR_VOLUMES", "VRCLV_DISABLE_ADDITIVE_VOLUMES",
                "VRCLV_DISABLE_POINT_LIGHTS", "VRCLV_DISABLE_SPOT_LIGHTS", "VRCLV_DISABLE_AREA_LIGHTS",
                "VRCLV_DISABLE_LIGHT_LUTS", "VRCLV_DISABLE_POINT_COOKIES", "VRCLV_DISABLE_SPOT_COOKIES", "VRCLV_DISABLE_AREA_COOKIES",
                "VRCLV_DISABLE_SHADOWS", "VRCLV_DISABLE_CLUSTERING",
                "VRCLV_DISABLE_VOLUME_ROTATION", "VRCLV_DISABLE_WORLD_SPACE_SHADOWS",
                "VRCLV_DISABLE_CUBEMAP_SHADOWS", "VRCLV_DISABLE_SINGLE_SLICE_SHADOWS",
                "VRCLV_DISABLE_LIGHT_PROBES_BLENDING", "VRCLV_DISABLE_SMOOTH_BOUNDS"
            };

            CollectionAssert.AreEquivalent(expected, ReadDisableDefines(source));
            Assert.That(source, Does.Not.Contain("#pragma"));
        }

        // Detection remains testable in mixed SDK projects while compiled scene profiles honor the avatar exclusion.
        private static LightVolumeShaderFeatures ExpectedConfiguredProfile(LightVolumeShaderFeatures requested) {
            return LightVolumeShaderFeatureConfig.HasAvatarSdk ? LightVolumeShaderFeatures.All : requested;
        }

        // Objects enter the isolated scene while inactive, before any authoring callbacks can observe them.
        private static GameObject CreateObject(Scene scene, string name) {
            GameObject gameObject = new GameObject(name);
            gameObject.SetActive(false);
            SceneManager.MoveGameObjectToScene(gameObject, scene);
            return gameObject;
        }

        // Components are added only after their object belongs to the isolated scene.
        private static T CreateComponent<T>(Scene scene, string name) where T : Component {
            return CreateObject(scene, name).AddComponent<T>();
        }

        // Creates a parametric light with no texture or shadow defaults to influence detection.
        private static PointLightVolumeInstance CreateLight(Scene scene, int lightType) {
            PointLightVolumeInstance light = CreateComponent<PointLightVolumeInstance>(scene, "Feature Test Light");
            light.LightType = lightType;
            return light;
        }

        // Maps authoring light values to their separate compile-time implementations.
        private static LightVolumeShaderFeatures GetLightFeature(int lightType) {
            if (lightType == 0) return LightVolumeShaderFeatures.PointLights;
            if (lightType == 1) return LightVolumeShaderFeatures.SpotLights;
            return LightVolumeShaderFeatures.AreaLights;
        }

        // Maps each projection geometry to its own sampling feature.
        private static LightVolumeShaderFeatures GetCookieFeature(int lightType) {
            if (lightType == 0) return LightVolumeShaderFeatures.PointCookies;
            if (lightType == 1) return LightVolumeShaderFeatures.SpotCookies;
            return LightVolumeShaderFeatures.AreaCookies;
        }

        // Supplies both supported source classes without creating project assets.
        private Object CreateSource(bool materialSource) {
            return materialSource ? (Object)CreateMaterial() : CreateTexture();
        }

        // Creates the smallest useful planar source; detection needs no pixel data or GPU upload.
        private Texture2D CreateTexture() {
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            _texturesAndMaterials.Add(texture);
            return texture;
        }

        // Gives point cookies an actual cubemap so source validation exercises the correct dimension.
        private Cubemap CreateCubemap() {
            Cubemap texture = new Cubemap(2, TextureFormat.RGBA32, false);
            _texturesAndMaterials.Add(texture);
            return texture;
        }

        // A built-in shader keeps material-source detection independent of the Light Volumes shader import state.
        private Material CreateMaterial() {
            Shader shader = Shader.Find("Hidden/InternalErrorShader");
            Assert.That(shader, Is.Not.Null);
            Material material = new Material(shader);
            _texturesAndMaterials.Add(material);
            return material;
        }

        // Parse emitted preprocessor definitions rather than comments or incidental token references.
        private static string[] ReadDisableDefines(string source) {
            MatchCollection matches = Regex.Matches(source, @"^[ \t]*#[ \t]*define[ \t]+(VRCLV_DISABLE_[A-Z_]+)\b", RegexOptions.Multiline);
            string[] names = new string[matches.Count];
            for (int i = 0; i < matches.Count; i++) names[i] = matches[i].Groups[1].Value;
            return names;
        }
    }

    // Session policy is exercised independently of Editor globals, scene callbacks, and generated-include imports.
    [Category("Editor")]
    public class LightVolumeShaderFeatureLifecycleTests {
        // Opening the Editor must never inherit a stripped scene profile from a previous session.
        [TestCase(false)]
        [TestCase(true)]
        public void NewEditSessionUsesAllFeatures(bool strippingAllowed) {
            LightVolumeShaderFeatureSession session = new LightVolumeShaderFeatureSession();

            Assert.That(session.IsPlaying, Is.False);
            Assert.That(session.IsBuilding, Is.False);
            Assert.That(session.GetFeatures(strippingAllowed), Is.EqualTo(LightVolumeShaderFeatures.All));
        }

        // Inspector changes in Edit Mode store authoring choices without scanning or specializing shaders.
        [TestCase(false)]
        [TestCase(true)]
        public void EditModeSelectionDoesNotScanOrApply(bool automatic) {
            LightVolumeShaderFeatureSession session = new LightVolumeShaderFeatureSession();
            int detections = 0;
            session.ChangePlaySelection(automatic, LightVolumeShaderFeatures.None, () => {
                detections++;
                return LightVolumeShaderFeatures.PointLights;
            });

            Assert.That(detections, Is.Zero);
            Assert.That(session.IsPlaying, Is.False);
            Assert.That(session.GetFeatures(true), Is.EqualTo(LightVolumeShaderFeatures.All));
        }

        // Runtime object changes and repeated Inspector events cannot shrink or expand the entry-time Auto contract.
        [Test]
        public void AutomaticPlaySelectionRetainsItsEntrySnapshot() {
            LightVolumeShaderFeatureSession session = new LightVolumeShaderFeatureSession();
            session.BeginPlay(true, LightVolumeShaderFeatures.All, LightVolumeShaderFeatures.PointLights);
            int detections = 0;
            for (int i = 0; i < 3; i++) {
                session.ChangePlaySelection(true, i == 0 ? LightVolumeShaderFeatures.None : LightVolumeShaderFeatures.All, () => {
                    detections++;
                    return LightVolumeShaderFeatures.SpotLights;
                });
            }

            Assert.That(detections, Is.Zero);
            Assert.That(session.Automatic, Is.True);
            Assert.That(session.DetectedFeatures, Is.EqualTo(LightVolumeShaderFeatures.PointLights));
            Assert.That(session.PlayFeatures, Is.EqualTo(LightVolumeShaderFeatures.PointLights));
            Assert.That(session.GetFeatures(true), Is.EqualTo(LightVolumeShaderFeatures.PointLights));
        }

        // The master changes compilation availability while preserving the manual selection and the captured Auto result.
        [TestCase(false)]
        [TestCase(true)]
        public void StrippingTogglePreservesTheRunningSelectionWithoutRescanning(bool automatic) {
            LightVolumeShaderFeatureSession session = new LightVolumeShaderFeatureSession();
            LightVolumeShaderFeatures manual = LightVolumeShaderFeatures.AreaLights;
            LightVolumeShaderFeatures detected = LightVolumeShaderFeatures.PointLights;
            LightVolumeShaderFeatures expected = automatic ? detected : manual;
            session.BeginPlay(automatic, manual, detected);
            Assert.That(session.StrippingEnabled, Is.True);
            Assert.That(session.GetFeatures(true), Is.EqualTo(expected));

            session.SetStrippingEnabled(false);
            int detections = 0;
            session.ChangePlaySelection(automatic, manual, () => {
                detections++;
                return LightVolumeShaderFeatures.SpotLights;
            });
            Assert.That(session.GetFeatures(true), Is.EqualTo(LightVolumeShaderFeatures.All));
            Assert.That(session.Automatic, Is.EqualTo(automatic));
            Assert.That(session.PlayFeatures, Is.EqualTo(expected));
            Assert.That(session.DetectedFeatures, Is.EqualTo(detected));

            session.SetStrippingEnabled(true);
            Assert.That(session.StrippingEnabled, Is.True);
            Assert.That(session.GetFeatures(true), Is.EqualTo(expected));
            Assert.That(detections, Is.Zero);
        }

        // A manual checkbox edit changes the active Play Mode profile immediately and removes orphaned children.
        [Test]
        public void ManualPlayChangesApplyWithoutDetection() {
            LightVolumeShaderFeatureSession session = new LightVolumeShaderFeatureSession();
            session.BeginPlay(false, LightVolumeShaderFeatures.PointLights, LightVolumeShaderFeatures.AreaLights);
            int detections = 0;
            LightVolumeShaderFeatures selection = LightVolumeShaderFeatures.RegularVolumes | LightVolumeShaderFeatures.VolumeRotation | LightVolumeShaderFeatures.PointCookies;
            session.ChangePlaySelection(false, selection, () => {
                detections++;
                return LightVolumeShaderFeatures.All;
            });

            Assert.That(session.Automatic, Is.False);
            Assert.That(session.PlayFeatures, Is.EqualTo(LightVolumeShaderFeatures.RegularVolumes | LightVolumeShaderFeatures.VolumeRotation));
            Assert.That(session.GetFeatures(true), Is.EqualTo(session.PlayFeatures));
            session.ChangePlaySelection(false, LightVolumeShaderFeatures.None, () => {
                detections++;
                return LightVolumeShaderFeatures.All;
            });
            Assert.That(detections, Is.Zero);
            Assert.That(session.GetFeatures(true), Is.EqualTo(LightVolumeShaderFeatures.None));
        }

        // Auto performs exactly one fresh scan per explicit off-to-on transition, never for an unchanged toggle.
        [Test]
        public void EnablingAutoCapturesOneNewSnapshotPerTransition() {
            LightVolumeShaderFeatureSession session = new LightVolumeShaderFeatureSession();
            session.BeginPlay(false, LightVolumeShaderFeatures.PointLights, LightVolumeShaderFeatures.None);
            int detections = 0;
            LightVolumeShaderFeatures sceneFeatures = LightVolumeShaderFeatures.SpotLights;
            System.Func<LightVolumeShaderFeatures> detect = () => {
                detections++;
                return sceneFeatures;
            };

            session.ChangePlaySelection(true, LightVolumeShaderFeatures.All, detect);
            Assert.That(detections, Is.EqualTo(1));
            Assert.That(session.GetFeatures(true), Is.EqualTo(LightVolumeShaderFeatures.SpotLights));
            sceneFeatures = LightVolumeShaderFeatures.AreaLights;
            session.ChangePlaySelection(true, LightVolumeShaderFeatures.None, detect);
            Assert.That(detections, Is.EqualTo(1));
            Assert.That(session.GetFeatures(true), Is.EqualTo(LightVolumeShaderFeatures.SpotLights));

            session.ChangePlaySelection(false, LightVolumeShaderFeatures.RegularVolumes, detect);
            Assert.That(session.GetFeatures(true), Is.EqualTo(LightVolumeShaderFeatures.RegularVolumes));
            session.ChangePlaySelection(true, LightVolumeShaderFeatures.None, detect);
            Assert.That(detections, Is.EqualTo(2));
            Assert.That(session.DetectedFeatures, Is.EqualTo(LightVolumeShaderFeatures.AreaLights));
            Assert.That(session.GetFeatures(true), Is.EqualTo(LightVolumeShaderFeatures.AreaLights));
        }

        // A new Play Mode entry receives a fresh authored snapshot instead of reviving the prior play session.
        [Test]
        public void LeavingPlayRestoresFullSupportAndNextEntryReplacesTheSnapshot() {
            LightVolumeShaderFeatureSession session = new LightVolumeShaderFeatureSession();
            session.BeginPlay(true, LightVolumeShaderFeatures.All, LightVolumeShaderFeatures.PointLights);
            session.EndPlay();

            Assert.That(session.IsPlaying, Is.False);
            Assert.That(session.GetFeatures(true), Is.EqualTo(LightVolumeShaderFeatures.All));
            session.BeginPlay(true, LightVolumeShaderFeatures.All, LightVolumeShaderFeatures.SpotLights);
            Assert.That(session.IsPlaying, Is.True);
            Assert.That(session.DetectedFeatures, Is.EqualTo(LightVolumeShaderFeatures.SpotLights));
            Assert.That(session.GetFeatures(true), Is.EqualTo(LightVolumeShaderFeatures.SpotLights));
        }

        // Build compilation owns its temporary profile; completing or cancelling the build returns to full Edit Mode.
        [TestCase(false)]
        [TestCase(true)]
        public void BuildFromEditModeIsTemporaryAndAvatarBuildStaysFull(bool avatarBuild) {
            LightVolumeShaderFeatureSession session = new LightVolumeShaderFeatureSession();
            LightVolumeShaderFeatures selection = LightVolumeShaderFeatures.PointLights | LightVolumeShaderFeatures.AreaCookies;
            session.BeginBuild(avatarBuild, selection);

            Assert.That(session.IsBuilding, Is.True);
            Assert.That(session.IsAvatarBuild, Is.EqualTo(avatarBuild));
            Assert.That(session.GetFeatures(true), Is.EqualTo(avatarBuild ? LightVolumeShaderFeatures.All : LightVolumeShaderFeatures.PointLights));
            session.EndBuild();
            Assert.That(session.IsBuilding, Is.False);
            Assert.That(session.GetFeatures(true), Is.EqualTo(LightVolumeShaderFeatures.All));
        }

        // A build can override a live Play Mode preview without overwriting its selected profile or Auto snapshot.
        [TestCase(false)]
        [TestCase(true)]
        public void BuildCompletionRestoresTheExistingPlayProfile(bool avatarBuild) {
            LightVolumeShaderFeatureSession session = new LightVolumeShaderFeatureSession();
            session.BeginPlay(true, LightVolumeShaderFeatures.All, LightVolumeShaderFeatures.PointLights);
            session.BeginBuild(avatarBuild, LightVolumeShaderFeatures.SpotLights);

            Assert.That(session.GetFeatures(true), Is.EqualTo(avatarBuild ? LightVolumeShaderFeatures.All : LightVolumeShaderFeatures.SpotLights));
            Assert.That(session.PlayFeatures, Is.EqualTo(LightVolumeShaderFeatures.PointLights));
            Assert.That(session.DetectedFeatures, Is.EqualTo(LightVolumeShaderFeatures.PointLights));
            session.EndBuild();
            Assert.That(session.IsPlaying, Is.True);
            Assert.That(session.GetFeatures(true), Is.EqualTo(LightVolumeShaderFeatures.PointLights));
        }

        // Build input is resolved separately from the preview master, and cleanup restores its disabled state.
        [TestCase(false)]
        [TestCase(true)]
        public void BuildProfileOverridesDisabledPlayStripping(bool avatarBuild) {
            LightVolumeShaderFeatureSession session = new LightVolumeShaderFeatureSession();
            session.BeginPlay(false, LightVolumeShaderFeatures.PointLights, LightVolumeShaderFeatures.None);
            session.SetStrippingEnabled(false);
            session.BeginBuild(avatarBuild, LightVolumeShaderFeatures.SpotLights);

            Assert.That(session.GetFeatures(true), Is.EqualTo(avatarBuild ? LightVolumeShaderFeatures.All : LightVolumeShaderFeatures.SpotLights));
            session.EndBuild();
            Assert.That(session.StrippingEnabled, Is.False);
            Assert.That(session.GetFeatures(true), Is.EqualTo(LightVolumeShaderFeatures.All));
            session.SetStrippingEnabled(true);
            Assert.That(session.GetFeatures(true), Is.EqualTo(LightVolumeShaderFeatures.PointLights));
        }

        // A queued Inspector notification during compilation cannot rescan Auto or replace the profile restored afterwards.
        [TestCase(false)]
        [TestCase(true)]
        public void BuildIgnoresPlaySelectionChangesUntilItCompletes(bool automatic) {
            LightVolumeShaderFeatureSession session = new LightVolumeShaderFeatureSession();
            session.BeginPlay(false, LightVolumeShaderFeatures.PointLights, LightVolumeShaderFeatures.None);
            session.BeginBuild(false, LightVolumeShaderFeatures.SpotLights);
            int detections = 0;
            session.ChangePlaySelection(automatic, LightVolumeShaderFeatures.AreaLights, () => {
                detections++;
                return LightVolumeShaderFeatures.All;
            });

            Assert.That(detections, Is.Zero);
            Assert.That(session.Automatic, Is.False);
            Assert.That(session.GetFeatures(true), Is.EqualTo(LightVolumeShaderFeatures.SpotLights));
            session.EndBuild();
            Assert.That(session.GetFeatures(true), Is.EqualTo(LightVolumeShaderFeatures.PointLights));
        }

        // Stopping Play Mode during a build must not leave its stripped profile active after build cleanup.
        [Test]
        public void BuildCleanupAfterPlayExitRestoresEditModeSupport() {
            LightVolumeShaderFeatureSession session = new LightVolumeShaderFeatureSession();
            session.BeginPlay(false, LightVolumeShaderFeatures.PointLights, LightVolumeShaderFeatures.None);
            session.BeginBuild(false, LightVolumeShaderFeatures.SpotLights);
            session.EndPlay();
            session.EndBuild();

            Assert.That(session.IsPlaying, Is.False);
            Assert.That(session.IsBuilding, Is.False);
            Assert.That(session.GetFeatures(true), Is.EqualTo(LightVolumeShaderFeatures.All));
        }

        // Projects blocked by the Avatars SDK never consume a specialized profile, including during a scene build.
        [Test]
        public void DisallowedProjectsKeepEveryFeatureDuringPlayAndBuild() {
            LightVolumeShaderFeatureSession session = new LightVolumeShaderFeatureSession();
            session.BeginPlay(false, LightVolumeShaderFeatures.None, LightVolumeShaderFeatures.None);
            Assert.That(session.GetFeatures(false), Is.EqualTo(LightVolumeShaderFeatures.All));
            session.BeginBuild(false, LightVolumeShaderFeatures.None);
            Assert.That(session.GetFeatures(false), Is.EqualTo(LightVolumeShaderFeatures.All));
            session.EndBuild();
            Assert.That(session.GetFeatures(false), Is.EqualTo(LightVolumeShaderFeatures.All));
        }

        // Neither a manual Play Mode entry nor a build may compile child features without their parents.
        [Test]
        public void ManualEntryAndBuildNormalizeTheirRequestedMasks() {
            LightVolumeShaderFeatureSession session = new LightVolumeShaderFeatureSession();
            LightVolumeShaderFeatures selection = LightVolumeShaderFeatures.AreaLights | LightVolumeShaderFeatures.LightLuts | LightVolumeShaderFeatures.PointCookies | LightVolumeShaderFeatures.SingleSliceShadows;
            session.BeginPlay(false, selection, LightVolumeShaderFeatures.All);
            Assert.That(session.GetFeatures(true), Is.EqualTo(LightVolumeShaderFeatures.AreaLights));
            session.BeginBuild(false, selection);
            Assert.That(session.GetFeatures(true), Is.EqualTo(LightVolumeShaderFeatures.AreaLights));
        }
    }
}
