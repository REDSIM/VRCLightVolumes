using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace VRCLightVolumes.Tests {
    [Category("Editor")]
    public class LightVolumeDebuggerDepthTests {
        private const string AssetRoot = "Packages/red.sim.lightvolumes/Extra/Light Volume Debugger/";

        [TestCase("ModularAvatar/Light Volume Debugger MA.prefab")]
        [TestCase("VRCFury/Light Volume Debugger VRCFury.prefab")]
        public void PrefabDepthLightProvidesCameraDepthWithoutChangingSceneColor(string prefabPath) {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null
                    || GraphicsSettings.currentRenderPipeline != null
                    || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat))
                Assert.Ignore("This test requires Built-in rendering and an ARGBFloat render target.");

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetRoot + prefabPath);
            Assert.That(prefab, Is.Not.Null);
            Light[] prefabLights = prefab.GetComponentsInChildren<Light>(true);
            Assert.That(prefabLights.Length, Is.EqualTo(1));
            Assert.That(prefabLights[0].gameObject.activeSelf, Is.False, "The depth light must start disabled.");

            Scene scene = EditorSceneManager.NewPreviewScene();
            ShadowQuality oldShadows = QualitySettings.shadows;
            float oldShadowDistance = QualitySettings.shadowDistance;
            int oldShadowCascades = QualitySettings.shadowCascades;
            int oldPixelLights = QualitySettings.pixelLightCount;
            Texture oldDepthTexture = Shader.GetGlobalTexture("_CameraDepthTexture");
            RenderTexture oldTarget = RenderTexture.active;
            List<Object> resources = new List<Object>();
            CommandBuffer commands = null;
            Camera camera = null;
            try {
                QualitySettings.shadows = ShadowQuality.All;
                QualitySettings.shadowDistance = 20;
                QualitySettings.shadowCascades = 0;
                QualitySettings.pixelLightCount = 4;

                camera = CreateObject(scene, "Debugger depth camera").AddComponent<Camera>();
                camera.scene = scene;
                camera.cameraType = CameraType.Game;
                camera.enabled = false;
                camera.renderingPath = RenderingPath.Forward;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 20;
                camera.fieldOfView = 60;
                camera.cullingMask = -1;
                camera.allowHDR = false;
                camera.allowMSAA = false;
                camera.useOcclusionCulling = false;
                camera.depthTextureMode = DepthTextureMode.None;
                RenderTexture colorTarget = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                resources.Add(colorTarget);
                Assert.That(colorTarget.Create(), Is.True);
                camera.targetTexture = colorTarget;
                RenderTexture depthTarget = new RenderTexture(64, 64, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                resources.Add(depthTarget);
                Assert.That(depthTarget.Create(), Is.True);
                Texture2D readback = new Texture2D(64, 64, TextureFormat.RGBAFloat, false, true);
                resources.Add(readback);

                Shader standard = Shader.Find("Standard");
                Assert.That(standard, Is.Not.Null);
                Material opaqueMaterial = new Material(standard);
                resources.Add(opaqueMaterial);
                CreateOpaque(scene, PrimitiveType.Cube, new Vector3(0, 0, 3), Vector3.one, opaqueMaterial);
                CreateOpaque(scene, PrimitiveType.Quad, new Vector3(0, 0, 8), new Vector3(20, 20, 1), opaqueMaterial);

                Light light = CreateObject(scene, "Prefab depth light clone").AddComponent<Light>();
                EditorUtility.CopySerialized(prefabLights[0], light);
                light.transform.rotation = prefabLights[0].transform.rotation;
                Assert.That(light.type, Is.EqualTo(LightType.Directional));
                Assert.That(light.cullingMask, Is.Not.Zero);
                Assert.That(light.cullingMask & 1, Is.Zero, "The helper must not illuminate the test's Default-layer geometry.");

                Shader probe = ShaderUtil.CreateShaderAsset(DepthProbeShader, false);
                resources.Add(probe);
                Assert.That(ShaderUtil.ShaderHasError(probe), Is.False);
                Material probeMaterial = new Material(probe);
                resources.Add(probeMaterial);
                commands = new CommandBuffer { name = "Debugger depth readback" };
                commands.Blit(BuiltinRenderTextureType.CameraTarget, depthTarget, probeMaterial);
                camera.AddCommandBuffer(CameraEvent.BeforeForwardAlpha, commands);

                light.enabled = false;
                Render(camera, depthTarget);
                ReadTarget(depthTarget, readback);
                Assert.That(Mathf.Abs(readback.GetPixel(32, 32).g - 2.5f), Is.GreaterThan(0.1f),
                    "The baseline must not already have the cube's camera depth.");
                ReadTarget(colorTarget, readback);
                Color[] baseline = readback.GetPixels();

                light.enabled = true;
                Render(camera, depthTarget);
                ReadTarget(depthTarget, readback);
                Assert.That(readback.GetPixel(32, 32).g, Is.EqualTo(2.5f).Within(0.05f), "Cube front depth.");
                Assert.That(readback.GetPixel(8, 32).g, Is.EqualTo(8f).Within(0.15f), "Background depth.");
                Assert.That(camera.depthTextureMode, Is.EqualTo(DepthTextureMode.None));
                ReadTarget(colorTarget, readback);
                Color[] actual = readback.GetPixels();
                float largestDifference = 0;
                for (int index = 0; index < actual.Length; index++) {
                    largestDifference = Mathf.Max(largestDifference,
                        Mathf.Abs(actual[index].r - baseline[index].r),
                        Mathf.Abs(actual[index].g - baseline[index].g),
                        Mathf.Abs(actual[index].b - baseline[index].b));
                }
                Assert.That(largestDifference, Is.LessThanOrEqualTo(1f / 255f),
                    "The helper must not change the rendered scene's color.");
            } finally {
                RenderTexture.active = oldTarget;
                if (camera != null) {
                    if (commands != null) camera.RemoveCommandBuffer(CameraEvent.BeforeForwardAlpha, commands);
                    camera.targetTexture = null;
                }
                if (commands != null) commands.Release();
                EditorSceneManager.ClosePreviewScene(scene);
                for (int index = resources.Count - 1; index >= 0; index--)
                    if (resources[index] != null) Object.DestroyImmediate(resources[index]);
                QualitySettings.shadows = oldShadows;
                QualitySettings.shadowDistance = oldShadowDistance;
                QualitySettings.shadowCascades = oldShadowCascades;
                QualitySettings.pixelLightCount = oldPixelLights;
                Shader.SetGlobalTexture("_CameraDepthTexture", oldDepthTexture);
            }
        }

        private static GameObject CreateObject(Scene scene, string name) {
            GameObject obj = new GameObject(name);
            SceneManager.MoveGameObjectToScene(obj, scene);
            return obj;
        }

        private static void CreateOpaque(Scene scene, PrimitiveType primitive, Vector3 position, Vector3 scale, Material material) {
            GameObject obj = GameObject.CreatePrimitive(primitive);
            SceneManager.MoveGameObjectToScene(obj, scene);
            obj.layer = 0;
            obj.transform.position = position;
            obj.transform.localScale = scale;
            obj.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static void Render(Camera camera, RenderTexture depthTarget) {
            for (int frame = 0; frame < 2; frame++) {
                Shader.SetGlobalTexture("_CameraDepthTexture", Texture2D.blackTexture);
                RenderTexture.active = depthTarget;
                GL.Clear(false, true, Color.magenta);
                RenderTexture.active = null;
                camera.Render();
            }
        }

        private static void ReadTarget(RenderTexture target, Texture2D readback) {
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, 64, 64), 0, 0, false);
            readback.Apply(false, false);
        }

        private const string DepthProbeShader = @"Shader ""Hidden/LV/DebuggerDepthTest"" {
            SubShader { Pass {
                Cull Off ZWrite Off ZTest Always
                CGPROGRAM
                #pragma target 3.0
                #pragma vertex vert_img
                #pragma fragment frag
                #include ""UnityCG.cginc""
                UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
                float4 frag(v2f_img i) : SV_Target {
                    float raw = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv);
                    return float4(raw, LinearEyeDepth(raw), 0, 1);
                }
                ENDCG
            } }
            Fallback Off
        }";
    }
}
