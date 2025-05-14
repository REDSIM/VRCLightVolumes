Shader "Hidden/LightVolumeData" {
    
    SubShader {
        
        Tags { "RenderType"="Opaque" }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Always

        Pass {
            
            HLSLPROGRAM

            #pragma target 5.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            #define LV_VOLUMES_COUNT 256
            #define LV_PIXELS_COUNT 2048
            #define LV_SEGMENT_COUNT 8

            uniform float3 UvwMin[768];
            uniform float3 UvwMax[768];
            uniform float3 Colors[256];
            uniform float  IsRotated[256];
            uniform float4 Rotation[256];

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

                uint volumeIndex = pixelIndex / LV_SEGMENT_COUNT;
                uint localIndex  = pixelIndex % LV_SEGMENT_COUNT;

                uint baseUVW = volumeIndex * 3;

                float4 values[8];
                values[0] = float4(UvwMin[baseUVW], 1.0);
                values[1] = float4(UvwMax[baseUVW], 1.0);
                values[2] = float4(UvwMin[baseUVW + 1], 1.0);
                values[3] = float4(UvwMax[baseUVW + 1], 1.0);
                values[4] = float4(UvwMin[baseUVW + 2], 1.0);
                values[5] = float4(UvwMax[baseUVW + 2], 1.0);
                values[6] = float4(Colors[volumeIndex], IsRotated[volumeIndex]);
                values[7] = Rotation[volumeIndex];

                return values[localIndex];

            }

            ENDHLSL

        }
    }
}