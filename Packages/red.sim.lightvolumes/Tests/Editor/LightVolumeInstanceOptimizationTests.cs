using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace VRCLightVolumes.Tests {
    [Category("Udon")]
    public class LightVolumeInstanceOptimizationTests {
        private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly List<GameObject> _objects = new List<GameObject>();

        // Releases every test object without leaving registered lights or shader owners in the Editor scene.
        [TearDown]
        public void TearDown() {
            for (int i = _objects.Count - 1; i >= 0; i--)
                if (_objects[i] != null) UnityEngine.Object.DestroyImmediate(_objects[i]);
            _objects.Clear();
        }

        // Exercises the actual setters against their original scalar arithmetic, including radius clamps and parent reflections.
        [Test]
        public void RegularSmoothingMatchesScalarDivisionExactly() {
            GameObject parent = CreateObject("Smoothing Parent", true);
            GameObject child = CreateObject("Smoothing Volume", true);
            child.transform.SetParent(parent.transform, false);
            LightVolumeInstance volume = child.AddComponent<LightVolumeInstance>();
            float[] radii = { 0f, -0f, -1f, 0.000001f, 0.00001f, 0.1f, 0.33333334f, 1f, float.PositiveInfinity, float.NegativeInfinity, float.NaN };
            for (int pose = 0; pose < 8; pose++) {
                parent.transform.localScale = new Vector3((pose & 1) == 0 ? 1.3f : -1.3f, (pose & 2) == 0 ? 0.37f : -0.37f, (pose & 4) == 0 ? 2.71f : -2.71f);
                parent.transform.rotation = Quaternion.Euler(13f * pose, -17f * pose, 7f * pose);
                child.transform.localScale = new Vector3(0.731f, 2.317f, 1.183f);
                child.transform.localRotation = Quaternion.Euler(11f, 23f, 37f);
                for (int i = 0; i < radii.Length; i++) {
                    float radius = radii[i];
                    float safeRadius = Mathf.Max(radius, 0.00001f);
                    Vector3 scale = child.transform.lossyScale;
                    Vector4 expected = new Vector4(scale.x / safeRadius, scale.y / safeRadius, scale.z / safeRadius, 0f);
                    if (volume.SmoothBlending == radius && volume.InvLocalEdgeSmoothing == expected) expected = volume.InvLocalEdgeSmoothing;
                    volume.SetSmoothBlending(radius);
                    AssertVectorExact(expected, volume.InvLocalEdgeSmoothing);

                    scale = child.transform.localToWorldMatrix.lossyScale;
                    volume.UpdateTransform();
                    AssertVectorExact(new Vector4(scale.x / safeRadius, scale.y / safeRadius, scale.z / safeRadius, 0f), volume.InvLocalEdgeSmoothing);
                }
            }
        }

        // Column access must preserve the signed Area cookie mirror even for sheared, reflected and nonfinite matrix data.
        [Test]
        public void AreaCookieMirrorMatchesOriginalComponentExtraction() {
            PointLightVolumeInstance point = CreateObject("Area Mirror Light", true).AddComponent<PointLightVolumeInstance>();
            MethodInfo refresh = typeof(PointLightVolumeInstance).GetMethod("RefreshAreaCookieMirror", InstanceMembers);
            float[] special = { 0f, -0f, 1f, -1f, float.PositiveInfinity, float.NegativeInfinity, float.NaN };
            Quaternion rotation = Quaternion.Euler(29f, -53f, 17f);
            for (int i = 0; i < special.Length; i++) {
                for (int j = 0; j < special.Length; j++) {
                    Matrix4x4 matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(7f, 19f, 31f), new Vector3(2f, -3f, 4f));
                    matrix.m00 = special[i];
                    matrix.m11 = special[j];
                    matrix.m20 = special[j];
                    matrix.m21 = special[i];
                    Vector3 xAxis = new Vector3(matrix.m00, matrix.m10, matrix.m20);
                    Vector3 yAxis = new Vector3(matrix.m01, matrix.m11, matrix.m21);
                    bool flipX = Vector3.Dot(xAxis, rotation * Vector3.right) < 0f;
                    bool flipY = Vector3.Dot(yAxis, rotation * Vector3.up) < 0f;
                    float expected = (flipY ? 2f : 1f) * (flipX ? -1f : 1f);

                    refresh.Invoke(point, new object[] { rotation, matrix });

                    Assert.That(point.AreaCookieMirror, Is.EqualTo(expected));
                }
            }
        }

        // The public setter and raw Udon hook retain their original structural-vs-record notification choice at every shading boundary.
        [TestCase(false)]
        [TestCase(true)]
        public void ShadingTransitionPreservesClampedZeroBoundary(bool rawHook) {
            LightVolumeManager manager = CreateObject("Shading Boundary Manager", false).AddComponent<LightVolumeManager>();
            PointLightVolumeInstance point = CreateObject("Shading Boundary Light", true).AddComponent<PointLightVolumeInstance>();
            point.LightVolumeManager = manager;
            point.RegistryOrder = 0;
            point.IsRangeDirty = false;
            manager.PointLightVolumeInstances = new[] { point };
            SetField(point, "_isRegisteredWithManager", true);
            SetField(manager, "_pointLightCount", 1);
            SetField(manager, "_isUpdatingVolumes", true);
            int[] pendingFlags = GetField<int[]>(manager, "_dirtyPointLightUpdateFlags");
            MethodInfo hook = typeof(PointLightVolumeInstance).GetMethod("_onVarChange_ShadingStrength", InstanceMembers);
            float[] values = { -1f, -0f, 0f, float.Epsilon, 0.5f, 1f, 2f, float.NegativeInfinity, float.PositiveInfinity, float.NaN };
            for (int i = 0; i < values.Length; i++) {
                for (int j = 0; j < values.Length; j++) {
                    float previous = values[i];
                    float authored = values[j];
                    float applied = rawHook ? authored : Mathf.Clamp01(authored);
                    point.ShadingStrength = rawHook ? authored : previous;
                    SetField(point, "_old_ShadingStrength", previous);
                    SetField(manager, "_dirtyPointLightCount", 0);
                    pendingFlags[0] = 0;
                    bool noChange = previous == applied;
                    bool rebuild = (Mathf.Clamp01(previous) <= 0f) != (Mathf.Clamp01(applied) <= 0f);

                    if (rawHook) hook.Invoke(point, null);
                    else point.SetShadingStrength(authored);

                    AssertFloatExact(!rawHook && noChange ? previous : applied, point.ShadingStrength);
                    Assert.That(GetField<int>(manager, "_dirtyPointLightCount"), Is.EqualTo(noChange || rebuild ? 0 : 1), "Incorrect notification for " + previous + " -> " + authored);
                }
            }
        }

        // Notification activity must continue to reflect both component and parent state, independently of registration and emission history.
        [Test]
        public void SourceNotificationsPreserveActivityAcrossParentAndComponentTransitions() {
            GameObject parent = CreateObject("Activity Parent", false);
            GameObject regularObject = CreateObject("Activity Regular", true);
            GameObject pointObject = CreateObject("Activity Point", true);
            regularObject.transform.SetParent(parent.transform, false);
            pointObject.transform.SetParent(parent.transform, false);
            LightVolumeInstance regular = regularObject.AddComponent<LightVolumeInstance>();
            PointLightVolumeInstance point = pointObject.AddComponent<PointLightVolumeInstance>();
            for (int pass = 0; pass < 8; pass++) {
                bool parentActive = (pass & 1) != 0;
                bool componentEnabled = (pass & 2) != 0;
                parent.SetActive(parentActive);
                regular.enabled = componentEnabled;
                point.enabled = componentEnabled;
                float intensity = (pass & 4) == 0 ? 0f : 2f;
                regular.SetColorAndIntensity(new Color(0.2f + pass * 0.05f, 0.3f, 0.4f), intensity);
                point.SetColorAndIntensity(new Color(0.2f + pass * 0.05f, 0.3f, 0.4f), intensity);
                bool expected = parentActive && componentEnabled && intensity != 0f;
                Assert.That(regular.IsActive, Is.EqualTo(expected));
                Assert.That(point.IsActive, Is.EqualTo(expected));
                regular.UpdateTransform();
                point.UpdatePosition();
                Assert.That(regular.IsActive, Is.EqualTo(expected));
                Assert.That(point.IsActive, Is.EqualTo(expected));
            }
        }

        // Creates a tracked scene object with its intended initial activity before adding any tested behaviours.
        private GameObject CreateObject(string name, bool active) {
            GameObject result = new GameObject(name);
            result.SetActive(active);
            _objects.Add(result);
            return result;
        }

        // Reads private runtime state without widening the production API.
        private static T GetField<T>(object instance, string name) {
            return (T)instance.GetType().GetField(name, InstanceMembers).GetValue(instance);
        }

        // Seeds private runtime state while keeping the manager's ordinary packing and notification implementation under test.
        private static void SetField(object instance, string name, object value) {
            instance.GetType().GetField(name, InstanceMembers).SetValue(instance, value);
        }

        // Compares each component bit-for-bit; any NaN payload is acceptable because the prior arithmetic did not promise a payload.
        private static void AssertVectorExact(Vector4 expected, Vector4 actual) {
            AssertFloatExact(expected.x, actual.x);
            AssertFloatExact(expected.y, actual.y);
            AssertFloatExact(expected.z, actual.z);
            AssertFloatExact(expected.w, actual.w);
        }

        // Includes signed-zero distinctions that approximate Unity vector assertions would miss.
        private static void AssertFloatExact(float expected, float actual) {
            if (float.IsNaN(expected)) Assert.That(float.IsNaN(actual), Is.True);
            else Assert.That(BitConverter.ToInt32(BitConverter.GetBytes(actual), 0), Is.EqualTo(BitConverter.ToInt32(BitConverter.GetBytes(expected), 0)));
        }
    }
}
