// Made with Amplify Shader Editor v1.9.9.5
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "Effect/EffectFunction_BRP"
{
	Properties
	{
		[Enum(UnityEngine.Rendering.CullMode)] _CullMode2( "CullMode", Int ) = 0
		[Enum(Additive,1,AlphaBlend,10)] _Dst( "Blend Mode", Int ) = 1
		[Enum(OFF,0,ON,2)] _Zwrite( "Zwrite", Int ) = 0
		[KeywordEnum( R,A )] _RASwitch( "RASwitch", Float ) = 1
		_DepthFadeIndensity( "DepthFadeIndensity", Range( 0.1, 5 ) ) = 0.1
		_DepthFadeDistance( "DepthFadeDistance", Float ) = 0
		[Toggle] _FresnelSwitch2( "FresnelSwitch", Float ) = 0
		[Toggle] _FresnelReverse( "FresnelReverse", Float ) = 0
		_FresnelBias( "FresnelBias", Float ) = 1
		_FresnelScale( "FresnelScale", Float ) = 0.96
		_FresnelPower( "FresnelPower", Float ) = 2
		[HDR] _FresnelColor( "FresnelColor", Color ) = ( 0, 0, 0, 0 )
		[Header(MainTexCustom)][Toggle] _Custom1UV( "Custom1UV", Float ) = 0
		[HDR] _Color( "Color", Color ) = ( 1, 1, 1, 1 )
		[HDR] _ColorBack( "ColorBack", Color ) = ( 1, 1, 1, 1 )
		_ColorIntensity( "ColorIntensity", Float ) = 1
		[Header(Main_Custom1XY)] _MainTex( "MainTex", 2D ) = "white" {}
		_Angle( "Angle", Range( 0, 360 ) ) = 0
		[KeywordEnum( UV,UClamp,VClamp,UVClamp )] _MainTex_UVClamp( "MainTex_UVClamp", Float ) = 0
		_MainU_Speed( "MainU_Speed", Float ) = 0
		_MainV_Speed( "MainV_Speed", Float ) = 0
		_GradientTex( "GradientTex", 2D ) = "white" {}
		_RampTex( "RampTex", 2D ) = "white" {}
		[Header(NoiseTexCustom)][Toggle] _Custom1WT( "Custom1WT", Float ) = 0
		[Enum(Normal,0,PolarCoordinates,1)] _NoisePolarCoordinates( "NoisePolarCoordinates", Float ) = 0
		_Radial( "Radial", Float ) = 1
		_Length( "Length", Float ) = 1
		[Header(NoiseCustom2XY)] _NoiseTex( "NoiseTex", 2D ) = "white" {}
		_NoiseMask( "NoiseMask", 2D ) = "white" {}
		_NoiseU_Speed( "NoiseU_Speed", Float ) = 0
		_NoiseV_Speed( "NoiseV_Speed", Float ) = 0
		_NoiseU_Intensity( "NoiseU_Intensity", Float ) = 0
		_NoiseV_Intensity( "NoiseV_Intensity", Float ) = 0
		[Header(MaskCustom1ZW)] _MaskTex01( "MaskTex01", 2D ) = "white" {}
		[KeywordEnum( UV,UClamp,VClamp,UVClamp )] _Mask01_UVClamp( "Mask01_UVClamp", Float ) = 0
		_Mask1Intensity( "Mask1Intensity", Float ) = 1
		_MaskTex02( "MaskTex02", 2D ) = "white" {}
		_Mask2Intensity( "Mask2Intensity", Float ) = 1
		[Header(XY_Mask1_ZW_Mask2)] _MaskSpeed( "MaskSpeed", Vector ) = ( 0, 0, 0, 0 )
		[Enum(ON,0,OFF,1)] _DissolveAffectedByNoise( "DissolveAffectedByNoise", Float ) = 0
		[Header(DissolveCustom2W)] _DissolveTex( "DissolveTex", 2D ) = "white" {}
		[Header(DissolveCustom2W)] _DissolveMask( "DissolveMask", 2D ) = "white" {}
		_DissolveU_Speed( "DissolveU_Speed", Float ) = 0
		_DissolveV_Speed( "DissolveV_Speed", Float ) = 0
		_DissolveProcess( "DissolveProcess", Range( -0.1, 1 ) ) = 0
		_DissolveHardness( "DissolveHardness", Range( 0, 1 ) ) = 0
		[HDR] _DissolveOutlineColor( "DissolveOutlineColor", Color ) = ( 1, 1, 1, 1 )
		_DissolveOutlineWidth( "DissolveOutlineWidth", Range( 0, 1 ) ) = 0.1

	}

	SubShader
	{
		

		Tags { "RenderType"="Opaque" "Queue"="Transparent" }

	LOD 0

		

		Blend SrcAlpha [_Dst]
		AlphaToMask Off
		Cull [_CullMode2]
		ColorMask RGBA
		ZWrite [_Zwrite]
		ZTest LEqual
		Offset 0 , 0
		

		CGINCLUDE
			#pragma target 3.5

			float4 ComputeClipSpacePosition( float2 screenPosNorm, float deviceDepth )
			{
				float4 positionCS = float4( screenPosNorm * 2.0 - 1.0, deviceDepth, 1.0 );
			#if UNITY_UV_STARTS_AT_TOP
				positionCS.y = -positionCS.y;
			#endif
				return positionCS;
			}
		ENDCG

		
		Pass
		{
			Name "Unlit"

			CGPROGRAM
				#define ASE_VERSION 19905

				#ifndef UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX
					#define UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input)
				#endif
				#pragma vertex vert
				#pragma fragment frag
				#pragma multi_compile_instancing
				#include "UnityCG.cginc"

				#include "UnityShaderVariables.cginc"
				#define ASE_NEEDS_TEXTURE_COORDINATES0
				#define ASE_NEEDS_FRAG_TEXTURE_COORDINATES0
				#define ASE_NEEDS_TEXTURE_COORDINATES2
				#define ASE_NEEDS_TEXTURE_COORDINATES1
				#define ASE_NEEDS_FRAG_SCREEN_POSITION_NORMALIZED
				#define ASE_NEEDS_FRAG_COLOR
				#pragma shader_feature_local _RASWITCH_R _RASWITCH_A
				#pragma shader_feature_local _MAINTEX_UVCLAMP_UV _MAINTEX_UVCLAMP_UCLAMP _MAINTEX_UVCLAMP_VCLAMP _MAINTEX_UVCLAMP_UVCLAMP
				#pragma shader_feature_local _MASK01_UVCLAMP_UV _MASK01_UVCLAMP_UCLAMP _MASK01_UVCLAMP_VCLAMP _MASK01_UVCLAMP_UVCLAMP


				struct appdata
				{
					float4 vertex : POSITION;
					float4 ase_texcoord : TEXCOORD0;
					float4 ase_texcoord2 : TEXCOORD2;
					float3 ase_normal : NORMAL;
					float4 ase_texcoord1 : TEXCOORD1;
					float4 ase_color : COLOR;
					UNITY_VERTEX_INPUT_INSTANCE_ID
				};

				struct v2f
				{
					float4 pos : SV_POSITION;
					float4 ase_texcoord : TEXCOORD0;
					float4 ase_texcoord1 : TEXCOORD1;
					float4 ase_texcoord2 : TEXCOORD2;
					float4 ase_texcoord3 : TEXCOORD3;
					float4 ase_texcoord4 : TEXCOORD4;
					float4 ase_color : COLOR;
					UNITY_VERTEX_INPUT_INSTANCE_ID
					UNITY_VERTEX_OUTPUT_STEREO
				};

				uniform int _Dst;
				uniform int _Zwrite;
				uniform int _CullMode2;
				uniform float _DissolveHardness;
				uniform float _DissolveOutlineWidth;
				uniform sampler2D _DissolveMask;
				uniform float4 _DissolveMask_ST;
				uniform sampler2D _DissolveTex;
				uniform float _DissolveU_Speed;
				uniform float _DissolveV_Speed;
				uniform float4 _DissolveTex_ST;
				uniform float _NoiseU_Intensity;
				uniform float _Custom1WT;
				uniform sampler2D _NoiseTex;
				uniform float _NoiseU_Speed;
				uniform float _NoiseV_Speed;
				uniform float4 _NoiseTex_ST;
				uniform float _Radial;
				uniform float _Length;
				uniform float _NoisePolarCoordinates;
				uniform sampler2D _NoiseMask;
				uniform float4 _NoiseMask_ST;
				uniform float _NoiseV_Intensity;
				uniform float _DissolveAffectedByNoise;
				uniform float _DissolveProcess;
				uniform float4 _DissolveOutlineColor;
				uniform float _FresnelSwitch2;
				uniform float _FresnelReverse;
				uniform float _FresnelBias;
				uniform float _FresnelScale;
				uniform float _FresnelPower;
				uniform float4 _FresnelColor;
				uniform float4 _Color;
				uniform float4 _ColorBack;
				uniform sampler2D _MainTex;
				uniform float _MainU_Speed;
				uniform float _MainV_Speed;
				uniform float4 _MainTex_ST;
				uniform float _Custom1UV;
				uniform float _Angle;
				uniform sampler2D _MaskTex01;
				uniform float4 _MaskSpeed;
				uniform float4 _MaskTex01_ST;
				uniform float _Mask1Intensity;
				uniform sampler2D _MaskTex02;
				uniform float4 _MaskTex02_ST;
				uniform float _Mask2Intensity;
				UNITY_DECLARE_DEPTH_TEXTURE( _CameraDepthTexture );
				uniform float4 _CameraDepthTexture_TexelSize;
				uniform float _DepthFadeDistance;
				uniform float _DepthFadeIndensity;
				uniform float _ColorIntensity;
				uniform sampler2D _GradientTex;
				uniform float4 _GradientTex_ST;
				uniform sampler2D _RampTex;


				
				v2f vert ( appdata v )
				{
					v2f o;
					UNITY_SETUP_INSTANCE_ID( v );
					UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO( o );
					UNITY_TRANSFER_INSTANCE_ID( v, o );

					float3 ase_positionWS = mul( unity_ObjectToWorld, float4( ( v.vertex ).xyz, 1 ) ).xyz;
					o.ase_texcoord2.xyz = ase_positionWS;
					float3 ase_normalWS = UnityObjectToWorldNormal( v.ase_normal );
					o.ase_texcoord3.xyz = ase_normalWS;
					
					o.ase_texcoord.xy = v.ase_texcoord.xy;
					o.ase_texcoord1 = v.ase_texcoord2;
					o.ase_texcoord4 = v.ase_texcoord1;
					o.ase_color = v.ase_color;
					
					//setting value to unused interpolator channels and avoid initialization warnings
					o.ase_texcoord.zw = 0;
					o.ase_texcoord2.w = 0;
					o.ase_texcoord3.w = 0;

					float3 vertexValue = float3( 0, 0, 0 );
					#if ASE_ABSOLUTE_VERTEX_POS
						vertexValue = v.vertex.xyz;
					#endif
					vertexValue = vertexValue;
					#if ASE_ABSOLUTE_VERTEX_POS
						v.vertex.xyz = vertexValue;
					#else
						v.vertex.xyz += vertexValue;
					#endif

					o.pos = UnityObjectToClipPos( v.vertex );
					return o;
				}

				half4 frag( v2f IN , uint ase_vface : SV_IsFrontFace ) : SV_Target
				{
					UNITY_SETUP_INSTANCE_ID( IN );
					UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX( IN );
					half4 finalColor;

					float4 ScreenPosNorm = float4( IN.pos.xy * ( _ScreenParams.zw - 1.0 ), IN.pos.zw );
					float4 ClipPos = ComputeClipSpacePosition( ScreenPosNorm.xy, IN.pos.z ) * IN.pos.w;
					float4 ScreenPos = ComputeScreenPos( ClipPos );

					float clampResult219 = clamp( _DissolveHardness , 0.0 , 0.5 );
					float2 uv_DissolveMask = IN.ase_texcoord.xy * _DissolveMask_ST.xy + _DissolveMask_ST.zw;
					float2 appendResult144 = (float2(_DissolveU_Speed , _DissolveV_Speed));
					float2 uv_DissolveTex = IN.ase_texcoord.xy * _DissolveTex_ST.xy + _DissolveTex_ST.zw;
					float2 panner141 = ( 1.0 * _Time.y * appendResult144 + uv_DissolveTex);
					float4 texCoord146 = IN.ase_texcoord1;
					texCoord146.xy = IN.ase_texcoord1.xy * float2( 1,1 ) + float2( 0,0 );
					float NoiseCustom2U263 = texCoord146.x;
					float lerpResult312 = lerp( _NoiseU_Intensity , ( _NoiseU_Intensity + NoiseCustom2U263 ) , _Custom1WT);
					float2 appendResult107 = (float2(_NoiseU_Speed , _NoiseV_Speed));
					float2 uv_NoiseTex = IN.ase_texcoord.xy * _NoiseTex_ST.xy + _NoiseTex_ST.zw;
					float2 temp_output_34_0_g19 = ( uv_NoiseTex - float2( 0.5,0.5 ) );
					float2 break39_g19 = temp_output_34_0_g19;
					float2 appendResult50_g19 = (float2(( _Radial * ( length( temp_output_34_0_g19 ) * 2.0 ) ) , ( ( atan2( break39_g19.x , break39_g19.y ) * ( 1.0 / 6.28318548202515 ) ) * _Length )));
					float2 lerpResult348 = lerp( uv_NoiseTex , appendResult50_g19 , _NoisePolarCoordinates);
					float2 panner96 = ( 1.0 * _Time.y * appendResult107 + lerpResult348);
					float4 tex2DNode100 = tex2D( _NoiseTex, panner96 );
					float2 uv_NoiseMask = IN.ase_texcoord.xy * _NoiseMask_ST.xy + _NoiseMask_ST.zw;
					float4 tex2DNode116 = tex2D( _NoiseMask, uv_NoiseMask );
					float NoiseU359 = ( lerpResult312 * tex2DNode100.r * tex2DNode116.r );
					float2 break365 = panner141;
					float NoiseCustom2V265 = texCoord146.y;
					float lerpResult317 = lerp( _NoiseV_Intensity , ( _NoiseV_Intensity + NoiseCustom2V265 ) , _Custom1WT);
					float NoiseV360 = ( lerpResult317 * tex2DNode100.r * tex2DNode116.r );
					float2 appendResult366 = (float2(( NoiseU359 + break365.x ) , ( break365.y + NoiseV360 )));
					float2 lerpResult369 = lerp( panner141 , appendResult366 , _DissolveAffectedByNoise);
					float3 desaturateInitialColor178 = tex2D( _DissolveTex, lerpResult369 ).rgb;
					float desaturateDot178 = dot( desaturateInitialColor178, float3( 0.299, 0.587, 0.114 ));
					float3 desaturateVar178 = lerp( desaturateInitialColor178, desaturateDot178.xxx, 0.0 );
					float DissolveCustom2W151 = texCoord146.z;
					float clampResult187 = clamp( ( (  (0.0 + ( ( ( 1.0 - tex2D( _DissolveMask, uv_DissolveMask ).r ) + (desaturateVar178).x ) - 0.0 ) * ( 1.0 - 0.0 ) / ( 3.0 - 0.0 ) ) + 1.0 ) - ( ( DissolveCustom2W151 + _DissolveProcess ) * 2.0 ) ) , 0.0 , 1.0 );
					float smoothstepResult214 = smoothstep( clampResult219 , ( 1.0 - clampResult219 ) , ( _DissolveOutlineWidth + clampResult187 ));
					float clampResult221 = clamp( _DissolveHardness , 0.0 , 0.5 );
					float smoothstepResult189 = smoothstep( clampResult221 , ( 1.0 - clampResult221 ) , clampResult187);
					float DissolveLine160 = ( smoothstepResult214 - smoothstepResult189 );
					float3 ase_positionWS = IN.ase_texcoord2.xyz;
					float3 ase_viewVectorWS = ( _WorldSpaceCameraPos.xyz - ase_positionWS );
					float3 ase_viewDirWS = normalize( ase_viewVectorWS );
					float3 ase_normalWS = IN.ase_texcoord3.xyz;
					float3 normalizedWorldNormal = normalize( ase_normalWS );
					float fresnelNdotV253 = dot( normalizedWorldNormal, ase_viewDirWS );
					float fresnelNode253 = ( _FresnelBias + _FresnelScale * pow( 1.0 - fresnelNdotV253, _FresnelPower ) );
					float Fresnel_RGB235 = (( _FresnelSwitch2 )?( (( _FresnelReverse )?( ( 1.0 - fresnelNode253 ) ):( fresnelNode253 )) ):( 1.0 ));
					float3 switchResult331 = (((ase_vface>0)?(_Color.rgb):(_ColorBack.rgb)));
					float2 appendResult4 = (float2(_MainU_Speed , _MainV_Speed));
					float2 uv_MainTex = IN.ase_texcoord.xy * _MainTex_ST.xy + _MainTex_ST.zw;
					float4 texCoord50 = IN.ase_texcoord4;
					texCoord50.xy = IN.ase_texcoord4.xy * float2( 1,1 ) + float2( 0,0 );
					float2 appendResult52 = (float2(texCoord50.x , texCoord50.y));
					float2 MainCustom1UV54 = appendResult52;
					float2 lerpResult308 = lerp( uv_MainTex , ( uv_MainTex + MainCustom1UV54 ) , _Custom1UV);
					float2 panner32 = ( 1.0 * _Time.y * appendResult4 + lerpResult308);
					float2 break112 = panner32;
					float2 appendResult115 = (float2(( NoiseU359 + break112.x ) , ( break112.y + NoiseV360 )));
					float2 temp_output_9_0_g24 = appendResult115;
					float2 break12_g24 = temp_output_9_0_g24;
					float2 appendResult13_g24 = (float2(saturate( break12_g24.x ) , break12_g24.y));
					float2 break3_g24 = temp_output_9_0_g24;
					float2 appendResult1_g24 = (float2(break3_g24.x , saturate( break3_g24.y )));
					#if defined( _MAINTEX_UVCLAMP_UV )
					float2 staticSwitch80 = temp_output_9_0_g24;
					#elif defined( _MAINTEX_UVCLAMP_UCLAMP )
					float2 staticSwitch80 = appendResult13_g24;
					#elif defined( _MAINTEX_UVCLAMP_VCLAMP )
					float2 staticSwitch80 = appendResult1_g24;
					#elif defined( _MAINTEX_UVCLAMP_UVCLAMP )
					float2 staticSwitch80 = saturate( temp_output_9_0_g24 );
					#else
					float2 staticSwitch80 = temp_output_9_0_g24;
					#endif
					float cos386 = cos( radians( _Angle ) );
					float sin386 = sin( radians( _Angle ) );
					float2 rotator386 = mul( staticSwitch80 - float2( 0.5,0.5 ) , float2x2( cos386 , -sin386 , sin386 , cos386 )) + float2( 0.5,0.5 );
					float4 tex2DNode1 = tex2D( _MainTex, rotator386 );
					#if defined( _RASWITCH_R )
					float staticSwitch409 = tex2DNode1.r;
					#elif defined( _RASWITCH_A )
					float staticSwitch409 = tex2DNode1.a;
					#else
					float staticSwitch409 = tex2DNode1.a;
					#endif
					float MainTex_A89 = staticSwitch409;
					float2 appendResult27 = (float2(_MaskSpeed.x , _MaskSpeed.y));
					float2 appendResult51 = (float2(texCoord50.z , texCoord50.w));
					float2 MaskCustom1WT53 = appendResult51;
					float2 uv_MaskTex01 = IN.ase_texcoord.xy * _MaskTex01_ST.xy + _MaskTex01_ST.zw;
					float2 panner33 = ( 1.0 * _Time.y * appendResult27 + ( MaskCustom1WT53 + uv_MaskTex01 ));
					float2 temp_output_9_0_g23 = panner33;
					float2 break12_g23 = temp_output_9_0_g23;
					float2 appendResult13_g23 = (float2(saturate( break12_g23.x ) , break12_g23.y));
					float2 break3_g23 = temp_output_9_0_g23;
					float2 appendResult1_g23 = (float2(break3_g23.x , saturate( break3_g23.y )));
					#if defined( _MASK01_UVCLAMP_UV )
					float2 staticSwitch75 = temp_output_9_0_g23;
					#elif defined( _MASK01_UVCLAMP_UCLAMP )
					float2 staticSwitch75 = appendResult13_g23;
					#elif defined( _MASK01_UVCLAMP_VCLAMP )
					float2 staticSwitch75 = appendResult1_g23;
					#elif defined( _MASK01_UVCLAMP_UVCLAMP )
					float2 staticSwitch75 = saturate( temp_output_9_0_g23 );
					#else
					float2 staticSwitch75 = temp_output_9_0_g23;
					#endif
					float3 desaturateInitialColor111 = tex2D( _MaskTex01, staticSwitch75 ).rgb;
					float desaturateDot111 = dot( desaturateInitialColor111, float3( 0.299, 0.587, 0.114 ));
					float3 desaturateVar111 = lerp( desaturateInitialColor111, desaturateDot111.xxx, 1.0 );
					float Mask0184 = ( (desaturateVar111).x * _Mask1Intensity );
					float2 appendResult34 = (float2(_MaskSpeed.z , _MaskSpeed.w));
					float2 uv_MaskTex02 = IN.ase_texcoord.xy * _MaskTex02_ST.xy + _MaskTex02_ST.zw;
					float2 panner36 = ( 1.0 * _Time.y * appendResult34 + uv_MaskTex02);
					float3 desaturateInitialColor108 = tex2D( _MaskTex02, panner36 ).rgb;
					float desaturateDot108 = dot( desaturateInitialColor108, float3( 0.299, 0.587, 0.114 ));
					float3 desaturateVar108 = lerp( desaturateInitialColor108, desaturateDot108.xxx, 1.0 );
					float Mask0285 = ( (desaturateVar108).x * _Mask2Intensity );
					float Dissolve_Part201 = smoothstepResult214;
					float screenDepth171 = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE( _CameraDepthTexture, ScreenPosNorm.xy ));
					float distanceDepth171 = abs( ( screenDepth171 - LinearEyeDepth( ScreenPosNorm.z ) ) / ( _DepthFadeDistance ) );
					float DepthFade281 = pow( saturate( distanceDepth171 ) , _DepthFadeIndensity );
					float temp_output_241_0 = saturate( ( ( MainTex_A89 * Mask0184 * Mask0285 * Dissolve_Part201 * Fresnel_RGB235 ) * DepthFade281 ) );
					float3 lerpResult267 = lerp( ( Fresnel_RGB235 * _FresnelColor.rgb ) , switchResult331 , temp_output_241_0);
					float4 MainTex_RGB82 = tex2DNode1;
					float2 uv_GradientTex = IN.ase_texcoord.xy * _GradientTex_ST.xy + _GradientTex_ST.zw;
					float4 GradientTex407 = tex2D( _GradientTex, uv_GradientTex );
					float Alpha397 = saturate( ( IN.ase_color.a * _Color.a * _ColorBack.a * temp_output_241_0 ) );
					float2 temp_cast_6 = (Alpha397).xx;
					float4 Color402 = ( ( float4( ( DissolveLine160 * _DissolveOutlineColor.rgb ) , 0.0 ) + ( float4( lerpResult267 , 0.0 ) * MainTex_RGB82 * _ColorIntensity * float4( switchResult331 , 0.0 ) * IN.ase_color * GradientTex407 ) ) * float4( tex2D( _RampTex, temp_cast_6 ).rgb , 0.0 ) );
					float4 appendResult415 = (float4(Color402.rgb , Alpha397));
					

					finalColor = appendResult415;

					return finalColor;
				}
			ENDCG
		}
	}
	CustomEditor "AmplifyShaderEditor.MaterialInspector"
	
	Fallback Off
}
/*ASEBEGIN
Version=19905
Node;AmplifyShaderEditor.CommentaryNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;145;-5840,-1712;Inherit;False;671.3208;292.2915;MaintexUV_MaskWT;4;146;151;263;265;CustomVertexNoiseDissolve;0,0.9783893,1,1;0;0
Node;AmplifyShaderEditor.CommentaryNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;93;-4864,-1072;Inherit;False;2139.971;881.916;;26;103;104;116;100;317;312;313;318;262;261;96;266;264;98;99;107;106;105;336;337;348;333;334;94;359;360;Noise;0.2622641,0.8558096,1,1;0;0
Node;AmplifyShaderEditor.TextureCoordinatesNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;146;-5808,-1648;Inherit;False;2;-1;4;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;333;-4832,-592;Inherit;False;Property;_Radial;Radial;25;0;Create;True;0;0;0;False;0;False;1;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;334;-4832,-528;Inherit;False;Property;_Length;Length;26;0;Create;True;0;0;0;False;0;False;1;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.TextureCoordinatesNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;94;-4848,-832;Inherit;False;0;100;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;263;-5504,-1664;Inherit;False;NoiseCustom2U;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;265;-5504,-1584;Inherit;False;NoiseCustom2V;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;105;-4384,-544;Inherit;False;Property;_NoiseU_Speed;NoiseU_Speed;29;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;106;-4384,-464;Inherit;False;Property;_NoiseV_Speed;NoiseV_Speed;30;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.FunctionNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;337;-4672,-672;Inherit;False;Polar Coordinates;-1;;19;7dab8e02884cf104ebefaa2e788e4162;0;4;1;FLOAT2;0,0;False;2;FLOAT2;0.5,0.5;False;3;FLOAT;1;False;4;FLOAT;1;False;3;FLOAT2;0;FLOAT;55;FLOAT;56
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;336;-4752,-448;Inherit;False;Property;_NoisePolarCoordinates;NoisePolarCoordinates;24;1;[Enum];Create;True;1;UV_PolarCoordinates;2;Normal;0;PolarCoordinates;1;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.DynamicAppendNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;107;-4208,-512;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;264;-4336,-944;Inherit;False;263;NoiseCustom2U;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;266;-4336,-752;Inherit;False;265;NoiseCustom2V;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.LerpOp, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;348;-4464,-672;Inherit;False;3;0;FLOAT2;0,0;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.CommentaryNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;77;-5856,-1344;Inherit;False;671.3208;292.2915;MaintexUV_MaskWT;5;50;51;53;52;54;CustomVertexMainMask;0,0.9783893,1,1;0;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;99;-4336,-864;Inherit;False;Property;_NoiseV_Intensity;NoiseV_Intensity;32;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;98;-4352,-1040;Inherit;False;Property;_NoiseU_Intensity;NoiseU_Intensity;31;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.PannerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;96;-4112,-688;Inherit;False;3;0;FLOAT2;0,0;False;2;FLOAT2;0,0;False;1;FLOAT;1;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleAddOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;261;-4048,-1024;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;262;-4048,-816;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.TextureCoordinatesNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;318;-4144,-384;Inherit;False;0;116;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;313;-4096,-896;Inherit;False;Property;_Custom1WT;Custom1WT;23;2;[Header];[Toggle];Create;True;1;NoiseTexCustom;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.TextureCoordinatesNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;50;-5808,-1280;Inherit;False;1;-1;4;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.LerpOp, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;312;-3808,-976;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.LerpOp, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;317;-3808,-816;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;100;-3920,-688;Inherit;True;Property;_NoiseTex;NoiseTex;27;1;[Header];Create;True;1;NoiseCustom2XY;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;False;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.SamplerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;116;-3920,-496;Inherit;True;Property;_NoiseMask;NoiseMask;28;0;Create;True;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;False;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.DynamicAppendNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;52;-5568,-1280;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;103;-3568,-880;Inherit;False;3;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;104;-3552,-672;Inherit;False;3;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.CommentaryNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;78;-4848,-1792;Inherit;False;3043.978;633.7066;;25;82;89;1;115;114;113;112;361;362;80;79;32;4;308;38;39;309;310;311;6;55;386;388;387;409;MainTex;1,0,0,1;0;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;54;-5424,-1296;Inherit;False;MainCustom1UV;-1;True;1;0;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.CommentaryNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;192;-6464,-208;Inherit;False;751.5742;379.1072;DissolvePanner;5;142;143;144;140;141;;0,1,0.01979423,1;0;0
Node;AmplifyShaderEditor.CommentaryNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;371;-5664,-224;Inherit;False;772;450.95;NoiseAffectsMask;8;365;364;368;367;363;366;369;370;;0,1,0.02352941,1;0;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;359;-3360,-880;Inherit;False;NoiseU;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;360;-3360,-656;Inherit;False;NoiseV;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;55;-4832,-1616;Inherit;False;54;MainCustom1UV;1;0;OBJECT;;False;1;FLOAT2;0
Node;AmplifyShaderEditor.TextureCoordinatesNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;6;-4832,-1744;Inherit;False;0;1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;142;-6432,-16;Inherit;False;Property;_DissolveU_Speed;DissolveU_Speed;42;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;143;-6432,64;Inherit;False;Property;_DissolveV_Speed;DissolveV_Speed;43;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.BreakToComponentsNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;365;-5584,-96;Inherit;False;FLOAT2;1;0;FLOAT2;0,0;False;16;FLOAT;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT;5;FLOAT;6;FLOAT;7;FLOAT;8;FLOAT;9;FLOAT;10;FLOAT;11;FLOAT;12;FLOAT;13;FLOAT;14;FLOAT;15
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;364;-5600,16;Inherit;False;360;NoiseV;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;363;-5616,-176;Inherit;False;359;NoiseU;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.DynamicAppendNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;51;-5568,-1184;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.WireNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;311;-4480,-1680;Inherit;False;1;0;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleAddOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;310;-4576,-1632;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;309;-4800,-1536;Inherit;False;Property;_Custom1UV;Custom1UV;12;2;[Header];[Toggle];Create;True;1;MainTexCustom;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;39;-4816,-1360;Inherit;False;Property;_MainV_Speed;MainV_Speed;20;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;38;-4832,-1440;Inherit;False;Property;_MainU_Speed;MainU_Speed;19;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.DynamicAppendNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;144;-6208,16;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.TextureCoordinatesNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;140;-6368,-160;Inherit;False;0;190;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleAddOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;368;-5408,-48;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;367;-5408,-160;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.CommentaryNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;81;-4864,672;Inherit;False;2524.222;832.8414;;23;46;84;49;111;110;108;109;37;85;48;47;23;75;36;74;35;34;61;33;56;27;24;25;MaskTex;0,0.2235241,1,1;0;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;53;-5424,-1184;Inherit;False;MaskCustom1WT;-1;True;1;0;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.LerpOp, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;308;-4432,-1616;Inherit;True;3;0;FLOAT2;0,0;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.DynamicAppendNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;4;-4640,-1440;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.PannerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;141;-6000,-112;Inherit;False;3;0;FLOAT2;0,0;False;2;FLOAT2;0,0;False;1;FLOAT;1;False;1;FLOAT2;0
Node;AmplifyShaderEditor.DynamicAppendNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;366;-5264,-112;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;370;-5360,112;Inherit;False;Property;_DissolveAffectedByNoise;DissolveAffectedByNoise;39;1;[Enum];Create;True;1;NoiseAffectsDissolve;2;ON;0;OFF;1;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.Vector4Node, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;25;-4800,1040;Inherit;False;Property;_MaskSpeed;MaskSpeed;38;0;Create;True;0;0;0;False;1;Header(XY_Mask1_ZW_Mask2);False;0,0,0,0;0,0,0,0;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;56;-4592,720;Inherit;False;53;MaskCustom1WT;1;0;OBJECT;;False;1;FLOAT2;0
Node;AmplifyShaderEditor.TextureCoordinatesNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;24;-4608,816;Inherit;False;0;23;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.PannerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;32;-4192,-1552;Inherit;False;3;0;FLOAT2;0,0;False;2;FLOAT2;0,0;False;1;FLOAT;1;False;1;FLOAT2;0
Node;AmplifyShaderEditor.CommentaryNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;176;-4880,-144;Inherit;False;3034.371;817.4512;;30;381;378;215;160;189;224;222;221;201;214;213;223;220;154;219;187;208;186;185;182;181;216;191;202;379;184;178;190;372;382;Dissolve;0,1,0.02352941,1;0;0
Node;AmplifyShaderEditor.LerpOp, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;369;-5072,0;Inherit;False;3;0;FLOAT2;0,0;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.DynamicAppendNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;27;-4496,976;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleAddOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;61;-4336,800;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;362;-3984,-1360;Inherit;False;360;NoiseV;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;361;-4000,-1632;Inherit;False;359;NoiseU;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.BreakToComponentsNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;112;-3952,-1504;Inherit;False;FLOAT2;1;0;FLOAT2;0,0;False;16;FLOAT;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT;5;FLOAT;6;FLOAT;7;FLOAT;8;FLOAT;9;FLOAT;10;FLOAT;11;FLOAT;12;FLOAT;13;FLOAT;14;FLOAT;15
Node;AmplifyShaderEditor.SamplerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;190;-4816,128;Inherit;True;Property;_DissolveTex;DissolveTex;40;1;[Header];Create;True;1;DissolveCustom2W;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;False;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.TextureCoordinatesNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;382;-4832,-96;Inherit;False;0;372;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.PannerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;33;-4160,896;Inherit;False;3;0;FLOAT2;0,0;False;2;FLOAT2;0,0;False;1;FLOAT;1;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleAddOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;113;-3712,-1600;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;114;-3712,-1456;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.DesaturateOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;178;-4496,144;Inherit;False;2;0;FLOAT3;0,0,0;False;1;FLOAT;0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.SamplerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;372;-4592,-80;Inherit;True;Property;_DissolveMask;DissolveMask;41;1;[Header];Create;True;1;DissolveCustom2W;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;False;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.DynamicAppendNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;34;-4560,1328;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.TextureCoordinatesNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;35;-4576,1168;Inherit;False;0;37;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.FunctionNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;74;-3904,912;Inherit;False;Group_UVClamp;-1;;23;bcf8b0335fee50b4c89373bf7281c788;0;1;9;FLOAT2;0,0;False;4;FLOAT2;23;FLOAT2;22;FLOAT2;15;FLOAT2;21
Node;AmplifyShaderEditor.DynamicAppendNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;115;-3600,-1536;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;151;-5504,-1504;Inherit;False;DissolveCustom2W;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.ComponentMaskNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;184;-4320,144;Inherit;False;True;False;False;True;1;0;FLOAT3;0,0,0;False;1;FLOAT;0
Node;AmplifyShaderEditor.OneMinusNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;379;-4240,16;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.PannerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;36;-4320,1328;Inherit;False;3;0;FLOAT2;0,0;False;2;FLOAT2;0,0;False;1;FLOAT;1;False;1;FLOAT2;0
Node;AmplifyShaderEditor.StaticSwitch, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;75;-3696,912;Inherit;False;Property;_Mask01_UVClamp;Mask01_UVClamp;34;0;Create;True;0;0;0;False;0;False;0;0;0;True;;KeywordEnum;4;UV;UClamp;VClamp;UVClamp;Create;True;True;All;9;1;FLOAT2;0,0;False;0;FLOAT2;0,0;False;2;FLOAT2;0,0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT2;0,0;False;6;FLOAT2;0,0;False;7;FLOAT2;0,0;False;8;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;387;-3200,-1264;Inherit;False;Property;_Angle;Angle;17;0;Create;True;0;0;0;False;0;False;0;0;0;360;0;1;FLOAT;0
Node;AmplifyShaderEditor.FunctionNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;79;-3424,-1568;Inherit;False;Group_UVClamp;-1;;24;bcf8b0335fee50b4c89373bf7281c788;0;1;9;FLOAT2;0,0;False;4;FLOAT2;23;FLOAT2;22;FLOAT2;15;FLOAT2;21
Node;AmplifyShaderEditor.SimpleAddOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;378;-4016,96;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;202;-4224,272;Inherit;False;151;DissolveCustom2W;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;191;-4224,352;Float;False;Property;_DissolveProcess;DissolveProcess;44;0;Create;True;0;0;0;False;0;False;0;0;-0.1;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.CommentaryNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;169;-2288,1072;Inherit;False;1178.854;322.6044;;7;270;281;272;269;174;171;170;DepthFade;1,0,0.7455859,1;0;0
Node;AmplifyShaderEditor.SamplerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;37;-3856,1168;Inherit;True;Property;_MaskTex02;MaskTex02;36;0;Create;True;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;False;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.SamplerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;23;-3456,864;Inherit;True;Property;_MaskTex01;MaskTex01;33;1;[Header];Create;True;1;MaskCustom1ZW;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;False;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.RadiansOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;388;-2928,-1264;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.StaticSwitch, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;80;-3216,-1568;Inherit;True;Property;_MainTex_UVClamp;MainTex_UVClamp;18;0;Create;True;0;0;0;False;0;False;0;0;0;True;;KeywordEnum;4;UV;UClamp;VClamp;UVClamp;Create;True;True;All;9;1;FLOAT2;0,0;False;0;FLOAT2;0,0;False;2;FLOAT2;0,0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT2;0,0;False;6;FLOAT2;0,0;False;7;FLOAT2;0,0;False;8;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.CommentaryNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;225;-4864,-2288;Inherit;False;2224.879;447.0945;Fresnel;10;238;236;235;229;227;226;239;253;255;324;Fresnel;1,0.9166667,0.5,1;0;0
Node;AmplifyShaderEditor.TFHCRemapNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;381;-3888,80;Inherit;False;5;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;3;False;3;FLOAT;0;False;4;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;216;-3888,304;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;181;-3920,416;Float;False;Constant;_Float2;Float 2;33;0;Create;True;0;0;0;False;0;False;2;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.DesaturateOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;108;-3552,1184;Inherit;False;2;0;FLOAT3;0,0,0;False;1;FLOAT;1;False;1;FLOAT3;0
Node;AmplifyShaderEditor.DesaturateOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;111;-3184,832;Inherit;False;2;0;FLOAT3;0,0,0;False;1;FLOAT;1;False;1;FLOAT3;0
Node;AmplifyShaderEditor.RotatorNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;386;-2816,-1568;Inherit;True;3;0;FLOAT2;0,0;False;1;FLOAT2;0.5,0.5;False;2;FLOAT;1;False;1;FLOAT2;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;170;-2272,1168;Float;False;Property;_DepthFadeDistance;DepthFadeDistance;5;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.ViewDirInputsCoordNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;226;-4816,-2080;Inherit;False;World;False;0;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.WorldNormalVector, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;227;-4816,-2240;Inherit;False;True;1;0;FLOAT3;0,0,1;False;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;238;-4624,-1936;Inherit;False;Property;_FresnelPower;FresnelPower;10;0;Create;True;0;0;0;False;0;False;2;2;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;236;-4640,-2048;Inherit;False;Property;_FresnelScale;FresnelScale;9;0;Create;True;0;0;0;False;0;False;0.96;0.96;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;255;-4640,-2128;Inherit;False;Property;_FresnelBias;FresnelBias;8;0;Create;True;0;0;0;False;0;False;1;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;185;-3632,96;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;182;-3760,304;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.ComponentMaskNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;110;-2992,832;Inherit;False;True;False;False;True;1;0;FLOAT3;0,0,0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;46;-2976,928;Inherit;False;Property;_Mask1Intensity;Mask1Intensity;35;0;Create;True;0;0;0;False;0;False;1;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;47;-3248,1344;Inherit;False;Property;_Mask2Intensity;Mask2Intensity;37;0;Create;True;0;0;0;False;0;False;1;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.ComponentMaskNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;109;-3360,1184;Inherit;False;True;False;False;True;1;0;FLOAT3;0,0,0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;1;-2544,-1568;Inherit;True;Property;_MainTex;MainTex;16;1;[Header];Create;True;1;Main_Custom1XY;0;0;False;0;False;-1;None;93614f31e7ecf2248809d0c812c6bdb6;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;False;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.DepthFade, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;171;-2048,1152;Inherit;False;True;False;True;2;1;FLOAT3;0,0,0;False;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.FresnelNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;253;-4368,-2240;Inherit;True;Standard;WorldNormal;ViewDir;False;False;5;0;FLOAT3;0,0,1;False;4;FLOAT3;0,0,0;False;1;FLOAT;0;False;2;FLOAT;1.17;False;3;FLOAT;0.44;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;186;-3536,240;Float;False;Property;_DissolveHardness;DissolveHardness;45;0;Create;True;0;0;0;False;0;False;0;0;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleSubtractOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;208;-3520,96;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.OneMinusNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;229;-4048,-2192;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;48;-3024,1184;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;49;-2768,816;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.StaticSwitch, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;409;-2240,-1472;Inherit;False;Property;_RASwitch;RASwitch;3;0;Create;True;0;0;0;False;0;False;0;1;1;True;;KeywordEnum;2;R;A;Create;True;True;All;9;1;FLOAT;0;False;0;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;4;FLOAT;0;False;5;FLOAT;0;False;6;FLOAT;0;False;7;FLOAT;0;False;8;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;174;-1664,1152;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;269;-1808,1232;Float;False;Property;_DepthFadeIndensity;DepthFadeIndensity;4;0;Create;True;0;0;0;False;0;False;0.1;0.1;0.1;5;0;1;FLOAT;0
Node;AmplifyShaderEditor.ClampOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;187;-3376,96;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.ClampOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;219;-3184,64;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0.5;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;154;-3600,-32;Float;False;Property;_DissolveOutlineWidth;DissolveOutlineWidth;47;0;Create;True;0;0;0;False;0;False;0.1;0.1;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.ToggleSwitchNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;239;-3888,-2224;Inherit;True;Property;_FresnelReverse;FresnelReverse;7;0;Create;True;0;0;0;False;0;False;0;True;Create;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.CommentaryNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;404;-1154,270;Inherit;False;1620;514.95;Alpha;12;87;86;90;206;240;88;282;349;241;307;397;20;;1,1,1,1;0;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;85;-2832,1184;Inherit;False;Mask02;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;84;-2592,816;Inherit;False;Mask01;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;201;-2288,-96;Inherit;True;Dissolve_Part;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;89;-2016,-1472;Inherit;True;MainTex_A;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.PowerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;272;-1520,1152;Inherit;False;False;2;0;FLOAT;0;False;1;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.ClampOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;221;-3168,240;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0.5;False;1;FLOAT;0
Node;AmplifyShaderEditor.OneMinusNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;220;-3024,80;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.WireNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;223;-3056,48;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;213;-3232,-48;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.ToggleSwitchNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;324;-3600,-2240;Inherit;False;Property;_FresnelSwitch2;FresnelSwitch;6;0;Create;True;0;0;0;False;0;False;0;True;Create;2;0;FLOAT;1;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;87;-1104,480;Inherit;False;85;Mask02;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;86;-1104,400;Inherit;False;84;Mask01;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;90;-1104,320;Inherit;False;89;MainTex_A;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;206;-1104,576;Inherit;False;201;Dissolve_Part;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;240;-1056,672;Inherit;False;235;Fresnel_RGB;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;281;-1360,1152;Inherit;True;DepthFade;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.OneMinusNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;222;-3008,256;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.WireNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;224;-3040,224;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SmoothstepOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;214;-2848,-64;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;235;-3312,-2224;Inherit;False;Fresnel_RGB;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;88;-832,400;Inherit;True;5;5;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;4;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;282;-816,640;Inherit;False;281;DepthFade;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.SmoothstepOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;189;-2832,176;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.TextureCoordinatesNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;406;-1920,-624;Inherit;False;0;405;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;349;-560,416;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.ColorNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;14;-1056,-288;Inherit;False;Property;_Color;Color;13;1;[HDR];Create;True;0;0;0;False;0;False;1,1,1,1;1,1,1,1;True;True;0;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.ColorNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;332;-1040,-80;Inherit;False;Property;_ColorBack;ColorBack;14;1;[HDR];Create;True;0;0;0;False;0;False;1,1,1,1;1,1,1,1;True;True;0;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;20;-160,336;Inherit;False;4;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;259;-896,-752;Inherit;False;235;Fresnel_RGB;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleSubtractOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;215;-2544,0;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.ColorNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;237;-912,-672;Inherit;False;Property;_FresnelColor;FresnelColor;11;1;[HDR];Create;True;0;0;0;False;0;False;0,0,0,0;0,0,0,0;True;True;0;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.SamplerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;405;-1648,-656;Inherit;True;Property;_GradientTex;GradientTex;21;0;Create;True;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;False;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.SaturateNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;241;-400,416;Inherit;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;307;16,368;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.CommentaryNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;399;-546,-1202;Inherit;False;468;362.7;ColorDissolveOutline;3;168;165;164;;1,1,1,1;0;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;234;-624,-752;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;160;-2288,128;Float;True;DissolveLine;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;407;-1300.968,-578.7756;Inherit;False;GradientTex;-1;True;1;0;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.SwitchByFaceNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;331;-672,-256;Inherit;False;2;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;82;-2256,-1600;Inherit;False;MainTex_RGB;-1;True;1;0;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.VertexColorNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;19;-448,-192;Inherit;False;0;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.CommentaryNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;400;256,-1120;Inherit;False;795.47;490.7;ColorRamp;4;396;393;398;412;;1,1,1,1;0;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;397;224,368;Inherit;True;Alpha;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;15;-480,-384;Inherit;False;Property;_ColorIntensity;ColorIntensity;15;0;Create;True;0;0;0;False;0;False;1;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;164;-480,-1152;Inherit;False;160;DissolveLine;1;0;OBJECT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.ColorNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;165;-496,-1072;Float;False;Property;_DissolveOutlineColor;DissolveOutlineColor;46;1;[HDR];Create;True;0;0;0;False;0;False;1,1,1,1;1,1,1,1;True;True;0;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.LerpOp, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;267;-272,-768;Inherit;True;3;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;2;FLOAT;0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;408;-224,-64;Inherit;False;407;GradientTex;1;0;OBJECT;;False;1;COLOR;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;83;-480,-496;Inherit;False;82;MainTex_RGB;1;0;OBJECT;;False;1;COLOR;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;168;-256,-1104;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;13;16,-336;Inherit;True;6;6;0;FLOAT3;0,0,0;False;1;COLOR;0,0,0,0;False;2;FLOAT;0;False;3;FLOAT3;0,0,0;False;4;COLOR;0,0,0,0;False;5;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;398;128,-848;Inherit;True;397;Alpha;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;167;32,-1104;Inherit;True;2;2;0;FLOAT3;0,0,0;False;1;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.SamplerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;393;528,-864;Inherit;True;Property;_RampTex;RampTex;22;0;Create;True;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;False;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;396;816,-1072;Inherit;True;2;2;0;COLOR;0,0,0,0;False;1;FLOAT3;0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;402;1136,-1040;Inherit;False;Color;-1;True;1;0;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.CommentaryNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;289;-2306,1518;Inherit;False;1492.983;717.0376;;11;299;298;297;294;295;287;291;302;293;292;290;VertexOffset;0.3350291,0,1,1;0;0
Node;AmplifyShaderEditor.CommentaryNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;283;1664,-96;Inherit;False;260;322.95;;3;9;207;22;;1,1,1,1;0;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;403;816,-272;Inherit;True;402;Color;1;0;OBJECT;;False;1;COLOR;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;401;736,0;Inherit;True;397;Alpha;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;298;-2256,2080;Inherit;False;Property;_VertexTexV_Speed;VertexTexV_Speed;51;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;297;-2256,2000;Inherit;False;Property;_VertexTexU_Speed;VertexTexU_Speed;50;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.TextureCoordinatesNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;295;-2080,1872;Inherit;False;0;293;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.DynamicAppendNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;299;-2032,2016;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.PannerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;294;-1808,1872;Inherit;False;3;0;FLOAT2;0,0;False;2;FLOAT2;0,0;False;1;FLOAT;1;False;1;FLOAT2;0
Node;AmplifyShaderEditor.NormalVertexDataNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;290;-1568,1584;Inherit;False;0;5;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SamplerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;293;-1584,1840;Inherit;True;Property;_VertexOffsetTex;VertexOffsetTex;49;0;Create;True;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;False;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.VertexColorNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;302;-1392,2048;Inherit;False;0;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;292;-1568,1744;Inherit;False;Property;_VertexOffsetInt;VertexOffsetInt;48;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;291;-1248,1616;Inherit;False;4;4;0;FLOAT3;0,0,0;False;1;FLOAT;0;False;2;COLOR;0,0,0,0;False;3;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;287;-1072,1584;Inherit;False;VertexOffset;-1;True;1;0;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.OneMinusNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;270;-1808,1072;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.IntNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;9;1712,-48;Inherit;False;Property;_Dst;Blend Mode;1;1;[Enum];Create;False;0;2;Additive;1;AlphaBlend;10;0;True;0;False;1;1;False;0;1;INT;0
Node;AmplifyShaderEditor.IntNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;207;1712,112;Inherit;False;Property;_Zwrite;Zwrite;2;1;[Enum];Create;True;0;2;OFF;0;ON;2;0;True;0;False;0;0;False;0;1;INT;0
Node;AmplifyShaderEditor.IntNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;22;1712,32;Inherit;False;Property;_CullMode2;CullMode;0;1;[Enum];Create;False;0;2;OFF;0;ON;2;1;UnityEngine.Rendering.CullMode;True;0;False;0;0;False;0;1;INT;0
Node;AmplifyShaderEditor.TextureCoordinatesNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;411;96,-624;Inherit;False;0;393;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;412;384,-752;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.DynamicAppendNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;415;1088,-160;Inherit;False;FLOAT4;4;0;FLOAT3;0,0,0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;288;944,336;Inherit;False;287;VertexOffset;1;0;OBJECT;;False;1;COLOR;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;414;1296,0;Float;False;True;-1;3;AmplifyShaderEditor.MaterialInspector;0;5;Effect/EffectFunction_BRP;0770190933193b94aaa3065e307002fa;True;Unlit;0;0;Unlit;2;True;True;2;5;False;;10;True;_Dst;0;1;False;;0;False;;True;0;False;;0;False;;False;False;False;False;False;False;False;False;False;True;0;False;;True;True;0;True;_CullMode2;False;True;True;True;True;True;0;False;;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;True;True;1;True;_Zwrite;True;3;False;;True;True;0;False;;0;False;;True;2;RenderType=Opaque=RenderType;Queue=Transparent=Queue=0;True;3;False;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;0;;0;0;Standard;1;Vertex Position;1;0;0;1;True;False;;False;0
Node;AmplifyShaderEditor.CommentaryNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;389;-3474,-1618;Inherit;False;929.3198;466.95;Clamp and angle;0;;1,1,1,1;0;0
WireConnection;263;0;146;1
WireConnection;265;0;146;2
WireConnection;337;1;94;0
WireConnection;337;3;333;0
WireConnection;337;4;334;0
WireConnection;107;0;105;0
WireConnection;107;1;106;0
WireConnection;348;0;94;0
WireConnection;348;1;337;0
WireConnection;348;2;336;0
WireConnection;96;0;348;0
WireConnection;96;2;107;0
WireConnection;261;0;98;0
WireConnection;261;1;264;0
WireConnection;262;0;99;0
WireConnection;262;1;266;0
WireConnection;312;0;98;0
WireConnection;312;1;261;0
WireConnection;312;2;313;0
WireConnection;317;0;99;0
WireConnection;317;1;262;0
WireConnection;317;2;313;0
WireConnection;100;1;96;0
WireConnection;116;1;318;0
WireConnection;52;0;50;1
WireConnection;52;1;50;2
WireConnection;103;0;312;0
WireConnection;103;1;100;1
WireConnection;103;2;116;1
WireConnection;104;0;317;0
WireConnection;104;1;100;1
WireConnection;104;2;116;1
WireConnection;54;0;52;0
WireConnection;359;0;103;0
WireConnection;360;0;104;0
WireConnection;365;0;141;0
WireConnection;51;0;50;3
WireConnection;51;1;50;4
WireConnection;311;0;6;0
WireConnection;310;0;6;0
WireConnection;310;1;55;0
WireConnection;144;0;142;0
WireConnection;144;1;143;0
WireConnection;368;0;365;1
WireConnection;368;1;364;0
WireConnection;367;0;363;0
WireConnection;367;1;365;0
WireConnection;53;0;51;0
WireConnection;308;0;311;0
WireConnection;308;1;310;0
WireConnection;308;2;309;0
WireConnection;4;0;38;0
WireConnection;4;1;39;0
WireConnection;141;0;140;0
WireConnection;141;2;144;0
WireConnection;366;0;367;0
WireConnection;366;1;368;0
WireConnection;32;0;308;0
WireConnection;32;2;4;0
WireConnection;369;0;141;0
WireConnection;369;1;366;0
WireConnection;369;2;370;0
WireConnection;27;0;25;1
WireConnection;27;1;25;2
WireConnection;61;0;56;0
WireConnection;61;1;24;0
WireConnection;112;0;32;0
WireConnection;190;1;369;0
WireConnection;33;0;61;0
WireConnection;33;2;27;0
WireConnection;113;0;361;0
WireConnection;113;1;112;0
WireConnection;114;0;112;1
WireConnection;114;1;362;0
WireConnection;178;0;190;0
WireConnection;372;1;382;0
WireConnection;34;0;25;3
WireConnection;34;1;25;4
WireConnection;74;9;33;0
WireConnection;115;0;113;0
WireConnection;115;1;114;0
WireConnection;151;0;146;3
WireConnection;184;0;178;0
WireConnection;379;0;372;1
WireConnection;36;0;35;0
WireConnection;36;2;34;0
WireConnection;75;1;74;23
WireConnection;75;0;74;22
WireConnection;75;2;74;15
WireConnection;75;3;74;21
WireConnection;79;9;115;0
WireConnection;378;0;379;0
WireConnection;378;1;184;0
WireConnection;37;1;36;0
WireConnection;23;1;75;0
WireConnection;388;0;387;0
WireConnection;80;1;79;23
WireConnection;80;0;79;22
WireConnection;80;2;79;15
WireConnection;80;3;79;21
WireConnection;381;0;378;0
WireConnection;216;0;202;0
WireConnection;216;1;191;0
WireConnection;108;0;37;0
WireConnection;111;0;23;0
WireConnection;386;0;80;0
WireConnection;386;2;388;0
WireConnection;185;0;381;0
WireConnection;182;0;216;0
WireConnection;182;1;181;0
WireConnection;110;0;111;0
WireConnection;109;0;108;0
WireConnection;1;1;386;0
WireConnection;171;0;170;0
WireConnection;253;0;227;0
WireConnection;253;4;226;0
WireConnection;253;1;255;0
WireConnection;253;2;236;0
WireConnection;253;3;238;0
WireConnection;208;0;185;0
WireConnection;208;1;182;0
WireConnection;229;0;253;0
WireConnection;48;0;109;0
WireConnection;48;1;47;0
WireConnection;49;0;110;0
WireConnection;49;1;46;0
WireConnection;409;1;1;1
WireConnection;409;0;1;4
WireConnection;174;0;171;0
WireConnection;187;0;208;0
WireConnection;219;0;186;0
WireConnection;239;0;253;0
WireConnection;239;1;229;0
WireConnection;85;0;48;0
WireConnection;84;0;49;0
WireConnection;201;0;214;0
WireConnection;89;0;409;0
WireConnection;272;0;174;0
WireConnection;272;1;269;0
WireConnection;221;0;186;0
WireConnection;220;0;219;0
WireConnection;223;0;219;0
WireConnection;213;0;154;0
WireConnection;213;1;187;0
WireConnection;324;1;239;0
WireConnection;281;0;272;0
WireConnection;222;0;221;0
WireConnection;224;0;221;0
WireConnection;214;0;213;0
WireConnection;214;1;223;0
WireConnection;214;2;220;0
WireConnection;235;0;324;0
WireConnection;88;0;90;0
WireConnection;88;1;86;0
WireConnection;88;2;87;0
WireConnection;88;3;206;0
WireConnection;88;4;240;0
WireConnection;189;0;187;0
WireConnection;189;1;224;0
WireConnection;189;2;222;0
WireConnection;349;0;88;0
WireConnection;349;1;282;0
WireConnection;20;0;19;4
WireConnection;20;1;14;4
WireConnection;20;2;332;4
WireConnection;20;3;241;0
WireConnection;215;0;214;0
WireConnection;215;1;189;0
WireConnection;405;1;406;0
WireConnection;241;0;349;0
WireConnection;307;0;20;0
WireConnection;234;0;259;0
WireConnection;234;1;237;5
WireConnection;160;0;215;0
WireConnection;407;0;405;0
WireConnection;331;0;14;5
WireConnection;331;1;332;5
WireConnection;82;0;1;0
WireConnection;397;0;307;0
WireConnection;267;0;234;0
WireConnection;267;1;331;0
WireConnection;267;2;241;0
WireConnection;168;0;164;0
WireConnection;168;1;165;5
WireConnection;13;0;267;0
WireConnection;13;1;83;0
WireConnection;13;2;15;0
WireConnection;13;3;331;0
WireConnection;13;4;19;0
WireConnection;13;5;408;0
WireConnection;167;0;168;0
WireConnection;167;1;13;0
WireConnection;393;1;398;0
WireConnection;396;0;167;0
WireConnection;396;1;393;5
WireConnection;402;0;396;0
WireConnection;299;0;297;0
WireConnection;299;1;298;0
WireConnection;294;0;295;0
WireConnection;294;2;299;0
WireConnection;293;1;294;0
WireConnection;291;0;290;0
WireConnection;291;1;292;0
WireConnection;291;2;293;0
WireConnection;291;3;302;0
WireConnection;287;0;291;0
WireConnection;270;0;171;0
WireConnection;412;0;398;0
WireConnection;412;1;411;0
WireConnection;415;0;403;0
WireConnection;415;3;401;0
WireConnection;414;0;415;0
ASEEND*/
//CHKSM=5D2CEFBC6F4A66663E1559300F4057F51ADF8C67