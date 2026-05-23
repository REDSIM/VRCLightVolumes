// Replacement shader that renders world-space radial distance from a point light into a cubemap face.
Shader "Hidden/VRCLV/PointLightShadow" {
    Properties {
        _MainTex("Albedo", 2D) = "white" {}
        _Color("Color", Color) = (1,1,1,1)
        _Cutoff("Alpha Cutoff", Range(0,1)) = 0.5
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MainTex;
    float4 _MainTex_ST;
    half4 _Color;
    half _Cutoff;

    struct appdata {
        float4 vertex : POSITION;
        float2 uv : TEXCOORD0;
    };

    struct v2f {
        float4 vertex : SV_POSITION;
        float3 viewPos : TEXCOORD0;
        float2 uv : TEXCOORD1;
    };

    v2f vert(appdata v) {
        v2f o;
        o.vertex = UnityObjectToClipPos(v.vertex);
        o.viewPos = UnityObjectToViewPos(v.vertex);
        o.uv = TRANSFORM_TEX(v.uv, _MainTex);
        return o;
    }

    float4 EncodeDepth(v2f i) {
        float depth = length(i.viewPos);
        return depth.xxxx;
    }

    float4 FragmentOpaque(v2f i) : SV_Target {
        return EncodeDepth(i);
    }

    float4 EncodeCutoutDepth(v2f i) {
        half alpha = tex2D(_MainTex, i.uv).a * _Color.a;
        clip(alpha - _Cutoff);
        return EncodeDepth(i);
    }

    float4 FragmentCutout(v2f i) : SV_Target {
        return EncodeCutoutDepth(i);
    }
    ENDCG

    Category {
        Cull Off
        ZWrite On
        ZTest LEqual

        SubShader {
            Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" }

            Pass {
                CGPROGRAM
                #pragma vertex vert
                #pragma fragment FragmentCutout
                ENDCG
            }
        }

        SubShader {
            Tags { "RenderType"="TreeTransparentCutout" "Queue"="AlphaTest" }

            Pass {
                CGPROGRAM
                #pragma vertex vert
                #pragma fragment FragmentCutout
                ENDCG
            }
        }

        SubShader {
            Tags { "RenderType"="Opaque" "Queue"="Geometry" }

            Pass {
                CGPROGRAM
                #pragma vertex vert
                #pragma fragment FragmentOpaque
                ENDCG
            }
        }
    }
}
