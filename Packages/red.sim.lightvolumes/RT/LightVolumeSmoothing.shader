Shader "Hidden/LightVolumeSmoothing" {
    
    SubShader {
        
        Tags { "RenderType"="Opaque" }
        Cull Off ZWrite Off ZTest Always

        Pass {
            
            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #define LV_PIXELS_COUNT 32

            uniform float3 InvLocalEdgeSmooth[32];

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float2 uv : TEXCOORD0; float4 pos : SV_POSITION; };

            v2f vert(appdata v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            float4 frag(v2f i) : SV_Target {
                uint pixelIndex = (uint)(i.uv.x * LV_PIXELS_COUNT);
                return float4(InvLocalEdgeSmooth[pixelIndex], 1);
            }

            ENDHLSL

        }
    }
}