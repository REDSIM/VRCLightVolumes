using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace VRCLightVolumes.Tests {
    [Category("Editor")]
    public class LightVolumeShaderBufferLayoutTests {
        [TestCase(ShaderCompilerPlatform.D3D, BuildTarget.StandaloneWindows64, "3.5")]
        [TestCase(ShaderCompilerPlatform.D3D, BuildTarget.StandaloneWindows64, "5.0")]
        [TestCase(ShaderCompilerPlatform.GLES3x, BuildTarget.Android, "3.5")]
        [TestCase(ShaderCompilerPlatform.OpenGLCore, BuildTarget.StandaloneWindows64, "3.5")]
        [TestCase(ShaderCompilerPlatform.Vulkan, BuildTarget.Android, "3.5")]
        [TestCase(ShaderCompilerPlatform.Metal, BuildTarget.StandaloneOSX, "3.5")]
        public void ConstantBuffersLeaveRoomForHostShaders(ShaderCompilerPlatform platform, BuildTarget buildTarget, string target) {
            bool direct3D = platform == ShaderCompilerPlatform.D3D;
            int slotLimit = direct3D ? 14 : 12;
            int reservedHostSlots = direct3D ? 12 : 9;
            int bufferSizeLimit = (direct3D ? 64 : 16) * 1024;
            Shader shader = ShaderUtil.CreateShaderAsset(BuildBudgetShader(target), false);
            try {
                // OpenGL and Vulkan return all stages through the vertex program.
                ShaderType stage = direct3D || platform == ShaderCompilerPlatform.Metal
                    ? ShaderType.Fragment : ShaderType.Vertex;
                var compiled = ShaderUtil.GetShaderData(shader).GetSubshader(0).GetPass(0)
                    .CompileVariant(stage, Array.Empty<string>(), platform, buildTarget, true);

                StringBuilder messages = new StringBuilder();
                foreach (var message in compiled.Messages) messages.AppendLine(message.message);
                Assert.That(compiled.Success, Is.True, messages.ToString());
                Assert.That(compiled.ShaderData, Is.Not.Empty, "The compiler returned no shader program.");
                Assert.That(compiled.ConstantBuffers, Is.Not.Empty, "The compiler returned no buffer layout.");

                // Count globals too. The remaining bindings are for Unity and the host shader.
                int freeSlots = slotLimit - compiled.ConstantBuffers.Length;
                Assert.That(freeSlots, Is.GreaterThanOrEqualTo(reservedHostSlots),
                    platform + ": Light Volumes exceeded its cbuffer slot budget.");
                foreach (var buffer in compiled.ConstantBuffers)
                    Assert.That(buffer.Size, Is.LessThanOrEqualTo(bufferSizeLimit),
                        platform + ": " + buffer.Name + " exceeded the cbuffer size limit.");

                TestContext.WriteLine(platform + ": " + compiled.ConstantBuffers.Length
                    + " buffers used; " + freeSlots + " slots available for Unity and the host shader.");
            } finally {
                UnityEngine.Object.DestroyImmediate(shader);
            }
        }

        private static string BuildBudgetShader(string target) {
            const string includePath = "Packages/red.sim.lightvolumes/Shaders/LightVolumes.cginc";
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string source = File.ReadAllText(Path.Combine(projectRoot, includePath));
            var uniforms = Regex.Matches(source,
                @"^[ \t]*uniform\s+(?:float|half|int|uint|bool)(?<rows>[1-4])?(?:x(?<columns>[1-4]))?\s+(?<name>\w+)\s*(?:\[(?<count>[^\]]+)\])?\s*;",
                RegexOptions.Multiline);
            Assert.That(uniforms.Count, Is.GreaterThan(0), "No numeric uniforms found in the include.");

            // Dynamic indices keep all array elements and vector/matrix components reachable.
            StringBuilder fragment = new StringBuilder(
                "float4 frag(float4 position : SV_Position) : SV_Target {\n"
                + "uint index = (uint)position.x; uint component = (uint)position.y;\n"
                + "float total = _BudgetHostParameter;\n");
            foreach (Match uniform in uniforms) {
                string value = uniform.Groups["name"].Value;
                if (uniform.Groups["count"].Success)
                    value += "[index % (" + uniform.Groups["count"].Value + ")]";
                if (uniform.Groups["rows"].Success)
                    value += "[component % " + uniform.Groups["rows"].Value + "]";
                if (uniform.Groups["columns"].Success)
                    value += "[(uint)position.z % " + uniform.Groups["columns"].Value + "]";
                fragment.AppendLine("total += " + value + ";");
            }
            fragment.AppendLine("return total.xxxx; }");

            return "Shader \"Hidden/VRCLightVolumes/BufferBudgetTest\" { SubShader { Pass { CGPROGRAM\n"
                + "#pragma target " + target + "\n#pragma vertex vert\n#pragma fragment frag\n"
                + "#define VRCLV_FORCE_FULL_FEATURES 1\n#include \"UnityCG.cginc\"\n"
                + "#include \"" + includePath + "\"\nfloat _BudgetHostParameter;\n"
                + "float4 vert(float4 position : POSITION) : SV_Position { return position; }\n"
                + fragment + "\nENDCG\n} } }";
        }
    }
}
