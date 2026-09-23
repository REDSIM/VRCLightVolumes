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

        // Both update paths keep edge data finite when the blend radius is below its minimum.
        [Test]
        public void RegularSmoothingClampsRadiiBelowTheMinimum() {
            LightVolumeInstance volume = CreateObject("Smoothing Volume", true).AddComponent<LightVolumeInstance>();
            float[] radii = { -1f, 0f, 0.000001f };
            Vector4 expected = new Vector4(100000f, 100000f, 100000f, 0f);
            foreach (float radius in radii) {
                volume.SetSmoothBlending(radius);
                Assert.That(Vector4.Distance(volume.InvLocalEdgeSmoothing, expected), Is.LessThan(0.1f));

                volume.InvLocalEdgeSmoothing = Vector4.zero;
                volume.UpdateTransform();
                Assert.That(Vector4.Distance(volume.InvLocalEdgeSmoothing, expected), Is.LessThan(0.1f));
            }
        }

        // A change across zero requires a rebuild; other changes update the existing light record.
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
            float[] values = { -1f, 0f, 0.5f, 1f, 2f };
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

                    Assert.That(point.ShadingStrength, Is.EqualTo(applied));
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
    }
}
