// Made with Amplify Shader Editor v1.9.9.9
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "Light Volume Samples/Light Volume PBR"
{
	Properties
	{
		_MainTex( "Albedo", 2D ) = "white" {}
		_Color( "Color", Color ) = ( 1, 1, 1, 1 )
		[NoScaleOffset] _MetallicGlossMap( "Metal AO Smoothness", 2D ) = "white" {}
		_Metallic( "Metallic", Range( 0, 1 ) ) = 0
		_Glossiness( "Smoothness", Range( 0, 1 ) ) = 1
		_OcclusionStrength( "AO", Range( 0, 1 ) ) = 1
		[NoScaleOffset] _BumpMap( "Normal", 2D ) = "bump" {}
		_BumpScale( "Normal Power", Float ) = 1
		[HDR][NoScaleOffset] _EmissionMap( "Emission Map", 2D ) = "white" {}
		[HDR] _EmissionColor( "Emission Color", Color ) = ( 0, 0, 0, 1 )
		_LightVolumesBias( "Light Volumes Bias", Float ) = 0
		_PointLightVolumesShading( "Point Light Volumes Shading", Float ) = 2
		[Toggle( _SPECULARS_ON )] _Speculars( "Speculars", Float ) = 1
		[Enum(UnityEngine.Rendering.CullMode)] _Culling( "Culling", Float ) = 2
		[HideInInspector] _texcoord( "", 2D ) = "white" {}
		[HideInInspector] __dirty( "", Int ) = 1
		[Header(Forward Rendering Options)]
		[ToggleOff] _SpecularHighlights("Specular Highlights", Float) = 1.0
		[ToggleOff] _GlossyReflections("Reflections", Float) = 1.0
	}

	SubShader
	{
		Tags{ "RenderType" = "Opaque"  "Queue" = "Geometry+0" "IsEmissive" = "true"  }
		Cull [_Culling]
		CGINCLUDE
		#include "UnityStandardUtils.cginc"
		#include "Packages/red.sim.lightvolumes/Shaders/LightVolumes.cginc"
		#include "UnityStandardBRDF.cginc"
		#include "UnityPBSLighting.cginc"
		#include "Lighting.cginc"
		#pragma target 3.5
		#pragma shader_feature _SPECULARHIGHLIGHTS_OFF
		#pragma shader_feature _GLOSSYREFLECTIONS_OFF
		#pragma shader_feature_local _SPECULARS_ON
		#define ASE_VERSION 19909
		#ifdef UNITY_PASS_SHADOWCASTER
			#undef INTERNAL_DATA
			#undef WorldReflectionVector
			#undef WorldNormalVector
			#define INTERNAL_DATA half3 internalSurfaceTtoW0; half3 internalSurfaceTtoW1; half3 internalSurfaceTtoW2;
			#define WorldReflectionVector(data,normal) reflect (data.worldRefl, half3(dot(data.internalSurfaceTtoW0,normal), dot(data.internalSurfaceTtoW1,normal), dot(data.internalSurfaceTtoW2,normal)))
			#define WorldNormalVector(data,normal) half3(dot(data.internalSurfaceTtoW0,normal), dot(data.internalSurfaceTtoW1,normal), dot(data.internalSurfaceTtoW2,normal))
		#endif
		struct Input
		{
			float2 uv_texcoord;
			float3 worldNormal;
			INTERNAL_DATA
			float3 worldPos;
		};

		uniform float _Culling;
		uniform sampler2D _BumpMap;
		uniform sampler2D _MainTex;
		uniform float4 _MainTex_ST;
		uniform float _BumpScale;
		uniform float4 _Color;
		uniform float4 _EmissionColor;
		uniform sampler2D _EmissionMap;
		uniform sampler2D _MetallicGlossMap;
		uniform float _Glossiness;
		uniform float _Metallic;
		uniform float _LightVolumesBias;
		uniform float _PointLightVolumesShading;
		uniform float _OcclusionStrength;

		void surf( Input i , inout SurfaceOutputStandard o )
		{
			float2 uv_MainTex = i.uv_texcoord * _MainTex_ST.xy + _MainTex_ST.zw;
			float3 tex2DNode5 = UnpackScaleNormal( tex2D( _BumpMap, uv_MainTex ), _BumpScale );
			float3 Normal437 = tex2DNode5;
			o.Normal = Normal437;
			float3 Albedo337 = ( _Color.rgb * tex2D( _MainTex, uv_MainTex ).rgb );
			o.Albedo = Albedo337;
			float3 normalizeResult349 = normalize( (WorldNormalVector( i , tex2DNode5 )) );
			float3 World_Normal112 = normalizeResult349;
			float3 worldNormal2_g222 = World_Normal112;
			float localLightVolumeSHSpecular1_g244 = ( 0.0 );
			float3 ase_positionWS = i.worldPos;
			float3 temp_output_6_0_g244 = ase_positionWS;
			float3 worldPos1_g244 = temp_output_6_0_g244;
			float3 L01_g244 = float3( 0,0,0 );
			float3 L1r1_g244 = float3( 0,0,0 );
			float3 L1g1_g244 = float3( 0,0,0 );
			float3 L1b1_g244 = float3( 0,0,0 );
			float3 Specular1_g244 = float3( 0,0,0 );
			float3 temp_output_21_0_g244 = Albedo337;
			float3 Albedo1_g244 = temp_output_21_0_g244;
			float4 tex2DNode50 = tex2D( _MetallicGlossMap, uv_MainTex );
			float Smoothness109 = ( tex2DNode50.a * _Glossiness );
			float temp_output_23_0_g244 = Smoothness109;
			float Smoothness1_g244 = temp_output_23_0_g244;
			float temp_output_54_0 = ( tex2DNode50.r * _Metallic );
			float Metallic334 = ( temp_output_54_0 * temp_output_54_0 );
			float temp_output_24_0_g244 = Metallic334;
			float Metallic1_g244 = temp_output_24_0_g244;
			float3 temp_output_18_0_g244 = World_Normal112;
			float3 WorldNormal1_g244 = temp_output_18_0_g244;
			float3 ase_viewVectorWS = ( ( unity_OrthoParams.w == 0 ) ? _WorldSpaceCameraPos - ase_positionWS : UNITY_MATRIX_V[ 2 ].xyz );
			float3 ase_viewDirSafeWS = Unity_SafeNormalize( ase_viewVectorWS );
			float3 normalizeResult26_g244 = normalize( ase_viewDirSafeWS );
			float3 temp_output_20_0_g244 = normalizeResult26_g244;
			float3 ViewDir1_g244 = temp_output_20_0_g244;
			float3 temp_output_475_0 = ( _LightVolumesBias * World_Normal112 );
			float3 temp_output_17_0_g244 = temp_output_475_0;
			float3 WorldPosOffset1_g244 = temp_output_17_0_g244;
			float temp_output_30_0_g244 = _PointLightVolumesShading;
			float PointLightShading1_g244 = temp_output_30_0_g244;
			LightVolumeSHSpecular( worldPos1_g244 , L01_g244 , L1r1_g244 , L1g1_g244 , L1b1_g244 , Specular1_g244 , Albedo1_g244 , Smoothness1_g244 , Metallic1_g244 , WorldNormal1_g244 , ViewDir1_g244 , WorldPosOffset1_g244 , PointLightShading1_g244 );
			float localLightVolumeAdditiveSHSpecular9_g245 = ( 0.0 );
			float3 temp_output_6_0_g245 = ase_positionWS;
			float3 worldPos9_g245 = temp_output_6_0_g245;
			float3 L09_g245 = float3( 0,0,0 );
			float3 L1r9_g245 = float3( 0,0,0 );
			float3 L1g9_g245 = float3( 0,0,0 );
			float3 L1b9_g245 = float3( 0,0,0 );
			float3 Specular9_g245 = float3( 0,0,0 );
			float3 temp_output_21_0_g245 = Albedo337;
			float3 Albedo9_g245 = temp_output_21_0_g245;
			float temp_output_23_0_g245 = Smoothness109;
			float Smoothness9_g245 = temp_output_23_0_g245;
			float temp_output_24_0_g245 = Metallic334;
			float Melallic9_g245 = temp_output_24_0_g245;
			float3 temp_output_18_0_g245 = World_Normal112;
			float3 WorldNormal9_g245 = temp_output_18_0_g245;
			float3 normalizeResult26_g245 = normalize( ase_viewDirSafeWS );
			float3 temp_output_20_0_g245 = normalizeResult26_g245;
			float3 ViewDir9_g245 = temp_output_20_0_g245;
			float3 temp_output_17_0_g245 = temp_output_475_0;
			float3 WorldPosOffset9_g245 = temp_output_17_0_g245;
			float temp_output_30_0_g245 = _PointLightVolumesShading;
			float PointLightShading9_g245 = temp_output_30_0_g245;
			LightVolumeAdditiveSHSpecular( worldPos9_g245 , L09_g245 , L1r9_g245 , L1g9_g245 , L1b9_g245 , Specular9_g245 , Albedo9_g245 , Smoothness9_g245 , Melallic9_g245 , WorldNormal9_g245 , ViewDir9_g245 , WorldPosOffset9_g245 , PointLightShading9_g245 );
			#ifdef LIGHTMAP_ON
				float3 staticSwitch472 = L09_g245;
			#else
				float3 staticSwitch472 = L01_g244;
			#endif
			float3 L02_g222 = staticSwitch472;
			#ifdef LIGHTMAP_ON
				float3 staticSwitch93 = L1r9_g245;
			#else
				float3 staticSwitch93 = L1r1_g244;
			#endif
			float3 L1r2_g222 = staticSwitch93;
			#ifdef LIGHTMAP_ON
				float3 staticSwitch94 = L1g9_g245;
			#else
				float3 staticSwitch94 = L1g1_g244;
			#endif
			float3 L1g2_g222 = staticSwitch94;
			#ifdef LIGHTMAP_ON
				float3 staticSwitch95 = L1b9_g245;
			#else
				float3 staticSwitch95 = L1b1_g244;
			#endif
			float3 L1b2_g222 = staticSwitch95;
			float3 localLightVolumeEvaluate2_g222 = LightVolumeEvaluate( worldNormal2_g222 , L02_g222 , L1r2_g222 , L1g2_g222 , L1b2_g222 );
			float3 temp_output_406_0 = ( localLightVolumeEvaluate2_g222 * Albedo337 * ( 1.0 - Metallic334 ) );
			#ifdef LIGHTMAP_ON
				float3 staticSwitch512 = Specular9_g245;
			#else
				float3 staticSwitch512 = Specular1_g244;
			#endif
			#ifdef _SPECULARS_ON
				float3 staticSwitch361 = ( temp_output_406_0 + staticSwitch512 );
			#else
				float3 staticSwitch361 = temp_output_406_0;
			#endif
			float lerpResult57 = lerp( 1.0 , tex2DNode50.g , _OcclusionStrength);
			float AO357 = lerpResult57;
			float3 IndirectAndSpeculars444 = ( staticSwitch361 * AO357 );
			float3 Emission452 = ( ( _EmissionColor.rgb * tex2D( _EmissionMap, uv_MainTex ).rgb ) + IndirectAndSpeculars444 );
			o.Emission = Emission452;
			o.Metallic = Metallic334;
			o.Smoothness = Smoothness109;
			o.Occlusion = AO357;
			o.Alpha = 1;
		}

		ENDCG
		CGPROGRAM
		#pragma surface surf Standard keepalpha fullforwardshadows exclude_path:deferred noambient 

		ENDCG
		Pass
		{
			Name "ShadowCaster"
			Tags{ "LightMode" = "ShadowCaster" }
			ZWrite On
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.5
			#pragma multi_compile_shadowcaster
			#pragma multi_compile UNITY_PASS_SHADOWCASTER
			#pragma skip_variants FOG_LINEAR FOG_EXP FOG_EXP2
			#include "HLSLSupport.cginc"
			#if ( SHADER_API_D3D11 || SHADER_API_GLCORE || SHADER_API_GLES || SHADER_API_GLES3 || SHADER_API_METAL || SHADER_API_VULKAN )
				#define CAN_SKIP_VPOS
			#endif
			#include "UnityCG.cginc"
			#include "Lighting.cginc"
			#include "UnityPBSLighting.cginc"
			struct v2f
			{
				V2F_SHADOW_CASTER;
				float2 customPack1 : TEXCOORD1;
				float4 tSpace0 : TEXCOORD2;
				float4 tSpace1 : TEXCOORD3;
				float4 tSpace2 : TEXCOORD4;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};
			v2f vert( appdata_full v )
			{
				v2f o;
				UNITY_SETUP_INSTANCE_ID( v );
				UNITY_INITIALIZE_OUTPUT( v2f, o );
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO( o );
				UNITY_TRANSFER_INSTANCE_ID( v, o );
				Input customInputData;
				float3 worldPos = mul( unity_ObjectToWorld, v.vertex ).xyz;
				half3 worldNormal = UnityObjectToWorldNormal( v.normal );
				half3 worldTangent = UnityObjectToWorldDir( v.tangent.xyz );
				half tangentSign = v.tangent.w * unity_WorldTransformParams.w;
				half3 worldBinormal = cross( worldNormal, worldTangent ) * tangentSign;
				o.tSpace0 = float4( worldTangent.x, worldBinormal.x, worldNormal.x, worldPos.x );
				o.tSpace1 = float4( worldTangent.y, worldBinormal.y, worldNormal.y, worldPos.y );
				o.tSpace2 = float4( worldTangent.z, worldBinormal.z, worldNormal.z, worldPos.z );
				o.customPack1.xy = customInputData.uv_texcoord;
				o.customPack1.xy = v.texcoord;
				TRANSFER_SHADOW_CASTER_NORMALOFFSET( o )
				return o;
			}
			half4 frag( v2f IN
			#if !defined( CAN_SKIP_VPOS )
			, UNITY_VPOS_TYPE vpos : VPOS
			#endif
			) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID( IN );
				Input surfIN;
				UNITY_INITIALIZE_OUTPUT( Input, surfIN );
				surfIN.uv_texcoord = IN.customPack1.xy;
				float3 worldPos = float3( IN.tSpace0.w, IN.tSpace1.w, IN.tSpace2.w );
				half3 worldViewDir = normalize( UnityWorldSpaceViewDir( worldPos ) );
				surfIN.worldPos = worldPos;
				surfIN.worldNormal = float3( IN.tSpace0.z, IN.tSpace1.z, IN.tSpace2.z );
				surfIN.internalSurfaceTtoW0 = IN.tSpace0.xyz;
				surfIN.internalSurfaceTtoW1 = IN.tSpace1.xyz;
				surfIN.internalSurfaceTtoW2 = IN.tSpace2.xyz;
				SurfaceOutputStandard o;
				UNITY_INITIALIZE_OUTPUT( SurfaceOutputStandard, o )
				surf( surfIN, o );
				#if defined( CAN_SKIP_VPOS )
				float2 vpos = IN.pos;
				#endif
				SHADOW_CASTER_FRAGMENT( IN )
			}
			ENDCG
		}
	}
	Fallback Off
	CustomEditor "AmplifyShaderEditor.MaterialInspector"
}
/*ASEBEGIN
Version=19909
Node;AmplifyShaderEditor.CommentaryNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;443;-864,-144;Inherit;False;1194.267;509.5228;Metallic Smoothness AO;12;357;57;334;56;109;470;51;54;52;53;50;477;;1,0.8500044,0.4735848,1;0;0
Node;AmplifyShaderEditor.TextureCoordinatesNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;477;-800,-80;Inherit;False;0;3;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;53;-544,176;Inherit;False;Property;_Metallic;Metallic;3;0;Create;False;0;0;0;False;0;False;0;0;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;50;-544,-96;Inherit;True;Property;_MetallicGlossMap;Metal AO Smoothness;2;1;[NoScaleOffset];Create;False;0;0;0;False;0;False;3;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;False;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;52;-544,96;Inherit;False;Property;_Glossiness;Smoothness;4;0;Create;False;0;0;0;False;0;False;1;0;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;54;-224,-48;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.CommentaryNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;439;-928,-544;Inherit;False;1252.668;359.5993;Normal;7;437;112;349;14;5;6;478;;0.5792453,0.6214049,1,1;0;0
Node;AmplifyShaderEditor.CommentaryNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;436;-3520,-256;Inherit;False;2482.793;662.1536;Light Volumes;27;124;532;464;444;469;361;406;338;80;336;350;93;113;473;524;474;489;475;508;530;531;512;509;507;95;472;94;VRC Light Volumes;0.9834821,1,0.7150943,1;0;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;51;-224,48;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;470;-64,-64;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;524;-3504,144;Inherit;False;112;World Normal;1;0;OBJECT;;False;1;FLOAT3;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;473;-3504,64;Inherit;False;Property;_LightVolumesBias;Light Volumes Bias;10;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;109;-64,48;Inherit;False;Smoothness;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.WorldNormalVector, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;14;-240,-384;Inherit;False;False;1;0;FLOAT3;0,0,1;False;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;334;80,-64;Inherit;False;Metallic;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;507;-3280,-128;Inherit;False;337;Albedo;1;0;OBJECT;;False;1;FLOAT3;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;509;-3280,32;Inherit;False;334;Metallic;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;508;-3312,-48;Inherit;False;109;Smoothness;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;475;-3248,112;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;489;-3376,224;Inherit;False;Property;_PointLightVolumesShading;Point Light Volumes Shading;11;0;Create;True;0;0;0;False;0;False;2;2;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;474;-3312,304;Inherit;False;112;World Normal;1;0;OBJECT;;False;1;FLOAT3;0
Node;AmplifyShaderEditor.NormalizeNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;349;-64,-384;Inherit;False;False;1;0;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.FunctionNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;531;-3056,-128;Inherit;False;LightVolumeSHSpecular;-1;;244;b3cce7da6bb78ce468b417bb9849dc30;5,10,0,11,0,12,0,8,0,27,0;8;6;FLOAT3;0,0,0;False;17;FLOAT3;0,0,0;False;18;FLOAT3;0,0,0;False;20;FLOAT3;0,0,0;False;21;FLOAT3;1,1,1;False;23;FLOAT;0;False;24;FLOAT;0;False;30;FLOAT;1;False;5;FLOAT3;13;FLOAT3;14;FLOAT3;15;FLOAT3;16;FLOAT3;28
Node;AmplifyShaderEditor.FunctionNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;530;-3056,112;Inherit;False;LightVolumeSHSpecular;-1;;245;b3cce7da6bb78ce468b417bb9849dc30;5,10,1,11,1,12,1,8,1,27,1;8;6;FLOAT3;0,0,0;False;17;FLOAT3;0,0,0;False;18;FLOAT3;0,0,0;False;20;FLOAT3;0,0,0;False;21;FLOAT3;1,1,1;False;23;FLOAT;0;False;24;FLOAT;0;False;30;FLOAT;1;False;5;FLOAT3;13;FLOAT3;14;FLOAT3;15;FLOAT3;16;FLOAT3;28
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;112;80,-384;Inherit;False;World Normal;-1;True;1;0;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.StaticSwitch, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;94;-2704,64;Inherit;False;Property;_AdditiveOnly;Additive Only;15;0;Create;True;0;0;0;False;0;False;0;0;0;True;;Toggle;2;Key0;Key1;Reference;472;True;True;All;9;1;FLOAT3;0,0,0;False;0;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT3;0,0,0;False;4;FLOAT3;0,0,0;False;5;FLOAT3;0,0,0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.StaticSwitch, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;472;-2704,-128;Inherit;False;Property;LIGHTMAP_ON;LIGHTMAP_ON;15;0;Create;False;0;0;0;False;0;False;0;0;0;False;LIGHTMAP_ON;Toggle;2;Key0;Key1;Fetch;True;True;All;9;1;FLOAT3;0,0,0;False;0;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT3;0,0,0;False;4;FLOAT3;0,0,0;False;5;FLOAT3;0,0,0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.StaticSwitch, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;95;-2704,160;Inherit;False;Property;_AdditiveOnly;Additive Only;15;0;Create;True;0;0;0;False;0;False;0;0;0;True;;Toggle;2;Key0;Key1;Reference;472;True;True;All;9;1;FLOAT3;0,0,0;False;0;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT3;0,0,0;False;4;FLOAT3;0,0,0;False;5;FLOAT3;0,0,0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.StaticSwitch, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;512;-2704,256;Inherit;False;Property;_AdditiveOnly;Additive Only;15;0;Create;True;0;0;0;False;0;False;0;0;0;True;;Toggle;2;Key0;Key1;Reference;472;True;True;All;9;1;FLOAT3;0,0,0;False;0;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT3;0,0,0;False;4;FLOAT3;0,0,0;False;5;FLOAT3;0,0,0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;113;-2704,-208;Inherit;False;112;World Normal;1;0;OBJECT;;False;1;FLOAT3;0
Node;AmplifyShaderEditor.StaticSwitch, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;93;-2704,-32;Inherit;False;Property;_AdditiveOnly;Additive Only;15;0;Create;True;0;0;0;False;0;False;0;0;0;True;;Toggle;2;Key0;Key1;Reference;472;True;True;All;9;1;FLOAT3;0,0,0;False;0;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT3;0,0,0;False;4;FLOAT3;0,0,0;False;5;FLOAT3;0,0,0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;336;-2448,160;Inherit;False;334;Metallic;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;56;-544,256;Inherit;False;Property;_OcclusionStrength;AO;5;0;Create;False;0;0;0;False;0;False;1;0;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.FunctionNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;350;-2432,-112;Inherit;False;LightVolumeEvaluate;-1;;222;4919cc1d83093f24f802ce655e9f3303;0;5;5;FLOAT3;0,0,0;False;13;FLOAT3;1,1,1;False;14;FLOAT3;0,0,0;False;15;FLOAT3;0,0,0;False;16;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.OneMinusNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;80;-2272,160;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;338;-2368,64;Inherit;False;337;Albedo;1;0;OBJECT;;False;1;FLOAT3;0
Node;AmplifyShaderEditor.WireNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;532;-2144,256;Inherit;False;1;0;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.LerpOp, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;57;-224,144;Inherit;False;3;0;FLOAT;1;False;1;FLOAT;0;False;2;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;406;-2096,-112;Inherit;False;3;3;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;2;FLOAT;0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.SimpleAddOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;124;-1904,-32;Inherit;False;2;2;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;357;-64,144;Inherit;False;AO;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.CommentaryNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;460;-896,384;Inherit;False;1224.969;486.9523;Emission Plus Indirect and Speculars;7;452;447;417;416;415;414;476;;1,1,1,1;0;0
Node;AmplifyShaderEditor.StaticSwitch, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;361;-1760,-112;Inherit;False;Property;_Speculars;Speculars;12;0;Create;True;0;0;0;False;0;False;0;1;1;True;;Toggle;2;Key0;Key1;Create;True;True;All;9;1;FLOAT3;0,0,0;False;0;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT3;0,0,0;False;4;FLOAT3;0,0,0;False;5;FLOAT3;0,0,0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;464;-1696,-16;Inherit;False;357;AO;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;469;-1488,-48;Inherit;False;2;2;0;FLOAT3;0,0,0;False;1;FLOAT;0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.TextureCoordinatesNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;476;-848,656;Inherit;False;0;3;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.CommentaryNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;457;-432,-1056;Inherit;False;756;475;Albedo;4;3;87;88;337;;1,1,1,1;0;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;444;-1328,-48;Inherit;False;IndirectAndSpeculars;-1;True;1;0;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.SamplerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;414;-608,640;Inherit;True;Property;_EmissionMap;Emission Map;8;2;[HDR];[NoScaleOffset];Create;False;0;0;0;False;0;False;3;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;False;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.ColorNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;415;-544,432;Inherit;False;Property;_EmissionColor;Emission Color;9;1;[HDR];Create;False;0;0;0;False;0;False;0,0,0,1;0,0,0,1;True;True;0;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.TextureCoordinatesNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;478;-864,-480;Inherit;False;0;3;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;6;-832,-336;Inherit;False;Property;_BumpScale;Normal Power;7;0;Create;False;0;0;0;False;0;False;1;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;3;-384,-816;Inherit;True;Property;_MainTex;Albedo;0;0;Create;False;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;False;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.ColorNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;87;-320,-1008;Inherit;False;Property;_Color;Color;1;0;Create;True;0;0;0;False;0;False;1,1,1,1;1,1,1,1;True;True;0;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;416;-288,560;Inherit;False;2;2;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;447;-320,720;Inherit;False;444;IndirectAndSpeculars;1;0;OBJECT;;False;1;FLOAT3;0
Node;AmplifyShaderEditor.SamplerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;5;-560,-384;Inherit;True;Property;_BumpMap;Normal;6;1;[NoScaleOffset];Create;False;0;0;0;False;0;False;3;None;None;True;0;True;bump;Auto;True;Object;-1;Auto;Texture2D;False;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;88;-64,-816;Inherit;False;2;2;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.SimpleAddOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;417;-32,560;Inherit;False;2;2;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;337;80,-816;Inherit;False;Albedo;-1;True;1;0;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;437;-240,-464;Inherit;False;Normal;-1;True;1;0;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;452;96,560;Inherit;False;Emission;-1;True;1;0;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;438;512,-256;Inherit;False;437;Normal;1;0;OBJECT;;False;1;FLOAT3;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;454;512,-336;Inherit;False;337;Albedo;1;0;OBJECT;;False;1;FLOAT3;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;453;512,-176;Inherit;False;452;Emission;1;0;OBJECT;;False;1;FLOAT3;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;442;512,64;Inherit;False;357;AO;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;440;512,-96;Inherit;False;334;Metallic;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;418;544,144;Inherit;False;Property;_Culling;Culling;13;1;[Enum];Create;False;0;0;1;UnityEngine.Rendering.CullMode;True;0;False;2;2;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;441;480,-16;Inherit;False;109;Smoothness;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.StandardSurfaceOutputNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;533;816,-224;Float;False;True;-1;3;AmplifyShaderEditor.MaterialInspector;0;0;Standard;Light Volume Samples/Light Volume PBR;False;False;False;False;True;False;False;False;False;False;False;False;False;False;False;False;False;False;True;True;False;Back;0;False;;0;False;;False;0;False;;0;False;;False;0;0;False;;0;Opaque;0.5;True;True;0;False;Opaque;;Geometry;ForwardOnly;12;all;True;True;True;True;0;False;;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;2;15;10;25;False;0.5;True;0;0;False;;0;False;;0;0;False;;0;False;;0;False;;0;False;;0;False;0;0,0,0,0;VertexOffset;True;False;Cylindrical;False;True;Relative;0;;-1;-1;-1;-1;0;False;0;0;True;_Culling;-1;0;False;;0;0;0;False;0.1;False;;0;False;;False;17;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT;0;False;4;FLOAT;0;False;5;FLOAT;0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT;0;False;9;FLOAT;0;False;10;FLOAT;0;False;13;FLOAT3;0,0,0;False;11;FLOAT3;0,0,0;False;12;FLOAT3;0,0,0;False;16;FLOAT4;0,0,0,0;False;14;FLOAT4;0,0,0,0;False;15;FLOAT3;0,0,0;False;0
WireConnection;50;1;477;0
WireConnection;54;0;50;1
WireConnection;54;1;53;0
WireConnection;51;0;50;4
WireConnection;51;1;52;0
WireConnection;470;0;54;0
WireConnection;470;1;54;0
WireConnection;109;0;51;0
WireConnection;14;0;5;0
WireConnection;334;0;470;0
WireConnection;475;0;473;0
WireConnection;475;1;524;0
WireConnection;349;0;14;0
WireConnection;531;17;475;0
WireConnection;531;18;474;0
WireConnection;531;21;507;0
WireConnection;531;23;508;0
WireConnection;531;24;509;0
WireConnection;531;30;489;0
WireConnection;530;17;475;0
WireConnection;530;18;474;0
WireConnection;530;21;507;0
WireConnection;530;23;508;0
WireConnection;530;24;509;0
WireConnection;530;30;489;0
WireConnection;112;0;349;0
WireConnection;94;1;531;15
WireConnection;94;0;530;15
WireConnection;472;1;531;13
WireConnection;472;0;530;13
WireConnection;95;1;531;16
WireConnection;95;0;530;16
WireConnection;512;1;531;28
WireConnection;512;0;530;28
WireConnection;93;1;531;14
WireConnection;93;0;530;14
WireConnection;350;5;113;0
WireConnection;350;13;472;0
WireConnection;350;14;93;0
WireConnection;350;15;94;0
WireConnection;350;16;95;0
WireConnection;80;0;336;0
WireConnection;532;0;512;0
WireConnection;57;1;50;2
WireConnection;57;2;56;0
WireConnection;406;0;350;0
WireConnection;406;1;338;0
WireConnection;406;2;80;0
WireConnection;124;0;406;0
WireConnection;124;1;532;0
WireConnection;357;0;57;0
WireConnection;361;1;406;0
WireConnection;361;0;124;0
WireConnection;469;0;361;0
WireConnection;469;1;464;0
WireConnection;444;0;469;0
WireConnection;414;1;476;0
WireConnection;416;0;415;5
WireConnection;416;1;414;5
WireConnection;5;1;478;0
WireConnection;5;5;6;0
WireConnection;88;0;87;5
WireConnection;88;1;3;5
WireConnection;417;0;416;0
WireConnection;417;1;447;0
WireConnection;337;0;88;0
WireConnection;437;0;5;0
WireConnection;452;0;417;0
WireConnection;533;0;454;0
WireConnection;533;1;438;0
WireConnection;533;2;453;0
WireConnection;533;3;440;0
WireConnection;533;4;441;0
WireConnection;533;5;442;0
ASEEND*/
//CHKSM=A1322B0E461665A120DF470723D9B85FBD109513