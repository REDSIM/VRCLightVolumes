// Made with Amplify Shader Editor v1.9.8.1
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "Light Volume Particle Lit"
{
	Properties
	{
		_MainTex("Main Texture", 2D) = "white" {}
		[HDR]_Color("Tint", Color) = (1,1,1,1)
		[NoScaleOffset]_BumpMap("Normal", 2D) = "bump" {}
		_BumpScale("Normal Power", Float) = 1
		[Toggle(_FLIPBOOKBLENDING_ON)] _FlipbookBlending("Flipbook Blending", Float) = 0
		[Toggle(_LIGHTVOLUMES_ON)] _LightVolumes("Enable Light Volumes", Float) = 1
		[Toggle(_ADDITIVEONLY_ON)] _AdditiveOnly("Additive Only", Float) = 0
		[Toggle(_ENABLESOFTPARTICLE_ON)] _EnableSoftparticle("Enable Softparticle", Float) = 0
		_SoftparticleDistance("Softparticle Distance", Range( 0 , 2)) = 0
		[Enum(UnityEngine.Rendering.CullMode)]_Culling("Culling", Float) = 2
		[HideInInspector] _texcoord( "", 2D ) = "white" {}
		[HideInInspector] _texcoord2( "", 2D ) = "white" {}
		[HideInInspector] __dirty( "", Int ) = 1
	}

	SubShader
	{
		Tags{ "RenderType" = "Transparent"  "Queue" = "Transparent+0" "IgnoreProjector" = "True" }
		Cull [_Culling]
		CGINCLUDE
		#include "../LightVolumes.cginc"
		#include "UnityStandardUtils.cginc"
		#include "UnityCG.cginc"
		#include "UnityPBSLighting.cginc"
		#include "Lighting.cginc"
		#pragma target 3.5
		#pragma shader_feature_local _FLIPBOOKBLENDING_ON
		#pragma shader_feature_local _LIGHTVOLUMES_ON
		#pragma shader_feature_local _ADDITIVEONLY_ON
		#pragma shader_feature_local _ENABLESOFTPARTICLE_ON
		#define ASE_VERSION 19801
		#ifdef UNITY_PASS_SHADOWCASTER
			#undef INTERNAL_DATA
			#undef WorldReflectionVector
			#undef WorldNormalVector
			#define INTERNAL_DATA half3 internalSurfaceTtoW0; half3 internalSurfaceTtoW1; half3 internalSurfaceTtoW2;
			#define WorldReflectionVector(data,normal) reflect (data.worldRefl, half3(dot(data.internalSurfaceTtoW0,normal), dot(data.internalSurfaceTtoW1,normal), dot(data.internalSurfaceTtoW2,normal)))
			#define WorldNormalVector(data,normal) half3(dot(data.internalSurfaceTtoW0,normal), dot(data.internalSurfaceTtoW1,normal), dot(data.internalSurfaceTtoW2,normal))
		#endif
		#undef TRANSFORM_TEX
		#define TRANSFORM_TEX(tex,name) float4(tex.xy * name##_ST.xy + name##_ST.zw, tex.z, tex.w)
		struct Input
		{
			float4 uv_texcoord;
			float2 uv2_texcoord2;
			float3 worldNormal;
			INTERNAL_DATA
			float3 worldPos;
			float4 vertexColor : COLOR;
			float4 screenPosition577;
		};

		uniform float _Culling;
		uniform sampler2D _MainTex;
		uniform sampler2D _BumpMap;
		uniform float _BumpScale;
		uniform float4 _Color;
		UNITY_DECLARE_DEPTH_TEXTURE( _CameraDepthTexture );
		uniform float4 _CameraDepthTexture_TexelSize;
		uniform float _SoftparticleDistance;

		void vertexDataFunc( inout appdata_full v, out Input o )
		{
			UNITY_INITIALIZE_OUTPUT( Input, o );
			float3 ase_positionOS = v.vertex.xyz;
			float3 vertexPos577 = ase_positionOS;
			float4 ase_positionSS577 = ComputeScreenPos( UnityObjectToClipPos( vertexPos577 ) );
			o.screenPosition577 = ase_positionSS577;
		}

		void surf( Input i , inout SurfaceOutputStandard o )
		{
			o.Normal = float3(0,0,1);
			float2 appendResult511 = (float2(i.uv_texcoord.x , i.uv_texcoord.y));
			float4 tex2DNode471 = tex2D( _MainTex, appendResult511 );
			float2 appendResult507 = (float2(i.uv_texcoord.z , i.uv_texcoord.w));
			float4 lerpResult508 = lerp( tex2DNode471 , tex2D( _MainTex, appendResult507 ) , i.uv2_texcoord2.x);
			#ifdef _FLIPBOOKBLENDING_ON
				float4 staticSwitch510 = lerpResult508;
			#else
				float4 staticSwitch510 = tex2DNode471;
			#endif
			float4 Albedo529 = staticSwitch510;
			float4 temp_output_2_0_g229 = Albedo529;
			float3 tex2DNode500 = UnpackScaleNormal( tex2D( _BumpMap, i.uv_texcoord.xy ), _BumpScale );
			float3 normalizeResult502 = normalize( (WorldNormalVector( i , tex2DNode500 )) );
			float3 World_Normal503 = normalizeResult502;
			float3 worldNormal2_g224 = World_Normal503;
			float3 appendResult427 = (float3(unity_SHAr.w , unity_SHAg.w , unity_SHAb.w));
			float localLightVolumeSH1_g3 = ( 0.0 );
			float3 ase_positionWS = i.worldPos;
			float3 temp_output_6_0_g3 = ase_positionWS;
			float3 worldPos1_g3 = temp_output_6_0_g3;
			float3 L01_g3 = float3( 0,0,0 );
			float3 L1r1_g3 = float3( 0,0,0 );
			float3 L1g1_g3 = float3( 0,0,0 );
			float3 L1b1_g3 = float3( 0,0,0 );
			LightVolumeSH( worldPos1_g3 , L01_g3 , L1r1_g3 , L1g1_g3 , L1b1_g3 );
			float localLightVolumeAdditiveSH9_g4 = ( 0.0 );
			float3 temp_output_6_0_g4 = ase_positionWS;
			float3 worldPos9_g4 = temp_output_6_0_g4;
			float3 L09_g4 = float3( 0,0,0 );
			float3 L1r9_g4 = float3( 0,0,0 );
			float3 L1g9_g4 = float3( 0,0,0 );
			float3 L1b9_g4 = float3( 0,0,0 );
			LightVolumeAdditiveSH( worldPos9_g4 , L09_g4 , L1r9_g4 , L1g9_g4 , L1b9_g4 );
			#ifdef _ADDITIVEONLY_ON
				float3 staticSwitch92 = L09_g4;
			#else
				float3 staticSwitch92 = L01_g3;
			#endif
			#ifdef _LIGHTVOLUMES_ON
				float3 staticSwitch431 = staticSwitch92;
			#else
				float3 staticSwitch431 = appendResult427;
			#endif
			float3 L098 = staticSwitch431;
			float3 L02_g224 = L098;
			#ifdef _ADDITIVEONLY_ON
				float3 staticSwitch93 = L1r9_g4;
			#else
				float3 staticSwitch93 = L1r1_g3;
			#endif
			#ifdef _LIGHTVOLUMES_ON
				float3 staticSwitch461 = staticSwitch93;
			#else
				float3 staticSwitch461 = (unity_SHAr).xyz;
			#endif
			float3 L1r99 = staticSwitch461;
			float3 L1r2_g224 = L1r99;
			#ifdef _ADDITIVEONLY_ON
				float3 staticSwitch94 = L1g9_g4;
			#else
				float3 staticSwitch94 = L1g1_g3;
			#endif
			#ifdef _LIGHTVOLUMES_ON
				float3 staticSwitch462 = staticSwitch94;
			#else
				float3 staticSwitch462 = (unity_SHAg).xyz;
			#endif
			float3 L1g100 = staticSwitch462;
			float3 L1g2_g224 = L1g100;
			#ifdef _ADDITIVEONLY_ON
				float3 staticSwitch95 = L1b9_g4;
			#else
				float3 staticSwitch95 = L1b1_g3;
			#endif
			#ifdef _LIGHTVOLUMES_ON
				float3 staticSwitch463 = staticSwitch95;
			#else
				float3 staticSwitch463 = (unity_SHAb).xyz;
			#endif
			float3 L1b101 = staticSwitch463;
			float3 L1b2_g224 = L1b101;
			float3 localLightVolumeEvaluate2_g224 = LightVolumeEvaluate( worldNormal2_g224 , L02_g224 , L1r2_g224 , L1g2_g224 , L1b2_g224 );
			float3 LVE_Color527 = localLightVolumeEvaluate2_g224;
			float4 appendResult4_g230 = (float4(( (temp_output_2_0_g229).rgb * LVE_Color527 ) , ( (temp_output_2_0_g229).a * i.vertexColor.a )));
			float4 temp_output_2_0_g231 = ( saturate( ( appendResult4_g230 * i.vertexColor ) ) * _Color );
			o.Albedo = (temp_output_2_0_g231).xyz;
			float4 ase_positionSS577 = i.screenPosition577;
			float4 ase_positionSSNorm = ase_positionSS577 / ase_positionSS577.w;
			ase_positionSSNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_positionSSNorm.z : ase_positionSSNorm.z * 0.5 + 0.5;
			float screenDepth577 = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE( _CameraDepthTexture, ase_positionSSNorm.xy ));
			float distanceDepth577 = abs( ( screenDepth577 - LinearEyeDepth( ase_positionSSNorm.z ) ) / ( _SoftparticleDistance ) );
			#ifdef _ENABLESOFTPARTICLE_ON
				float staticSwitch582 = saturate( distanceDepth577 );
			#else
				float staticSwitch582 = 1.0;
			#endif
			o.Alpha = ( (temp_output_2_0_g231).w * staticSwitch582 );
		}

		ENDCG
		CGPROGRAM
		#pragma surface surf Standard alpha:fade keepalpha fullforwardshadows vertex:vertexDataFunc 

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
			sampler3D _DitherMaskLOD;
			struct v2f
			{
				V2F_SHADOW_CASTER;
				float4 customPack1 : TEXCOORD1;
				float2 customPack2 : TEXCOORD2;
				float4 customPack3 : TEXCOORD3;
				float4 tSpace0 : TEXCOORD4;
				float4 tSpace1 : TEXCOORD5;
				float4 tSpace2 : TEXCOORD6;
				half4 color : COLOR0;
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
				vertexDataFunc( v, customInputData );
				float3 worldPos = mul( unity_ObjectToWorld, v.vertex ).xyz;
				half3 worldNormal = UnityObjectToWorldNormal( v.normal );
				half3 worldTangent = UnityObjectToWorldDir( v.tangent.xyz );
				half tangentSign = v.tangent.w * unity_WorldTransformParams.w;
				half3 worldBinormal = cross( worldNormal, worldTangent ) * tangentSign;
				o.tSpace0 = float4( worldTangent.x, worldBinormal.x, worldNormal.x, worldPos.x );
				o.tSpace1 = float4( worldTangent.y, worldBinormal.y, worldNormal.y, worldPos.y );
				o.tSpace2 = float4( worldTangent.z, worldBinormal.z, worldNormal.z, worldPos.z );
				o.customPack1.xyzw = customInputData.uv_texcoord;
				o.customPack1.xyzw = v.texcoord;
				o.customPack2.xy = customInputData.uv2_texcoord2;
				o.customPack2.xy = v.texcoord1;
				o.customPack3.xyzw = customInputData.screenPosition577;
				TRANSFER_SHADOW_CASTER_NORMALOFFSET( o )
				o.color = v.color;
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
				surfIN.uv_texcoord = IN.customPack1.xyzw;
				surfIN.uv2_texcoord2 = IN.customPack2.xy;
				surfIN.screenPosition577 = IN.customPack3.xyzw;
				float3 worldPos = float3( IN.tSpace0.w, IN.tSpace1.w, IN.tSpace2.w );
				half3 worldViewDir = normalize( UnityWorldSpaceViewDir( worldPos ) );
				surfIN.worldPos = worldPos;
				surfIN.worldNormal = float3( IN.tSpace0.z, IN.tSpace1.z, IN.tSpace2.z );
				surfIN.internalSurfaceTtoW0 = IN.tSpace0.xyz;
				surfIN.internalSurfaceTtoW1 = IN.tSpace1.xyz;
				surfIN.internalSurfaceTtoW2 = IN.tSpace2.xyz;
				surfIN.vertexColor = IN.color;
				SurfaceOutputStandard o;
				UNITY_INITIALIZE_OUTPUT( SurfaceOutputStandard, o )
				surf( surfIN, o );
				#if defined( CAN_SKIP_VPOS )
				float2 vpos = IN.pos;
				#endif
				half alphaRef = tex3D( _DitherMaskLOD, float3( vpos.xy * 0.25, o.Alpha * 0.9375 ) ).a;
				clip( alphaRef - 0.01 );
				SHADOW_CASTER_FRAGMENT( IN )
			}
			ENDCG
		}
	}
	Fallback "Diffuse"
	CustomEditor "LightVolumeParticleLitShaderGUI"
}
/*ASEBEGIN
Version=19801
Node;AmplifyShaderEditor.CommentaryNode;585;-2016,-1488;Inherit;False;606.2039;360.8224;Variables;3;418;547;549;;1,1,1,1;0;0
Node;AmplifyShaderEditor.CommentaryNode;498;-2016,-416;Inherit;False;1135.773;405.9057;Normal;7;592;499;504;503;502;501;500;;0.5792453,0.6214049,1,1;0;0
Node;AmplifyShaderEditor.CommentaryNode;531;-2016,-1056;Inherit;False;1604;620.2453;Albedo + Flipbook Blending;10;505;507;511;471;506;509;508;510;529;550;;1,1,1,1;0;0
Node;AmplifyShaderEditor.TexturePropertyNode;549;-1968,-1440;Inherit;True;Property;_MainTex;Main Texture;0;0;Create;False;0;0;0;False;0;False;None;None;False;white;Auto;Texture2D;-1;0;2;SAMPLER2D;0;SAMPLERSTATE;1
Node;AmplifyShaderEditor.RangedFloatNode;499;-1776,-96;Inherit;False;Property;_BumpScale;Normal Power;3;0;Create;False;0;0;0;False;0;False;1;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;592;-1984,-272;Inherit;False;0;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RegisterLocalVarNode;547;-1696,-1440;Inherit;False;MainTexture;-1;True;1;0;SAMPLER2D;;False;1;SAMPLER2D;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;505;-1920,-832;Inherit;False;0;-1;4;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.CommentaryNode;436;-2016,736;Inherit;False;580;475;Light Volumes;6;78;79;93;95;94;92;;0.9834821,1,0.7150943,1;0;0
Node;AmplifyShaderEditor.CommentaryNode;435;-2032,16;Inherit;False;579.2;735.9199;Defaul Unity Light Probes;7;427;426;425;424;430;429;428;;0.8294254,1,0.6396227,1;0;0
Node;AmplifyShaderEditor.SamplerNode;500;-1776,-272;Inherit;True;Property;_BumpMap;Normal;2;1;[NoScaleOffset];Create;False;0;0;0;False;0;False;-1;None;None;True;0;True;bump;Auto;True;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.GetLocalVarNode;550;-1744,-1008;Inherit;False;547;MainTexture;1;0;OBJECT;;False;1;SAMPLER2D;0
Node;AmplifyShaderEditor.DynamicAppendNode;511;-1712,-928;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.DynamicAppendNode;507;-1696,-672;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.FunctionNode;78;-1968,816;Inherit;False;LightVolume;-1;;3;78706f2b7f33b1c44b4f381a7904a7e1;4,8,0,10,0,11,0,12,0;1;6;FLOAT3;0,0,0;False;4;FLOAT3;13;FLOAT3;14;FLOAT3;15;FLOAT3;16
Node;AmplifyShaderEditor.FunctionNode;79;-1968,976;Inherit;False;LightVolume;-1;;4;78706f2b7f33b1c44b4f381a7904a7e1;4,8,1,10,1,11,1,12,1;1;6;FLOAT3;0,0,0;False;4;FLOAT3;13;FLOAT3;14;FLOAT3;15;FLOAT3;16
Node;AmplifyShaderEditor.Vector4Node;424;-1968,208;Inherit;False;Global;unity_SHAr;unity_SHAr;17;0;Fetch;True;0;0;0;False;0;False;0,0,0,0;0,0,0,0;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.Vector4Node;425;-1968,384;Inherit;False;Global;unity_SHAg;unity_SHAg;17;0;Fetch;True;0;0;0;False;0;False;0,0,0,0;0,0,0,0;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.Vector4Node;426;-1968,560;Inherit;False;Global;unity_SHAb;unity_SHAb;17;0;Fetch;True;0;0;0;False;0;False;0,0,0,0;0,0,0,0;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.WorldNormalVector;501;-1456,-272;Inherit;False;False;1;0;FLOAT3;0,0,1;False;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.SamplerNode;506;-1536,-672;Inherit;True;Property;_MainTex2;Albedo;1;0;Create;False;0;0;0;False;0;False;-1;None;None;True;1;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.SamplerNode;471;-1536,-1008;Inherit;True;Property;_MainTex1;Albedo;2;0;Create;False;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.TextureCoordinatesNode;509;-1216,-672;Inherit;False;1;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.StaticSwitch;93;-1712,880;Inherit;False;Property;_AdditiveOnly;Additive Only;8;0;Create;True;0;0;0;False;0;False;0;0;0;True;;Toggle;2;Key0;Key1;Reference;-1;True;True;All;9;1;FLOAT3;0,0,0;False;0;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT3;0,0,0;False;4;FLOAT3;0,0,0;False;5;FLOAT3;0,0,0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.StaticSwitch;94;-1712,976;Inherit;False;Property;_AdditiveOnly;Additive Only;8;0;Create;True;0;0;0;False;0;False;0;0;0;True;;Toggle;2;Key0;Key1;Reference;-1;True;True;All;9;1;FLOAT3;0,0,0;False;0;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT3;0,0,0;False;4;FLOAT3;0,0,0;False;5;FLOAT3;0,0,0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.StaticSwitch;92;-1712,784;Inherit;False;Property;_AdditiveOnly;Additive Only;6;0;Create;True;0;0;0;False;0;False;0;0;0;True;;Toggle;2;Key0;Key1;Create;True;True;All;9;1;FLOAT3;0,0,0;False;0;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT3;0,0,0;False;4;FLOAT3;0,0,0;False;5;FLOAT3;0,0,0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.ComponentMaskNode;428;-1712,208;Inherit;False;True;True;True;False;1;0;FLOAT4;0,0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.ComponentMaskNode;429;-1712,384;Inherit;False;True;True;True;False;1;0;FLOAT4;0,0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.ComponentMaskNode;430;-1712,560;Inherit;False;True;True;True;False;1;0;FLOAT4;0,0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.DynamicAppendNode;427;-1648,80;Inherit;False;FLOAT3;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.StaticSwitch;95;-1712,1072;Inherit;False;Property;_AdditiveOnly;Additive Only;8;0;Create;True;0;0;0;False;0;False;0;0;0;True;;Toggle;2;Key0;Key1;Reference;-1;True;True;All;9;1;FLOAT3;0,0,0;False;0;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT3;0,0,0;False;4;FLOAT3;0,0,0;False;5;FLOAT3;0,0,0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.NormalizeNode;502;-1280,-272;Inherit;False;False;1;0;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.LerpOp;508;-1216,-880;Inherit;True;3;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;2;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.StaticSwitch;461;-1344,608;Inherit;False;Property;_Keyword0;Keyword 0;5;0;Create;True;0;0;0;False;0;False;0;0;0;True;;Toggle;2;Key0;Key1;Reference;431;True;True;All;9;1;FLOAT3;0,0,0;False;0;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT3;0,0,0;False;4;FLOAT3;0,0,0;False;5;FLOAT3;0,0,0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.StaticSwitch;462;-1344,704;Inherit;False;Property;_Keyword1;Keyword 1;5;0;Create;True;0;0;0;False;0;False;0;0;0;True;;Toggle;2;Key0;Key1;Reference;431;True;True;All;9;1;FLOAT3;0,0,0;False;0;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT3;0,0,0;False;4;FLOAT3;0,0,0;False;5;FLOAT3;0,0,0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.StaticSwitch;463;-1344,800;Inherit;False;Property;_Keyword2;Keyword 2;5;0;Create;True;0;0;0;False;0;False;0;0;0;True;;Toggle;2;Key0;Key1;Reference;431;True;True;All;9;1;FLOAT3;0,0,0;False;0;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT3;0,0,0;False;4;FLOAT3;0,0,0;False;5;FLOAT3;0,0,0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.StaticSwitch;431;-1344,512;Inherit;False;Property;_LightVolumes;Enable Light Volumes;5;0;Create;False;0;0;0;False;0;False;0;1;1;True;;Toggle;2;Key0;Key1;Create;True;True;All;9;1;FLOAT3;0,0,0;False;0;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT3;0,0,0;False;4;FLOAT3;0,0,0;False;5;FLOAT3;0,0,0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;503;-1104,-272;Inherit;False;World Normal;-1;True;1;0;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.StaticSwitch;510;-960,-1008;Inherit;False;Property;_FlipbookBlending;Flipbook Blending;4;0;Create;True;0;0;0;False;0;False;0;0;0;True;;Toggle;2;Key0;Key1;Create;True;True;All;9;1;COLOR;0,0,0,0;False;0;COLOR;0,0,0,0;False;2;COLOR;0,0,0,0;False;3;COLOR;0,0,0,0;False;4;COLOR;0,0,0,0;False;5;COLOR;0,0,0,0;False;6;COLOR;0,0,0,0;False;7;COLOR;0,0,0,0;False;8;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;99;-1088,608;Inherit;False;L1r;-1;True;1;0;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;100;-1088,704;Inherit;False;L1g;-1;True;1;0;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;101;-1088,800;Inherit;False;L1b;-1;True;1;0;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;98;-1088,512;Inherit;False;L0;-1;True;1;0;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.GetLocalVarNode;485;-1088,416;Inherit;False;503;World Normal;1;0;OBJECT;;False;1;FLOAT3;0
Node;AmplifyShaderEditor.CommentaryNode;532;-352,-1056;Inherit;False;2000.661;804.8961;OUTPUT;22;578;476;571;574;582;552;583;576;477;551;577;580;480;526;515;475;517;474;528;516;530;584;;1,1,1,1;0;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;529;-656,-1008;Inherit;False;Albedo;-1;True;1;0;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.FunctionNode;490;-848,496;Inherit;False;LightVolumeEvaluate;-1;;224;4919cc1d83093f24f802ce655e9f3303;0;5;5;FLOAT3;0,0,0;False;13;FLOAT3;1,1,1;False;14;FLOAT3;0,0,0;False;15;FLOAT3;0,0,0;False;16;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.GetLocalVarNode;530;-320,-944;Inherit;False;529;Albedo;1;0;OBJECT;;False;1;COLOR;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;527;-592,496;Inherit;False;LVE Color;-1;True;1;0;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.FunctionNode;516;-112,-944;Inherit;False;Alpha Split;-1;;229;07dab7960105b86429ac8eebd729ed6d;0;1;2;COLOR;0,0,0,0;False;2;FLOAT3;0;FLOAT;6
Node;AmplifyShaderEditor.VertexColorNode;474;-16,-720;Inherit;False;0;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.GetLocalVarNode;528;-112,-832;Inherit;False;527;LVE Color;1;0;OBJECT;;False;1;FLOAT3;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;475;192,-832;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.WireNode;584;570.4828,-685.3073;Inherit;False;1;0;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;517;112,-944;Inherit;False;2;2;0;FLOAT3;0,0,0;False;1;FLOAT3;1,1,1;False;1;FLOAT3;0
Node;AmplifyShaderEditor.FunctionNode;515;384,-944;Inherit;True;Alpha Merge;-1;;230;e0d79828992f19c4f90bfc29aa19b7a5;0;2;2;FLOAT3;0,0,0;False;3;FLOAT;0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.WireNode;526;608,-704;Inherit;False;1;0;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;480;640,-944;Inherit;False;2;2;0;FLOAT4;0,0,0,0;False;1;COLOR;0,0,0,0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.PosVertexDataNode;580;144,-432;Inherit;False;0;0;5;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RangedFloatNode;578;432,-336;Inherit;False;Property;_SoftparticleDistance;Softparticle Distance;8;0;Create;True;0;0;0;False;0;False;0;0.308;0;2;0;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode;476;800,-944;Inherit;False;1;0;FLOAT4;0,0,0,0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.DepthFade;577;464,-432;Inherit;False;True;False;True;2;1;FLOAT3;0,0,0;False;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.ColorNode;551;720,-784;Inherit;False;Property;_Color;Tint;1;1;[HDR];Create;False;0;0;0;False;0;False;1,1,1,1;1,1,1,1;True;True;0;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.SaturateNode;576;704,-432;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;583;704,-512;Inherit;False;Constant;_Float1;Float1;10;0;Create;True;0;0;0;False;0;False;1;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;477;960,-944;Inherit;True;2;2;0;FLOAT4;0,0,0,0;False;1;COLOR;0,0,0,0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.FunctionNode;552;1168,-944;Inherit;False;Alpha Split;-1;;231;07dab7960105b86429ac8eebd729ed6d;0;1;2;FLOAT4;0,0,0,0;False;2;FLOAT3;0;FLOAT;6
Node;AmplifyShaderEditor.StaticSwitch;582;912,-464;Inherit;False;Property;_EnableSoftparticle;Enable Softparticle;7;0;Create;True;0;0;0;False;0;False;0;0;0;True;;Toggle;2;Key0;Key1;Create;True;True;All;9;1;FLOAT;0;False;0;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;4;FLOAT;0;False;5;FLOAT;0;False;6;FLOAT;0;False;7;FLOAT;0;False;8;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;418;-1968,-1248;Inherit;False;Property;_Culling;Culling;9;1;[Enum];Create;False;0;0;1;UnityEngine.Rendering.CullMode;True;0;False;2;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;504;-1456,-352;Inherit;False;Normal;-1;True;1;0;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;574;1248,-720;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.StandardSurfaceOutputNode;571;1408,-944;Float;False;True;-1;3;LightVolumeParticleLitShaderGUI;0;12;Standard;Light Volume Particle Lit;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;False;False;False;False;False;False;Back;0;False;;0;False;;False;0;False;;0;False;;False;0;Transparent;0.5;True;True;0;False;Transparent;;Transparent;All;12;all;True;True;True;True;0;False;;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;2;15;10;25;False;0.5;True;2;5;False;;10;False;;0;0;False;;0;False;;0;False;;0;False;;0;False;0;0,0,0,0;VertexOffset;True;False;Cylindrical;False;True;Relative;0;;-1;-1;-1;-1;0;False;0;0;True;_Culling;-1;0;False;;0;0;0;False;0.1;False;;0;False;;False;17;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT;0;False;4;FLOAT;0;False;5;FLOAT;0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT;0;False;9;FLOAT;0;False;10;FLOAT;0;False;13;FLOAT3;0,0,0;False;11;FLOAT3;0,0,0;False;12;FLOAT3;0,0,0;False;16;FLOAT4;0,0,0,0;False;14;FLOAT4;0,0,0,0;False;15;FLOAT3;0,0,0;False;0
WireConnection;547;0;549;0
WireConnection;500;1;592;0
WireConnection;500;5;499;0
WireConnection;511;0;505;1
WireConnection;511;1;505;2
WireConnection;507;0;505;3
WireConnection;507;1;505;4
WireConnection;501;0;500;0
WireConnection;506;0;550;0
WireConnection;506;1;507;0
WireConnection;471;0;550;0
WireConnection;471;1;511;0
WireConnection;93;1;78;14
WireConnection;93;0;79;14
WireConnection;94;1;78;15
WireConnection;94;0;79;15
WireConnection;92;1;78;13
WireConnection;92;0;79;13
WireConnection;428;0;424;0
WireConnection;429;0;425;0
WireConnection;430;0;426;0
WireConnection;427;0;424;4
WireConnection;427;1;425;4
WireConnection;427;2;426;4
WireConnection;95;1;78;16
WireConnection;95;0;79;16
WireConnection;502;0;501;0
WireConnection;508;0;471;0
WireConnection;508;1;506;0
WireConnection;508;2;509;1
WireConnection;461;1;428;0
WireConnection;461;0;93;0
WireConnection;462;1;429;0
WireConnection;462;0;94;0
WireConnection;463;1;430;0
WireConnection;463;0;95;0
WireConnection;431;1;427;0
WireConnection;431;0;92;0
WireConnection;503;0;502;0
WireConnection;510;1;471;0
WireConnection;510;0;508;0
WireConnection;99;0;461;0
WireConnection;100;0;462;0
WireConnection;101;0;463;0
WireConnection;98;0;431;0
WireConnection;529;0;510;0
WireConnection;490;5;485;0
WireConnection;490;13;98;0
WireConnection;490;14;99;0
WireConnection;490;15;100;0
WireConnection;490;16;101;0
WireConnection;527;0;490;0
WireConnection;516;2;530;0
WireConnection;475;0;516;6
WireConnection;475;1;474;4
WireConnection;584;0;474;0
WireConnection;517;0;516;0
WireConnection;517;1;528;0
WireConnection;515;2;517;0
WireConnection;515;3;475;0
WireConnection;526;0;584;0
WireConnection;480;0;515;0
WireConnection;480;1;526;0
WireConnection;476;0;480;0
WireConnection;577;1;580;0
WireConnection;577;0;578;0
WireConnection;576;0;577;0
WireConnection;477;0;476;0
WireConnection;477;1;551;0
WireConnection;552;2;477;0
WireConnection;582;1;583;0
WireConnection;582;0;576;0
WireConnection;504;0;500;0
WireConnection;574;0;552;6
WireConnection;574;1;582;0
WireConnection;571;0;552;0
WireConnection;571;9;574;0
ASEEND*/
//CHKSM=1F414A02B112BCAF32A2AA44437E1C988A6D5196