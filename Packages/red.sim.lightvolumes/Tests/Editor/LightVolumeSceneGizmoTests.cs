using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace VRCLightVolumes.Tests {
    [Category("Editor")]
    public class LightVolumeSceneGizmoTests {
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const int GridSize = 8;
        private const int ImageSize = 512;
        private readonly List<Object> _resources = new List<Object>();
        private Object[] _previousSelection;
        private Color _previousColor;
        private Matrix4x4 _previousMatrix;
        private CompareFunction _previousZTest;

        // Keeps the user's selection and global handle state out of the regression fixture.
        [SetUp]
        public void SetUp() {
            _previousSelection = Selection.objects;
            _previousColor = Handles.color;
            _previousMatrix = Handles.matrix;
            _previousZTest = Handles.zTest;
            Selection.objects = Array.Empty<Object>();
        }

        // Destroys Inspectors before their inspected objects and restores editor state.
        [TearDown]
        public void TearDown() {
            for (int i = 0; i < _resources.Count; i++)
                if (_resources[i] is GameObject go && go != null && go.TryGetComponent(out Camera camera)) camera.targetTexture = null;
            for (int i = _resources.Count - 1; i >= 0; i--)
                if (_resources[i] != null) Object.DestroyImmediate(_resources[i]);
            _resources.Clear();
            Selection.objects = _previousSelection;
            Handles.color = _previousColor;
            Handles.matrix = _previousMatrix;
            Handles.zTest = _previousZTest;
        }

        // UdonSharp must find no per-target callback that updates the entire SerializedObject selection.
        [TestCase(typeof(PointLightVolumeEditor))]
        [TestCase(typeof(LightVolumeEditor))]
        public void SceneDrawingBypassesUdonSharpPerTargetDispatch(Type editorType) {
            Assert.That(editorType.GetMethod("OnSceneGUI", InstanceFlags, null, Type.EmptyTypes, null), Is.Null);
        }

        // One Inspector owns one Scene View subscription regardless of the selection size.
        [TestCase(false)]
        [TestCase(true)]
        public void SceneCallbackIsRegisteredOnceAndRemovedWhenInspectorCloses(bool volumes) {
            Object[] targets = CreateTargets(volumes ? 3 : 0);
            Type editorType = volumes ? typeof(LightVolumeEditor) : typeof(PointLightVolumeEditor);
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(targets, editorType);
            _resources.Add(editor);
            Assert.That(CountSceneCallbacks(editor), Is.EqualTo(1));

            Object.DestroyImmediate(editor);

            Assert.That(CountSceneCallbacks(editor), Is.Zero);
            editor = UnityEditor.Editor.CreateEditor(targets, editorType);
            _resources.Add(editor);
            Assert.That(CountSceneCallbacks(editor), Is.EqualTo(1));
        }

        // Each light type and volume must leave visible pixels at every inspected position, even with another object selected.
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void MassSelectionDrawsEveryInspectedObjectAndPreservesHandleState(int kind) {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("A graphics device is required to verify Scene View wireframes.");
            Object[] targets = CreateTargets(kind);
            Type editorType = kind == 3 ? typeof(LightVolumeEditor) : typeof(PointLightVolumeEditor);
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(targets, editorType);
            _resources.Add(editor);
            SceneView sceneView = ScriptableObject.CreateInstance<SceneView>();
            _resources.Add(sceneView);
            sceneView.drawGizmos = true;
            Camera camera = CreateCamera();
            Selection.activeGameObject = camera.gameObject;
            RenderTexture texture = new RenderTexture(ImageSize, ImageSize, 24, RenderTextureFormat.ARGB32);
            _resources.Add(texture);
            Assert.That(texture.Create(), Is.True);
            camera.targetTexture = texture;
            Texture2D readback = new Texture2D(ImageSize, ImageSize, TextureFormat.RGBA32, false);
            _resources.Add(readback);
            MethodInfo draw = editorType.GetMethod("DrawSceneGUI", InstanceFlags);
            Assert.That(draw, Is.Not.Null);
            SceneGizmoTestWindow window = ScriptableObject.CreateInstance<SceneGizmoTestWindow>();
            _resources.Add(window);
            window.ShowUtility();

            Color color = new Color(0.1f, 0.2f, 0.3f, 0.4f);
            Matrix4x4 matrix = Matrix4x4.Translate(new Vector3(100f, 200f, 300f));
            Handles.color = color;
            Handles.matrix = matrix;
            Handles.zTest = CompareFunction.Always;
            DrawToTexture(window, editor, draw, sceneView, camera, texture, readback, EventType.Repaint);

            Color32[] pixels = readback.GetPixels32();
            for (int i = 0; i < targets.Length; i++)
                Assert.That(CountLitPixels(pixels, i % GridSize, i / GridSize), Is.GreaterThan(4), "Missing gizmo at inspected target " + i);

            SceneVisibilityManager.instance.Hide(((Component)targets[0]).gameObject, true);
            DrawToTexture(window, editor, draw, sceneView, camera, texture, readback, EventType.Repaint);
            pixels = readback.GetPixels32();
            Assert.That(CountLitPixels(pixels, 0, 0), Is.Zero, "Hidden objects must not retain Scene View gizmos.");
            Assert.That(CountLitPixels(pixels, 1, 0), Is.GreaterThan(4));

            Object.DestroyImmediate(((Component)targets[targets.Length - 1]).gameObject);
            DrawToTexture(window, editor, draw, sceneView, camera, texture, readback, EventType.Repaint);
            Assert.That(CountLitPixels(readback.GetPixels32(), GridSize - 1, GridSize - 1), Is.Zero);

            // Layout must not issue wire draws.
            DrawToTexture(window, editor, draw, sceneView, camera, texture, readback, EventType.Layout);
            Assert.That(CountLitPixels(readback.GetPixels32()), Is.Zero);

            sceneView.drawGizmos = false;
            DrawToTexture(window, editor, draw, sceneView, camera, texture, readback, EventType.Repaint);
            Assert.That(CountLitPixels(readback.GetPixels32()), Is.Zero);
        }

        // Builds separate positions so repeated drawing of only one target cannot pass the image checks.
        private Object[] CreateTargets(int kind) {
            Object[] targets = new Object[GridSize * GridSize];
            for (int i = 0; i < targets.Length; i++) {
                GameObject go = new GameObject("Scene Gizmo Regression " + i);
                _resources.Add(go);
                go.SetActive(false);
                go.transform.position = new Vector3((i % GridSize - 3.5f) * 3f, (i / GridSize - 3.5f) * 3f, 0f);
                if (kind == 3) targets[i] = go.AddComponent<LightVolumeInstance>();
                else {
                    PointLightVolumeInstance light = go.AddComponent<PointLightVolumeInstance>();
                    light.LightType = kind == 4 ? i % 3 : kind;
                    light.LightSourceSize = 0.5f;
                    light.Intensity = 0f;
                    targets[i] = light;
                }
            }
            return targets;
        }

        // Uses a fixed orthographic projection with one image cell per target.
        private Camera CreateCamera() {
            GameObject go = new GameObject("Scene Gizmo Regression Camera");
            _resources.Add(go);
            go.SetActive(false);
            Camera camera = go.AddComponent<Camera>();
            camera.transform.position = new Vector3(0f, 0f, -30f);
            camera.orthographic = true;
            camera.orthographicSize = 12f;
            camera.aspect = 1f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
            return camera;
        }

        // Runs the real callback inside OnGUI, where Unity makes Event.current available to Handles.
        private static void DrawToTexture(SceneGizmoTestWindow window, UnityEditor.Editor editor, MethodInfo draw, SceneView sceneView, Camera camera, RenderTexture texture, Texture2D readback, EventType eventType) {
            bool invoked = false;
            Exception exception = null;
            Color color = Handles.color;
            Matrix4x4 matrix = Handles.matrix;
            CompareFunction zTest = Handles.zTest;
            window.Draw = () => {
                if (Event.current.type != eventType) return;
                RenderTexture previousTarget = RenderTexture.active;
                Camera previousCamera = Camera.current;
                GL.PushMatrix();
                try {
                    RenderTexture.active = texture;
                    GL.Clear(true, true, Color.black);
                    Handles.SetCamera(new Rect(0f, 0f, ImageSize, ImageSize), camera);
                    Handles.color = color;
                    Handles.matrix = matrix;
                    Handles.zTest = zTest;
                    draw.Invoke(editor, new object[] { sceneView });
                    Assert.That(Handles.color, Is.EqualTo(color));
                    Assert.That(Handles.matrix, Is.EqualTo(matrix));
                    Assert.That(Handles.zTest, Is.EqualTo(zTest));
                    RenderTexture.active = texture;
                    readback.ReadPixels(new Rect(0f, 0f, ImageSize, ImageSize), 0, 0, false);
                    readback.Apply(false, false);
                    invoked = true;
                } catch (Exception error) {
                    exception = error;
                } finally {
                    if (previousCamera != null) Handles.SetCamera(previousCamera);
                    GL.PopMatrix();
                    RenderTexture.active = previousTarget;
                }
            };
            try {
                window.SendEvent(new Event { type = eventType });
                if (exception != null) throw exception;
                Assert.That(invoked, Is.True, "The test window must dispatch the requested GUI event.");
            } finally {
                window.Draw = null;
            }
        }

        // Supplies a real IMGUI context for the offscreen wireframe assertions.
        private sealed class SceneGizmoTestWindow : EditorWindow {
            internal Action Draw;

            // Dispatches the test draw only while the fixture requests it.
            private void OnGUI() {
                Draw?.Invoke();
            }
        }

        // Counts one target's image cell, or the full image for empty-draw checks.
        private static int CountLitPixels(Color32[] pixels, int column = -1, int row = -1) {
            int cellSize = ImageSize / GridSize;
            int minX = column < 0 ? 0 : column * cellSize;
            int minY = row < 0 ? 0 : row * cellSize;
            int maxX = column < 0 ? ImageSize : minX + cellSize;
            int maxY = row < 0 ? ImageSize : minY + cellSize;
            int count = 0;
            for (int y = minY; y < maxY; y++) {
                for (int x = minX; x < maxX; x++) {
                    Color32 pixel = pixels[y * ImageSize + x];
                    if (pixel.r > 8 || pixel.g > 8 || pixel.b > 8) count++;
                }
            }
            return count;
        }

        // Inspects the event backing field to detect leaked or duplicate subscriptions across Inspector lifetimes.
        private static int CountSceneCallbacks(UnityEditor.Editor editor) {
            FieldInfo field = typeof(SceneView).GetField("duringSceneGui", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            Delegate callbacks = field.GetValue(null) as Delegate;
            if (callbacks == null) return 0;
            int count = 0;
            foreach (Delegate callback in callbacks.GetInvocationList())
                if (ReferenceEquals(callback.Target, editor)) count++;
            return count;
        }
    }
}
