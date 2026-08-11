using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace VRCLightVolumes.Tests {
    [Category("Editor")]
    public class LightVolumeEditorPipelineTests {

        private const float Epsilon = 0.0001f;
        private const string TargetSwitchUdonSharpGuard = "#if !UDONSHARP && (UNITY_EDITOR || COMPILER_UDONSHARP)\n#define UDONSHARP\n#endif";

        private static readonly string[] UdonSharpSourcePaths = {
            "Packages/red.sim.lightvolumes/UScripts/LightVolumeManager.cs",
            "Packages/red.sim.lightvolumes/UScripts/LightVolumeManager.Buffers.cs",
            "Packages/red.sim.lightvolumes/UScripts/LightVolumeManager.Clustering.cs",
            "Packages/red.sim.lightvolumes/UScripts/LightVolumeManager.Core.cs",
            "Packages/red.sim.lightvolumes/UScripts/LightVolumeManager.Textures.cs",
            "Packages/red.sim.lightvolumes/UScripts/LightVolumeInstance.cs",
            "Packages/red.sim.lightvolumes/UScripts/PointLightVolumeInstance.cs",
            "Packages/red.sim.lightvolumes/Extra/Audio Link/LightVolumeAudioLink.cs",
            "Packages/red.sim.lightvolumes/Extra/TV Global Illumination/LightVolumeTVGI.cs",
            "Packages/red.sim.lightvolumes/Extra/Shadow Runtime Baker/PointLightShadowRuntimeBaker.cs"
        };

        private GameObject _legacyObject;
        private GameObject _unifiedObject;

        [TearDown]
        public void TearDown() {
            if (_legacyObject != null) UnityEngine.Object.DestroyImmediate(_legacyObject);
            if (_unifiedObject != null) UnityEngine.Object.DestroyImmediate(_unifiedObject);
        }

        // The unified Udon component owns every persistent Light Volume authoring value.
        [Test]
        public void MigrationCopiesLegacyLightVolumePersistentData() {
            _legacyObject = new GameObject("Legacy Light Volume");
            _unifiedObject = new GameObject("Unified Light Volume");
            LightVolume legacy = _legacyObject.AddComponent<LightVolume>();
            LightVolumeInstance unified = _unifiedObject.AddComponent<LightVolumeInstance>();
            Texture3D texture0 = new Texture3D(1, 1, 1, TextureFormat.RGBAHalf, false);
            Texture3D texture1 = new Texture3D(1, 1, 1, TextureFormat.RGBAHalf, false);
            Texture3D texture2 = new Texture3D(1, 1, 1, TextureFormat.RGBAHalf, false);

            legacy.Dynamic = true;
            legacy.Additive = true;
            legacy.Color = new Color(0.25f, 0.5f, 0.75f);
            legacy.Intensity = 2.5f;
            legacy.SmoothBlending = 0.4f;
            legacy.Texture0 = texture0;
            legacy.Texture1 = texture1;
            legacy.Texture2 = texture2;
            legacy.Exposure = 1.25f;
            legacy.Shadows = -0.2f;
            legacy.Highlights = 0.3f;
            legacy.Bake = false;
            legacy.ReserveUVSpace = true;
            legacy.AdaptiveResolution = false;
            legacy.VoxelsPerUnit = 4.5f;
            legacy.Resolution = new Vector3Int(7, 8, 9);

            MethodInfo copy = typeof(LightVolumeMigration).GetMethod("CopyLegacyLightVolume", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(copy, Is.Not.Null);
            copy.Invoke(null, new object[] { legacy, unified });

            Assert.That(unified.IsDynamic, Is.True);
            Assert.That(unified.IsAdditive, Is.True);
            Assert.That(unified.Color, Is.EqualTo(legacy.Color));
            Assert.That(unified.Intensity, Is.EqualTo(legacy.Intensity).Within(Epsilon));
            Assert.That(unified.SmoothBlending, Is.EqualTo(legacy.SmoothBlending).Within(Epsilon));
            Assert.That(unified.Texture0, Is.SameAs(texture0));
            Assert.That(unified.Texture1, Is.SameAs(texture1));
            Assert.That(unified.Texture2, Is.SameAs(texture2));
            Assert.That(unified.Exposure, Is.EqualTo(legacy.Exposure).Within(Epsilon));
            Assert.That(unified.Shadows, Is.EqualTo(legacy.Shadows).Within(Epsilon));
            Assert.That(unified.Highlights, Is.EqualTo(legacy.Highlights).Within(Epsilon));
            Assert.That(unified.Bake, Is.False);
            Assert.That(unified.ReserveUVSpace, Is.True);
            Assert.That(unified.AdaptiveResolution, Is.False);
            Assert.That(unified.VoxelsPerUnit, Is.EqualTo(legacy.VoxelsPerUnit).Within(Epsilon));
            Assert.That(unified.Resolution, Is.EqualTo(legacy.Resolution));

            UnityEngine.Object.DestroyImmediate(texture0);
            UnityEngine.Object.DestroyImmediate(texture1);
            UnityEngine.Object.DestroyImmediate(texture2);
        }

        // UdonSharp adds its project define after a new build-target group has already reloaded once.
        // Every U# source must keep the U# branch active during that Editor reload and direct U# compilation.
        [Test]
        public void UdonSharpSourcesKeepStableProxyTypesDuringTargetDefineInitialization() {
            for (int i = 0; i < UdonSharpSourcePaths.Length; i++) {
                string path = UdonSharpSourcePaths[i];
                string source = File.ReadAllText(path).Replace("\r\n", "\n");
                Assert.That(source, Does.StartWith(TargetSwitchUdonSharpGuard),
                    path + " can temporarily change its serialized proxy layout during a build-target switch.");
            }
        }

        // Optional plugins must never become hard dependencies of the VRCLV core or stale global-define gates.
        [Test]
        public void OptionalPluginAssembliesRemainConditionalAndCoreIndependent() {
            const string audioLinkGuid = "58281da7f948e9644aceb5d0178bf06b";
            const string bakeryRuntimeGuid = "a1653399f63795746b1857281d1e400d";
            const string bakeryEditorGuid = "290dd5870d0ead646bcb6ea5c6a60af5";
            string[] coreAsmdefs = {
                "Packages/red.sim.lightvolumes/UScripts/red.sim.LightVolumesUdon.asmdef",
                "Packages/red.sim.lightvolumes/Scripts/red.sim.LightVolumes.asmdef",
                "Packages/red.sim.lightvolumes/Scripts/Editor/red.sim.LightVolumesEditor.asmdef"
            };
            for (int i = 0; i < coreAsmdefs.Length; i++) {
                string asmdef = File.ReadAllText(coreAsmdefs[i]);
                Assert.That(asmdef, Does.Not.Contain(audioLinkGuid), coreAsmdefs[i]);
                Assert.That(asmdef, Does.Not.Contain(bakeryRuntimeGuid), coreAsmdefs[i]);
                Assert.That(asmdef, Does.Not.Contain(bakeryEditorGuid), coreAsmdefs[i]);
            }

            const string optionalAsmdefPath = "Packages/red.sim.lightvolumes/Extra/Audio Link/red.sim.LightVolumes.AudioLinkUdon.asmdef";
            const string optionalAssemblyAssetPath = "Packages/red.sim.lightvolumes/Extra/Audio Link/red.sim.LightVolumes.AudioLinkUdon.asset";
            string optionalAsmdef = File.ReadAllText(optionalAsmdefPath);
            Assert.That(optionalAsmdef, Does.Contain("com.llealloo.audiolink"));
            Assert.That(optionalAsmdef, Does.Contain("VRCLV_AUDIOLINK"));
            Assert.That(optionalAsmdef, Does.Contain("\"defineConstraints\""));
            Assert.That(optionalAsmdef, Does.Contain("\"versionDefines\""));
            Assert.That(optionalAsmdef, Does.Contain(audioLinkGuid));
            Assert.That(File.Exists(optionalAssemblyAssetPath), Is.True);
            Assert.That(File.ReadAllText(optionalAssemblyAssetPath), Does.Contain(AssetDatabase.AssetPathToGUID(optionalAsmdefPath)));
            Assert.That(File.Exists("Packages/red.sim.lightvolumes/Extra/Audio Link/UdonLightVolumesRef.asmref"), Is.False);

            string[] productionRoots = {
                "Packages/red.sim.lightvolumes/UScripts",
                "Packages/red.sim.lightvolumes/Scripts",
                "Packages/red.sim.lightvolumes/Extra"
            };
            for (int rootIndex = 0; rootIndex < productionRoots.Length; rootIndex++) {
                string[] sources = Directory.GetFiles(productionRoots[rootIndex], "*.cs", SearchOption.AllDirectories);
                for (int i = 0; i < sources.Length; i++) {
                    string source = File.ReadAllText(sources[i]);
                    Assert.That(source, Does.Not.Contain("BAKERY_INCLUDED"), sources[i]);
                    Assert.That(source, Does.Not.Contain("#if AUDIOLINK"), sources[i]);
                    Assert.That(source, Does.Not.Contain("#elif AUDIOLINK"), sources[i]);
                }
            }
        }

        // A unique co-located Udon component is authoritative even when an obsolete serialized link points elsewhere.
        [Test]
        public void MigrationResolverUsesUniqueCoLocatedDestination() {
            _legacyObject = new GameObject("Legacy Pair Source");
            _unifiedObject = new GameObject("Foreign Unified Destination");
            LightVolume legacy = _legacyObject.AddComponent<LightVolume>();
            LightVolumeInstance attached = _legacyObject.AddComponent<LightVolumeInstance>();
            LightVolumeInstance foreign = _unifiedObject.AddComponent<LightVolumeInstance>();
            MethodInfo resolve = typeof(LightVolumeMigration).GetMethod(
                "ResolveLightVolumeInstance",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(resolve, Is.Not.Null);

            legacy.LightVolumeInstance = foreign;
            Assert.That(resolve.Invoke(null, new object[] { legacy }), Is.SameAs(attached));

            legacy.LightVolumeInstance = attached;
            Assert.That(resolve.Invoke(null, new object[] { legacy }), Is.SameAs(attached));
        }

        // Resolving an incomplete old payload is read-only and must never create a replacement Udon component.
        [Test]
        public void MigrationResolverDoesNotCreateMissingDestination() {
            _legacyObject = new GameObject("Incomplete Migration Source");
            LightVolume legacy = _legacyObject.AddComponent<LightVolume>();
            MethodInfo resolve = typeof(LightVolumeMigration).GetMethod(
                "ResolveLightVolumeInstance",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(resolve, Is.Not.Null);
            Assert.That(resolve.Invoke(null, new object[] { legacy }), Is.Null);
            Assert.That(_legacyObject.GetComponents<LightVolumeInstance>(), Is.Empty);
        }

        // Applying the same compatibility payload twice must produce the same unified serialized state.
        [Test]
        public void MigrationPointLightPayloadCopyIsIdempotent() {
            _legacyObject = new GameObject("Point Light Migration Source");
            _unifiedObject = new GameObject("Point Light Migration Destination");
            PointLightVolume source = _legacyObject.AddComponent<PointLightVolume>();
            PointLightVolumeInstance destination = _unifiedObject.AddComponent<PointLightVolumeInstance>();
            Texture2D cookie = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            source.Dynamic = true;
            source.Type = PointLightVolume.LightType.SpotLight;
            source.Projection = PointLightVolume.LightProjection.Custom;
            source.Cookie = cookie;
            source.Range = 17f;
            source.Intensity = 3.5f;
            source.Angle = 42f;
            source.Falloff = 0.6f;
            source.Shadows = true;
            source.BakeInGame = true;
            MethodInfo copy = typeof(LightVolumeMigration).GetMethod(
                "CopyLegacyPointLight",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(copy, Is.Not.Null);
            copy.Invoke(null, new object[] { source, destination });
            destination.EditorApplyAuthoringData(true, true, false);
            string firstState = JsonUtility.ToJson(destination);
            copy.Invoke(null, new object[] { source, destination });
            destination.EditorApplyAuthoringData(true, true, false);

            Assert.That(JsonUtility.ToJson(destination), Is.EqualTo(firstState));
            Assert.That(destination.LightType, Is.EqualTo(1));
            Assert.That(destination.Projection, Is.EqualTo(2));
            Assert.That(destination.ProjectionMode, Is.EqualTo(2));
            Assert.That(destination.Cookie, Is.SameAs(cookie));
            Assert.That(destination.CustomTexture, Is.SameAs(cookie));
            Assert.That(destination.Intensity, Is.EqualTo(3.5f).Within(Epsilon));
            UnityEngine.Object.DestroyImmediate(cookie);
        }

        // The affected upgrade retained a matching effective runtime source while losing the duplicate cookie field.
        [Test]
        public void MigrationRecoversMissingSpotCookieFromMatchingLegacyRuntimeState() {
            _legacyObject = new GameObject("Partially Deserialized Point Light Source");
            _unifiedObject = new GameObject("Working Legacy Runtime Destination");
            PointLightVolume source = _legacyObject.AddComponent<PointLightVolume>();
            PointLightVolumeInstance destination = _unifiedObject.AddComponent<PointLightVolumeInstance>();
            Texture2D cookie = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            source.Type = PointLightVolume.LightType.SpotLight;
            source.Projection = PointLightVolume.LightProjection.Custom;
            destination.LightType = 1;
            destination.ProjectionMode = 2;
            destination.ProjectionType = 1;
            destination.CustomTexture = cookie;
            MethodInfo copy = typeof(LightVolumeMigration).GetMethod(
                "CopyLegacyPointLight",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(copy, Is.Not.Null);
            copy.Invoke(null, new object[] { source, destination });
            destination.EditorApplyAuthoringData(true, true, false);

            Assert.That(destination.LightType, Is.EqualTo(1));
            Assert.That(destination.Projection, Is.EqualTo(2));
            Assert.That(destination.ProjectionMode, Is.EqualTo(2));
            Assert.That(destination.Cookie, Is.SameAs(cookie));
            Assert.That(destination.CustomTexture, Is.SameAs(cookie));
            UnityEngine.Object.DestroyImmediate(cookie);
        }

        // Runtime fields are derived cache and must not override a different legacy authoring type or mode.
        [Test]
        public void MigrationDoesNotImportMismatchedLegacyRuntimeProjectionState() {
            _legacyObject = new GameObject("Authoritative Parametric Point Source");
            _unifiedObject = new GameObject("Stale Spot Cookie Runtime Destination");
            PointLightVolume source = _legacyObject.AddComponent<PointLightVolume>();
            PointLightVolumeInstance destination = _unifiedObject.AddComponent<PointLightVolumeInstance>();
            Texture2D staleCookie = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            destination.LightType = 1;
            destination.ProjectionMode = 2;
            destination.ProjectionType = 1;
            destination.CustomTexture = staleCookie;
            MethodInfo copy = typeof(LightVolumeMigration).GetMethod(
                "CopyLegacyPointLight",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(copy, Is.Not.Null);
            copy.Invoke(null, new object[] { source, destination });
            destination.EditorApplyAuthoringData(true, true, false);

            Assert.That(destination.LightType, Is.Zero);
            Assert.That(destination.Projection, Is.Zero);
            Assert.That(destination.ProjectionMode, Is.Zero);
            Assert.That(destination.GetProjectionSource(), Is.Null);
            Assert.That(destination.CustomTexture, Is.Null);
            UnityEngine.Object.DestroyImmediate(staleCookie);
        }

        [Test]
        public void LegacyObjectMaskIsNotAliasedToExclusionMask() {
            FieldInfo field = typeof(PointLightVolume).GetField(nameof(PointLightVolume.ExclusionMask));
            object[] aliases = field?.GetCustomAttributes(typeof(UnityEngine.Serialization.FormerlySerializedAsAttribute), false);
            bool aliasesObjectMask = false;
            if (aliases != null) {
                for (int i = 0; i < aliases.Length; i++) {
                    UnityEngine.Serialization.FormerlySerializedAsAttribute alias =
                        (UnityEngine.Serialization.FormerlySerializedAsAttribute)aliases[i];
                    if (alias.oldName == "ObjectMask") aliasesObjectMask = true;
                }
            }

            Assert.That(field, Is.Not.Null);
            Assert.That(aliasesObjectMask, Is.False);
        }

        [Test]
        public void MigrationPreservesProjectionSourcesForEveryLightTypeAndMode() {
            MethodInfo copy = typeof(LightVolumeMigration).GetMethod(
                "CopyLegacyPointLight",
                BindingFlags.Static | BindingFlags.NonPublic);
            Texture2D lut = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Texture2D cookie = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Cubemap cubemap = new Cubemap(2, TextureFormat.RGBA32, false);
            Assert.That(copy, Is.Not.Null);

            AssertProjectionCopy(copy, PointLightVolume.LightType.PointLight, PointLightVolume.LightProjection.Parametric, null, 0);
            AssertProjectionCopy(copy, PointLightVolume.LightType.PointLight, PointLightVolume.LightProjection.LUT, lut, 1);
            AssertProjectionCopy(copy, PointLightVolume.LightType.PointLight, PointLightVolume.LightProjection.Custom, cubemap, 2);
            AssertProjectionCopy(copy, PointLightVolume.LightType.SpotLight, PointLightVolume.LightProjection.Parametric, null, 0);
            AssertProjectionCopy(copy, PointLightVolume.LightType.SpotLight, PointLightVolume.LightProjection.LUT, lut, 1);
            AssertProjectionCopy(copy, PointLightVolume.LightType.SpotLight, PointLightVolume.LightProjection.Custom, cookie, 2);
            AssertProjectionCopy(copy, PointLightVolume.LightType.AreaLight, PointLightVolume.LightProjection.Parametric, null, 0);
            AssertProjectionCopy(copy, PointLightVolume.LightType.AreaLight, PointLightVolume.LightProjection.Parametric, cookie, 2);

            UnityEngine.Object.DestroyImmediate(lut);
            UnityEngine.Object.DestroyImmediate(cookie);
            UnityEngine.Object.DestroyImmediate(cubemap);
        }

        [Test]
        public void MigrationPreservesShadowMapForEveryLightTypeAndWhenShadowsAreDisabled() {
            MethodInfo copy = typeof(LightVolumeMigration).GetMethod(
                "CopyLegacyPointLight",
                BindingFlags.Static | BindingFlags.NonPublic);
            Texture2D shadowMap = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Assert.That(copy, Is.Not.Null);

            AssertShadowCopy(copy, PointLightVolume.LightType.PointLight, shadowMap, true, true);
            AssertShadowCopy(copy, PointLightVolume.LightType.SpotLight, shadowMap, true, false);
            AssertShadowCopy(copy, PointLightVolume.LightType.AreaLight, shadowMap, true, true);
            AssertShadowCopy(copy, PointLightVolume.LightType.SpotLight, shadowMap, false, false);

            UnityEngine.Object.DestroyImmediate(shadowMap);
        }

        // Resolution validation belongs to unified authoring and rejects invalid or overflowing grids.
        [Test]
        public void UnifiedVoxelCountRejectsInvalidAndOverflowingResolution() {
            Assert.That(LightVolumeTools.GetVoxelCount(new Vector3Int(2, 3, 4)), Is.EqualTo(24));
            Assert.That(LightVolumeTools.GetVoxelCount(new Vector3Int(0, 3, 4)), Is.EqualTo(-1));
            Assert.That(LightVolumeTools.GetVoxelCount(new Vector3Int(int.MaxValue, 2, 2)), Is.EqualTo(-1));
        }

        // Destroyed Unity texture wrappers still satisfy C# type patterns and must be rejected with
        // Unity's overloaded null comparison before their native properties are read.
        [Test]
        public void ManagerStatsIgnoreDestroyedShadowTextures() {
            MethodInfo getTextureTexels = typeof(LightVolumeManagerEditor).GetMethod(
                "GetTextureTexels",
                BindingFlags.Static | BindingFlags.NonPublic);
            Cubemap destroyedCubemap = new Cubemap(4, TextureFormat.RGBAHalf, false);
            Assert.That(getTextureTexels, Is.Not.Null);
            UnityEngine.Object.DestroyImmediate(destroyedCubemap);

            Assert.That((ulong)getTextureTexels.Invoke(null, new object[] { destroyedCubemap }), Is.Zero);
        }

        // Two same-name lights receive separate shadow assets, while a rebake keeps the path already
        // owned by that light.
        [Test]
        public void ShadowBakePathsDoNotCollideForSameNameLights() {
            MethodInfo resolvePath = typeof(PointLightShadowBaker).GetMethod(
                "ResolveShadowAssetPath",
                BindingFlags.Static | BindingFlags.NonPublic);
            string firstPath = AssetDatabase.GenerateUniqueAssetPath("Assets/VRCLightVolumesShadowPathTest.asset");
            GameObject firstObject = new GameObject("Point Light");
            GameObject secondObject = new GameObject("Point Light");
            Texture2D firstShadow = new Texture2D(2, 2, TextureFormat.RGBAHalf, false);

            try {
                Assert.That(resolvePath, Is.Not.Null);
                AssetDatabase.CreateAsset(firstShadow, firstPath);
                PointLightVolumeInstance first = firstObject.AddComponent<PointLightVolumeInstance>();
                PointLightVolumeInstance second = secondObject.AddComponent<PointLightVolumeInstance>();
                first.ShadowMap = firstShadow;

                string rebakePath = (string)resolvePath.Invoke(null, new object[] { first, firstPath });
                string secondPath = (string)resolvePath.Invoke(null, new object[] { second, firstPath });

                Assert.That(rebakePath, Is.EqualTo(firstPath));
                Assert.That(secondPath, Is.Not.EqualTo(firstPath));
            } finally {
                UnityEngine.Object.DestroyImmediate(firstObject);
                UnityEngine.Object.DestroyImmediate(secondObject);
                AssetDatabase.DeleteAsset(firstPath);
            }
        }

        // Re-baking copies the new pixels into the already assigned native asset so its complete
        // Unity identity and references held by other lights remain valid.
        [TestCase(false)]
        [TestCase(true)]
        public void ShadowRebakePreservesAssetIdentityAndSharedReferences(bool cubemap) {
            MethodInfo saveAtPath = typeof(PointLightShadowBaker).GetMethod(
                "SaveShadowAssetAtPath",
                BindingFlags.Static | BindingFlags.NonPublic);
            string assetKind = cubemap ? "Cubemap" : "Texture";
            string path = AssetDatabase.GenerateUniqueAssetPath($"Assets/VRCLightVolumesShadow{assetKind}IdentityTest.asset");
            GameObject ownerObject = new GameObject("Shadow Owner");
            GameObject observerObject = new GameObject("Shadow Observer");
            Texture existingShadow = cubemap
                ? (Texture)new Cubemap(2, TextureFormat.RGBAHalf, false)
                : new Texture2D(2, 2, TextureFormat.RGBAHalf, false, true);
            existingShadow.name = "Stable Shadow";
            existingShadow.filterMode = FilterMode.Bilinear;
            Texture replacementShadow = null;

            try {
                Assert.That(saveAtPath, Is.Not.Null);
                SetShadowPixels(existingShadow, Color.black);
                AssetDatabase.CreateAsset(existingShadow, path);
                string assetNameBefore = existingShadow.name;

                PointLightVolumeInstance owner = ownerObject.AddComponent<PointLightVolumeInstance>();
                PointLightVolumeInstance observer = observerObject.AddComponent<PointLightVolumeInstance>();
                owner.ShadowMap = existingShadow;
                observer.ShadowMap = existingShadow;

                Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(existingShadow, out string guidBefore, out long localIdBefore), Is.True);

                replacementShadow = cubemap
                    ? (Texture)new Cubemap(4, TextureFormat.RGBAHalf, false)
                    : new Texture2D(4, 4, TextureFormat.RGBAHalf, false, true);
                replacementShadow.name = "Temporary Replacement";
                replacementShadow.filterMode = FilterMode.Point;
                SetShadowPixels(replacementShadow, Color.white);

                UnityEngine.Object savedShadow = saveAtPath.Invoke(null, new object[] { replacementShadow, path }) as UnityEngine.Object;
                owner.ShadowMap = savedShadow;

                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                Texture reloadedShadow = AssetDatabase.LoadAssetAtPath<Texture>(path);
                Assert.That(reloadedShadow, Is.Not.Null);
                Assert.That(reloadedShadow.GetType(), Is.EqualTo(existingShadow.GetType()));
                Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(reloadedShadow, out string guidAfter, out long localIdAfter), Is.True);
                Assert.That(guidAfter, Is.EqualTo(guidBefore));
                Assert.That(localIdAfter, Is.EqualTo(localIdBefore));
                Assert.That(owner.ShadowMap, Is.EqualTo(reloadedShadow));
                Assert.That(observer.ShadowMap, Is.EqualTo(reloadedShadow));
                Assert.That(reloadedShadow.name, Is.EqualTo(assetNameBefore));
                Assert.That(reloadedShadow.width, Is.EqualTo(4));
                Assert.That(reloadedShadow.height, Is.EqualTo(4));
                Assert.That(reloadedShadow.filterMode, Is.EqualTo(FilterMode.Point));
                Color firstPixel = reloadedShadow is Cubemap reloadedCubemap
                    ? reloadedCubemap.GetPixel(CubemapFace.PositiveX, 0, 0)
                    : ((Texture2D)reloadedShadow).GetPixel(0, 0);
                Assert.That(firstPixel.r, Is.EqualTo(1f).Within(1f / 255f));
            } finally {
                UnityEngine.Object.DestroyImmediate(ownerObject);
                UnityEngine.Object.DestroyImmediate(observerObject);
                if (replacementShadow != null) UnityEngine.Object.DestroyImmediate(replacementShadow);
                AssetDatabase.DeleteAsset(path);
            }
        }

        private static void SetShadowPixels(Texture texture, Color color) {
            Color[] pixels = new Color[texture.width * texture.height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;

            if (texture is Cubemap cubemap) {
                for (int i = 0; i < 6; i++) cubemap.SetPixels(pixels, (CubemapFace)i);
                cubemap.Apply(false);
                return;
            }

            Texture2D texture2D = (Texture2D)texture;
            texture2D.SetPixels(pixels);
            texture2D.Apply(false);
        }

        private static void AssertProjectionCopy(MethodInfo copy, PointLightVolume.LightType lightType, PointLightVolume.LightProjection projection, UnityEngine.Object sourceObject, int expectedMode) {
            GameObject legacyObject = new GameObject($"Legacy {lightType} {projection}");
            GameObject unifiedObject = new GameObject($"Unified {lightType} {projection}");
            try {
                PointLightVolume source = legacyObject.AddComponent<PointLightVolume>();
                PointLightVolumeInstance destination = unifiedObject.AddComponent<PointLightVolumeInstance>();
                source.Type = lightType;
                source.Projection = projection;
                if (lightType == PointLightVolume.LightType.AreaLight) source.Cookie = sourceObject;
                else if (projection == PointLightVolume.LightProjection.LUT) source.FalloffLUT = sourceObject;
                else if (lightType == PointLightVolume.LightType.PointLight) source.Cubemap = sourceObject;
                else source.Cookie = sourceObject;

                copy.Invoke(null, new object[] { source, destination });
                destination.EditorApplyAuthoringData(true, true, false);

                Assert.That(destination.LightType, Is.EqualTo((int)lightType), $"{lightType} {projection} type");
                Assert.That(destination.ProjectionMode, Is.EqualTo(expectedMode), $"{lightType} {projection} mode");
                Assert.That(destination.GetProjectionSource(), Is.SameAs(sourceObject), $"{lightType} {projection} source");
                Assert.That(destination.CustomTexture, Is.SameAs(sourceObject as Texture), $"{lightType} {projection} runtime texture");
            } finally {
                UnityEngine.Object.DestroyImmediate(legacyObject);
                UnityEngine.Object.DestroyImmediate(unifiedObject);
            }
        }

        private static void AssertShadowCopy(MethodInfo copy, PointLightVolume.LightType lightType, UnityEngine.Object shadowMap, bool shadows, bool expectedCubemap) {
            GameObject legacyObject = new GameObject($"Legacy {lightType} Shadows {shadows}");
            GameObject unifiedObject = new GameObject($"Unified {lightType} Shadows {shadows}");
            try {
                PointLightVolume source = legacyObject.AddComponent<PointLightVolume>();
                PointLightVolumeInstance destination = unifiedObject.AddComponent<PointLightVolumeInstance>();
                source.Type = lightType;
                source.Shadows = shadows;
                source.ShadowMap = shadowMap;

                copy.Invoke(null, new object[] { source, destination });
                destination.EditorApplyAuthoringData(true, true, false);

                Assert.That(destination.LightType, Is.EqualTo((int)lightType));
                Assert.That(destination.Shadows, Is.EqualTo(shadows));
                Assert.That(destination.ShadowMap, Is.SameAs(shadowMap), $"{lightType} authoring shadow source");
                Assert.That(destination.ShadowMapTexture, shadows ? Is.SameAs(shadowMap) : Is.Null, $"{lightType} runtime shadow source");
                Assert.That(destination.ShadowMapID, shadows ? Is.Zero : Is.EqualTo(-1f));
                Assert.That(destination.ShadowMapUsesCubemap, Is.EqualTo(shadows && expectedCubemap));
            } finally {
                UnityEngine.Object.DestroyImmediate(legacyObject);
                UnityEngine.Object.DestroyImmediate(unifiedObject);
            }
        }
    }
}
