using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace VRCLightVolumes.Tests {
    [Category("Editor")]
    public class PointLightVolumeHandleTests {
        private const float Tolerance = 0.00001f;
        private readonly List<GameObject> _objects = new List<GameObject>();
        private readonly List<Object> _assets = new List<Object>();

        [TearDown]
        public void TearDown() {
            for (int i = _objects.Count - 1; i >= 0; i--)
                if (_objects[i] != null) Object.DestroyImmediate(_objects[i]);
            _objects.Clear();
            for (int i = _assets.Count - 1; i >= 0; i--)
                if (_assets[i] != null) Object.DestroyImmediate(_assets[i]);
            _assets.Clear();
        }

        [Test]
        public void CornerResizeKeepsTheOppositeCornerFixed() {
            Rect result = PointLightVolumeEditor.ResizeAreaRect(
                new Rect(-2f, -1f, 4f, 2f), Vector2.one, new Vector2(4f, 3f), false, false);

            AssertRect(result, new Rect(-2f, -1f, 6f, 4f));
        }

        [Test]
        public void EdgeResizeKeepsTheOppositeEdgeAndOtherDimensionFixed() {
            Rect result = PointLightVolumeEditor.ResizeAreaRect(
                new Rect(-2f, -1f, 4f, 2f), Vector2.left, new Vector2(-4f, 100f), false, false);

            AssertRect(result, new Rect(-4f, -1f, 6f, 2f));
        }

        [Test]
        public void SymmetricResizeKeepsTheOriginalCenterFixed() {
            Rect result = PointLightVolumeEditor.ResizeAreaRect(
                new Rect(1f, 2f, 4f, 2f), Vector2.one, new Vector2(6f, 5f), true, false);

            AssertRect(result, new Rect(0f, 1f, 6f, 4f));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ProportionalCornerResizeUsesTheOriginalDiagonal(bool fromCenter) {
            Rect result = PointLightVolumeEditor.ResizeAreaRect(
                new Rect(-2f, -1f, 4f, 2f), Vector2.one, new Vector2(4f, 3f), fromCenter, true);

            Rect expected = fromCenter
                ? new Rect(-4.4f, -2.2f, 8.8f, 4.4f)
                : new Rect(-2f, -1f, 6.4f, 3.2f);
            AssertRect(result, expected);
        }

        [Test]
        public void DragPastTheAnchorClampsSizeWithoutFlipping() {
            Rect start = new Rect(-2f, -1f, 4f, 2f);
            Vector2 movedPosition = new Vector2(-5f, -5f);

            Rect freeResult = PointLightVolumeEditor.ResizeAreaRect(start, Vector2.one, movedPosition, false, false);
            Rect proportionalResult = PointLightVolumeEditor.ResizeAreaRect(start, Vector2.one, movedPosition, false, true);

            AssertRect(freeResult, new Rect(-2f, -1f, 0.001f, 0.001f));
            AssertRect(proportionalResult, new Rect(-2f, -1f, 0.002f, 0.001f));
        }

        [Test]
        public void ApplyAreaRectPreservesRotationAndScaleSignsUnderAScaledParent() {
            Transform parent = CreateTransform("Area Handle Parent");
            parent.position = new Vector3(-4f, 2f, 7f);
            parent.rotation = Quaternion.Euler(20f, -35f, 15f);
            parent.localScale = new Vector3(-2f, 3f, 0.75f);
            Transform light = CreateTransform("Area Handle Light");
            light.SetParent(parent, false);
            light.localPosition = new Vector3(1f, -2f, 0.5f);
            light.localRotation = Quaternion.Euler(-25f, 40f, 10f);
            light.localScale = new Vector3(-1.3f, -0.8f, -0.6f);
            Quaternion originalRotation = light.rotation;
            Matrix4x4 frame = Matrix4x4.TRS(light.position, originalRotation, Vector3.one);
            Rect rect = new Rect(-1.5f, -3.5f, 6f, 3f);
            Vector3 expectedPosition = frame.MultiplyPoint3x4(new Vector3(1.5f, -2f, 0f));

            PointLightVolumeEditor.ApplyAreaRect(light, frame, rect);

            Assert.That(Vector3.Distance(light.position, expectedPosition), Is.LessThan(Tolerance));
            Assert.That(Mathf.Abs(light.lossyScale.x), Is.EqualTo(6f).Within(Tolerance));
            Assert.That(Mathf.Abs(light.lossyScale.y), Is.EqualTo(3f).Within(Tolerance));
            Assert.That(light.localScale.x, Is.LessThan(0f));
            Assert.That(light.localScale.y, Is.LessThan(0f));
            Assert.That(light.localScale.z, Is.EqualTo(-0.6f));
            Assert.That(Quaternion.Angle(light.rotation, originalRotation), Is.LessThan(0.001f));
        }

        [Test]
        public void ApplyAreaRectRecoversZeroChildDimensionsUnderAScaledParent() {
            Transform parent = CreateTransform("Zero Area Handle Parent");
            parent.rotation = Quaternion.Euler(10f, 35f, -20f);
            parent.localScale = new Vector3(2f, -3f, 0.5f);
            Transform light = CreateTransform("Zero Area Handle Light");
            light.SetParent(parent, false);
            light.localPosition = new Vector3(2f, 3f, 4f);
            light.localRotation = Quaternion.Euler(30f, -15f, 25f);
            light.localScale = new Vector3(0f, 0f, -0.5f);
            Vector3 originalPosition = light.position;
            Matrix4x4 frame = Matrix4x4.TRS(originalPosition, light.rotation, Vector3.one);

            PointLightVolumeEditor.ApplyAreaRect(light, frame, new Rect(-2f, -1f, 4f, 2f));

            Assert.That(Vector3.Distance(light.position, originalPosition), Is.LessThan(Tolerance));
            Assert.That(Mathf.Abs(light.lossyScale.x), Is.EqualTo(4f).Within(Tolerance));
            Assert.That(Mathf.Abs(light.lossyScale.y), Is.EqualTo(2f).Within(Tolerance));
            Assert.That(light.localScale.z, Is.EqualTo(-0.5f));
        }

        [TestCase(0, 0, 3f, 0.35f, 0.025f)]
        [TestCase(1, 0, -2f, 0.8f, -0.1f)]
        [TestCase(0, 2, 4f, 1.2f, 0f)]
        [TestCase(1, 2, -6f, 0.15f, 0.12f)]
        public void PointAndSpotIntensityRangesMatchTheRuntime(int lightType, int projection, float initialIntensity, float cutoff, float sourceSize) {
            PointLightVolumeInstance light = CreateLight(lightType);
            LightVolumeManager manager = CreateManager();
            light.Projection = projection;
            light.Color = lightType == 0 ? new Color(0.2f, 0.75f, 0.4f) : new Color(0.5f, 3.5f, 0.2f);
            light.Intensity = initialIntensity;
            light.LightSourceSize = sourceSize;
            light.transform.localScale = new Vector3(2f, -0.5f, 3f);
            if (projection == 2 && lightType == 0) {
                Cubemap cubemap = new Cubemap(1, TextureFormat.RGBA32, false);
                _assets.Add(cubemap);
                light.Cubemap = cubemap;
            } else if (projection == 2) {
                light.Cookie = Texture2D.whiteTexture;
            }

            Assert.That(PointLightVolumeEditor.TryGetIntensityHandleRange(light, cutoff, out float handleRange), Is.True);
            float runtimeRange = GetRuntimeRange(manager, light, cutoff);
            AssertRange(handleRange, runtimeRange);
            float requestedRange = runtimeRange * 1.75f + 0.15f;
            light.LightSourceSize = sourceSize;

            Assert.That(PointLightVolumeEditor.TryGetIntensityForRange(light, cutoff, requestedRange, out float intensity), Is.True);

            Assert.That(Mathf.Sign(intensity), Is.EqualTo(Mathf.Sign(initialIntensity)));
            light.Intensity = intensity;
            AssertRange(GetRuntimeRange(manager, light, cutoff), requestedRange);
        }

        [TestCase(1f, 0.35f, 4f, 0.25f, 2f)]
        [TestCase(-2f, 0.8f, -4f, 0.25f, -2f)]
        [TestCase(2f, 0.5f, 0.00005f, 0.00002f, 0.003f)]
        public void AreaIntensityRangesMatchTheRuntimeForRectangularAndMirroredLights(float initialIntensity, float cutoff, float scaleX, float scaleY, float scaleZ) {
            PointLightVolumeInstance light = CreateLight(2);
            LightVolumeManager manager = CreateManager();
            light.Color = new Color(0.2f, 1.5f, 0.7f);
            light.Intensity = initialIntensity;
            light.transform.localScale = new Vector3(scaleX, scaleY, scaleZ);

            Assert.That(PointLightVolumeEditor.TryGetIntensityHandleRange(light, cutoff, out float handleRange), Is.True);
            float runtimeRange = GetRuntimeRange(manager, light, cutoff);
            AssertRange(handleRange, runtimeRange);
            float requestedRange = runtimeRange * 1.5f;

            Assert.That(PointLightVolumeEditor.TryGetIntensityForRange(light, cutoff, requestedRange, out float intensity), Is.True);

            Assert.That(Mathf.Sign(intensity), Is.EqualTo(Mathf.Sign(initialIntensity)));
            light.Intensity = intensity;
            AssertRange(GetRuntimeRange(manager, light, cutoff), requestedRange);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void ZeroIntensityRemainsEditableAndZeroRangeTurnsTheLightOff(int lightType) {
            PointLightVolumeInstance light = CreateLight(lightType);
            LightVolumeManager manager = CreateManager();
            light.Intensity = 0f;

            Assert.That(PointLightVolumeEditor.TryGetIntensityHandleRange(light, 0.35f, out float range), Is.True);
            Assert.That(range, Is.Zero);
            Assert.That(PointLightVolumeEditor.TryGetIntensityForRange(light, 0.35f, 2f, out float enabledIntensity), Is.True);
            Assert.That(enabledIntensity, Is.GreaterThan(0f));
            light.Intensity = enabledIntensity;
            AssertRange(GetRuntimeRange(manager, light, 0.35f), 2f);

            light.Intensity = -enabledIntensity;
            Assert.That(PointLightVolumeEditor.TryGetIntensityForRange(light, 0.35f, 0f, out float disabledIntensity), Is.True);
            Assert.That(disabledIntensity, Is.Zero);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void AssignedLutSourcesExcludeOnlyPointAndSpotIntensityHandles(int lightType) {
            PointLightVolumeInstance light = CreateLight(lightType);
            light.Projection = 1;
            Material material = new Material(Shader.Find("Hidden/InternalErrorShader"));
            _assets.Add(material);
            Object[] sources = { Texture2D.whiteTexture, material };

            foreach (Object source in sources) {
                light.FalloffLUT = source;
                bool expected = lightType == 2;
                Assert.That(PointLightVolumeEditor.TryGetIntensityHandleRange(light, 0.35f, out _), Is.EqualTo(expected), source.GetType().Name);
                Assert.That(PointLightVolumeEditor.TryGetIntensityForRange(light, 0.35f, 2f, out _), Is.EqualTo(expected), source.GetType().Name);
            }

            light.FalloffLUT = null;
            Assert.That(PointLightVolumeEditor.TryGetIntensityHandleRange(light, 0.35f, out _), Is.True);
            Assert.That(PointLightVolumeEditor.TryGetIntensityForRange(light, 0.35f, 2f, out _), Is.True);
        }

        [TestCase(0)]
        [TestCase(2)]
        public void InvalidLightDataCannotProduceIntensityHandles(int lightType) {
            PointLightVolumeInstance light = CreateLight(lightType);
            foreach (float cutoff in new[] { 0f, -0.5f, float.NaN, float.PositiveInfinity })
                AssertIntensityRangeUnavailable(light, cutoff);

            Color[] invalidColors = {
                Color.black,
                new Color(-1f, -2f, -3f),
                new Color(float.NaN, 1f, 1f),
                new Color(1f, float.PositiveInfinity, 1f)
            };
            foreach (Color color in invalidColors) {
                light.Color = color;
                AssertIntensityRangeUnavailable(light, 0.35f);
            }
            light.Color = Color.white;

            foreach (float intensity in new[] { float.NaN, float.PositiveInfinity }) {
                light.Intensity = intensity;
                AssertIntensityRangeUnavailable(light, 0.35f);
            }
            light.Intensity = 1f;

            if (lightType != 2) {
                foreach (float sourceSize in new[] { float.NaN, float.PositiveInfinity }) {
                    light.LightSourceSize = sourceSize;
                    AssertIntensityRangeUnavailable(light, 0.35f);
                }
                light.LightSourceSize = 0.025f;
            }
            foreach (float range in new[] { float.NaN, float.PositiveInfinity })
                Assert.That(PointLightVolumeEditor.TryGetIntensityForRange(light, 0.35f, range, out _), Is.False);

            light.transform.localScale = Vector3.zero;
            AssertIntensityRangeUnavailable(light, 0.35f);
        }

        private PointLightVolumeInstance CreateLight(int lightType) {
            Transform lightTransform = CreateTransform("Intensity Handle Light");
            lightTransform.gameObject.SetActive(false);
            PointLightVolumeInstance light = lightTransform.gameObject.AddComponent<PointLightVolumeInstance>();
            light.LightType = lightType;
            return light;
        }

        private LightVolumeManager CreateManager() {
            Transform managerTransform = CreateTransform("Intensity Handle Manager");
            managerTransform.gameObject.SetActive(false);
            return managerTransform.gameObject.AddComponent<LightVolumeManager>();
        }

        private static float GetRuntimeRange(LightVolumeManager manager, PointLightVolumeInstance light, float cutoff) {
            light.EditorApplyAuthoringData(false, false, false);
            manager.LightsBrightnessCutoff = cutoff;
            manager.RecalculatePointLightRange(light);
            return Mathf.Sqrt(light.SquaredRange);
        }

        private static void AssertRange(float actual, float expected) {
            Assert.That(actual, Is.EqualTo(expected).Within(Mathf.Max(Tolerance, Mathf.Abs(expected) * 0.0002f)));
        }

        private static void AssertIntensityRangeUnavailable(PointLightVolumeInstance light, float cutoff) {
            Assert.That(PointLightVolumeEditor.TryGetIntensityHandleRange(light, cutoff, out _), Is.False);
            Assert.That(PointLightVolumeEditor.TryGetIntensityForRange(light, cutoff, 1f, out _), Is.False);
        }

        private Transform CreateTransform(string name) {
            GameObject result = new GameObject(name);
            _objects.Add(result);
            return result.transform;
        }

        private static void AssertRect(Rect actual, Rect expected) {
            Assert.That(actual.xMin, Is.EqualTo(expected.xMin).Within(Tolerance), "Left edge");
            Assert.That(actual.yMin, Is.EqualTo(expected.yMin).Within(Tolerance), "Bottom edge");
            Assert.That(actual.width, Is.EqualTo(expected.width).Within(Tolerance), "Width");
            Assert.That(actual.height, Is.EqualTo(expected.height).Within(Tolerance), "Height");
        }
    }
}
