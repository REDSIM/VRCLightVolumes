using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VRCLightVolumes.Tests {
    [Category("Editor")]
    public class LightVolumeOverdrawPreviewTests {
        private const int MaxLights = 128;
        private const int MaxVolumes = 32;
        private const float ColorTolerance = 0.0006f;
        private Material _material;
        private Mesh _mesh;
        private RenderTexture _target;
        private Texture2D _readback;
        private RenderTexture _clusterMask;
        private Material _clusterMaterial;
        private Vector4[] _lightPositions;
        private Vector4[] _lightColors;
        private Vector4[] _lightData;
        private Matrix4x4[] _volumeMatrices;

        [SetUp]
        public void SetUp() {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null
                    || SystemInfo.graphicsShaderLevel < 35
                    || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat)
                    || !SystemInfo.SupportsTextureFormat(TextureFormat.RGBAFloat))
                Assert.Ignore("The active graphics API cannot render the overdraw preview test.");

            Shader shader = Shader.Find("Hidden/LV_DebugDisplayOverdraw");
            Assert.That(shader, Is.Not.Null);
            Assert.That(shader.isSupported, Is.True, "The overdraw preview shader must compile on this graphics API.");
            _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            _mesh.vertices = new[] {
                new Vector3(-1, -1, 0), new Vector3(-1, 1, 0),
                new Vector3(1, 1, 0), new Vector3(1, -1, 0)
            };
            // Both windings keep the sample independent of the render-target projection convention.
            _mesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 2, 1, 0, 3, 2, 0 };
            _target = new RenderTexture(3, 3, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear) {
                hideFlags = HideFlags.HideAndDontSave
            };
            Assert.That(_target.Create(), Is.True);
            _readback = new Texture2D(3, 3, TextureFormat.RGBAFloat, false, true) {
                hideFlags = HideFlags.HideAndDontSave
            };

            _lightPositions = new Vector4[MaxLights];
            _lightColors = new Vector4[MaxLights];
            _lightData = new Vector4[MaxLights];
            _volumeMatrices = new Matrix4x4[MaxVolumes];
            Vector4[] edgeSmooth = new Vector4[MaxVolumes];
            for (int i = 0; i < MaxLights; i++) {
                _lightPositions[i] = new Vector4(0, 0, 0.25f, 1);
                _lightColors[i] = new Vector4(1, 1, 1, 0);
                _lightData[i] = new Vector4(0, 0, 1, 0);
            }
            for (int i = 0; i < MaxVolumes; i++) {
                _volumeMatrices[i] = Matrix4x4.identity;
                edgeSmooth[i] = new Vector4(10, 10, 10, 0);
            }

            // Material overrides isolate these draws from the loaded scene's shader globals.
            _material.SetFloat("_UdonLightVolumeEnabled", 1);
            _material.SetFloat("_UdonLightVolumeVersion", 3);
            _material.SetFloat("_UdonLightVolumeCount", 0);
            _material.SetFloat("_UdonLightVolumeAdditiveCount", 0);
            _material.SetFloat("_UdonPointLightVolumeCount", 0);
            _material.SetFloat("_UdonLightVolumeAdditiveMaxOverdraw", MaxLights);
            _material.SetFloat("_UdonLightVolumeProbesBlend", 1);
            _material.SetFloat("_UdonLightVolumeSharpBounds", 0);
            _material.SetFloat("_UdonClusteringEnabled", 0);
            _material.SetVectorArray("_UdonLightVolumeInvLocalEdgeSmooth", edgeSmooth);
            _material.SetVectorArray("_UdonPointLightVolumeDirection", new Vector4[MaxLights]);
            _material.SetVectorArray("_UdonPointLightVolumeExtraData", new Vector4[MaxLights]);
        }

        [TearDown]
        public void TearDown() {
            if (_clusterMaterial != null) Object.DestroyImmediate(_clusterMaterial);
            if (_clusterMask != null) Object.DestroyImmediate(_clusterMask);
            if (_readback != null) Object.DestroyImmediate(_readback);
            if (_target != null) Object.DestroyImmediate(_target);
            if (_mesh != null) Object.DestroyImmediate(_mesh);
            if (_material != null) Object.DestroyImmediate(_material);
        }

        [Test]
        public void EmptySceneIsBlack() {
            AssertCount(0);
        }

        [TestCase(0, 3)]
        [TestCase(1, 1)]
        public void DisabledOrUnsupportedVolumesAreBlack(float enabled, float version) {
            SetCounts(8, 3, 2);
            _material.SetFloat("_UdonLightVolumeEnabled", enabled);
            _material.SetFloat("_UdonLightVolumeVersion", version);
            AssertCount(0);
        }

        [Test]
        public void InitialOverlapIsVisibleAndBrightnessStepsDecrease() {
            SetCounts(1, 0, 0);
            float first = AssertCount(1).r;
            SetCounts(2, 0, 0);
            float second = AssertCount(2).r;
            SetCounts(16, 0, 0);
            float sixteenth = AssertCount(16).r;
            SetCounts(17, 0, 0);
            float seventeenth = AssertCount(17).r;

            Assert.That(first, Is.GreaterThan(1f / 162));
            Assert.That(second - first, Is.GreaterThan(seventeenth - sixteenth));
        }

        [TestCase(40, 0)]
        [TestCase(20, 20)]
        [TestCase(8, 32)]
        public void FortyOverlapsReachEightyPercentIntensity(int lights, int additiveVolumes) {
            SetCounts(lights, additiveVolumes, additiveVolumes);
            Assert.That(RenderCenter().r, Is.EqualTo(0.8f).Within(ColorTolerance));
        }

        [Test]
        public void BrightnessGrowthSlowsAfterFortyOverlaps() {
            SetCounts(40, 0, 0);
            float atForty = RenderCenter().r;
            SetCounts(80, 0, 0);
            float atEighty = RenderCenter().r;
            SetCounts(120, 0, 0);
            float atOneTwenty = RenderCenter().r;

            Assert.That(atEighty, Is.GreaterThan(atForty));
            Assert.That(atOneTwenty, Is.GreaterThan(atEighty).And.LessThan(1));
            Assert.That(atOneTwenty - atEighty, Is.LessThan(atEighty - atForty));
        }

        [Test]
        public void PointLightRangeRejectsDoNotConsumeTheBudget() {
            SetCounts(2, 0, 0);
            _lightPositions[0] = new Vector4(2, 0, 0, 1);
            _material.SetFloat("_UdonLightVolumeAdditiveMaxOverdraw", 1);
            AssertCount(1);

            _lightPositions[1] = new Vector4(-2, 0, 0, 1);
            AssertCount(0);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(3)]
        public void PointLightOverdrawRespectsTheConfiguredCap(int cap) {
            SetCounts(8, 0, 0);
            _material.SetFloat("_UdonLightVolumeAdditiveMaxOverdraw", cap);
            AssertCount(cap);
        }

        [Test]
        public void PointLightCountDoesNotDependOnBrightness() {
            SetCounts(1, 0, 0);
            _lightColors[0] = new Vector4(0.001f, 0.001f, 0.001f, 0);
            Color dim = AssertCount(1);
            _lightColors[0] = new Vector4(100, 100, 100, 0);
            Color bright = AssertCount(1);
            Assert.That(bright.r, Is.EqualTo(dim.r).Within(ColorTolerance));
        }

        [Test]
        public void RegularVolumesCountTheSecondSampleOnlyAtTheBlendBoundary() {
            SetCounts(0, 3, 0);
            AssertCount(1);

            _volumeMatrices[0] = Matrix4x4.Translate(new Vector3(0.45f, 0, 0));
            AssertCount(2);
        }

        [TestCase(true, 0)]
        [TestCase(false, 1)]
        public void OutsideRegularVolumesCountsTheSelectedFallback(bool blendProbes, int expectedCount) {
            SetCounts(0, 2, 0);
            _volumeMatrices[0] = Matrix4x4.Translate(Vector3.right);
            _volumeMatrices[1] = Matrix4x4.Translate(Vector3.left);
            _material.SetFloat("_UdonLightVolumeProbesBlend", blendProbes ? 1 : 0);
            AssertCount(expectedCount);
        }

        [TestCase(true, false, 1)]
        [TestCase(false, true, 1)]
        [TestCase(false, false, 2)]
        public void RegularBoundaryFallbackCountsOnlyVolumeSamples(bool blendProbes, bool sharpBounds, int expectedCount) {
            SetCounts(0, 1, 0);
            _volumeMatrices[0] = Matrix4x4.Translate(new Vector3(0.45f, 0, 0));
            _material.SetFloat("_UdonLightVolumeProbesBlend", blendProbes ? 1 : 0);
            _material.SetFloat("_UdonLightVolumeSharpBounds", sharpBounds ? 1 : 0);
            AssertCount(expectedCount);
        }

        [Test]
        public void AdditiveBoundsRejectsDoNotConsumeTheBudget() {
            SetCounts(0, 3, 3);
            _volumeMatrices[0] = Matrix4x4.Translate(Vector3.right);
            _material.SetFloat("_UdonLightVolumeAdditiveMaxOverdraw", 1);
            AssertCount(1);

            _volumeMatrices[1] = Matrix4x4.Translate(Vector3.right);
            _volumeMatrices[2] = Matrix4x4.Translate(Vector3.right);
            AssertCount(0);
        }

        [Test]
        public void PointsAdditiveVolumesAndRegularBlendAccumulateTogether() {
            SetCounts(3, 4, 2);
            _volumeMatrices[2] = Matrix4x4.Translate(new Vector3(0.45f, 0, 0));
            AssertCount(7);
        }

        [Test]
        public void PointAndAdditiveBudgetsApplyIndependently() {
            SetCounts(5, 5, 5);
            _material.SetFloat("_UdonLightVolumeAdditiveMaxOverdraw", 2);
            AssertCount(4);
        }

        [TestCase(30)]
        [TestCase(32)]
        public void FullBuffersUseTheFixed162SampleReference(int additiveCount) {
            SetCounts(MaxLights, MaxVolumes, additiveCount);
            if (additiveCount == 30)
                _volumeMatrices[30] = Matrix4x4.Translate(new Vector3(0.45f, 0, 0));
            // The current 32-volume buffer is shared by regular and additive volumes.
            AssertCount(160);
        }

        [Test]
        public void ClusteringCountsOnlyCandidatesAndFallsBackOutsideTheCamera() {
            GraphicsFormat format = GraphicsFormat.R32G32B32A32_SInt;
            if (!SystemInfo.IsFormatSupported(format, FormatUsage.Render))
                Assert.Ignore("The active graphics API cannot render integer cluster masks.");

            _clusterMask = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBInt, RenderTextureReadWrite.Linear) {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point
            };
            Assert.That(_clusterMask.Create(), Is.True);
            Shader maskShader = Shader.Find("Hidden/VRCLV/Tests/OverdrawClusterMask");
            Assert.That(maskShader, Is.Not.Null);
            Assert.That(maskShader.isSupported, Is.True);
            _clusterMaterial = new Material(maskShader) { hideFlags = HideFlags.HideAndDontSave };
            FillClusterMask(1, 1 << 31);
            _material.SetTexture("_UdonClusterMask", _clusterMask);
            _material.SetFloat("_UdonClusteringEnabled", 1);
            _material.SetVector("_UdonFroxelGrid", new Vector4(1, 1, 1, 0));
            _material.SetVector("_UdonFroxelDepth", new Vector4(0.1f, 10, 10, 1));
            _material.SetVector("_UdonFroxelProjection", new Vector4(1, 1, 0, 0));
            _material.SetVector("_UdonFroxelRight", new Vector4(1, 0, 0, 0));
            _material.SetVector("_UdonFroxelUp", new Vector4(0, 1, 0, 0));
            _material.SetVector("_UdonFroxelForward", new Vector4(0, 0, 1, -1));
            SetCounts(MaxLights, 0, 0);
            AssertCount(2);

            FillClusterMask(0, 0);
            AssertCount(0);

            _material.SetVector("_UdonFroxelForward", new Vector4(0, 0, 1, 1));
            AssertCount(MaxLights);
        }

        private void FillClusterMask(int firstWord, int lastWord) {
            RenderTexture previousTarget = RenderTexture.active;
            try {
                _clusterMaterial.SetInteger("_FirstWord", firstWord);
                _clusterMaterial.SetInteger("_LastWord", lastWord);
                Graphics.Blit(null, _clusterMask, _clusterMaterial);
            } finally {
                RenderTexture.active = previousTarget;
            }
        }

        private void SetCounts(int lights, int volumes, int additiveVolumes) {
            _material.SetFloat("_UdonPointLightVolumeCount", lights);
            _material.SetFloat("_UdonLightVolumeCount", volumes);
            _material.SetFloat("_UdonLightVolumeAdditiveCount", additiveVolumes);
        }

        private Color AssertCount(int count) {
            Color actual = RenderCenter();
            float load = count / 162f;
            float intensity = (1 - Mathf.Pow(2, -9.3685586f * load)) / (1 - Mathf.Pow(2, -9.3685586f));
            Assert.That(actual.r, Is.EqualTo(intensity).Within(ColorTolerance), "Red encodes the nonlinear sample-count intensity.");
            Assert.That(actual.g, Is.EqualTo(intensity * Mathf.Lerp(0.4f, 1, load * load)).Within(ColorTolerance));
            Assert.That(actual.b, Is.EqualTo(intensity * Mathf.Lerp(0.2f, 1, load * load)).Within(ColorTolerance));
            Assert.That(actual.a, Is.EqualTo(1).Within(ColorTolerance));
            return actual;
        }

        private Color RenderCenter() {
            _material.SetVectorArray("_UdonPointLightVolumePosition", _lightPositions);
            _material.SetVectorArray("_UdonPointLightVolumeColor", _lightColors);
            _material.SetVectorArray("_UdonPointLightVolumeCustomID", _lightData);
            _material.SetMatrixArray("_UdonLightVolumeInvWorldMatrix", _volumeMatrices);

            RenderTexture previousTarget = RenderTexture.active;
            CommandBuffer commands = new CommandBuffer { name = "Light Volumes overdraw preview test" };
            try {
                Matrix4x4 view = Matrix4x4.Scale(new Vector3(1, 1, -1))
                    * Matrix4x4.Translate(Vector3.forward);
                Matrix4x4 projection = GL.GetGPUProjectionMatrix(
                    Matrix4x4.Ortho(-0.01f, 0.01f, -0.01f, 0.01f, 0.1f, 10), true);
                commands.SetRenderTarget(_target);
                commands.SetViewport(new Rect(0, 0, 3, 3));
                commands.ClearRenderTarget(true, true, Color.magenta);
                commands.SetViewProjectionMatrices(view, projection);
                commands.DrawMesh(_mesh, Matrix4x4.Scale(Vector3.one * 0.01f), _material, 0, 0);
                Graphics.ExecuteCommandBuffer(commands);
                RenderTexture.active = _target;
                _readback.ReadPixels(new Rect(0, 0, 3, 3), 0, 0, false);
                _readback.Apply(false, false);
                return _readback.GetPixel(1, 1);
            } finally {
                commands.Release();
                RenderTexture.active = previousTarget;
            }
        }
    }
}
