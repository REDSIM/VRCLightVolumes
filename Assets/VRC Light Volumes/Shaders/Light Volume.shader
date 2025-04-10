Shader "Light Volume" {
	
	Properties {
		_MainTex("Albedo", 2D) = "white" {}
		_BumpMap("Normal", 2D) = "bump" {}
		_NormalPower("Normal Power", Float) = 1
		[Toggle(BICUBIC_LIGHT_VOLUME_SAMPLING_ENABLED)] _BicubicLightVolumeSampling("Bicubic Sampling", Float) = 0
		[HideInInspector] _texcoord( "", 2D ) = "white" {}
	}

	SubShader {

		Tags { "RenderType"="Opaque" }
		//LOD 100

		CGINCLUDE
		#include "LightVolumes.cginc"
		#include "UnityPBSLighting.cginc"
		#include "Lighting.cginc"

		#pragma target 3.5

		ENDCG
		Blend Off
		AlphaToMask Off
		Cull Back
		ColorMask RGBA
		ZWrite On
		ZTest LEqual
		Offset 0, 0
		
		Pass {

			Name "Unlit"

			CGPROGRAM

			#ifndef UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX
			#define UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input)
			#endif
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing

			struct appdata {
				float4 vertex : POSITION;
				float2 texcoord : TEXCOORD0;
				float4 tangent : TANGENT;
				float3 normal : NORMAL;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct v2f {
				float4 vertex : SV_POSITION;
				float3 worldPos : TEXCOORD0;
				float2 texcoord : TEXCOORD1;
				float3 tangent : TEXCOORD2;
				float3 normal : TEXCOORD3;
				float3 bitangent : TEXCOORD4;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			uniform sampler2D _MainTex;
			uniform float4 _MainTex_ST;
			uniform sampler2D _BumpMap;
			uniform float4 _BumpMap_ST;
			uniform float _NormalPower;

			// VERTEX
			v2f vert ( appdata v ) {

				v2f o;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
				UNITY_TRANSFER_INSTANCE_ID(v, o);

				float3 tangentWS = UnityObjectToWorldDir(v.tangent);
				o.tangent = tangentWS;

				float3 normalWS = UnityObjectToWorldNormal(v.normal);
				o.normal = normalWS;

				float tangentSign = v.tangent.w * (unity_WorldTransformParams.w >= 0.0 ? 1.0 : -1.0);
				o.bitangent = cross(normalWS, tangentWS) * tangentSign;
				
				o.texcoord = v.texcoord;
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;

				return o;

			}

			// FRAGMENT
			fixed4 frag (v2f i ) : SV_Target {

				UNITY_SETUP_INSTANCE_ID(i);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

				float2 mainTexUV = i.texcoord * _MainTex_ST.xy + _MainTex_ST.zw;
				float2 bumpMapUV = i.texcoord * _BumpMap_ST.xy + _BumpMap_ST.zw;

				float3 tanNormal = UnpackScaleNormal(tex2D(_BumpMap, bumpMapUV), _NormalPower);
				float3 worldNormal = CalculateWorldNormal(i.normal, i.tangent, i.bitangent, tanNormal);

				float4 shColor = float4(LightVolume(worldNormal, i.worldPos), 1);
				
				float4 color = tex2D(_MainTex, mainTexUV);

				return shColor * color;

			}

			ENDCG

		}
	}

	Fallback Off
}