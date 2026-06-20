Shader "Hidden/VRCLV/PointLightShadowDepthEncode" {
    Properties {
        _ShadowDepthTex("Shadow Depth Texture", 2D) = "" {}
        _ShadowFarClip("Shadow Far Clip", Float) = 16
        _ShadowNearClip("Shadow Near Clip", Float) = 0.01
        _ShadowBakeBias("Shadow Bake Bias", Float) = 0
        _ShadowTanHalfFov("Shadow Tan Half FOV", Float) = 1
    }

    SubShader {
        Tags { "RenderType"="Opaque" }
        Cull Off ZWrite Off ZTest Always

        Pass {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            UNITY_DECLARE_DEPTH_TEXTURE(_ShadowDepthTex);

            float _ShadowFarClip;
            float _ShadowNearClip;
            float _ShadowBakeBias;
            float _ShadowTanHalfFov;

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

            // Encodes depth as VSM moments.
            float4 EncodeVSMDepth01(float depth) {
                depth = saturate(depth);
                float depth2 = depth * depth;
                return float4(depth, depth2, 0.0f, 0.0f);
            }

            float LinearShadowEyeDepth(float rawDepth) {
                float invNear = rcp(max(_ShadowNearClip, 0.0001f));
                float invFar = rcp(max(_ShadowFarClip, 0.0001f));
#if defined(UNITY_REVERSED_Z)
                return rcp(rawDepth * (invNear - invFar) + invFar);
#else
                return rcp(rawDepth * (invFar - invNear) + invNear);
#endif
            }

            float DynamicDepth01(float2 uv) {
                float rawDepth = SAMPLE_DEPTH_TEXTURE(_ShadowDepthTex, uv);
                float eyeDepth = LinearShadowEyeDepth(rawDepth);
                float2 ndc = (uv * 2.0f - 1.0f) * _ShadowTanHalfFov;
                float radialDepth = eyeDepth * sqrt(dot(ndc, ndc) + 1.0f);
                float nearClip = max(_ShadowNearClip, 0.0001f);
                float farClip = max(_ShadowFarClip, nearClip + 0.0001f);
                return saturate((radialDepth + max(_ShadowBakeBias, 0.0f) - nearClip) * rcp(farClip - nearClip));
            }

            float4 frag(v2f i) : SV_Target {
                return EncodeVSMDepth01(DynamicDepth01(i.uv));
            }
            ENDCG
        }
    }
}
