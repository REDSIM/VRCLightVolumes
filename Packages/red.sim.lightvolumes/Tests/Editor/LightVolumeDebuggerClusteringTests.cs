using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace VRCLightVolumes.Tests {
    [Category("Editor")]
    public class LightVolumeDebuggerClusteringTests {
        private const string AssetRoot = "Packages/red.sim.lightvolumes/Extra/Light Volume Debugger/";
        private const string ShaderName = "Light Volume Samples/Light Volume Debugger Clustering";
        private const int ImageSize = 64;
        private const int GridSize = 8;
        private const float GridNear = 0.1f;
        private const float GridFar = 30f;
        private static readonly Vector2Int[] SamplePixels = {
            new Vector2Int(13, 17), new Vector2Int(39, 24),
            new Vector2Int(23, 46), new Vector2Int(51, 49)
        };

        [TestCase(ShaderCompilerPlatform.D3D, BuildTarget.StandaloneWindows64, "")]
        [TestCase(ShaderCompilerPlatform.D3D, BuildTarget.StandaloneWindows64, "STEREO_INSTANCING_ON")]
        [TestCase(ShaderCompilerPlatform.D3D, BuildTarget.StandaloneWindows64, "UNITY_SINGLE_PASS_STEREO")]
        [TestCase(ShaderCompilerPlatform.GLES3x, BuildTarget.Android, "")]
        [TestCase(ShaderCompilerPlatform.GLES3x, BuildTarget.Android, "STEREO_MULTIVIEW_ON")]
        [TestCase(ShaderCompilerPlatform.Vulkan, BuildTarget.StandaloneWindows64, "")]
        [TestCase(ShaderCompilerPlatform.Metal, BuildTarget.StandaloneOSX, "")]
        public void ClusteringShaderCompilesWithDepthAndIntegerMasks(ShaderCompilerPlatform platform, BuildTarget target, string keyword) {
            Shader shader = LoadAsset<Shader>("Light Volume Debugger Clustering.shader");
            Assert.That(shader.name, Is.EqualTo(ShaderName));
            string[] keywords = keyword.Length == 0 ? Array.Empty<string>() : new[] { keyword };
            var pass = ShaderUtil.GetShaderData(shader).GetSubshader(0).GetPass(0);
            ShaderType[] stages = platform == ShaderCompilerPlatform.D3D || platform == ShaderCompilerPlatform.Metal
                ? new[] { ShaderType.Vertex, ShaderType.Fragment } : new[] { ShaderType.Vertex };
            foreach (ShaderType stage in stages) {
                var compiled = pass.CompileVariant(stage, keywords, platform, target, true);
                StringBuilder messages = new StringBuilder();
                foreach (var message in compiled.Messages) messages.AppendLine(message.message);
                Assert.That(compiled.Success, Is.True, platform + " " + keyword + " " + stage + ": " + messages);
                Assert.That(compiled.ShaderData, Is.Not.Empty);
            }
        }

        [Test]
        public void ClusteringUsesOneCubeAndOneMaterialPass() {
            Mesh mesh = LoadAsset<Mesh>("LightVolumeDebuggerClustering.asset");
            Assert.That(mesh.vertexCount, Is.EqualTo(8));
            Assert.That(mesh.subMeshCount, Is.EqualTo(1));
            Assert.That(mesh.GetIndexCount(0), Is.EqualTo(36));
            Material material = LoadAsset<Material>("Light Volume Debugger Clustering.mat");
            Assert.That(material.shader.name, Is.EqualTo(ShaderName));
            Assert.That(material.passCount, Is.EqualTo(1));
            foreach (string global in new[] {
                "_UdonLightVolumeEnabled", "_UdonLightVolumeVersion", "_UdonClusteringEnabled",
                "_UdonClusterMask", "_CameraDepthTexture"
            }) Assert.That(material.HasProperty(global), Is.False, global + " must remain a shader global.");
        }

        [TestCase(1f)]
        [TestCase(6f)]
        public void RealCameraDepthSelectsTheExpectedWorldClusters(float surfaceDistance) {
            using (RenderFixture fixture = new RenderFixture()) {
                fixture.Wall.transform.position = new Vector3(0, 0, surfaceDistance);
                fixture.Render();
                fixture.AssertSurfaceColors();
            }
        }

        [Test]
        public void TranslatedAndRotatedCameraUsesWorldSpaceClusterAddressing() {
            using (RenderFixture fixture = new RenderFixture()) {
                fixture.Camera.transform.SetPositionAndRotation(new Vector3(0.5f, 0.25f, 0.5f), Quaternion.Euler(-5, 15, 0));
                fixture.Render();
                fixture.AssertSurfaceColors();
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AsymmetricAndObliqueProjectionsReconstructTheSameSurface(bool oblique) {
            using (RenderFixture fixture = new RenderFixture()) {
                Matrix4x4 projection = fixture.Camera.projectionMatrix;
                projection.m02 = 0.24f;
                projection.m12 = -0.19f;
                fixture.Camera.projectionMatrix = projection;
                if (oblique) {
                    // The wall remains beyond this slanted camera-space near plane.
                    Vector3 normal = new Vector3(0.2f, -0.15f, -1f).normalized;
                    fixture.Camera.projectionMatrix = fixture.Camera.CalculateObliqueMatrix(
                        new Vector4(normal.x, normal.y, normal.z, -0.5f));
                }
                fixture.Render();
                fixture.AssertSurfaceColors();
            }
        }

        [TestCase(0f, 3f, 1f)]
        [TestCase(1f, 2f, 1f)]
        [TestCase(1f, 3f, 0f)]
        public void DisabledOrUnsupportedClusteringLeavesTheSceneVisible(float enabled, float version, float clustering) {
            using (RenderFixture fixture = new RenderFixture()) {
                fixture.Enabled = enabled;
                fixture.Version = version;
                fixture.Clustering = clustering;
                fixture.AssertUnchangedFromNormalView();
            }
        }

        [Test]
        public void CameraOutsideThreeMetersDoesNotReceiveTheOverlay() {
            using (RenderFixture fixture = new RenderFixture()) {
                fixture.Camera.transform.position = new Vector3(3.1f, 0, 0);
                fixture.AssertUnchangedFromNormalView();
            }
        }

        [Test]
        public void SmallRotatedAvatarStillCoversACameraInsideTheWorldRadius() {
            using (RenderFixture fixture = new RenderFixture()) {
                fixture.Overlay.transform.localScale = Vector3.one * 0.1f;
                fixture.Overlay.transform.rotation = Quaternion.Euler(30, 60, 20);
                fixture.Camera.transform.position = new Vector3(0, 0, 2.5f);
                fixture.Render();
                fixture.AssertSurfaceColors();
            }
        }

        [Test]
        public void ClearDepthLeavesTheSkyVisible() {
            using (RenderFixture fixture = new RenderFixture()) {
                fixture.Wall.SetActive(false);
                fixture.AssertUnchangedFromNormalView();
            }
        }

        [Test]
        public void EmptyClusterIsBlackAndOutsideGridLeavesTheSceneVisible() {
            using (RenderFixture fixture = new RenderFixture()) {
                fixture.EmptyMask = true;
                fixture.UploadMask();
                fixture.Render();
                foreach (Vector2Int pixel in SamplePixels) AssertColor(fixture.ReadPixel(pixel), Color.black, "Empty cluster");
                fixture.Wall.transform.position = new Vector3(0, 0, GridFar + 5f);
                fixture.AssertUnchangedFromNormalView();
            }
        }

        private sealed class RenderFixture : IDisposable {
            private static readonly string[] FloatGlobals = {
                "_UdonLightVolumeEnabled", "_UdonLightVolumeVersion", "_UdonClusteringEnabled"
            };
            private static readonly string[] VectorGlobals = {
                "_UdonFroxelGrid", "_UdonFroxelDepth", "_UdonFroxelProjection",
                "_UdonFroxelRight", "_UdonFroxelUp", "_UdonFroxelForward"
            };
            private readonly Dictionary<string, float> _savedFloats = new Dictionary<string, float>();
            private readonly Dictionary<string, Vector4> _savedVectors = new Dictionary<string, Vector4>();
            private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
            private readonly Texture _savedMask;
            private readonly Texture _savedDepth;
            private readonly RenderTexture _previousTarget;
            private Scene _scene;
            private RenderTexture _target;
            private Texture2D _readback;
            private RenderTexture _mask;
            private Material _maskMaterial;
            public Camera Camera { get; private set; }
            public GameObject Wall { get; private set; }
            public MeshRenderer Overlay { get; private set; }
            public float Enabled = 1f;
            public float Version = 3f;
            public float Clustering = 1f;
            public bool EmptyMask;

            public RenderFixture() {
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || SystemInfo.graphicsShaderLevel < 35
                        || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat)
                        || !SystemInfo.SupportsTextureFormat(TextureFormat.RGBAFloat)
                        || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBInt))
                    Assert.Ignore("The active graphics API cannot render the clustering overlay test.");

                foreach (string name in FloatGlobals) _savedFloats[name] = Shader.GetGlobalFloat(name);
                foreach (string name in VectorGlobals) _savedVectors[name] = Shader.GetGlobalVector(name);
                _savedMask = Shader.GetGlobalTexture("_UdonClusterMask");
                _savedDepth = Shader.GetGlobalTexture("_CameraDepthTexture");
                _previousTarget = RenderTexture.active;
                try {
                    _scene = EditorSceneManager.NewPreviewScene();
                    Camera = CreateObject("Clustering test camera").AddComponent<Camera>();
                    Camera.cameraType = CameraType.Preview;
                    Camera.scene = _scene;
                    Camera.enabled = false;
                    Camera.clearFlags = CameraClearFlags.SolidColor;
                    Camera.backgroundColor = new Color(0.025f, 0.05f, 0.075f, 1);
                    Camera.nearClipPlane = 0.1f;
                    Camera.farClipPlane = 50f;
                    Camera.fieldOfView = 60f;
                    Camera.aspect = 1f;
                    Camera.allowMSAA = false;
                    Camera.allowHDR = true;
                    Camera.depthTextureMode = DepthTextureMode.Depth;
                    _target = Own(new RenderTexture(ImageSize, ImageSize, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
                    Assert.That(_target.Create(), Is.True);
                    Camera.targetTexture = _target;
                    _readback = Own(new Texture2D(ImageSize, ImageSize, TextureFormat.RGBAFloat, false, true));
                    _mask = Own(new RenderTexture(GridSize * GridSize, GridSize, 0, RenderTextureFormat.ARGBInt, RenderTextureReadWrite.Linear) {
                        filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
                    });
                    Assert.That(_mask.Create(), Is.True);
                    Shader maskShader = Own(ShaderUtil.CreateShaderAsset(@"
Shader ""Hidden/VRCLVClusteringTestMask"" {
    Properties { _Empty(""Empty"", Float) = 0 }
    SubShader {
        Pass {
            Cull Off ZWrite Off ZTest Always
            CGPROGRAM
            #pragma target 3.5
            #pragma require integers
            #pragma vertex vert_img
            #pragma fragment frag
            #include ""UnityCG.cginc""
            float _Empty;
            int4 frag(v2f_img input) : SV_Target {
                if (_Empty > 0.5) return 0;
                uint2 texel = (uint2)input.pos.xy;
                uint x = texel.x & 7u;
                uint y = texel.x >> 3u;
                uint z = texel.y;
                uint4 mask = uint4(
                    1u << ((x + z) & 31u),
                    1u << ((y + 8u) & 31u),
                    1u << ((x * 3u + y * 5u + z * 7u) & 31u),
                    0x80000000u | (x + y * 8u + z * 64u));
                return asint(mask);
            }
            ENDCG
        }
    }
}", false));
                    Assert.That(maskShader.isSupported, Is.True);
                    _maskMaterial = Own(new Material(maskShader));
                    UploadMask();

                    Mesh wallMesh = Own(new Mesh());
                    wallMesh.vertices = new[] {
                        new Vector3(-20, -20, 0), new Vector3(20, -20, 0),
                        new Vector3(-20, 20, 0), new Vector3(20, 20, 0)
                    };
                    wallMesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
                    wallMesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
                    wallMesh.RecalculateBounds();
                    Shader wallShader = Shader.Find("Standard");
                    Assert.That(wallShader, Is.Not.Null);
                    Material wallMaterial = Own(new Material(wallShader));
                    wallMaterial.color = Color.black;
                    wallMaterial.SetColor("_EmissionColor", new Color(0.12f, 0.18f, 0.08f));
                    wallMaterial.EnableKeyword("_EMISSION");
                    Wall = CreateObject("Opaque depth surface");
                    Wall.transform.position = new Vector3(0, 0, 6);
                    Wall.AddComponent<MeshFilter>().sharedMesh = wallMesh;
                    Wall.AddComponent<MeshRenderer>().sharedMaterial = wallMaterial;

                    GameObject overlayObject = CreateObject("Clustering overlay");
                    overlayObject.AddComponent<MeshFilter>().sharedMesh = LoadAsset<Mesh>("LightVolumeDebuggerClustering.asset");
                    Overlay = overlayObject.AddComponent<MeshRenderer>();
                    Overlay.sharedMaterial = LoadAsset<Material>("Light Volume Debugger Clustering.mat");
                    Overlay.shadowCastingMode = ShadowCastingMode.Off;
                    Overlay.receiveShadows = false;
                    Overlay.allowOcclusionWhenDynamic = false;
                    // The editor preview hook disables clustering at pre-cull for non-SceneView cameras.
                    UnityEngine.Camera.onPreRender += SetTestGlobals;
                } catch {
                    Dispose();
                    throw;
                }
            }

            public void UploadMask() {
                RenderTexture previous = RenderTexture.active;
                try {
                    _maskMaterial.SetFloat("_Empty", EmptyMask ? 1f : 0f);
                    Graphics.Blit(Texture2D.blackTexture, _mask, _maskMaterial);
                } finally {
                    RenderTexture.active = previous;
                }
            }

            public void Render() {
                RenderTexture previous = RenderTexture.active;
                try {
                    Camera.Render();
                    RenderTexture.active = _target;
                    _readback.ReadPixels(new Rect(0, 0, ImageSize, ImageSize), 0, 0, false);
                    _readback.Apply(false, false);
                } finally {
                    RenderTexture.active = previous;
                }
            }

            public Color ReadPixel(Vector2Int pixel) { return _readback.GetPixel(pixel.x, pixel.y); }

            public void AssertSurfaceColors() {
                foreach (Vector2Int pixel in SamplePixels) {
                    Ray ray = Camera.ViewportPointToRay(new Vector3((pixel.x + 0.5f) / ImageSize, (pixel.y + 0.5f) / ImageSize, 0));
                    float distance = (Wall.transform.position.z - ray.origin.z) / ray.direction.z;
                    Assert.That(distance, Is.GreaterThan(0));
                    Vector3 surface = ray.GetPoint(distance);
                    Assert.That(surface.z, Is.InRange(GridNear, GridFar));
                    Vector2 uv = new Vector2(surface.x, surface.y) * (0.5f / surface.z) + Vector2.one * 0.5f;
                    Assert.That(uv.x, Is.InRange(0f, 1f));
                    Assert.That(uv.y, Is.InRange(0f, 1f));
                    int x = Mathf.Min((int)(uv.x * GridSize), GridSize - 1);
                    int y = Mathf.Min((int)(uv.y * GridSize), GridSize - 1);
                    int z = Mathf.Min((int)(Mathf.Log(surface.z / GridNear, 2f) * GridSize / Mathf.Log(GridFar / GridNear, 2f)), GridSize - 1);
                    AssertColor(ReadPixel(pixel), MaskColor(MaskAt(x, y, z)), "Pixel " + pixel + ", cell " + new Vector3Int(x, y, z));
                }
            }

            public void AssertUnchangedFromNormalView() {
                Overlay.enabled = false;
                Render();
                Color[] baseline = new Color[SamplePixels.Length];
                for (int i = 0; i < baseline.Length; i++) baseline[i] = ReadPixel(SamplePixels[i]);
                Overlay.enabled = true;
                Render();
                for (int i = 0; i < baseline.Length; i++) AssertColor(ReadPixel(SamplePixels[i]), baseline[i], "Normal view at " + SamplePixels[i]);
            }

            private void SetTestGlobals(Camera camera) {
                if (camera != Camera) return;
                Shader.SetGlobalFloat("_UdonLightVolumeEnabled", Enabled);
                Shader.SetGlobalFloat("_UdonLightVolumeVersion", Version);
                Shader.SetGlobalFloat("_UdonClusteringEnabled", Clustering);
                Shader.SetGlobalVector("_UdonFroxelGrid", new Vector4(GridSize, GridSize, GridSize, 3));
                Shader.SetGlobalVector("_UdonFroxelDepth", new Vector4(GridNear, GridFar, 1f / GridNear, GridSize / Mathf.Log(GridFar / GridNear, 2f)));
                Shader.SetGlobalVector("_UdonFroxelProjection", new Vector4(1, 1, 0, 0));
                Shader.SetGlobalVector("_UdonFroxelRight", new Vector4(1, 0, 0, 0));
                Shader.SetGlobalVector("_UdonFroxelUp", new Vector4(0, 1, 0, 0));
                Shader.SetGlobalVector("_UdonFroxelForward", new Vector4(0, 0, 1, 0));
                Shader.SetGlobalTexture("_UdonClusterMask", _mask);
            }

            private GameObject CreateObject(string name) {
                GameObject gameObject = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(gameObject, _scene);
                return gameObject;
            }

            private T Own<T>(T value) where T : UnityEngine.Object {
                value.hideFlags = HideFlags.HideAndDontSave;
                _owned.Add(value);
                return value;
            }

            public void Dispose() {
                UnityEngine.Camera.onPreRender -= SetTestGlobals;
                if (Camera != null) Camera.targetTexture = null;
                if (_scene.IsValid()) EditorSceneManager.ClosePreviewScene(_scene);
                foreach (UnityEngine.Object value in _owned) if (value != null) UnityEngine.Object.DestroyImmediate(value);
                RenderTexture.active = _previousTarget;
                foreach (var pair in _savedFloats) Shader.SetGlobalFloat(pair.Key, pair.Value);
                foreach (var pair in _savedVectors) Shader.SetGlobalVector(pair.Key, pair.Value);
                Shader.SetGlobalTexture("_UdonClusterMask", _savedMask);
                Shader.SetGlobalTexture("_CameraDepthTexture", _savedDepth);
            }
        }

        private static uint[] MaskAt(int x, int y, int z) {
            return new[] {
                1u << ((x + z) & 31), 1u << ((y + 8) & 31),
                1u << ((x * 3 + y * 5 + z * 7) & 31), 0x80000000u | (uint)(x + y * GridSize + z * GridSize * GridSize)
            };
        }

        private static Color MaskColor(uint[] mask) {
            uint hash = 2166136261u;
            unchecked {
                foreach (uint word in mask) hash = (hash ^ word) * 16777619u;
                hash ^= hash >> 16;
                hash *= 2146121005u;
                hash ^= hash >> 15;
                hash *= 2221713035u;
                hash ^= hash >> 16;
            }
            float hue = (hash & 16777215u) / 16777216f;
            float saturation = 0.84f + ((hash >> 24) & 3u) * 0.04f;
            Color rgb = new Color(HueChannel(hue), HueChannel(hue + 0.6666667f), HueChannel(hue + 0.3333333f), 1);
            return Color.Lerp(Color.white, rgb, saturation);
        }

        private static float HueChannel(float hue) {
            return Mathf.Clamp01(Mathf.Abs((hue - Mathf.Floor(hue)) * 6f - 3f) - 1f);
        }

        private static void AssertColor(Color actual, Color expected, string context) {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.006f), context + " R");
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.006f), context + " G");
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.006f), context + " B");
        }

        private static T LoadAsset<T>(string relativePath) where T : UnityEngine.Object {
            T asset = AssetDatabase.LoadAssetAtPath<T>(AssetRoot + relativePath);
            Assert.That(asset, Is.Not.Null, AssetRoot + relativePath);
            return asset;
        }
    }
}
