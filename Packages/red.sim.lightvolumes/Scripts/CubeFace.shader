Shader "Hidden/CubeFace"
{
    Properties {
        _CubeTex("Cubemap", Cube) = "" {}
        _FaceIndex("FaceIndex", Int) = 0
    }
    SubShader {
        Tags { "RenderType" = "Opaque" }
        Cull Off ZWrite Off ZTest Always

        Pass {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            samplerCUBE _CubeTex;
            int _FaceIndex;

            struct appdata {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            v2f vert(appdata v) {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            static const float3 faceDirs[6][3] = {
                { float3( 1,  0,  0), float3( 0,  0, -1), float3(0, -1, 0) }, // +X
                { float3(-1,  0,  0), float3( 0,  0,  1), float3(0, -1, 0) }, // -X
                { float3( 0,  1,  0), float3( 1,  0,  0), float3(0,  0, 1) }, // +Y
                { float3( 0, -1,  0), float3( 1,  0,  0), float3(0,  0, -1)}, // -Y
                { float3( 0,  0,  1), float3( 1,  0,  0), float3(0, -1, 0) }, // +Z
                { float3( 0,  0, -1), float3(-1,  0,  0), float3(0, -1, 0) }  // -Z
            };

            float4 frag(v2f i) : SV_Target {
                float2 uv = i.uv * 2 - 1;
                float3 viewDir = faceDirs[_FaceIndex][0] + uv.x * faceDirs[_FaceIndex][1] + uv.y * faceDirs[_FaceIndex][2];

                return texCUBElod(_CubeTex, float4(-normalize(viewDir), 0));
            }
            ENDCG
        }
    }
}
