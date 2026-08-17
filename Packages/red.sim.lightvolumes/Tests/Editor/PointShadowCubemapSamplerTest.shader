Shader "Hidden/VRCLV/Tests/PointShadowCubemapSampler" {
    Properties {
        _TestDirection("Test Direction", Vector) = (1,0,0,0)
    }

    SubShader {
        Pass {
            Cull Off ZWrite Off ZTest Always

            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #define VRCLV_DISABLE_CLUSTERING
            #define VRCLV_DISABLE_AREA_LIGHTS
            #include "UnityCG.cginc"
            #include "Packages/red.sim.lightvolumes/Shaders/LightVolumes.cginc"

            float3 _TestDirection;

            struct appdata {
                float4 vertex : POSITION;
            };

            struct v2f {
                float4 vertex : SV_POSITION;
            };

            v2f vert(appdata input) {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                return output;
            }

            float4 frag(v2f input) : SV_Target {
                return LV_SampleShadowCubemapArray(0u, _TestDirection);
            }
            ENDCG
        }
    }
}
