Shader "Hidden/LightVolumeMatrix" {
    
    SubShader {
        
        Tags { "RenderType"="Opaque" }
        Cull Off ZWrite Off ZTest Always

        Pass {
            
            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #define LV_PIXELS_COUNT 768

            uniform float4x4 InvWorldMatrix[256];

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
                uint matrixIndex = pixelIndex / 3;
                uint columnIndex = pixelIndex % 3;
                return InvWorldMatrix[matrixIndex][columnIndex];
            }

            ENDHLSL

        }
    }
}