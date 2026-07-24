using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace VRCLightVolumes.Tests {
    [Category("Editor")]
    public class LightVolumeProbeBakingTests {
        private const float Epsilon = 0.0001f;
        private const float L1Coefficient = 1.65f;

        // The editor integration type may move files, but its public manager-oriented contract stays stable.
        [Test]
        public void ManagerEditorApiExposesProbeAndBakeSurface() {
            Type manager = typeof(LightVolumeManager);
            Type api = typeof(LightVolumeManagerTools);
            Type vectorArray = typeof(Vector3[]);
            Type[] baseParameters = { typeof(int), vectorArray, vectorArray, vectorArray, vectorArray };
            Type[] denoiseParameters = { typeof(int), vectorArray, vectorArray, vectorArray, vectorArray, typeof(bool) };
            Type[] validityParameters = { typeof(int), vectorArray, vectorArray, vectorArray, vectorArray, typeof(float[]) };
            Type[] combinedParameters = { typeof(int), vectorArray, vectorArray, vectorArray, vectorArray, typeof(float[]), typeof(bool) };

            Assert.That(GetCustomBakeMethod(baseParameters), Is.Not.Null);
            Assert.That(GetCustomBakeMethod(denoiseParameters), Is.Not.Null);
            Assert.That(GetCustomBakeMethod(validityParameters), Is.Not.Null);
            Assert.That(GetCustomBakeMethod(combinedParameters), Is.Not.Null);
            Assert.That(api.GetMethod("GetCustomProbesCount", new[] { manager }), Is.Not.Null);
            Assert.That(api.GetMethod("GetCustomProbes", new[] { manager, typeof(int) }), Is.Not.Null);
            Assert.That(api.GetMethod("GenerateAtlas", new[] { manager }), Is.Not.Null);
            Assert.That(api.GetMethod("BakeShadowMaps", new[] { manager }), Is.Not.Null);
            Assert.That(api.GetMethod("RegisterPostProcessorCRT", new[] { manager, typeof(CustomRenderTexture) }), Is.Not.Null);
            Assert.That(api.GetMethod("RegisterPostProcessor", new[] { manager, typeof(LightVolumeManager.PostProcessor) }), Is.Not.Null);
            Assert.That(api.GetMethod("UnregisterPostProcessorCRT", new[] { manager, typeof(CustomRenderTexture) }), Is.Not.Null);
            Assert.That(api.GetMethod("UnregisterPostProcessor", new[] { manager, typeof(RenderTexture) }), Is.Not.Null);
            Assert.That(api.GetMethod("UnregisterPostProcessor", new[] { manager, typeof(LightVolumeManager.PostProcessor) }), Is.Not.Null);
        }

        // Registration keeps the serializable processor projection aligned and unregister removes it atomically.
        [Test]
        public void ManagerEditorApiPersistsUpdatesAndRemovesPostProcessor() {
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
                LightVolumeManager.PostProcessor processor = new LightVolumeManager.PostProcessor {
                    RT = target,
                    Mat = material,
                    TextureName = "_AtlasInput",
                    Update = update
                };

                manager.RegisterPostProcessor(processor);

                Assert.That(manager.AtlasPostProcessorTargets, Is.EqualTo(new[] { target }));
                Assert.That(manager.AtlasPostProcessorMaterials, Is.EqualTo(new[] { material }));
                Assert.That(manager.AtlasPostProcessorTextureNames, Is.EqualTo(new[] { "_AtlasInput" }));

                processor.RT = replacementTarget;
                manager.RegisterPostProcessor(processor);

                Assert.That(manager.AtlasPostProcessorTargets, Is.EqualTo(new[] { replacementTarget }));
                Assert.That(manager.AtlasPostProcessorMaterials, Is.EqualTo(new[] { material }));
                Assert.That(manager.AtlasPostProcessorTextureNames, Is.EqualTo(new[] { "_AtlasInput" }));

                processor.TextureName = "_UpdatedInput";
                manager.RegisterPostProcessor(processor);

                Assert.That(manager.AtlasPostProcessorTargets, Has.Length.EqualTo(1));
                Assert.That(manager.AtlasPostProcessorMaterials, Has.Length.EqualTo(1));
                Assert.That(manager.AtlasPostProcessorTextureNames, Is.EqualTo(new[] { "_UpdatedInput" }));

                manager.UnregisterPostProcessor(new LightVolumeManager.PostProcessor { Update = update });

                Assert.That(manager.AtlasPostProcessorTargets, Is.Empty);
                Assert.That(manager.AtlasPostProcessorMaterials, Is.Empty);
                Assert.That(manager.AtlasPostProcessorTextureNames, Is.Empty);
            } finally {
                target.Release();
                replacementTarget.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(replacementTarget);
                UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        // A recycled instance ID cannot inherit callbacks registered by a different manager owner.
        [Test]
        public void ManagerEditorApiRejectsTransientProcessorsOwnedByAnotherManager() {
            GameObject staleOwnerObject = new GameObject("Stale Post Processor Owner");
            GameObject managerObject = new GameObject("Current Post Processor Owner");
            RenderTexture staleTarget = new RenderTexture(4, 4, 0);
            RenderTexture currentTarget = new RenderTexture(4, 4, 0);
            Dictionary<int, LightVolumeManager.PostProcessor[]> processors = null;
            Dictionary<int, LightVolumeManager> owners = null;
            int managerId = 0;
            try {
                LightVolumeManager staleOwner = staleOwnerObject.AddComponent<LightVolumeManager>();
                LightVolumeManager manager = managerObject.AddComponent<LightVolumeManager>();
                managerId = manager.GetInstanceID();
                processors = (Dictionary<int, LightVolumeManager.PostProcessor[]>)typeof(LightVolumeManagerTools)
                    .GetField("_atlasPostProcessors", BindingFlags.Static | BindingFlags.NonPublic)
                    .GetValue(null);
                owners = (Dictionary<int, LightVolumeManager>)typeof(LightVolumeManagerTools)
                    .GetField("_atlasPostProcessorOwners", BindingFlags.Static | BindingFlags.NonPublic)
                    .GetValue(null);
                Action staleUpdate = () => { };
                Action currentUpdate = () => { };
                processors[managerId] = new[] {
                    new LightVolumeManager.PostProcessor { RT = staleTarget, Update = staleUpdate }
                };
                owners[managerId] = staleOwner;

                manager.RegisterPostProcessor(new LightVolumeManager.PostProcessor {
                    RT = currentTarget,
                    Update = currentUpdate
                });

                Assert.That(owners[managerId], Is.SameAs(manager));
                Assert.That(processors[managerId], Has.Length.EqualTo(1));
                Assert.That(processors[managerId][0].RT, Is.SameAs(currentTarget));

                manager.UnregisterPostProcessor(new LightVolumeManager.PostProcessor { Update = currentUpdate });
                Assert.That(processors.ContainsKey(managerId), Is.False);
                Assert.That(owners.ContainsKey(managerId), Is.False);
            } finally {
                if (managerId != 0) {
                    processors?.Remove(managerId);
                    owners?.Remove(managerId);
                }
                staleTarget.Release();
                currentTarget.Release();
                UnityEngine.Object.DestroyImmediate(staleTarget);
                UnityEngine.Object.DestroyImmediate(currentTarget);
                UnityEngine.Object.DestroyImmediate(staleOwnerObject);
                UnityEngine.Object.DestroyImmediate(managerObject);
            }
        }

        // Verifies invalid dimensions, null SH, mismatched SH and mismatched validity fail without partial output.
        [Test]
        public void ProbeProcessingRejectsInvalidInputs() {
            Vector3[] one = { Vector3.one };

            AssertPrepareFails("Resolution is invalid or the voxel count is too large.", one, one, one, one, null, 0, 1, 1);
            AssertPrepareFails("Resolution is invalid or the voxel count is too large.", one, one, one, one, null, int.MaxValue, 2, 1);
            AssertPrepareFails("SH arrays cannot be null.", null, one, one, one, null, 1, 1, 1);
            AssertPrepareFails("Every SH array must contain exactly 1 elements.", one, one, one, Array.Empty<Vector3>(), null, 1, 1, 1);
            AssertPrepareFails("The validity array must contain exactly 1 elements.", one, one, one, one, Array.Empty<float>(), 1, 1, 1);
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

        // Calls the shared probe processor using the standard dilation threshold.
        private static bool Prepare(Vector3[] l0, Vector3[] l1r, Vector3[] l1g, Vector3[] l1b, float[] validity, int w, int h, int d, int iterations, bool denoise, out Color[][] colors, out string error) {
            return LVUtils.TryPrepareLightVolumeProbeData(l0, l1r, l1g, l1b, validity, w, h, d, iterations, 0.1f, denoise, out colors, out error);
        }

        // Asserts one invalid processor input produces the expected error and no texture data.
        private static void AssertPrepareFails(string expectedError, Vector3[] l0, Vector3[] l1r, Vector3[] l1g, Vector3[] l1b, float[] validity, int w, int h, int d) {
            bool result = Prepare(l0, l1r, l1g, l1b, validity, w, h, d, 1, false, out Color[][] colors, out string error);

            Assert.That(result, Is.False);
            Assert.That(colors, Is.Null);
            Assert.That(error, Is.EqualTo(expectedError));
        }

        // Returns the requested scalar values replicated into Vector3 SH entries.
        private static Vector3[] Scalars(params float[] values) {
            Vector3[] result = new Vector3[values.Length];
            for (int i = 0; i < values.Length; i++) result[i] = Vector3.one * values[i];
            return result;
        }

        // Resolves one exact public SetCustomProbesBaked overload.
        private static MethodInfo GetCustomBakeMethod(Type[] parameters) {
            Type[] extensionParameters = new Type[parameters.Length + 1];
            extensionParameters[0] = typeof(LightVolumeManager);
            Array.Copy(parameters, 0, extensionParameters, 1, parameters.Length);
            return typeof(LightVolumeManagerTools).GetMethod("SetCustomProbesBaked", extensionParameters);
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
