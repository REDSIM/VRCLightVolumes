using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VRCLightVolumes.Editor;

namespace VRCLightVolumes.Tests {
    [Category("Editor")]
    public class LightVolumeProbeBakingTests {
        private const float Epsilon = 0.0001f;
        private const float L1Coefficient = 1.65f;

        // One dark first probe must not make an L2 result look like Bakery L1 data.
        [Test]
        public void L2DetectionChecksEveryLightProbe() {
            SphericalHarmonicsL2[] probes = new SphericalHarmonicsL2[2];
            probes[0][0, 0] = 1f;
            probes[1][2, 8] = 0.000001f;

            Assert.That(LightVolumeBaker.HasL2ProbeData(probes), Is.True);

            probes[1][2, 8] = 0f;
            Assert.That(LightVolumeBaker.HasL2ProbeData(probes), Is.False);
        }

        [Test]
        public void DeclaredBakeryL2ModeSkipsL1FixWhenHigherBandsAreZero() {
            SphericalHarmonicsL2[] probes = new SphericalHarmonicsL2[1];
            probes[0][0, 0] = 1f;

            Assert.That(LightVolumeBaker.HasL2ProbeData(probes), Is.False);
            Assert.That(LightVolumeBaker.ShouldDeringLightProbes(true, true, probes), Is.False);
            Assert.That(LightVolumeBaker.ShouldDeringLightProbes(true, false, probes), Is.True);
        }

