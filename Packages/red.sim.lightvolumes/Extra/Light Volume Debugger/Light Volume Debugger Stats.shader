Shader "Light Volume Samples/Light Volume Debugger Stats" {
    Properties {
        [NoScaleOffset] _MainTex("Panel Atlas", 2D) = "white" {}
        _PanelDistance("Distance (m)", Range(0.4, 2)) = 1
        _PanelWidth("Width (m)", Range(0.2, 1.2)) = 0.6
        _PanelYOffset("Vertical Offset (m)", Range(-0.5, 0.5)) = 0
    }
    SubShader {
        Tags { "Queue" = "Overlay" "RenderType" = "Opaque" "IgnoreProjector" = "True" "DisableBatching" = "True" "VRCFallback" = "Hidden" }
        Pass {
            Cull Off
            ZWrite On
            ZTest LEqual
            Blend Off

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _PanelDistance;
            float _PanelWidth;
            float _PanelYOffset;
            // Keep these global. Material properties would hide the world's values.
            float _UdonLightVolumeEnabled;
            float _UdonLightVolumeVersion;
            float _UdonLightVolumeCount;
            float _UdonLightVolumeAdditiveCount;
            float _UdonPointLightVolumeCount;
            float _UdonClusteringEnabled;

            struct appdata {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                nointerpolation float4 counts : TEXCOORD1;
                nointerpolation float clustering : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v) {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.uv = v.uv;

                float2 panel = (v.uv - 0.5) * float2(_PanelWidth, _PanelWidth * 0.7);
                float3 localPosition = float3(panel.x, panel.y + _PanelYOffset, _PanelDistance);
                // MA and VRCFury attach this renderer to the wearer's Head bone.
                o.position = UnityObjectToClipPos(localPosition);

                // v1 and early v2 store the version in the enable flag.
                float version = _UdonLightVolumeEnabled > 0.0
                    ? (_UdonLightVolumeVersion > 0.0 ? _UdonLightVolumeVersion : _UdonLightVolumeEnabled)
                    : 0.0;
                float total = version > 0.0 ? max(_UdonLightVolumeCount, 0.0) : 0.0;
                float additive = version > 0.0 ? clamp(_UdonLightVolumeAdditiveCount, 0.0, total) : 0.0;
                float lights = version >= 2.0 ? max(_UdonPointLightVolumeCount, 0.0) : 0.0;
                o.counts = floor(clamp(float4(version, total - additive, additive, lights), 0.0, 999.0));
                o.clustering = version >= 3.0 && _UdonClusteringEnabled > 0.5 ? 1.0 : 0.0;
                return o;
            }

            half4 frag(v2f i, float facing : VFACE) : SV_Target {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 uv = i.uv;
                // Keep text readable from either side of the panel.
                if (facing < 0.0) uv.x = 1.0 - uv.x;
                float2 pixel = float2(uv.x * 640.0, (1.0 - uv.y) * 448.0);
                float2 atlasUV = float2(pixel.x, 512.0 - pixel.y) / float2(1024.0, 512.0);
                float2 dx = ddx(atlasUV);
                float2 dy = ddy(atlasUV);

                // Status tiles have 32-pixel gutters to isolate filtered neighboring words.
                // Five 64-pixel rows share a strip of digits and status words.
                float row = floor((pixel.y - 100.0) / 64.0);
                if (pixel.y >= 100.0 && pixel.y < 420.0) {
                    float tileX = -1.0;
                    if (row == 0.0 && i.counts.x == 0.0) {
                        if (pixel.x >= 344.0 && pixel.x < 600.0)
                            tileX = 704.0 + pixel.x - 344.0;
                    } else if (row == 4.0) {
                        if (pixel.x >= 472.0 && pixel.x < 600.0)
                            tileX = (i.clustering > 0.5 ? 384.0 : 544.0) + pixel.x - 472.0;
                    } else if (pixel.x >= 534.0 && pixel.x < 600.0) {
                        float value = row == 0.0 ? i.counts.x
                            : row == 1.0 ? i.counts.y
                            : row == 2.0 ? i.counts.z : i.counts.w;
                        float column = floor((pixel.x - 534.0) / 22.0);
                        float divisor = column == 0.0 ? 100.0 : column == 1.0 ? 10.0 : 1.0;
                        float digit = floor(value / divisor);
                        // Tile 10 is blank. Keep a single zero for empty counts.
                        float tile = column < 2.0 && digit == 0.0 ? 10.0 : fmod(digit, 10.0);
                        tileX = tile * 32.0 + 5.0 + fmod(pixel.x - 534.0, 22.0);
                    }
                    if (tileX >= 0.0)
                        atlasUV = float2(tileX / 1024.0, (64.0 - (pixel.y - 100.0 - row * 64.0)) / 512.0);
                }
                // Explicit gradients avoid mip seams at digit and word boundaries.
                return half4(tex2Dgrad(_MainTex, atlasUV, dx, dy).rgb, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
