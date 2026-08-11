using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace VRCLightVolumes.Tests {
    [Category("Editor")]
    public class LightVolumeShaderBufferLayoutTests {
        private const int PortableUniformBlockLimit = 16 * 1024;

        private static readonly Regex _numericUniformRegex = new Regex(
            @"^[ \t]*uniform[ \t]+(?<type>float(?:[1-4](?:x[1-4])?)?)[ \t]+(?<name>[A-Za-z_][A-Za-z0-9_]*)[ \t]*(?:\[[ \t]*(?<count>[^\]\r\n]+)[ \t]*\])?[ \t]*;",
            RegexOptions.Multiline);

        // Keeps every explicit block within the 16 KiB floor while preserving UBO slots for
        // arbitrary host shaders. The heaviest known integration uses seven non-VRCLV blocks.
        [Test]
        public void NumericUniformsUseThreeFrequencyPartitionedPortableConstantBuffers() {
            string source = ReadIncludeSource();
            Assert.That(ResolveArrayCount("VRCLV_MAX_VOLUMES_COUNT", source), Is.EqualTo(32));
            Assert.That(ResolveArrayCount("VRCLV_MAX_LIGHTS_COUNT", source), Is.EqualTo(128));
            MatchCollection buffers = Regex.Matches(source, @"\bcbuffer[ \t]+[A-Za-z_][A-Za-z0-9_]*[ \t]*\{", RegexOptions.Multiline);
            string coldBody = FindBufferBody(source, "LightVolumeUniforms");
            string clusteringBody = FindBufferBody(source, "LightVolumeClusteringUniforms");
            string pointBody = FindBufferBody(source, "PointLightVolumeUniforms");

            Assert.That(buffers.Count, Is.EqualTo(3), "Adding another constant buffer spends scarce host-shader binding headroom.");
            Assert.That(_numericUniformRegex.Matches(coldBody).Count + _numericUniformRegex.Matches(clusteringBody).Count + _numericUniformRegex.Matches(pointBody).Count,
                Is.EqualTo(_numericUniformRegex.Matches(source).Count), "Numeric globals outside the explicit blocks would recreate an implicit oversized buffer.");

            int coldBytes = EstimateBufferBytes(coldBody, source);
            int clusteringBytes = EstimateBufferBytes(clusteringBody, source);
            int pointBytes = EstimateBufferBytes(pointBody, source);
            Assert.That(coldBytes, Is.LessThanOrEqualTo(PortableUniformBlockLimit));
            Assert.That(clusteringBytes, Is.LessThanOrEqualTo(PortableUniformBlockLimit));
            Assert.That(pointBytes, Is.LessThanOrEqualTo(PortableUniformBlockLimit));

            AssertBufferContainsExactly(coldBody,
                "_UdonLightVolumeEnabled", "_UdonLightVolumeVersion", "_UdonLightVolumeCount",
                "_UdonLightVolumeAdditiveMaxOverdraw", "_UdonLightVolumeAdditiveCount",
                "_UdonLightVolumeProbesBlend", "_UdonLightVolumeSharpBounds", "_UdonClusteringEnabled",
                "_UdonPointLightVolumeCount", "_UdonPointLightVolumeCubeCount",
                "_UdonPointLightVolumeShadowCubeCount", "_UdonPointLightVolumeShadowCount",
                "_UdonPointLightVolumeShadowReceiverParams", "_UdonLightBrightnessCutoff",
                "_UdonPointLightVolumeTextureTexelCount", "_UdonPointLightVolumeTextureMaxMip",
                "_UdonFroxelGrid", "_UdonFroxelDepth", "_UdonFroxelProjection",
                "_UdonLightVolumeInvWorldMatrix", "_UdonLightVolumeRotation",
                "_UdonLightVolumeInvLocalEdgeSmooth", "_UdonLightVolumeUvwScale", "_UdonLightVolumeColor",
                "_UdonPointLightVolumeShadowReprojectionData", "_UdonPointLightVolumeShadowRotationData");
            AssertBufferContainsExactly(clusteringBody,
                "_UdonFroxelRight", "_UdonFroxelUp", "_UdonFroxelForward");
            AssertBufferContainsExactly(pointBody,
                "_UdonPointLightVolumePosition", "_UdonPointLightVolumeColor",
                "_UdonPointLightVolumeExtraData", "_UdonPointLightVolumeDirection",
                "_UdonPointLightVolumeCustomID");
        }

        // The manager and legacy Light Volumes versions publish these names independently of cbuffer membership.
        [Test]
        public void ShaderGlobalContractsRemainStableAcrossBufferSplit() {
            string source = ReadIncludeSource();
            string[,] expectedContracts = {
                { "_UdonLightVolumeEnabled", "float", "" },
                { "_UdonLightVolumeVersion", "float", "" },
                { "_UdonLightVolumeCount", "float", "" },
                { "_UdonLightVolumeAdditiveMaxOverdraw", "float", "" },
                { "_UdonLightVolumeAdditiveCount", "float", "" },
                { "_UdonLightVolumeProbesBlend", "float", "" },
                { "_UdonLightVolumeSharpBounds", "float", "" },
                { "_UdonClusteringEnabled", "float", "" },
                { "_UdonLightVolumeInvWorldMatrix", "float4x4", "VRCLV_MAX_VOLUMES_COUNT" },
                { "_UdonLightVolumeRotation", "float4", "VRCLV_MAX_VOLUMES_COUNT*2" },
                { "_UdonLightVolumeInvLocalEdgeSmooth", "float3", "VRCLV_MAX_VOLUMES_COUNT" },
                { "_UdonLightVolumeUvwScale", "float4", "VRCLV_MAX_VOLUMES_COUNT*3" },
                { "_UdonLightVolumeColor", "float4", "VRCLV_MAX_VOLUMES_COUNT" },
                { "_UdonFroxelGrid", "float4", "" },
                { "_UdonFroxelDepth", "float4", "" },
                { "_UdonFroxelProjection", "float4", "" },
                { "_UdonFroxelRight", "float4", "" },
                { "_UdonFroxelUp", "float4", "" },
                { "_UdonFroxelForward", "float4", "" },
                { "_UdonPointLightVolumeCount", "float", "" },
                { "_UdonPointLightVolumeCubeCount", "float", "" },
                { "_UdonPointLightVolumeShadowCubeCount", "float", "" },
                { "_UdonPointLightVolumeShadowCount", "float", "" },
                { "_UdonPointLightVolumeShadowReceiverParams", "float4", "" },
                { "_UdonLightBrightnessCutoff", "float", "" },
                { "_UdonPointLightVolumeTextureTexelCount", "float", "" },
                { "_UdonPointLightVolumeTextureMaxMip", "float", "" },
                { "_UdonPointLightVolumePosition", "float4", "VRCLV_MAX_LIGHTS_COUNT" },
                { "_UdonPointLightVolumeColor", "float4", "VRCLV_MAX_LIGHTS_COUNT" },
                { "_UdonPointLightVolumeExtraData", "float4", "VRCLV_MAX_LIGHTS_COUNT" },
                { "_UdonPointLightVolumeDirection", "float4", "VRCLV_MAX_LIGHTS_COUNT" },
                { "_UdonPointLightVolumeCustomID", "float4", "VRCLV_MAX_LIGHTS_COUNT" },
                { "_UdonPointLightVolumeShadowReprojectionData", "float4", "VRCLV_MAX_LIGHTS_COUNT" },
                { "_UdonPointLightVolumeShadowRotationData", "float4", "VRCLV_MAX_LIGHTS_COUNT" }
            };

            MatchCollection declarations = _numericUniformRegex.Matches(source);
            Assert.That(declarations.Count, Is.EqualTo(expectedContracts.GetLength(0)), "Unexpected numeric shader-global contract change.");

            for (int expectedIndex = 0; expectedIndex < expectedContracts.GetLength(0); expectedIndex++) {
                string expectedName = expectedContracts[expectedIndex, 0];
                int matchingDeclarations = 0;
                for (int declarationIndex = 0; declarationIndex < declarations.Count; declarationIndex++) {
                    Match declaration = declarations[declarationIndex];
                    if (declaration.Groups["name"].Value != expectedName) continue;

                    matchingDeclarations++;
                    Assert.That(declaration.Groups["type"].Value, Is.EqualTo(expectedContracts[expectedIndex, 1]), "Type changed for " + expectedName);
                    Assert.That(NormalizeArrayCount(declaration.Groups["count"].Value), Is.EqualTo(expectedContracts[expectedIndex, 2]), "Array length changed for " + expectedName);
                }

                Assert.That(matchingDeclarations, Is.EqualTo(1), "Missing or duplicate shader global " + expectedName);
            }
        }

        // Removes insignificant source whitespace before comparing fixed array-size expressions.
        private static string NormalizeArrayCount(string expression) {
            return Regex.Replace(expression, @"\s+", string.Empty);
        }

        private static void AssertBufferContainsExactly(string body, params string[] expectedNames) {
            MatchCollection declarations = _numericUniformRegex.Matches(body);
            string[] actualNames = new string[declarations.Count];
            for (int i = 0; i < declarations.Count; i++)
                actualNames[i] = declarations[i].Groups["name"].Value;
            CollectionAssert.AreEquivalent(expectedNames, actualNames);
        }

        // Reads the embedded package include from the Unity project.
        private static string ReadIncludeSource() {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string includePath = Path.Combine(projectRoot, "Packages", "red.sim.lightvolumes", "Shaders", "LightVolumes.cginc");
            Assert.That(File.Exists(includePath), Is.True, "Missing shader include at " + includePath);
            return File.ReadAllText(includePath);
        }

        // Extracts one cbuffer body without depending on the surface-analysis preprocessor branch.
        private static string FindBufferBody(string source, string bufferName) {
            Match match = Regex.Match(source, @"\bcbuffer[ \t]+" + Regex.Escape(bufferName) + @"[ \t]*\{(?<body>.*?)\}", RegexOptions.Singleline);
            Assert.That(match.Success, Is.True, "Missing constant buffer " + bufferName);
            return match.Groups["body"].Value;
        }

        // Applies HLSL's 16-byte register and array-stride packing rules conservatively.
        private static int EstimateBufferBytes(string body, string source) {
            int offset = 0;
            MatchCollection uniforms = _numericUniformRegex.Matches(body);
            for (int i = 0; i < uniforms.Count; i++) {
                Match uniform = uniforms[i];
                int typeBytes = GetTypeBytes(uniform.Groups["type"].Value);
                Group countGroup = uniform.Groups["count"];
                if (countGroup.Success) {
                    offset = Align16(offset);
                    offset += Align16(typeBytes) * ResolveArrayCount(countGroup.Value, source);
                    continue;
                }

                int registerOffset = offset & 15;
                if (registerOffset + typeBytes > 16) offset = Align16(offset);
                offset += typeBytes;
            }
            return Align16(offset);
        }

        // Resolves the simple integer products used by the include's fixed-capacity arrays.
        private static int ResolveArrayCount(string expression, string source) {
            string[] factors = expression.Replace("(", string.Empty).Replace(")", string.Empty).Split('*');
            int result = 1;
            for (int i = 0; i < factors.Length; i++) {
                string factor = factors[i].Trim();
                int value;
                if (!int.TryParse(factor, out value)) {
                    Match define = Regex.Match(source, @"^[ \t]*#define[ \t]+" + Regex.Escape(factor) + @"[ \t]+(?<value>[0-9]+)[ \t]*\r?$", RegexOptions.Multiline);
                    Assert.That(define.Success, Is.True, "Unsupported array-size expression factor " + factor);
                    value = int.Parse(define.Groups["value"].Value);
                }
                result *= value;
            }
            return result;
        }

        // Returns the constant-buffer storage occupied by one scalar, vector, or column-major matrix.
        private static int GetTypeBytes(string typeName) {
            if (typeName == "float" || typeName == "float1") return 4;
            if (typeName == "float2") return 8;
            if (typeName == "float3") return 12;
            if (typeName == "float4") return 16;

            Match matrix = Regex.Match(typeName, @"^float(?<rows>[1-4])x(?<columns>[1-4])$");
            Assert.That(matrix.Success, Is.True, "Unsupported constant-buffer type " + typeName);
            return 16 * int.Parse(matrix.Groups["columns"].Value);
        }

        // Rounds a byte offset up to the next four-float register.
        private static int Align16(int value) {
            return (value + 15) & ~15;
        }
    }
}