        // Matches LTCGI's material-driven registration, where a separate editor updater renders the target.
        [Test]
        public void ManagerEditorFacadeAcceptsMaterialProcessorWithoutCallback() {
            GameObject gameObject = new GameObject("LTCGI Style Post Processor Manager");
            RenderTexture target = new RenderTexture(4, 4, 0);
            Material material = null;
            try {
                Shader shader = Shader.Find("Hidden/InternalErrorShader");
                Assert.That(shader, Is.Not.Null);
                material = new Material(shader);
                LightVolumeManager manager = gameObject.AddComponent<LightVolumeManager>();

                manager.Editor.RegisterPostProcessor(new AtlasPostProcessor {
                    Target = target,
                    Material = material,
                    InputTextureProperty = "_LV_Volume"
                });

                Assert.That(manager.Editor.ContainsPostProcessor(target, material), Is.True);
            } finally {
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        // Destroyed integration targets must not leave their material and callback rooted by the
        // manager's serialized projection and transient post-processor cache.
        [Test]
        public void ManagerEditorFacadeCompactsDestroyedPostProcessorTargets() {
            GameObject gameObject = new GameObject("Destroyed Post Processor Manager");
            RenderTexture target = new RenderTexture(4, 4, 0);
            Material material = null;
            try {
                Shader shader = Shader.Find("Hidden/InternalErrorShader");
                Assert.That(shader, Is.Not.Null);
                material = new Material(shader);
                LightVolumeManager manager = gameObject.AddComponent<LightVolumeManager>();
                manager.Editor.RegisterPostProcessor(new AtlasPostProcessor {
                    Target = target,
                    Material = material,
                    InputTextureProperty = "_MainTex"
                });

                UnityEngine.Object.DestroyImmediate(target);

                Assert.That(manager.Editor.GetPostProcessors(), Is.Empty);
                Assert.That(manager.AtlasPostProcessorTargets, Is.Empty);
                Assert.That(manager.AtlasPostProcessorMaterials, Is.Empty);
                Assert.That(manager.AtlasPostProcessorTextureNames, Is.Empty);
            } finally {
                if (target != null) UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void ManagerEditorContextExposesPerManagerPostProcessorRefreshEvent() {
            GameObject gameObject = new GameObject("Post Processor Event Manager");
            try {
                LightVolumeManager manager = gameObject.AddComponent<LightVolumeManager>();
                int invocationCount = 0;
                Action callback = () => invocationCount++;

                manager.Editor.AtlasPostProcessorsChanged += callback;
                manager.Editor.RefreshPostProcessors();
                manager.Editor.AtlasPostProcessorsChanged -= callback;
                manager.Editor.RefreshPostProcessors();

                Assert.That(invocationCount, Is.EqualTo(1));
            } finally {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        // Any registered processor, including a regular RenderTexture used by LTCGI, minimizes atlas depth.
        [Test]
        public void AtlasPackingMinimizesDepthForAnyPostProcessorType() {
            GameObject gameObject = new GameObject("Post Processor Packing Manager");
            RenderTexture target = new RenderTexture(4, 4, 0);
            Material material = null;
            try {
                Shader shader = Shader.Find("Hidden/InternalErrorShader");
                Assert.That(shader, Is.Not.Null);
                material = new Material(shader);
                LightVolumeManager manager = gameObject.AddComponent<LightVolumeManager>();
                MethodInfo resolveStrategy = typeof(LightVolumeManagerEditorBackend).GetMethod("ResolveAtlasPackingStrategy", BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(resolveStrategy, Is.Not.Null);

                Assert.That(resolveStrategy.Invoke(null, new object[] { manager }), Is.EqualTo(TexturePackingStrategy.MinimumVRAM));

                manager.Editor.RegisterPostProcessor(new AtlasPostProcessor {
                    Target = target,
                    Material = material,
                    InputTextureProperty = "_LV_Volume"
                });

                Assert.That(resolveStrategy.Invoke(null, new object[] { manager }), Is.EqualTo(TexturePackingStrategy.MinimumDepth));
            } finally {
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        // Registration keeps the serializable processor projection aligned and unregister removes it atomically.
        [Test]
        public void ManagerEditorFacadePersistsUpdatesAndRemovesPostProcessor() {
            GameObject gameObject = new GameObject("Post Processor Manager");
            RenderTexture target = new RenderTexture(4, 4, 0);
            RenderTexture replacementTarget = new RenderTexture(4, 4, 0);
            Material material = null;
            try {
                Shader shader = Shader.Find("Hidden/InternalErrorShader");
                Assert.That(shader, Is.Not.Null);
                material = new Material(shader);
                LightVolumeManager manager = gameObject.AddComponent<LightVolumeManager>();
                Action update = () => { };
                AtlasPostProcessor processor = new AtlasPostProcessor {
                    Target = target,
                    Material = material,
                    InputTextureProperty = "_AtlasInput",
                    Update = update
                };

                manager.Editor.RegisterPostProcessor(processor);

                Assert.That(manager.Editor.GetPostProcessors(), Has.Length.EqualTo(1));
                Assert.That(manager.AtlasPostProcessorTargets, Is.EqualTo(new[] { target }));
                Assert.That(manager.AtlasPostProcessorMaterials, Is.EqualTo(new[] { material }));
                Assert.That(manager.AtlasPostProcessorTextureNames, Is.EqualTo(new[] { "_AtlasInput" }));

                processor.Target = replacementTarget;
                manager.Editor.RegisterPostProcessor(processor);

                Assert.That(manager.AtlasPostProcessorTargets, Is.EqualTo(new[] { replacementTarget }));
                Assert.That(manager.AtlasPostProcessorMaterials, Is.EqualTo(new[] { material }));
                Assert.That(manager.AtlasPostProcessorTextureNames, Is.EqualTo(new[] { "_AtlasInput" }));

                processor.InputTextureProperty = "_UpdatedInput";
                manager.Editor.RegisterPostProcessor(processor);

                Assert.That(manager.AtlasPostProcessorTargets, Has.Length.EqualTo(1));
                Assert.That(manager.AtlasPostProcessorMaterials, Has.Length.EqualTo(1));
                Assert.That(manager.AtlasPostProcessorTextureNames, Is.EqualTo(new[] { "_UpdatedInput" }));

                manager.Editor.UnregisterPostProcessor(new AtlasPostProcessor { Update = update });

                Assert.That(manager.AtlasPostProcessorTargets, Is.Empty);
                Assert.That(manager.AtlasPostProcessorMaterials, Is.Empty);
                Assert.That(manager.AtlasPostProcessorTextureNames, Is.Empty);
                Assert.That(manager.Editor.GetPostProcessors(), Is.Empty);
            } finally {
                target.Release();
                replacementTarget.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(replacementTarget);
                UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        // Editor-only callback state belongs to the manager object and cannot leak between managers.
        [Test]
        public void ManagerEditorFacadeKeepsTransientProcessorsIsolatedPerManager() {
            GameObject firstObject = new GameObject("First Post Processor Owner");
            GameObject secondObject = new GameObject("Second Post Processor Owner");
            RenderTexture firstTarget = new RenderTexture(4, 4, 0);
            RenderTexture secondTarget = new RenderTexture(4, 4, 0);
            try {
                LightVolumeManager first = firstObject.AddComponent<LightVolumeManager>();
                LightVolumeManager second = secondObject.AddComponent<LightVolumeManager>();
                Action firstUpdate = () => { };
                Action secondUpdate = () => { };

                first.Editor.RegisterPostProcessor(new AtlasPostProcessor { Target = firstTarget, Update = firstUpdate });
                second.Editor.RegisterPostProcessor(new AtlasPostProcessor { Target = secondTarget, Update = secondUpdate });

                Assert.That(first.Editor.GetPostProcessors(), Has.Length.EqualTo(1));
                Assert.That(first.Editor.GetPostProcessors()[0].Update, Is.EqualTo(firstUpdate));
                Assert.That(second.Editor.GetPostProcessors(), Has.Length.EqualTo(1));
                Assert.That(second.Editor.GetPostProcessors()[0].Update, Is.EqualTo(secondUpdate));

                first.Editor.UnregisterPostProcessor(new AtlasPostProcessor { Update = firstUpdate });
                Assert.That(first.Editor.GetPostProcessors(), Is.Empty);
                Assert.That(second.Editor.GetPostProcessors(), Has.Length.EqualTo(1));
            } finally {
                firstTarget.Release();
                secondTarget.Release();
                UnityEngine.Object.DestroyImmediate(firstTarget);
                UnityEngine.Object.DestroyImmediate(secondTarget);
                UnityEngine.Object.DestroyImmediate(firstObject);
                UnityEngine.Object.DestroyImmediate(secondObject);
            }
        }

        // The Manager API passes each processor the previous output and keeps the old 3D atlas target format.
        [Test]
        public void ManagerEditorFacadeRunsPostProcessorChainInRegistrationOrder() {
            GameObject gameObject = new GameObject("Post Processor Chain Manager");
            Texture3D atlas = new Texture3D(3, 4, 5, TextureFormat.RGBAHalf, false);
            RenderTexture firstTarget = new RenderTexture(1, 1, 0);
            RenderTexture secondTarget = new RenderTexture(1, 1, 0);
            try {
                LightVolumeManager manager = gameObject.AddComponent<LightVolumeManager>();
                Texture firstInput = null;
                Texture secondInput = null;
                manager.LightVolumeAtlasBase = atlas;
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) {
                    LogAssert.Expect(LogType.Error, "RenderTexture.Create failed: volume texture not supported.");
                    LogAssert.Expect(LogType.Error, "RenderTexture.Create failed: volume texture not supported.");
                    LogAssert.Expect(LogType.Error, "RenderTexture.Create failed: volume texture not supported.");
                }

                manager.Editor.RegisterPostProcessor(new AtlasPostProcessor {
                    Target = firstTarget,
                    UpdateWithInput = input => firstInput = input
                });
                manager.Editor.RegisterPostProcessor(new AtlasPostProcessor {
                    Target = secondTarget,
                    UpdateWithInput = input => secondInput = input
                });

                Assert.That(firstInput, Is.SameAs(atlas));
                Assert.That(secondInput, Is.SameAs(firstTarget));
                Assert.That(manager.LightVolumeAtlas, Is.SameAs(secondTarget));
                Assert.That(firstTarget.dimension, Is.EqualTo(TextureDimension.Tex3D));
                Assert.That(firstTarget.width, Is.EqualTo(3));
                Assert.That(firstTarget.height, Is.EqualTo(4));
                Assert.That(firstTarget.volumeDepth, Is.EqualTo(5));
                Assert.That(secondTarget.dimension, Is.EqualTo(TextureDimension.Tex3D));
                Assert.That(secondTarget.width, Is.EqualTo(3));
                Assert.That(secondTarget.height, Is.EqualTo(4));
                Assert.That(secondTarget.volumeDepth, Is.EqualTo(5));
            } finally {
                firstTarget.Release();
                secondTarget.Release();
                UnityEngine.Object.DestroyImmediate(firstTarget);
                UnityEngine.Object.DestroyImmediate(secondTarget);
                UnityEngine.Object.DestroyImmediate(atlas);
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        // Verifies invalid dimensions, null SH, mismatched SH and mismatched validity fail without partial output.
        [Test]
        public void ProbeProcessingRejectsInvalidInputs() {
            Vector3[] one = { Vector3.one };

            AssertPrepareFails(one, one, one, one, null, 0, 1, 1);
            AssertPrepareFails(one, one, one, one, null, int.MaxValue, 2, 1);
            AssertPrepareFails(null, one, one, one, null, 1, 1, 1);
            AssertPrepareFails(one, one, one, Array.Empty<Vector3>(), null, 1, 1, 1);
            AssertPrepareFails(one, one, one, one, Array.Empty<float>(), 1, 1, 1);
        }

        // Verifies the no-validity/no-denoise path preserves L0 and packs every L1 component identically to Progressive.
        [Test]
        public void ProbeProcessingPacksRawProgressiveTextureChannels() {
            Vector3[] l0 = { new Vector3(1f, 2f, 3f) };
            Vector3[] l1r = { new Vector3(4f, 5f, 6f) };
            Vector3[] l1g = { new Vector3(7f, 8f, 9f) };
            Vector3[] l1b = { new Vector3(10f, 11f, 12f) };

            Assert.That(Prepare(l0, l1r, l1g, l1b, null, 1, 1, 1, 1, false, out Color[][] colors, out string error), Is.True, error);

            AssertColorClose(new Color(1f, 2f, 3f, 6f * L1Coefficient), colors[0][0]);
            AssertColorClose(new Color(4f, 7f, 10f, 9f) * L1Coefficient, colors[1][0]);
            AssertColorClose(new Color(5f, 8f, 11f, 12f) * L1Coefficient, colors[2][0]);
        }

        // Verifies validity dilation averages all valid neighbors for every SH channel without mutating caller data.
        [Test]
        public void ProbeProcessingDilatesAllChannelsWithoutMutatingInputs() {
            Vector3[] l0 = Scalars(1f, 99f, 3f);
            Vector3[] l1r = Scalars(10f, 99f, 30f);
            Vector3[] l1g = Scalars(100f, 99f, 300f);
            Vector3[] l1b = Scalars(1000f, 99f, 3000f);
            float[] validity = { 0f, 1f, 0f };
            Vector3[] originalL0 = (Vector3[])l0.Clone();
            Vector3[] originalL1r = (Vector3[])l1r.Clone();
            Vector3[] originalL1g = (Vector3[])l1g.Clone();
            Vector3[] originalL1b = (Vector3[])l1b.Clone();
            float[] originalValidity = (float[])validity.Clone();

            Assert.That(Prepare(l0, l1r, l1g, l1b, validity, 3, 1, 1, 1, false, out Color[][] colors, out string error), Is.True, error);

            AssertColorClose(new Color(2f, 2f, 2f, 20f * L1Coefficient), colors[0][1]);
            AssertColorClose(new Color(20f, 200f, 2000f, 200f) * L1Coefficient, colors[1][1]);
            AssertColorClose(new Color(20f, 200f, 2000f, 2000f) * L1Coefficient, colors[2][1]);
            Assert.That(l0, Is.EqualTo(originalL0));
            Assert.That(l1r, Is.EqualTo(originalL1r));
            Assert.That(l1g, Is.EqualTo(originalL1g));
            Assert.That(l1b, Is.EqualTo(originalL1b));
            Assert.That(validity, Is.EqualTo(originalValidity));
        }

        // Verifies dilation expands by one neighboring voxel per configured iteration.
        [Test]
        public void ProbeProcessingHonorsDilationIterationCount() {
            Vector3[] l0 = Scalars(4f, 0f, 0f, 0f, 0f);
            Vector3[] zero = new Vector3[5];
            float[] validity = { 0f, 1f, 1f, 1f, 1f };

            Assert.That(Prepare(l0, zero, zero, zero, validity, 5, 1, 1, 2, false, out Color[][] colors, out string error), Is.True, error);

            Assert.That(colors[0][0].r, Is.EqualTo(4f).Within(Epsilon));
            Assert.That(colors[0][1].r, Is.EqualTo(4f).Within(Epsilon));
            Assert.That(colors[0][2].r, Is.EqualTo(4f).Within(Epsilon));
            Assert.That(colors[0][3].r, Is.EqualTo(0f).Within(Epsilon));
        }

        // Verifies the 3x3x3 dilation neighborhood crosses X, Y and Z diagonals.
        [Test]
        public void ProbeProcessingDilatesAcrossThreeDimensionalDiagonals() {
            Vector3[] l0 = new Vector3[8];
            Vector3[] zero = new Vector3[8];
            float[] validity = { 0f, 1f, 1f, 1f, 1f, 1f, 1f, 1f };
            l0[0] = Vector3.one * 5f;

            Assert.That(Prepare(l0, zero, zero, zero, validity, 2, 2, 2, 1, false, out Color[][] colors, out string error), Is.True, error);

            Assert.That(colors[0][7].r, Is.EqualTo(5f).Within(Epsilon));
        }

        // Verifies equality with the validity threshold remains invalid and no-source voxels keep their original data.
        [Test]
        public void ProbeProcessingLeavesVoxelsWithoutValidNeighborsUnchanged() {
            Vector3[] l0 = Scalars(7f, 8f);
            Vector3[] zero = new Vector3[2];
            float[] validity = { 0.1f, 1f };

            Assert.That(Prepare(l0, zero, zero, zero, validity, 2, 1, 1, 1, false, out Color[][] colors, out string error), Is.True, error);

            Assert.That(colors[0][0].r, Is.EqualTo(7f).Within(Epsilon));
            Assert.That(colors[0][1].r, Is.EqualTo(8f).Within(Epsilon));
        }

        // Verifies supplying validity with zero iterations is an explicit no-dilation path.
        [Test]
        public void ProbeProcessingSkipsDilationWhenIterationCountIsZero() {
            Vector3[] l0 = Scalars(1f, 99f, 3f);
            Vector3[] zero = new Vector3[3];
            float[] validity = { 0f, 1f, 0f };

            Assert.That(Prepare(l0, zero, zero, zero, validity, 3, 1, 1, 0, false, out Color[][] colors, out string error), Is.True, error);

            Assert.That(colors[0][1].r, Is.EqualTo(99f).Within(Epsilon));
        }

        // Verifies optional denoise uses the same bilateral implementation as Progressive and preserves inputs.
        [Test]
        public void ProbeProcessingUsesProgressiveBilateralDenoise() {
            Vector3[] l0 = Scalars(0.1f, 0.11f, 0.14f);
            Vector3[] zero = new Vector3[3];
            Vector3[] original = (Vector3[])l0.Clone();
            Vector3[] expected = LVUtils.BilateralDenoise3D(l0, 3, 1, 1, 1f, 0.05f);

            Assert.That(Prepare(l0, zero, zero, zero, null, 3, 1, 1, 1, true, out Color[][] colors, out string error), Is.True, error);

            for (int i = 0; i < expected.Length; i++) AssertColorClose(new Color(expected[i].x, expected[i].y, expected[i].z, 0f), colors[0][i]);
            Assert.That(l0, Is.EqualTo(original));
        }

        // Verifies the combined overload contract performs dilation before the shared Progressive denoise.
        [Test]
        public void ProbeProcessingDilatesBeforeDenoising() {
            Vector3[] l0 = Scalars(0.1f, 99f, 0.14f);
            Vector3[] zero = new Vector3[3];
            float[] validity = { 0f, 1f, 0f };
            Vector3[] expected = LVUtils.BilateralDenoise3D(Scalars(0.1f, 0.12f, 0.14f), 3, 1, 1, 1f, 0.05f);

            Assert.That(Prepare(l0, zero, zero, zero, validity, 3, 1, 1, 1, true, out Color[][] colors, out string error), Is.True, error);

            for (int i = 0; i < expected.Length; i++) AssertColorClose(new Color(expected[i].x, expected[i].y, expected[i].z, 0f), colors[0][i]);
        }

        // A partial AssetDatabase failure must release every unadopted native texture and preserve
        // the previous channel whose destination could not be saved.
        [Test]
        public void CustomProbeBakeFailureDoesNotLeakTransientTextures() {
            string scenePath = AssetDatabase.GenerateUniqueAssetPath("Assets/VRCLightVolumesCustomBakeLeakTest.unity");
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            string sceneDataRootPath = null;
            string sceneDataPath = null;
            Texture3D previous0 = null;
            Texture3D previous1 = null;
            Texture3D previous2 = null;
            try {
                Assert.That(EditorSceneManager.SaveScene(scene, scenePath), Is.True);
                GameObject managerObject = new GameObject("Custom Bake Failure Manager");
                GameObject volumeObject = new GameObject("Custom Bake Failure Volume");
                SceneManager.MoveGameObjectToScene(managerObject, scene);
                SceneManager.MoveGameObjectToScene(volumeObject, scene);
                LightVolumeManager manager = managerObject.AddComponent<LightVolumeManager>();
                LightVolumeInstance volume = volumeObject.AddComponent<LightVolumeInstance>();
                volume.LightVolumeManager = manager;
                volume.Resolution = Vector3Int.one;
                manager.DilationIterations = 0;

                previous0 = CreateTransientProbeTexture("Previous Probe Texture 0");
                previous1 = CreateTransientProbeTexture("Previous Probe Texture 1");
                previous2 = CreateTransientProbeTexture("Previous Probe Texture 2");
                volume.Texture0 = previous0;
                volume.Texture1 = previous1;
                volume.Texture2 = previous2;

                sceneDataRootPath = Path.ChangeExtension(scenePath, null).Replace('\\', '/');
                sceneDataPath = $"{sceneDataRootPath}/VRCLightVolumes/Temp";
                string blockedAssetPath = $"{sceneDataPath}/{LVUtils.EscapeFileName(volumeObject.name)}_0.asset";
                Directory.CreateDirectory(blockedAssetPath);
                AssetDatabase.Refresh();
                int transientCountBefore = CountTransientTexture3D();
                Vector3[] l0 = { Vector3.one };
                Vector3[] zero = { Vector3.zero };

                LogAssert.Expect(LogType.Error, new Regex("^Can't create asset at .*Custom Bake Failure Volume_0\\.asset because it's a folder\\.$"));
                LogAssert.Expect(LogType.Error, new Regex("^\\[LightVolumes\\] Save failed:"));
                LogAssert.Expect(LogType.Error, $"[LightVolumes] Failed to persist every baked texture for light volume {volumeObject.name}. Transient texture objects were released.");
                bool saved = LightVolumeBaker.SaveCustomProbesBaked(volume, l0, zero, zero, zero, null, false);

                Assert.That(saved, Is.False);
                Assert.That(volume.Texture0, Is.SameAs(previous0));
                Assert.That(previous0 == null, Is.False);
                Assert.That(previous1 == null, Is.True);
                Assert.That(previous2 == null, Is.True);
                Assert.That(volume.Texture1, Is.Not.Null);
                Assert.That(volume.Texture2, Is.Not.Null);
                Assert.That(EditorUtility.IsPersistent(volume.Texture1), Is.True);
                Assert.That(EditorUtility.IsPersistent(volume.Texture2), Is.True);
                Assert.That(CountTransientTexture3D(), Is.EqualTo(transientCountBefore - 2));
            } finally {
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (previous0 != null) UnityEngine.Object.DestroyImmediate(previous0);
                if (previous1 != null) UnityEngine.Object.DestroyImmediate(previous1);
                if (previous2 != null) UnityEngine.Object.DestroyImmediate(previous2);
                if (!string.IsNullOrEmpty(sceneDataRootPath)
                    && sceneDataRootPath.StartsWith("Assets/VRCLightVolumesCustomBakeLeakTest", StringComparison.Ordinal)) {
                    AssetDatabase.DeleteAsset(sceneDataRootPath);
                }
                if (!string.IsNullOrEmpty(scenePath)) AssetDatabase.DeleteAsset(scenePath);
            }
        }

        // Verifies the threaded bilateral implementation matches the former sequential algorithm across depth slices.
        [Test]
        public void BilateralDenoise3DMatchesSequentialReference() {
            Vector3[] source = new Vector3[24];
            for (int i = 0; i < source.Length; i++) source[i] = new Vector3(i * 0.003f, (i % 5) * 0.007f, (i % 7) * 0.005f);

            Vector3[] expected = BilateralDenoiseReference(source, 4, 3, 2, 1f, 0.05f);
            Vector3[] result = LVUtils.BilateralDenoise3D(source, 4, 3, 2, 1f, 0.05f);

            Assert.That(result, Has.Length.EqualTo(source.Length));
            for (int i = 0; i < result.Length; i++) AssertVectorClose(expected[i], result[i]);
        }

        // Runs the original sequential bilateral algorithm as a multithreading regression reference.
        private static Vector3[] BilateralDenoiseReference(Vector3[] input, int w, int h, int d, float sigmaSpatial, float sigmaRange) {
            Vector3[] output = new Vector3[input.Length];
            int radius = Mathf.CeilToInt(2f * sigmaSpatial);

            for (int z = 0; z < d; z++)
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++) {
                        int centerIndex = x + y * w + z * w * h;
                        Vector3 center = input[centerIndex];
                        Vector3 sum = Vector3.zero;
                        float weightSum = 0f;

                        for (int dz = -radius; dz <= radius; dz++)
                            for (int dy = -radius; dy <= radius; dy++)
                                for (int dx = -radius; dx <= radius; dx++) {
                                    int neighborX = x + dx;
                                    int neighborY = y + dy;
                                    int neighborZ = z + dz;
                                    if (neighborX < 0 || neighborY < 0 || neighborZ < 0 || neighborX >= w || neighborY >= h || neighborZ >= d) continue;

                                    Vector3 neighbor = input[neighborX + neighborY * w + neighborZ * w * h];
                                    float spatialWeight = Mathf.Exp(-(dx * dx + dy * dy + dz * dz) / (2f * sigmaSpatial * sigmaSpatial));
                                    float rangeWeight = Mathf.Exp(-(neighbor - center).sqrMagnitude / (2f * sigmaRange * sigmaRange));
                                    float weight = spatialWeight * rangeWeight;
                                    sum += neighbor * weight;
                                    weightSum += weight;
                                }

                        output[centerIndex] = weightSum > 0f ? sum / weightSum : center;
                    }

            return output;
        }

        // Creates one non-persistent Texture3D owned by the test until the bake replaces it.
        private static Texture3D CreateTransientProbeTexture(string name) {
            Texture3D texture = new Texture3D(1, 1, 1, TextureFormat.RGBAHalf, false) { name = name };
            texture.SetPixels(new[] { Color.clear });
            texture.Apply(false);
            return texture;
        }

        // Counts live non-asset Texture3D objects to catch native allocations that lose their owner.
        private static int CountTransientTexture3D() {
            Texture3D[] textures = Resources.FindObjectsOfTypeAll<Texture3D>();
            int count = 0;
            for (int i = 0; i < textures.Length; i++) {
                if (textures[i] != null && !EditorUtility.IsPersistent(textures[i])) count++;
            }
            return count;
        }

        // Calls the shared probe processor using the standard dilation threshold.
        private static bool Prepare(Vector3[] l0, Vector3[] l1r, Vector3[] l1g, Vector3[] l1b, float[] validity, int w, int h, int d, int iterations, bool denoise, out Color[][] colors, out string error) {
            return LVUtils.TryPrepareLightVolumeProbeData(l0, l1r, l1g, l1b, validity, w, h, d, iterations, 0.1f, denoise, out colors, out error);
        }

        // Asserts one invalid processor input fails with a diagnostic and no texture data.
        private static void AssertPrepareFails(Vector3[] l0, Vector3[] l1r, Vector3[] l1g, Vector3[] l1b, float[] validity, int w, int h, int d) {
            bool result = Prepare(l0, l1r, l1g, l1b, validity, w, h, d, 1, false, out Color[][] colors, out string error);

            Assert.That(result, Is.False);
            Assert.That(colors, Is.Null);
            Assert.That(error, Is.Not.Empty);
        }

        // Returns the requested scalar values replicated into Vector3 SH entries.
        private static Vector3[] Scalars(params float[] values) {
            Vector3[] result = new Vector3[values.Length];
            for (int i = 0; i < values.Length; i++) result[i] = Vector3.one * values[i];
            return result;
        }

        // Asserts colors with the shared test tolerance.
        private static void AssertColorClose(Color expected, Color actual) {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(Epsilon));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(Epsilon));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(Epsilon));
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(Epsilon));
        }

        // Asserts vectors with the shared test tolerance.
        private static void AssertVectorClose(Vector3 expected, Vector3 actual) {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(Epsilon));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(Epsilon));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(Epsilon));
        }
    }
}
