using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace VRCLightVolumes.Tests {
    [Category("Editor")]
    public class LightVolumeDebuggerStatsTests {
        private const string AssetRoot = "Packages/red.sim.lightvolumes/Extra/Light Volume Debugger/";
        private const string ShaderName = "Light Volume Samples/Light Volume Debugger Stats";

        [TestCase(ShaderCompilerPlatform.D3D, BuildTarget.StandaloneWindows64, "")]
        [TestCase(ShaderCompilerPlatform.D3D, BuildTarget.StandaloneWindows64, "STEREO_INSTANCING_ON")]
        [TestCase(ShaderCompilerPlatform.D3D, BuildTarget.StandaloneWindows64, "UNITY_SINGLE_PASS_STEREO")]
        [TestCase(ShaderCompilerPlatform.GLES3x, BuildTarget.Android, "")]
        [TestCase(ShaderCompilerPlatform.GLES3x, BuildTarget.Android, "STEREO_MULTIVIEW_ON")]
        [TestCase(ShaderCompilerPlatform.Vulkan, BuildTarget.Android, "")]
        [TestCase(ShaderCompilerPlatform.Metal, BuildTarget.StandaloneOSX, "")]
        public void StatsShaderCompilesForAvatarPlatforms(ShaderCompilerPlatform platform, BuildTarget target, string keyword) {
            Shader shader = LoadAsset<Shader>("Light Volume Debugger Stats.shader");
            Assert.That(shader.name, Is.EqualTo(ShaderName));
            string[] keywords = keyword.Length == 0 ? Array.Empty<string>() : new[] { keyword };
            var pass = ShaderUtil.GetShaderData(shader).GetSubshader(0).GetPass(0);
            // OpenGL and Vulkan return all stages through the vertex program.
            ShaderType[] stages = platform == ShaderCompilerPlatform.D3D || platform == ShaderCompilerPlatform.Metal
                ? new[] { ShaderType.Vertex, ShaderType.Fragment }
                : new[] { ShaderType.Vertex };
            foreach (ShaderType stage in stages) {
                var compiled = pass.CompileVariant(stage, keywords, platform, target, true);
                StringBuilder messages = new StringBuilder();
                foreach (var message in compiled.Messages) messages.AppendLine(message.message);
                Assert.That(compiled.Success, Is.True, platform + " " + stage + ": " + messages);
                Assert.That(compiled.ShaderData, Is.Not.Empty, "The compiler returned no shader program.");
            }
        }

        [Test]
        public void StatsAssetsUseOneQuadOnePassAndOneAtlas() {
            Mesh mesh = LoadAsset<Mesh>("LightVolumeDebuggerStats.asset");
            Assert.That(mesh.vertexCount, Is.EqualTo(4));
            Assert.That(mesh.subMeshCount, Is.EqualTo(1));
            Assert.That(mesh.GetIndexCount(0), Is.EqualTo(6));
            Assert.That(mesh.GetTopology(0), Is.EqualTo(MeshTopology.Triangles));

            Material material = LoadAsset<Material>("Light Volume Debugger Stats.mat");
            Assert.That(material.shader.name, Is.EqualTo(ShaderName));
            Assert.That(material.passCount, Is.EqualTo(1));
            Assert.That(material.GetTexturePropertyNames().Length, Is.EqualTo(1));
            Texture atlas = material.GetTexture(material.GetTexturePropertyNames()[0]);
            Assert.That(atlas, Is.EqualTo(LoadAsset<Texture2D>("LightVolumeDebuggerStats.png")));
            Assert.That(atlas.width, Is.EqualTo(1024));
            Assert.That(atlas.height, Is.EqualTo(512));

            // A saved material property would hide the world's live shader global.
            foreach (string global in new[] {
                "_UdonLightVolumeEnabled", "_UdonLightVolumeVersion", "_UdonLightVolumeCount",
                "_UdonLightVolumeAdditiveCount", "_UdonPointLightVolumeCount", "_UdonClusteringEnabled"
            }) Assert.That(material.HasProperty(global), Is.False, global + " must remain a shader global.");
        }

        [TestCase("ModularAvatar/Light Volume Debugger MA.prefab")]
        [TestCase("VRCFury/Light Volume Debugger VRCFury.prefab")]
        public void DebuggerPrefabsKeepOneRendererAndOneMaterialSlot(string path) {
            GameObject prefab = LoadAsset<GameObject>(path);
            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers.Length, Is.EqualTo(1));
            Assert.That(renderers[0], Is.TypeOf<MeshRenderer>());
            Assert.That(renderers[0].sharedMaterials.Length, Is.EqualTo(1));
            Assert.That(renderers[0].GetComponent<MeshFilter>(), Is.Not.Null);
        }

        [TestCase("ModularAvatar/Controller/Animation/LVDebugger Statistics ON.anim",
            "ModularAvatar/Controller/Animation/LVDebugger Toggle ON.anim",
            "ModularAvatar/Controller/Animation/LVDebugger Clustering ON.anim", "Debugger")]
        [TestCase("VRCFury/LVDebuggerStatistics.anim", "VRCFury/LVDebuggerActive.anim", "VRCFury/LVDebuggerClustering.anim", "")]
        public void DisplayAnimationsSwapTheExistingSlotAndDisableUnusedDepthLight(string statsPath, string cardsPath, string clusterPath, string childPath) {
            Scene scene = EditorSceneManager.NewPreviewScene();
            try {
                GameObject root = new GameObject("Stats animation test");
                SceneManager.MoveGameObjectToScene(root, scene);
                root.AddComponent<Animator>();
                GameObject debugger = root;
                if (childPath.Length != 0) {
                    debugger = new GameObject(childPath);
                    debugger.transform.SetParent(root.transform, false);
                }
                MeshFilter filter = debugger.AddComponent<MeshFilter>();
                MeshRenderer renderer = debugger.AddComponent<MeshRenderer>();
                GameObject depthLight = new GameObject("Depth Light");
                depthLight.transform.SetParent(debugger.transform, false);
                Material cardsMaterial = LoadAsset<Material>("Light Volume Debugger.mat");
                Mesh cardsMesh = LoadAsset<Mesh>("LightVolumeDebuggerCards.asset");
                renderer.sharedMaterial = cardsMaterial;
                AnimationClip cards = LoadAsset<AnimationClip>(cardsPath);
                AnimationClip stats = LoadAsset<AnimationClip>(statsPath);
                cards.SampleAnimation(root, 0.01f);
                Assert.That(filter.sharedMesh, Is.EqualTo(cardsMesh));
                stats.SampleAnimation(root, 0.01f);
                Assert.That(depthLight.activeSelf, Is.False);
                Assert.That(filter.sharedMesh, Is.EqualTo(LoadAsset<Mesh>("LightVolumeDebuggerStats.asset")));
                Assert.That(renderer.sharedMaterial, Is.EqualTo(LoadAsset<Material>("Light Volume Debugger Stats.mat")));
                Assert.That(renderer.sharedMaterials.Length, Is.EqualTo(1));
                AnimationClip clustering = LoadAsset<AnimationClip>(clusterPath);
                clustering.SampleAnimation(root, 0.01f);
                Assert.That(filter.sharedMesh, Is.EqualTo(LoadAsset<Mesh>("LightVolumeDebuggerClustering.asset")));
                Assert.That(renderer.sharedMaterial, Is.EqualTo(LoadAsset<Material>("Light Volume Debugger Clustering.mat")));
                Assert.That(renderer.sharedMaterials.Length, Is.EqualTo(1));
                Assert.That(depthLight.activeSelf, Is.True);
                stats.SampleAnimation(root, 0.01f);
                Assert.That(depthLight.activeSelf, Is.False);
                clustering.SampleAnimation(root, 0.01f);
                cards.SampleAnimation(root, 0.01f);
                Assert.That(depthLight.activeSelf, Is.False);
                Assert.That(filter.sharedMesh, Is.EqualTo(cardsMesh));
                Assert.That(renderer.sharedMaterial, Is.EqualTo(cardsMaterial));
                Assert.That(renderer.sharedMaterials.Length, Is.EqualTo(1));
                if (childPath.Length != 0) {
                    clustering.SampleAnimation(root, 0.01f);
                    LoadAsset<AnimationClip>("ModularAvatar/Controller/Animation/LVDebugger Toggle OFF.anim").SampleAnimation(root, 0.01f);
                    Assert.That(debugger.activeSelf, Is.False);
                    Assert.That(filter.sharedMesh, Is.Null);
                    Assert.That(renderer.sharedMaterial, Is.EqualTo(cardsMaterial));
                    Assert.That(depthLight.activeSelf, Is.False);
                }
            } finally {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [Test]
        public void ModularAvatarControllerSwitchesDisplaysAndPreservesVisibility() {
            AnimatorController controller = LoadAsset<AnimatorController>("ModularAvatar/Controller/LightVolumeDebugger Controller.controller");
            Mesh cards = LoadAsset<Mesh>("LightVolumeDebuggerCards.asset");
            Mesh quad = LoadAsset<Mesh>("LightVolumeDebuggerStats.asset");
            Mesh cube = LoadAsset<Mesh>("LightVolumeDebuggerClustering.asset");
            Material cardsMaterial = LoadAsset<Material>("Light Volume Debugger.mat");
            Material statsMaterial = LoadAsset<Material>("Light Volume Debugger Stats.mat");
            Material clusteringMaterial = LoadAsset<Material>("Light Volume Debugger Clustering.mat");
            Scene scene = EditorSceneManager.NewPreviewScene();
            PlayableGraph graph = PlayableGraph.Create("Stats controller test");
            try {
                GameObject root = new GameObject("Stats controller test");
                SceneManager.MoveGameObjectToScene(root, scene);
                GameObject debugger = new GameObject("Debugger");
                debugger.transform.SetParent(root.transform, false);
                GameObject depthLight = new GameObject("Depth Light");
                depthLight.transform.SetParent(debugger.transform, false);
                depthLight.SetActive(false);
                MeshFilter filter = debugger.AddComponent<MeshFilter>();
                MeshRenderer renderer = debugger.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = cardsMaterial;
                debugger.SetActive(false);
                Animator animator = root.AddComponent<Animator>();
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                AnimatorControllerPlayable playable = AnimatorControllerPlayable.Create(graph, controller);
                AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "Debugger", animator);
                output.SetSourcePlayable(playable);
                graph.Play();
                graph.Evaluate(1f / 60f);

                Action<string, float, float, float, float, int> check = (label, toggle, stats, localOnly, local, mode) => {
                    playable.SetFloat("LVDebugger/Internal/DirectTree1", 1);
                    playable.SetFloat("LVDebugger/Toggle", toggle);
                    playable.SetFloat("LVDebugger/Statistics", stats);
                    playable.SetFloat("LVDebugger/LocalOnly", localOnly);
                    playable.SetFloat("IsLocal", local);
                    graph.Evaluate(1f / 60f);
                    graph.Evaluate(1f / 60f);
                    Assert.That(debugger.activeSelf, Is.EqualTo(mode != 0), label);
                    Assert.That(filter.sharedMesh, Is.EqualTo(mode == 0 ? null : mode == 1 ? cards : mode == 2 ? quad : cube), label);
                    Assert.That(renderer.sharedMaterial, Is.EqualTo(mode == 3 ? clusteringMaterial : mode == 2 ? statsMaterial : cardsMaterial), label);
                    Assert.That(renderer.sharedMaterials.Length, Is.EqualTo(1), label);
                    Assert.That(depthLight.activeSelf, Is.EqualTo(mode == 3), label);
                };
                check("Initially off", 0, 0, 1, 1, 0);
                check("Cards locally", 1, 0, 1, 1, 1);
                check("Statistics locally", 1, 1, 1, 1, 2);
                check("Clustering locally", 1, 2, 1, 1, 3);
                check("Statistics after clustering", 1, 1, 1, 1, 2);
                check("Clustering before cards", 1, 2, 1, 1, 3);
                check("Cards restored", 1, 0, 1, 1, 1);
                check("Statistics with master off", 0, 1, 1, 1, 0);
                check("Statistics restored", 1, 1, 1, 1, 2);
                check("Statistics hidden remotely", 1, 1, 1, 0, 0);
                check("Statistics visible remotely", 1, 1, 0, 0, 2);
                check("Statistics visible locally", 1, 1, 0, 1, 2);
                check("Cards visible remotely", 1, 0, 0, 0, 1);
                check("Cards hidden remotely", 1, 0, 1, 0, 0);
                check("Master off after statistics", 0, 1, 0, 0, 0);
                check("Clustering visible remotely", 1, 2, 0, 0, 3);
                check("Clustering hidden remotely", 1, 2, 1, 0, 0);
                check("Clustering restored locally", 1, 2, 1, 1, 3);
                check("Master off after clustering", 0, 2, 1, 1, 0);
            } finally {
                if (graph.IsValid()) graph.Destroy();
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [TestCase(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false)]
        [TestCase(0, 3, 32, 8, 128, 1, 0, 0, 0, 0, false)]
        [TestCase(1, 0, 12, 3, 128, 1, 1, 9, 3, 0, false)]
        [TestCase(2, 0, 32, 8, 64, 1, 2, 24, 8, 64, false)]
        [TestCase(1, 2, 32, 8, 64, 1, 2, 24, 8, 64, false)]
        [TestCase(1, 3, 0, 0, 0, 0, 3, 0, 0, 0, false)]
        [TestCase(1, 3, 32, 8, 128, 1, 3, 24, 8, 128, true)]
        [TestCase(1, 3, 2, 8, -1, 0, 3, 0, 2, 0, false)]
        public void PanelReadsLiveCountsAndLegacyVersions(float enabled, float version, float total, float additive,
                float lights, float clustering, int expectedVersion, int expectedRegular, int expectedAdditive,
                int expectedLights, bool expectedClustering) {
            using (PanelRenderFixture fixture = new PanelRenderFixture()) {
                fixture.Material.SetFloat("_PanelYOffset", 0);
                fixture.Values = new[] { enabled, version, total, additive, lights, clustering };
                fixture.Render();
                if (expectedVersion == 0) AssertPanelColor(fixture.Readback, 0, 500, new Color(0.9f, 0.2f, 0.4f));
                else AssertPanelNumber(fixture.Readback, 0, expectedVersion);
                AssertPanelNumber(fixture.Readback, 1, expectedRegular);
                AssertPanelNumber(fixture.Readback, 2, expectedAdditive);
                AssertPanelNumber(fixture.Readback, 3, expectedLights);
                AssertPanelColor(fixture.Readback, 4, 536, new Color(0.2f, expectedClustering ? 0.8f : 0.2f, 0.4f));
            }
        }

        [Test]
        public void PanelFollowsItsHeadParentInsteadOfTheObservingCamera() {
            using (PanelRenderFixture fixture = new PanelRenderFixture()) {
                fixture.Camera.orthographic = false;
                fixture.Camera.fieldOfView = 60;
                fixture.Material.SetTexture("_MainTex", Texture2D.whiteTexture);
                Assert.That(fixture.Material.GetFloat("_PanelDistance"), Is.EqualTo(1f).Within(0.0001f));
                Assert.That(fixture.Material.GetFloat("_PanelWidth"), Is.EqualTo(0.6f).Within(0.0001f));
                Assert.That(fixture.Material.GetFloat("_PanelYOffset"), Is.EqualTo(0f).Within(0.0001f));
                fixture.Render();
                RectInt initial = AssertProjectedPanelBounds(fixture);

                fixture.Camera.transform.position = new Vector3(0.2f, 0, 0);
                fixture.Render();
                RectInt secondObserver = AssertProjectedPanelBounds(fixture);
                Assert.That(secondObserver.center.x, Is.LessThan(initial.center.x - 10), "The panel must not recenter for another observer.");

                fixture.Camera.transform.position = Vector3.zero;
                fixture.Head.transform.SetPositionAndRotation(new Vector3(0.18f, 0.08f, 0.35f), Quaternion.Euler(-5, 12, 7));
                fixture.Head.transform.localScale = Vector3.one * 0.8f;
                fixture.Render();
                RectInt movedHead = AssertProjectedPanelBounds(fixture);
                Assert.That(movedHead.center.x, Is.GreaterThan(initial.center.x + 10));
                Assert.That(movedHead.width, Is.LessThan(initial.width - 5));
                Assert.That(fixture.Panel.transform.localPosition, Is.EqualTo(Vector3.zero));
            }
        }

        [Test]
        public void HeadAttachedPanelHasOneWorldPositionForBothEyes() {
            using (PanelRenderFixture fixture = new PanelRenderFixture()) {
                fixture.Camera.orthographic = false;
                fixture.Camera.fieldOfView = 60;
                fixture.Material.SetTexture("_MainTex", Texture2D.whiteTexture);
                Vector3 position = new Vector3(3, 2, -5);
                Quaternion rotation = Quaternion.Euler(-8, 35, 4);
                fixture.Head.transform.SetPositionAndRotation(position, rotation);
                fixture.Camera.transform.SetPositionAndRotation(position - rotation * Vector3.right * 0.032f, rotation);
                fixture.Render();
                RectInt leftEye = AssertProjectedPanelBounds(fixture);
                fixture.Camera.transform.position = position + rotation * Vector3.right * 0.032f;
                fixture.Render();
                RectInt rightEye = AssertProjectedPanelBounds(fixture);

                float disparity = leftEye.center.x - rightEye.center.x;
                Assert.That(disparity, Is.GreaterThan(3), "A shared world-space panel must have stereo disparity.");
                float measuredDistance = 0.064f * fixture.Camera.projectionMatrix.m00 * 160f * 0.5f / disparity;
                Assert.That(measuredDistance, Is.EqualTo(1f).Within(0.15f));
                Assert.That(rightEye.width, Is.EqualTo(leftEye.width).Within(1));
                Assert.That(rightEye.height, Is.EqualTo(leftEye.height).Within(1));
            }
        }

        [Test]
        public void OpaqueGeometryOccludesTheHeadAttachedPanel() {
            using (PanelRenderFixture fixture = new PanelRenderFixture()) {
                fixture.Material.SetFloat("_PanelYOffset", 0);
                fixture.Material.SetTexture("_MainTex", Texture2D.whiteTexture);
                fixture.Render();
                Assert.That(fixture.Readback.GetPixel(80, 56).g, Is.GreaterThan(0.99f));
                fixture.CreateOccluder();
                fixture.Render();
                Color covered = fixture.Readback.GetPixel(80, 56);
                Assert.That(covered.r, Is.LessThan(0.006f));
                Assert.That(covered.g, Is.LessThan(0.006f));
                Assert.That(covered.b, Is.LessThan(0.006f));
                Assert.That(fixture.Readback.GetPixel(20, 20).g, Is.GreaterThan(0.99f), "The uncovered panel must remain visible.");
            }
        }

        [TestCase(0f)]
        [TestCase(55f)]
        public void FilteredOffLabelDoesNotShowTheNeighboringOnGlyph(float angle) {
            using (PanelRenderFixture fixture = new PanelRenderFixture(640, 448)) {
                fixture.Camera.orthographic = false;
                fixture.Camera.fieldOfView = 60;
                fixture.Material.SetTexture("_MainTex", LoadAsset<Texture2D>("LightVolumeDebuggerStats.png"));
                fixture.Values[5] = 0;
                Quaternion rotation = Quaternion.Euler(0, angle, 0);
                float largestBleed = 0;
                foreach (float distance in new[] { 0.7f, 1.15f }) {
                    fixture.Material.SetFloat("_PanelDistance", distance);
                    fixture.Head.transform.SetPositionAndRotation(Vector3.forward * distance - rotation * Vector3.forward * distance, rotation);
                    float worldPixel = 2f * distance * Mathf.Tan(30f * Mathf.Deg2Rad) / fixture.Readback.height;
                    for (int phase = 0; phase < 4; phase++) {
                        fixture.Camera.transform.position = Vector3.right * (worldPixel * phase * 0.25f);
                        fixture.Render();
                        float background = ReadPanelPixel(fixture, 520, 390).r;
                        // This strip is blank before OFF. Filtered ON pixels must not appear here.
                        for (float x = 472; x <= 492; x += 0.5f)
                            for (float y = 378; y <= 406; y += 2)
                                largestBleed = Mathf.Max(largestBleed, ReadPanelPixel(fixture, x, y).r - background);
                        float textBrightness = 0;
                        for (float x = 552; x < 600; x += 2)
                            for (float y = 378; y <= 406; y += 2)
                                textBrightness = Mathf.Max(textBrightness, ReadPanelPixel(fixture, x, y).r - background);
                        Assert.That(textBrightness, Is.GreaterThan(0.08f), "OFF must remain visible at distance " + distance + ".");
                    }
                }
                Assert.That(largestBleed, Is.LessThanOrEqualTo(0.015f), "Gold from ON leaked into the blank strip before OFF at angle " + angle + ".");
            }
        }

        private static Color ReadPanelPixel(PanelRenderFixture fixture, float x, float y) {
            float width = fixture.Material.GetFloat("_PanelWidth");
            Vector3 local = new Vector3((x / 640f - 0.5f) * width,
                (0.5f - y / 448f) * width * 0.7f + fixture.Material.GetFloat("_PanelYOffset"), fixture.Material.GetFloat("_PanelDistance"));
            Vector3 viewport = fixture.Camera.WorldToViewportPoint(fixture.Panel.transform.TransformPoint(local));
            return fixture.Readback.GetPixel(Mathf.FloorToInt(viewport.x * fixture.Readback.width), Mathf.FloorToInt(viewport.y * fixture.Readback.height));
        }

        private static RectInt AssertProjectedPanelBounds(PanelRenderFixture fixture) {
            int minX = 160, minY = 112, maxX = -1, maxY = -1;
            Color[] pixels = fixture.Readback.GetPixels();
            for (int y = 0; y < 112; y++) {
                for (int x = 0; x < 160; x++) {
                    Color color = pixels[y * 160 + x];
                    if (color.r < 0.99f || color.g < 0.99f || color.b < 0.99f) continue;
                    minX = Mathf.Min(minX, x);
                    minY = Mathf.Min(minY, y);
                    maxX = Mathf.Max(maxX, x);
                    maxY = Mathf.Max(maxY, y);
                }
            }
            Assert.That(maxX, Is.GreaterThanOrEqualTo(minX), "The panel must render.");
            float width = fixture.Material.GetFloat("_PanelWidth");
            Vector2 expectedMin = Vector2.one;
            Vector2 expectedMax = Vector2.zero;
            foreach (Vector2 corner in new[] { new Vector2(-1, -1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(1, 1) }) {
                Vector3 local = new Vector3(corner.x * width * 0.5f,
                    corner.y * width * 0.35f + fixture.Material.GetFloat("_PanelYOffset"), fixture.Material.GetFloat("_PanelDistance"));
                Vector3 projected = fixture.Camera.WorldToViewportPoint(fixture.Panel.transform.TransformPoint(local));
                expectedMin = Vector2.Min(expectedMin, projected);
                expectedMax = Vector2.Max(expectedMax, projected);
            }
            Assert.That(minX, Is.EqualTo(expectedMin.x * 160).Within(1.5f));
            Assert.That(minY, Is.EqualTo(expectedMin.y * 112).Within(1.5f));
            Assert.That(maxX + 1, Is.EqualTo(expectedMax.x * 160).Within(1.5f));
            Assert.That(maxY + 1, Is.EqualTo(expectedMax.y * 112).Within(1.5f));
            return new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        private sealed class PanelRenderFixture : IDisposable {
            private static readonly string[] Globals = {
                "_UdonLightVolumeEnabled", "_UdonLightVolumeVersion", "_UdonLightVolumeCount",
                "_UdonLightVolumeAdditiveCount", "_UdonPointLightVolumeCount", "_UdonClusteringEnabled"
            };
            private readonly float[] _previousGlobals = new float[Globals.Length];
            private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
            private readonly RenderTexture _previousTarget;
            private Scene _scene;
            private RenderTexture _target;
            public float[] Values = { 1, 3, 32, 8, 128, 1 };
            public Camera Camera { get; private set; }
            public GameObject Head { get; private set; }
            public GameObject Panel { get; private set; }
            public Material Material { get; private set; }
            public Texture2D Readback { get; private set; }

            public PanelRenderFixture(int width = 160, int height = 112) {
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                    Assert.Ignore("The active graphics API cannot render the stats panel test.");
                _previousTarget = RenderTexture.active;
                for (int index = 0; index < Globals.Length; index++) _previousGlobals[index] = Shader.GetGlobalFloat(Globals[index]);
                try {
                    _scene = EditorSceneManager.NewPreviewScene();
                    Camera = CreateObject("Stats test camera").AddComponent<Camera>();
                    Camera.scene = _scene;
                    Camera.cameraType = CameraType.Preview;
                    Camera.enabled = false;
                    Camera.clearFlags = CameraClearFlags.SolidColor;
                    Camera.backgroundColor = Color.magenta;
                    Camera.orthographic = true;
                    Camera.orthographicSize = 0.21f;
                    Camera.aspect = (float)width / height;
                    Camera.nearClipPlane = 0.1f;
                    Camera.farClipPlane = 20;
                    Camera.allowHDR = false;
                    Camera.allowMSAA = false;
                    Camera.useOcclusionCulling = false;
                    _target = Own(new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear));
                    Assert.That(_target.Create(), Is.True);
                    Camera.targetTexture = _target;
                    Readback = Own(new Texture2D(width, height, TextureFormat.RGBA32, false, true));
                    Material = Own(new Material(LoadAsset<Material>("Light Volume Debugger Stats.mat")));
                    Material.SetTexture("_MainTex", Own(CreateColorAtlas()));
                    Head = CreateObject("Head");
                    Panel = CreateObject("Stats panel");
                    Panel.transform.SetParent(Head.transform, false);
                    Panel.AddComponent<MeshFilter>().sharedMesh = LoadAsset<Mesh>("LightVolumeDebuggerStats.asset");
                    MeshRenderer renderer = Panel.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = Material;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.allowOcclusionWhenDynamic = false;
                    UnityEngine.Camera.onPreRender += SetTestGlobals;
                } catch {
                    Dispose();
                    throw;
                }
            }

            public void Render() {
                RenderTexture previous = RenderTexture.active;
                try {
                    Camera.Render();
                    RenderTexture.active = _target;
                    Readback.ReadPixels(new Rect(0, 0, _target.width, _target.height), 0, 0, false);
                    Readback.Apply(false, false);
                } finally {
                    RenderTexture.active = previous;
                }
            }

            public void CreateOccluder() {
                GameObject occluder = GameObject.CreatePrimitive(PrimitiveType.Quad);
                SceneManager.MoveGameObjectToScene(occluder, _scene);
                occluder.transform.position = new Vector3(0, 0, 0.5f);
                occluder.transform.localScale = new Vector3(0.2f, 0.2f, 1);
                Shader shader = Shader.Find("Unlit/Color");
                Assert.That(shader, Is.Not.Null);
                Material material = Own(new Material(shader));
                material.color = Color.black;
                occluder.GetComponent<Renderer>().sharedMaterial = material;
            }

            private void SetTestGlobals(Camera camera) {
                if (camera != Camera) return;
                for (int index = 0; index < Globals.Length; index++) Shader.SetGlobalFloat(Globals[index], Values[index]);
            }

            private GameObject CreateObject(string name) {
                GameObject obj = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(obj, _scene);
                return obj;
            }

            private T Own<T>(T value) where T : UnityEngine.Object {
                value.hideFlags = HideFlags.HideAndDontSave;
                _owned.Add(value);
                return value;
            }

            public void Dispose() {
                UnityEngine.Camera.onPreRender -= SetTestGlobals;
                RenderTexture.active = _previousTarget;
                if (Camera != null) Camera.targetTexture = null;
                if (_scene.IsValid()) EditorSceneManager.ClosePreviewScene(_scene);
                foreach (UnityEngine.Object value in _owned) if (value != null) UnityEngine.Object.DestroyImmediate(value);
                for (int index = 0; index < Globals.Length; index++) Shader.SetGlobalFloat(Globals[index], _previousGlobals[index]);
            }
        }

        private static Texture2D CreateColorAtlas() {
            Texture2D atlas = new Texture2D(1024, 512, TextureFormat.RGBA32, false, true) {
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
            };
            Color[] pixels = new Color[1024 * 512];
            for (int y = 0; y < 64; y++) {
                for (int x = 0; x < 960; x++) {
                    Color color = x < 352 ? DigitColor(x / 32)
                        : x < 384 ? Color.black
                        : x < 512 ? new Color(0.2f, 0.8f, 0.4f)
                        : x < 544 ? Color.black
                        : x < 672 ? new Color(0.2f, 0.2f, 0.4f)
                        : x < 704 ? Color.black
                        : new Color(0.9f, 0.2f, 0.4f);
                    pixels[y * 1024 + x] = color;
                }
            }
            atlas.SetPixels(pixels);
            atlas.Apply(false, false);
            return atlas;
        }

        private static Color DigitColor(int digit) {
            return digit == 10 ? Color.black : new Color((digit + 1) / 16f, 0.4f, 0.7f);
        }

        private static void AssertPanelNumber(Texture2D readback, int row, int value) {
            AssertPanelColor(readback, row, 545, DigitColor(value < 100 ? 10 : value / 100));
            AssertPanelColor(readback, row, 567, DigitColor(value < 10 ? 10 : value / 10 % 10));
            AssertPanelColor(readback, row, 589, DigitColor(value % 10));
        }

        private static void AssertPanelColor(Texture2D readback, int row, int x, Color expected) {
            Color actual = readback.GetPixel(x / 4, readback.height - 1 - (132 + row * 64) / 4);
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.006f), "Row " + row + ", x " + x);
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.006f), "Row " + row + ", x " + x);
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.006f), "Row " + row + ", x " + x);
        }

        private static T LoadAsset<T>(string relativePath) where T : UnityEngine.Object {
            T asset = AssetDatabase.LoadAssetAtPath<T>(AssetRoot + relativePath);
            Assert.That(asset, Is.Not.Null, AssetRoot + relativePath);
            return asset;
        }
    }
}
