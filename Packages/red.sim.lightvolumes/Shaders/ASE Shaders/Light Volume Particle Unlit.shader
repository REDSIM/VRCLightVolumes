// Made with Amplify Shader Editor v1.9.8.1
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "Light Volume Particle Unlit"
{
	Properties
	{
		_TintColor ("Tint Color", Color) = (0.5,0.5,0.5,0.5)
		_MainTex ("Particle Texture", 2D) = "white" {}
		[NoScaleOffset]_BumpMap("Normal", 2D) = "bump" {}
		_BumpScale("Normal Power", Float) = 1
		[Toggle(_LIGHTVOLUMES_ON)] _LightVolumes("Enable Light Volumes", Float) = 1
		[Toggle(_ADDITIVEONLY_ON)] _AdditiveOnly("Additive Only", Float) = 0
		[Toggle(_FLIPBOOKBLENDING_ON)] _FlipbookBlending("Flipbook Blending", Float) = 0
		[Toggle(_ENABLESOFTPARTICLE_ON)] _EnableSoftparticle("Enable Softparticle", Float) = 0
		_SoftparticleDistance("Softparticle Distance", Range( 0 , 2)) = 0
		[Enum(UnityEngine.Rendering.CullMode)]_Culling("Culling", Float) = 2
		[HDR]_Color("Tint", Color) = (0,0,0,0)
		[HideInInspector] _texcoord( "", 2D ) = "white" {}

	}


	Category
	{
		SubShader
		{
		LOD 0

			Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
			Blend SrcAlpha OneMinusSrcAlpha
			ColorMask RGB
			Cull Off
			Lighting Off
			ZWrite Off
			ZTest LEqual
			
			Pass {

				CGPROGRAM
				#define ASE_VERSION 19801

				#ifndef UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX
				#define UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input)
				#endif

				#pragma vertex vert
				#pragma fragment frag
				#pragma target 3.5
				#pragma multi_compile_instancing
				#pragma multi_compile_particles
				#pragma multi_compile_fog
				#include "Packages/red.sim.lightvolumes/Shaders/LightVolumes.cginc"
				#include "UnityStandardUtils.cginc"
				#define ASE_NEEDS_FRAG_COLOR
				#define ASE_NEEDS_VERT_POSITION
				#pragma shader_feature_local _FLIPBOOKBLENDING_ON
				#pragma shader_feature_local _LIGHTVOLUMES_ON
				#pragma shader_feature_local _ADDITIVEONLY_ON
				#pragma shader_feature_local _ENABLESOFTPARTICLE_ON


				#include "UnityCG.cginc"

				struct appdata_t
				{
					float4 vertex : POSITION;
					fixed4 color : COLOR;
					float4 texcoord : TEXCOORD0;
					UNITY_VERTEX_INPUT_INSTANCE_ID
					float4 ase_texcoord1 : TEXCOORD1;
					float4 ase_tangent : TANGENT;
					float3 ase_normal : NORMAL;
				};

				struct v2f
				{
					float4 vertex : SV_POSITION;
					fixed4 color : COLOR;
					float4 texcoord : TEXCOORD0;
					UNITY_FOG_COORDS(1)
					#ifdef SOFTPARTICLES_ON
					float4 projPos : TEXCOORD2;
					#endif
					UNITY_VERTEX_INPUT_INSTANCE_ID
					UNITY_VERTEX_OUTPUT_STEREO
					float4 ase_texcoord3 : TEXCOORD3;
					float4 ase_texcoord4 : TEXCOORD4;
					float4 ase_texcoord5 : TEXCOORD5;
					float4 ase_texcoord6 : TEXCOORD6;
					float4 ase_texcoord7 : TEXCOORD7;
					float4 ase_texcoord8 : TEXCOORD8;
				};


				#if UNITY_VERSION >= 560
				UNITY_DECLARE_DEPTH_TEXTURE( _CameraDepthTexture );
				#else
				uniform sampler2D_float _CameraDepthTexture;
				#endif

				//Don't delete this comment
				// uniform sampler2D_float _CameraDepthTexture;

				uniform sampler2D _MainTex;
				uniform fixed4 _TintColor;
				uniform float4 _MainTex_ST;
				uniform float _Culling;
				uniform sampler2D _BumpMap;
				uniform float _BumpScale;
				uniform float4 _Color;
				uniform float4 _CameraDepthTexture_TexelSize;
				uniform float _SoftparticleDistance;


				v2f vert ( appdata_t v  )
				{
					v2f o;
					UNITY_SETUP_INSTANCE_ID(v);
					UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
					UNITY_TRANSFER_INSTANCE_ID(v, o);
					float3 ase_tangentWS = UnityObjectToWorldDir( v.ase_tangent );
					o.ase_texcoord4.xyz = ase_tangentWS;
					float3 ase_normalWS = UnityObjectToWorldNormal( v.ase_normal );
					o.ase_texcoord5.xyz = ase_normalWS;
					float ase_tangentSign = v.ase_tangent.w * ( unity_WorldTransformParams.w >= 0.0 ? 1.0 : -1.0 );
					float3 ase_bitangentWS = cross( ase_normalWS, ase_tangentWS ) * ase_tangentSign;
					o.ase_texcoord6.xyz = ase_bitangentWS;
					float3 ase_positionWS = mul( unity_ObjectToWorld, float4( ( v.vertex ).xyz, 1 ) ).xyz;
					o.ase_texcoord7.xyz = ase_positionWS;
					float3 vertexPos537 = v.vertex.xyz;
					float4 ase_positionCS537 = UnityObjectToClipPos( vertexPos537 );
					float4 screenPos537 = ComputeScreenPos( ase_positionCS537 );
					o.ase_texcoord8 = screenPos537;
					
					o.ase_texcoord3.xy = v.ase_texcoord1.xy;
					
					//setting value to unused interpolator channels and avoid initialization warnings
					o.ase_texcoord3.zw = 0;
					o.ase_texcoord4.w = 0;
					o.ase_texcoord5.w = 0;
					o.ase_texcoord6.w = 0;
					o.ase_texcoord7.w = 0;

					v.vertex.xyz +=  float3( 0, 0, 0 ) ;
					o.vertex = UnityObjectToClipPos(v.vertex);
					#ifdef SOFTPARTICLES_ON
						o.projPos = ComputeScreenPos (o.vertex);
						COMPUTE_EYEDEPTH(o.projPos.z);
					#endif
					o.color = v.color;
					o.texcoord = v.texcoord;
					UNITY_TRANSFER_FOG(o,o.vertex);
					return o;
				}

				fixed4 frag ( v2f i  ) : SV_Target
				{
					UNITY_SETUP_INSTANCE_ID( i );
					UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX( i );

					/*
					#ifdef SOFTPARTICLES_ON
						float sceneZ = LinearEyeDepth (SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.projPos)));
						float partZ = i.projPos.z;
						float fade = saturate (_InvFade * (sceneZ-partZ));
						i.color.a *= fade;
					#endif
					*/

					float4 texCoord505 = i.texcoord;
					texCoord505.xy = i.texcoord.xy * float2( 1,1 ) + float2( 0,0 );
					float2 appendResult511 = (float2(texCoord505.x , texCoord505.y));
					float4 tex2DNode471 = tex2D( _MainTex, appendResult511 );
					float2 appendResult507 = (float2(texCoord505.z , texCoord505.w));
					float2 texCoord509 = i.ase_texcoord3.xy * float2( 1,1 ) + float2( 0,0 );
					float4 lerpResult508 = lerp( tex2DNode471 , tex2D( _MainTex, appendResult507 ) , texCoord509.x);
					#ifdef _FLIPBOOKBLENDING_ON
					float4 staticSwitch510 = lerpResult508;
					#else
					float4 staticSwitch510 = tex2DNode471;
					#endif
					float4 Albedo529 = staticSwitch510;
					float4 temp_output_2_0_g229 = Albedo529;
					float2 uv_BumpMap500 = i.texcoord.xy;
					float3 tex2DNode500 = UnpackScaleNormal( tex2D( _BumpMap, uv_BumpMap500 ), _BumpScale );
					float3 ase_tangentWS = i.ase_texcoord4.xyz;
					float3 ase_normalWS = i.ase_texcoord5.xyz;
					float3 ase_bitangentWS = i.ase_texcoord6.xyz;
					float3 tanToWorld0 = float3( ase_tangentWS.x, ase_bitangentWS.x, ase_normalWS.x );
					float3 tanToWorld1 = float3( ase_tangentWS.y, ase_bitangentWS.y, ase_normalWS.y );
					float3 tanToWorld2 = float3( ase_tangentWS.z, ase_bitangentWS.z, ase_normalWS.z );
					float3 tanNormal501 = tex2DNode500;
					float3 worldNormal501 = float3( dot( tanToWorld0, tanNormal501 ), dot( tanToWorld1, tanNormal501 ), dot( tanToWorld2, tanNormal501 ) );
					float3 normalizeResult502 = normalize( worldNormal501 );
					float3 World_Normal503 = normalizeResult502;
					float3 worldNormal2_g224 = World_Normal503;
					float3 appendResult427 = (float3(unity_SHAr.w , unity_SHAg.w , unity_SHAb.w));
					float localLightVolumeSH1_g3 = ( 0.0 );
					float3 ase_positionWS = i.ase_texcoord7.xyz;
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
					float4 appendResult4_g230 = (float4(( (temp_output_2_0_g229).rgb * LVE_Color527 ) , ( (temp_output_2_0_g229).a * i.color.a )));
					float4 temp_output_2_0_g231 = ( saturate( ( appendResult4_g230 * i.color ) ) * _Color );
					float4 screenPos537 = i.ase_texcoord8;
					float4 ase_positionSSNorm = screenPos537 / screenPos537.w;
					ase_positionSSNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_positionSSNorm.z : ase_positionSSNorm.z * 0.5 + 0.5;
					float screenDepth537 = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE( _CameraDepthTexture, ase_positionSSNorm.xy ));
					float distanceDepth537 = abs( ( screenDepth537 - LinearEyeDepth( ase_positionSSNorm.z ) ) / ( _SoftparticleDistance ) );
					#ifdef _ENABLESOFTPARTICLE_ON
					float staticSwitch540 = saturate( distanceDepth537 );
					#else
					float staticSwitch540 = 1.0;
					#endif
					float4 appendResult4_g232 = (float4((temp_output_2_0_g231).xyz , ( (temp_output_2_0_g231).w * staticSwitch540 )));
					

					fixed4 col = appendResult4_g232;
					UNITY_APPLY_FOG(i.fogCoord, col);
					return col;
				}
				ENDCG
			}
		}
	}
	CustomEditor "LightVolumeParticleUnlitShaderGUI"
	
	Fallback Off
}
/*ASEBEGIN
Version=19801
Node;AmplifyShaderEditor.CommentaryNode;498;-2016,-416;Inherit;False;1124;357;Normal;6;504;503;502;501;500;499;;0.5792453,0.6214049,1,1;0;0
Node;AmplifyShaderEditor.CommentaryNode;531;-2016,-1056;Inherit;False;1604;620.2453;Albedo + Flipbook Blending;10;505;507;470;511;471;506;509;508;510;529;;1,1,1,1;0;0
Node;AmplifyShaderEditor.RangedFloatNode;499;-1968,-224;Inherit;False;Property;_BumpScale;Normal Power;1;0;Create;False;0;0;0;False;0;False;1;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;505;-1968,-880;Inherit;False;0;-1;4;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.CommentaryNode;436;-2016,736;Inherit;False;580;475;Light Volumes;6;78;79;93;95;94;92;;0.9834821,1,0.7150943,1;0;0
Node;AmplifyShaderEditor.CommentaryNode;435;-2016,-32;Inherit;False;579.2;735.9199;Defaul Unity Light Probes;7;427;426;425;424;430;429;428;;0.8294254,1,0.6396227,1;0;0
Node;AmplifyShaderEditor.SamplerNode;500;-1776,-272;Inherit;True;Property;_BumpMap;Normal;0;1;[NoScaleOffset];Create;False;0;0;0;False;0;False;-1;None;None;True;0;True;bump;Auto;True;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.DynamicAppendNode;507;-1728,-672;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.DynamicAppendNode;511;-1728,-848;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.TemplateShaderPropertyNode;470;-1776,-1008;Inherit;False;0;0;_MainTex;Shader;False;0;5;SAMPLER2D;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.WorldNormalVector;501;-1456,-272;Inherit;False;False;1;0;FLOAT3;0,0,1;False;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.FunctionNode;78;-1968,816;Inherit;False;LightVolume;-1;;3;78706f2b7f33b1c44b4f381a7904a7e1;4,8,0,10,0,11,0,12,0;1;6;FLOAT3;0,0,0;False;4;FLOAT3;13;FLOAT3;14;FLOAT3;15;FLOAT3;16
Node;AmplifyShaderEditor.FunctionNode;79;-1968,976;Inherit;False;LightVolume;-1;;4;78706f2b7f33b1c44b4f381a7904a7e1;4,8,1,10,1,11,1,12,1;1;6;FLOAT3;0,0,0;False;4;FLOAT3;13;FLOAT3;14;FLOAT3;15;FLOAT3;16
Node;AmplifyShaderEditor.Vector4Node;424;-1952,160;Inherit;False;Global;unity_SHAr;unity_SHAr;17;0;Fetch;True;0;0;0;False;0;False;0,0,0,0;0,0,0,0;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.Vector4Node;425;-1952,336;Inherit;False;Global;unity_SHAg;unity_SHAg;17;0;Fetch;True;0;0;0;False;0;False;0,0,0,0;0,0,0,0;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.Vector4Node;426;-1952,512;Inherit;False;Global;unity_SHAb;unity_SHAb;17;0;Fetch;True;0;0;0;False;0;False;0,0,0,0;0,0,0,0;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.TextureCoordinatesNode;509;-1216,-672;Inherit;False;1;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SamplerNode;471;-1536,-1008;Inherit;True;Property;_MainTex1;Albedo;1;0;Create;False;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.SamplerNode;506;-1536,-672;Inherit;True;Property;_MainTex2;Albedo;1;0;Create;False;0;0;0;False;0;False;-1;None;None;True;1;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.NormalizeNode;502;-1280,-272;Inherit;False;False;1;0;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.StaticSwitch;93;-1712,880;Inherit;False;Property;_AdditiveOnly;Additive Only;8;0;Create;True;0;0;0;False;0;False;0;0;0;True;;Toggle;2;Key0;Key1;Reference;-1;True;True;All;9;1;FLOAT3;0,0,0;False;0;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT3;0,0,0;False;4;FLOAT3;0,0,0;False;5;FLOAT3;0,0,0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.StaticSwitch;94;-1712,976;Inherit;False;Property;_AdditiveOnly;Additive Only;8;0;Create;True;0;0;0;False;0;False;0;0;0;True;;Toggle;2;Key0;Key1;Reference;-1;True;True;All;9;1;FLOAT3;0,0,0;False;0;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT3;0,0,0;False;4;FLOAT3;0,0,0;False;5;FLOAT3;0,0,0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.StaticSwitch;92;-1712,784;Inherit;False;Property;_AdditiveOnly;Additive Only;3;0;Create;True;0;0;0;False;0;False;0;0;0;True;;Toggle;2;Key0;Key1;Create;True;True;All;9;1;FLOAT3;0,0,0;False;0;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT3;0,0,0;False;4;FLOAT3;0,0,0;False;5;FLOAT3;0,0,0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.ComponentMaskNode;428;-1696,160;Inherit;False;True;True;True;False;1;0;FLOAT4;0,0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.ComponentMaskNode;429;-1696,336;Inherit;False;True;True;True;False;1;0;FLOAT4;0,0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.ComponentMaskNode;430;-1696,512;Inherit;False;True;True;True;False;1;0;FLOAT4;0,0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.DynamicAppendNode;427;-1632,32;Inherit;False;FLOAT3;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.StaticSwitch;95;-1712,1072;Inherit;False;Property;_AdditiveOnly;Additive Only;8;0;Create;True;0;0;0;False;0;False;0;0;0;True;;Toggle;2;Key0;Key1;Reference;-1;True;True;All;9;1;FLOAT3;0,0,0;False;0;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT3;0,0,0;False;4;FLOAT3;0,0,0;False;5;FLOAT3;0,0,0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.LerpOp;508;-1216,-880;Inherit;True;3;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;2;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;503;-1136,-272;Inherit;False;World Normal;-1;True;1;0;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.StaticSwitch;461;-1344,608;Inherit;False;Property;_Keyword0;Keyword 0;2;0;Create;True;0;0;0;False;0;False;0;0;0;True;;Toggle;2;Key0;Key1;Reference;431;True;True;All;9;1;FLOAT3;0,0,0;False;0;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT3;0,0,0;False;4;FLOAT3;0,0,0;False;5;FLOAT3;0,0,0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.StaticSwitch;462;-1344,704;Inherit;False;Property;_Keyword1;Keyword 1;2;0;Create;True;0;0;0;False;0;False;0;0;0;True;;Toggle;2;Key0;Key1;Reference;431;True;True;All;9;1;FLOAT3;0,0,0;False;0;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT3;0,0,0;False;4;FLOAT3;0,0,0;False;5;FLOAT3;0,0,0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.StaticSwitch;463;-1344,800;Inherit;False;Property;_Keyword2;Keyword 2;2;0;Create;True;0;0;0;False;0;False;0;0;0;True;;Toggle;2;Key0;Key1;Reference;431;True;True;All;9;1;FLOAT3;0,0,0;False;0;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT3;0,0,0;False;4;FLOAT3;0,0,0;False;5;FLOAT3;0,0,0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.StaticSwitch;431;-1344,512;Inherit;False;Property;_LightVolumes;Enable Light Volumes;2;0;Create;False;0;0;0;False;0;False;0;1;1;True;;Toggle;2;Key0;Key1;Create;True;True;All;9;1;FLOAT3;0,0,0;False;0;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT3;0,0,0;False;4;FLOAT3;0,0,0;False;5;FLOAT3;0,0,0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.StaticSwitch;510;-960,-1008;Inherit;False;Property;_FlipbookBlending;Flipbook Blending;4;0;Create;True;0;0;0;False;0;False;0;0;0;True;;Toggle;2;Key0;Key1;Create;True;True;All;9;1;COLOR;0,0,0,0;False;0;COLOR;0,0,0,0;False;2;COLOR;0,0,0,0;False;3;COLOR;0,0,0,0;False;4;COLOR;0,0,0,0;False;5;COLOR;0,0,0,0;False;6;COLOR;0,0,0,0;False;7;COLOR;0,0,0,0;False;8;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;99;-1088,608;Inherit;False;L1r;-1;True;1;0;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;100;-1088,704;Inherit;False;L1g;-1;True;1;0;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;101;-1088,800;Inherit;False;L1b;-1;True;1;0;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;98;-1088,512;Inherit;False;L0;-1;True;1;0;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.GetLocalVarNode;485;-1088,416;Inherit;False;503;World Normal;1;0;OBJECT;;False;1;FLOAT3;0
Node;AmplifyShaderEditor.CommentaryNode;532;-352,-1056;Inherit;False;1974.781;919.1291;OUTPUT;22;540;539;538;537;536;535;541;542;543;418;476;480;515;526;475;517;474;528;516;530;547;548;;1,1,1,1;0;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;529;-656,-1008;Inherit;False;Albedo;-1;True;1;0;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.FunctionNode;490;-848,496;Inherit;False;LightVolumeEvaluate;-1;;224;4919cc1d83093f24f802ce655e9f3303;0;5;5;FLOAT3;0,0,0;False;13;FLOAT3;1,1,1;False;14;FLOAT3;0,0,0;False;15;FLOAT3;0,0,0;False;16;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.GetLocalVarNode;530;-320,-976;Inherit;False;529;Albedo;1;0;OBJECT;;False;1;COLOR;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;527;-592,496;Inherit;False;LVE Color;-1;True;1;0;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.FunctionNode;516;-112,-976;Inherit;False;Alpha Split;-1;;229;07dab7960105b86429ac8eebd729ed6d;0;1;2;COLOR;0,0,0,0;False;2;FLOAT3;0;FLOAT;6
Node;AmplifyShaderEditor.VertexColorNode;474;-16,-768;Inherit;False;0;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.GetLocalVarNode;528;-112,-848;Inherit;False;527;LVE Color;1;0;OBJECT;;False;1;FLOAT3;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;475;176,-864;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;517;112,-976;Inherit;False;2;2;0;FLOAT3;0,0,0;False;1;FLOAT3;1,1,1;False;1;FLOAT3;0
Node;AmplifyShaderEditor.WireNode;526;448,-768;Inherit;False;1;0;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.FunctionNode;515;384,-976;Inherit;False;Alpha Merge;-1;;230;e0d79828992f19c4f90bfc29aa19b7a5;0;2;2;FLOAT3;0,0,0;False;3;FLOAT;0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.PosVertexDataNode;535;64,-480;Inherit;False;0;0;5;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RangedFloatNode;536;352,-384;Inherit;False;Property;_SoftparticleDistance;Softparticle Distance;6;0;Create;True;0;0;0;False;0;False;0;0.2;0;2;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;480;576,-976;Inherit;False;2;2;0;FLOAT4;0,0,0,0;False;1;COLOR;0,0,0,0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.DepthFade;537;384,-480;Inherit;False;True;False;True;2;1;FLOAT3;0,0,0;False;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode;476;720,-976;Inherit;False;1;0;FLOAT4;0,0,0,0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.ColorNode;547;656,-848;Inherit;False;Property;_Color;Tint;8;1;[HDR];Create;False;0;0;0;False;0;False;0,0,0,0;1,1,1,1;True;True;0;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.SaturateNode;538;624,-480;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;539;624,-560;Inherit;False;Constant;_Float1;Float1;10;0;Create;True;0;0;0;False;0;False;1;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;548;864,-976;Inherit;False;2;2;0;FLOAT4;0,0,0,0;False;1;COLOR;0,0,0,0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.StaticSwitch;540;832,-512;Inherit;False;Property;_EnableSoftparticle;Enable Softparticle;5;0;Create;True;0;0;0;False;0;False;0;0;1;True;;Toggle;2;Key0;Key1;Create;True;True;All;9;1;FLOAT;0;False;0;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;4;FLOAT;0;False;5;FLOAT;0;False;6;FLOAT;0;False;7;FLOAT;0;False;8;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.FunctionNode;542;1008,-976;Inherit;False;Alpha Split;-1;;231;07dab7960105b86429ac8eebd729ed6d;0;1;2;FLOAT4;0,0,0,0;False;2;FLOAT3;0;FLOAT;6
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;541;1136,-864;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;504;-1456,-352;Inherit;False;Normal;-1;True;1;0;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.RangedFloatNode;418;1408,-880;Inherit;False;Property;_Culling;Culling;7;1;[Enum];Create;False;0;0;1;UnityEngine.Rendering.CullMode;True;0;False;2;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.FunctionNode;543;1232,-976;Inherit;False;Alpha Merge;-1;;232;e0d79828992f19c4f90bfc29aa19b7a5;0;2;2;FLOAT3;0,0,0;False;3;FLOAT;0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;545;1408,-976;Float;False;True;-1;3;LightVolumeParticleUnlitShaderGUI;0;11;Light Volume Particle Unlit;0b6a9f8b4f707c74ca64c0be8e590de0;True;SubShader 0 Pass 0;0;0;SubShader 0 Pass 0;2;False;True;2;5;False;;10;False;;0;1;False;;0;False;;False;False;False;False;False;False;False;False;False;False;False;False;True;2;False;;False;True;True;True;True;False;0;False;;False;False;False;False;False;False;False;False;False;True;2;False;;True;3;False;;False;True;4;Queue=Transparent=Queue=0;IgnoreProjector=True;RenderType=Transparent=RenderType;PreviewType=Plane;False;False;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;3;False;0;;0;0;Standard;0;0;1;True;False;;False;0
WireConnection;500;5;499;0
WireConnection;507;0;505;3
WireConnection;507;1;505;4
WireConnection;511;0;505;1
WireConnection;511;1;505;2
WireConnection;501;0;500;0
WireConnection;471;0;470;0
WireConnection;471;1;511;0
WireConnection;506;0;470;0
WireConnection;506;1;507;0
WireConnection;502;0;501;0
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
WireConnection;508;0;471;0
WireConnection;508;1;506;0
WireConnection;508;2;509;1
WireConnection;503;0;502;0
WireConnection;461;1;428;0
WireConnection;461;0;93;0
WireConnection;462;1;429;0
WireConnection;462;0;94;0
WireConnection;463;1;430;0
WireConnection;463;0;95;0
WireConnection;431;1;427;0
WireConnection;431;0;92;0
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
WireConnection;517;0;516;0
WireConnection;517;1;528;0
WireConnection;526;0;474;0
WireConnection;515;2;517;0
WireConnection;515;3;475;0
WireConnection;480;0;515;0
WireConnection;480;1;526;0
WireConnection;537;1;535;0
WireConnection;537;0;536;0
WireConnection;476;0;480;0
WireConnection;538;0;537;0
WireConnection;548;0;476;0
WireConnection;548;1;547;0
WireConnection;540;1;539;0
WireConnection;540;0;538;0
WireConnection;542;2;548;0
WireConnection;541;0;542;6
WireConnection;541;1;540;0
WireConnection;504;0;500;0
WireConnection;543;2;542;0
WireConnection;543;3;541;0
WireConnection;545;0;543;0
ASEEND*/
//CHKSM=7C057E83158D697C98563569AC88D47353F63635