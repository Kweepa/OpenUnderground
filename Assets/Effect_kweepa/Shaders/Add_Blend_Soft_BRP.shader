// Made with Amplify Shader Editor v1.9.8
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "Effect/Add_Blend_Soft_UPR"
{
	Properties
	{
		_DepthFadeDistance("DepthFade Distance", Range( 0 , 10)) = 0
		_ColorIntensity2("ColorIntensity", Float) = 1
		_Color2("Color", Color) = (1,1,1,1)
		[Enum(OFF,0,ON,2)]_Zwrite("Zwrite", Int) = 0
		[Enum(Add,1,AlphaBlend,10)]_Dst("BlendMode", Int) = 1
		[KeywordEnum(UV,UClamp,VClamp,UVClamp)] _Clamp("Clamp", Float) = 0
		[Enum(OFF,0,ON,2)]_CullMode3("CullMode", Int) = 0
		_MainTex("MainTex", 2D) = "white" {}
		_MaskIntensity1("MaskIntensity", Float) = 1
		_Mask("Mask", 2D) = "white" {}
		[Header(XY_MainTex_ZW_Mask)]_Speed1("Speed", Vector) = (0,0,0,0)
		[Toggle][Header(MainCustomData)]_Custom1xy3("Custom1xy", Float) = 0
		[Toggle][Header(MaskCustomData)]_Custom1zw1("Custom1zw", Float) = 0

	}
	
	SubShader
	{
		
		
		Tags { "RenderType"="Opaque" }
	LOD 100

		CGINCLUDE
		#pragma target 3.0
		ENDCG
		Blend SrcAlpha [_Dst]
		AlphaToMask Off
		Cull Back
		ColorMask RGBA
		ZWrite [_Zwrite]
		ZTest LEqual
		Offset 0 , 0
		
		
		
		Pass
		{
			Name "Unlit"

			CGPROGRAM

			#define ASE_VERSION 19800


			#ifndef UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX
			//only defining to not throw compilation error over Unity 5.5
			#define UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input)
			#endif
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing
			#include "UnityCG.cginc"
			#include "UnityShaderVariables.cginc"
			#define ASE_NEEDS_FRAG_COLOR
			#pragma shader_feature_local _CLAMP_UV _CLAMP_UCLAMP _CLAMP_VCLAMP _CLAMP_UVCLAMP


			struct appdata
			{
				float4 vertex : POSITION;
				float4 color : COLOR;
				float4 ase_texcoord : TEXCOORD0;
				float4 ase_texcoord1 : TEXCOORD1;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};
			
			struct v2f
			{
				float4 vertex : SV_POSITION;
				#ifdef ASE_NEEDS_FRAG_WORLD_POSITION
				float3 worldPos : TEXCOORD0;
				#endif
				float4 ase_texcoord1 : TEXCOORD1;
				float4 ase_texcoord2 : TEXCOORD2;
				float4 ase_color : COLOR;
				float4 ase_texcoord3 : TEXCOORD3;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			uniform int _CullMode3;
			uniform int _Dst;
			uniform int _Zwrite;
			uniform sampler2D _MainTex;
			uniform float4 _Speed1;
			uniform float4 _MainTex_ST;
			uniform float _Custom1xy3;
			uniform float4 _Color2;
			uniform float _ColorIntensity2;
			uniform sampler2D _Mask;
			uniform float4 _Mask_ST;
			uniform float _Custom1zw1;
			uniform float _MaskIntensity1;
			UNITY_DECLARE_DEPTH_TEXTURE( _CameraDepthTexture );
			uniform float4 _CameraDepthTexture_TexelSize;
			uniform float _DepthFadeDistance;

			
			v2f vert ( appdata v )
			{
				v2f o;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
				UNITY_TRANSFER_INSTANCE_ID(v, o);

				float4 ase_positionCS = UnityObjectToClipPos(v.vertex);
				float4 screenPos = ComputeScreenPos(ase_positionCS);
				o.ase_texcoord3 = screenPos;
				
				o.ase_texcoord1.xy = v.ase_texcoord.xy;
				o.ase_texcoord2 = v.ase_texcoord1;
				o.ase_color = v.color;
				
				//setting value to unused interpolator channels and avoid initialization warnings
				o.ase_texcoord1.zw = 0;
				float3 vertexValue = float3(0, 0, 0);
				#if ASE_ABSOLUTE_VERTEX_POS
				vertexValue = v.vertex.xyz;
				#endif
				vertexValue = vertexValue;
				#if ASE_ABSOLUTE_VERTEX_POS
				v.vertex.xyz = vertexValue;
				#else
				v.vertex.xyz += vertexValue;
				#endif
				o.vertex = UnityObjectToClipPos(v.vertex);

				#ifdef ASE_NEEDS_FRAG_WORLD_POSITION
				o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
				#endif
				return o;
			}
			
			fixed4 frag (v2f i ) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(i);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
				fixed4 finalColor;
				#ifdef ASE_NEEDS_FRAG_WORLD_POSITION
				float3 WorldPosition = i.worldPos;
				#endif
				float2 appendResult78 = (float2(_Speed1.x , _Speed1.y));
				float2 uv_MainTex = i.ase_texcoord1.xy * _MainTex_ST.xy + _MainTex_ST.zw;
				float2 panner79 = ( 1.0 * _Time.y * appendResult78 + uv_MainTex);
				float4 texCoord34 = i.ase_texcoord2;
				texCoord34.xy = i.ase_texcoord2.xy * float2( 1,1 ) + float2( 0,0 );
				float2 appendResult38 = (float2(texCoord34.x , texCoord34.y));
				float2 Custom1xy42 = appendResult38;
				float2 lerpResult82 = lerp( panner79 , ( Custom1xy42 + uv_MainTex ) , _Custom1xy3);
				float2 temp_output_9_0_g1 = lerpResult82;
				float2 break12_g1 = temp_output_9_0_g1;
				float2 appendResult13_g1 = (float2(saturate( break12_g1.x ) , break12_g1.y));
				float2 break3_g1 = temp_output_9_0_g1;
				float2 appendResult1_g1 = (float2(break3_g1.x , saturate( break3_g1.y )));
				#if defined( _CLAMP_UV )
				float2 staticSwitch84 = temp_output_9_0_g1;
				#elif defined( _CLAMP_UCLAMP )
				float2 staticSwitch84 = appendResult13_g1;
				#elif defined( _CLAMP_VCLAMP )
				float2 staticSwitch84 = appendResult1_g1;
				#elif defined( _CLAMP_UVCLAMP )
				float2 staticSwitch84 = saturate( temp_output_9_0_g1 );
				#else
				float2 staticSwitch84 = temp_output_9_0_g1;
				#endif
				float4 tex2DNode59 = tex2D( _MainTex, staticSwitch84 );
				float2 appendResult41 = (float2(_Speed1.z , _Speed1.w));
				float2 uv_Mask = i.ase_texcoord1.xy * _Mask_ST.xy + _Mask_ST.zw;
				float2 panner47 = ( 1.0 * _Time.y * appendResult41 + uv_Mask);
				float2 appendResult35 = (float2(texCoord34.z , texCoord34.w));
				float2 Custom1zw37 = appendResult35;
				float2 lerpResult50 = lerp( panner47 , ( Custom1zw37 + uv_Mask ) , _Custom1zw1);
				float4 screenPos = i.ase_texcoord3;
				float4 ase_positionSSNorm = screenPos / screenPos.w;
				ase_positionSSNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_positionSSNorm.z : ase_positionSSNorm.z * 0.5 + 0.5;
				float screenDepth99 = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE( _CameraDepthTexture, ase_positionSSNorm.xy ));
				float distanceDepth99 = saturate( abs( ( screenDepth99 - LinearEyeDepth( ase_positionSSNorm.z ) ) / ( _DepthFadeDistance ) ) );
				float4 appendResult63 = (float4(( tex2DNode59 * _Color2 * _ColorIntensity2 * i.ase_color ).rgb , ( tex2DNode59.a * _Color2.a * ( tex2D( _Mask, lerpResult50 ).r * _MaskIntensity1 ) * i.ase_color.a * distanceDepth99 )));
				
				
				finalColor = appendResult63;
				return finalColor;
			}
			ENDCG
		}
	}
	CustomEditor "ASEMaterialInspector"
	
	Fallback Off
}
/*ASEBEGIN
Version=19800
Node;AmplifyShaderEditor.TextureCoordinatesNode;34;-2631.522,-1166.234;Inherit;False;1;-1;4;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.DynamicAppendNode;38;-2388.108,-1173.659;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.DynamicAppendNode;35;-2388.358,-1074.381;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.Vector4Node;77;-1961.32,-892.8873;Inherit;False;Property;_Speed1;Speed;10;0;Create;True;0;0;0;False;1;Header(XY_MainTex_ZW_Mask);False;0,0,0,0;0,0,0,0;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RegisterLocalVarNode;42;-2252.602,-1179.773;Inherit;False;Custom1xy;-1;True;1;0;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;37;-2250.602,-1079.177;Inherit;False;Custom1zw;-1;True;1;0;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.DynamicAppendNode;78;-1625.106,-841.812;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.GetLocalVarNode;85;-1568.847,-1116.917;Inherit;False;42;Custom1xy;1;0;OBJECT;;False;1;FLOAT2;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;86;-1690.903,-1010.221;Inherit;False;0;59;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.DynamicAppendNode;41;-1250.525,-202.0323;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;39;-1323.361,-367.7225;Inherit;False;0;54;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.GetLocalVarNode;40;-1297.814,-482.3723;Inherit;False;37;Custom1zw;1;0;OBJECT;;False;1;FLOAT2;0
Node;AmplifyShaderEditor.PannerNode;79;-1394.763,-1006.641;Inherit;False;3;0;FLOAT2;0,0;False;2;FLOAT2;0,0;False;1;FLOAT;1;False;1;FLOAT2;0
Node;AmplifyShaderEditor.RangedFloatNode;80;-1323.18,-859.2984;Inherit;False;Property;_Custom1xy3;Custom1xy;11;1;[Toggle];Create;True;0;0;0;False;1;Header(MainCustomData);False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;81;-1346.647,-1110.83;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.PannerNode;47;-1055.82,-281.0207;Inherit;False;3;0;FLOAT2;0,0;False;2;FLOAT2;0,0;False;1;FLOAT;1;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleAddOpNode;48;-1010.881,-376.3616;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.RangedFloatNode;43;-1036.748,-149.8976;Inherit;False;Property;_Custom1zw1;Custom1zw;12;1;[Toggle];Create;True;0;0;0;False;1;Header(MaskCustomData);False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.LerpOp;82;-1140.345,-1007.255;Inherit;False;3;0;FLOAT2;0,0;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.LerpOp;50;-778.7377,-387.0356;Inherit;False;3;0;FLOAT2;0,0;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.FunctionNode;101;-976,-1008;Inherit;False;Group_UVClamp;-1;;1;00c878ae88e386a48aac8ddb5cec53ee;0;1;9;FLOAT2;0,0;False;4;FLOAT2;23;FLOAT2;22;FLOAT2;15;FLOAT2;21
Node;AmplifyShaderEditor.RangedFloatNode;55;-454.6133,-214.5281;Inherit;False;Property;_MaskIntensity1;MaskIntensity;8;0;Create;True;0;0;0;False;0;False;1;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.StaticSwitch;84;-759.126,-1030.466;Inherit;False;Property;_Clamp;Clamp;5;0;Create;True;0;0;0;False;0;False;0;0;0;True;;KeywordEnum;4;UV;UClamp;VClamp;UVClamp;Create;True;True;All;9;1;FLOAT2;0,0;False;0;FLOAT2;0,0;False;2;FLOAT2;0,0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT2;0,0;False;6;FLOAT2;0,0;False;7;FLOAT2;0,0;False;8;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SamplerNode;54;-575.73,-415.9217;Inherit;True;Property;_Mask;Mask;9;0;Create;False;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.RangedFloatNode;98;-318.7758,-137.3591;Inherit;False;Property;_DepthFadeDistance;DepthFade Distance;0;0;Create;True;0;0;0;False;0;False;0;1.4;0;10;0;1;FLOAT;0
Node;AmplifyShaderEditor.VertexColorNode;60;-462.8941,-594.3391;Inherit;False;0;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;57;-247.4102,-386.0867;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode;59;-548.5059,-1060.308;Inherit;True;Property;_MainTex;MainTex;7;0;Create;True;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.ColorNode;56;-499.3816,-852.9705;Inherit;False;Property;_Color2;Color;2;0;Create;True;0;0;0;False;0;False;1,1,1,1;1,1,1,1;True;True;0;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.DepthFade;99;-42.3288,-150.6382;Inherit;False;True;True;True;2;1;FLOAT3;0,0,0;False;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;58;-485.1549,-683.8524;Inherit;False;Property;_ColorIntensity2;ColorIntensity;1;0;Create;True;0;0;0;False;0;False;1;3;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;62;-103.3528,-841.1827;Inherit;False;4;4;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;2;FLOAT;0;False;3;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;61;-97.02539,-664.1588;Inherit;False;5;5;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;4;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.DynamicAppendNode;63;97.55653,-904.5482;Inherit;True;FLOAT4;4;0;FLOAT3;0,0,0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.IntNode;64;511.0825,-634.9964;Inherit;False;Property;_CullMode3;CullMode;6;1;[Enum];Create;False;0;2;OFF;0;ON;2;0;True;0;False;0;0;False;0;1;INT;0
Node;AmplifyShaderEditor.IntNode;97;510.7754,-558.1544;Inherit;False;Property;_Dst;BlendMode;4;1;[Enum];Create;False;0;2;Add;1;AlphaBlend;10;0;True;0;False;1;1;False;0;1;INT;0
Node;AmplifyShaderEditor.IntNode;102;512,-480;Inherit;False;Property;_Zwrite;Zwrite;3;1;[Enum];Create;True;0;2;OFF;0;ON;2;0;True;0;False;0;0;False;0;1;INT;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;105;544,-832;Float;False;True;-1;3;ASEMaterialInspector;100;5;Effect/Add_Blend_Soft_UPR;0770190933193b94aaa3065e307002fa;True;Unlit;0;0;Unlit;2;True;True;2;5;False;;10;True;_Dst;0;1;False;;0;False;;True;0;False;;0;False;;False;False;False;False;False;False;False;False;False;True;0;False;;True;True;0;False;;False;True;True;True;True;True;0;False;;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;True;True;2;True;_Zwrite;True;3;False;;True;True;0;False;;0;False;;True;1;RenderType=Opaque=RenderType;True;2;False;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;0;;0;0;Standard;1;Vertex Position,InvertActionOnDeselection;1;0;0;1;True;False;;False;0
WireConnection;38;0;34;1
WireConnection;38;1;34;2
WireConnection;35;0;34;3
WireConnection;35;1;34;4
WireConnection;42;0;38;0
WireConnection;37;0;35;0
WireConnection;78;0;77;1
WireConnection;78;1;77;2
WireConnection;41;0;77;3
WireConnection;41;1;77;4
WireConnection;79;0;86;0
WireConnection;79;2;78;0
WireConnection;81;0;85;0
WireConnection;81;1;86;0
WireConnection;47;0;39;0
WireConnection;47;2;41;0
WireConnection;48;0;40;0
WireConnection;48;1;39;0
WireConnection;82;0;79;0
WireConnection;82;1;81;0
WireConnection;82;2;80;0
WireConnection;50;0;47;0
WireConnection;50;1;48;0
WireConnection;50;2;43;0
WireConnection;101;9;82;0
WireConnection;84;1;101;23
WireConnection;84;0;101;22
WireConnection;84;2;101;15
WireConnection;84;3;101;21
WireConnection;54;1;50;0
WireConnection;57;0;54;1
WireConnection;57;1;55;0
WireConnection;59;1;84;0
WireConnection;99;0;98;0
WireConnection;62;0;59;0
WireConnection;62;1;56;0
WireConnection;62;2;58;0
WireConnection;62;3;60;0
WireConnection;61;0;59;4
WireConnection;61;1;56;4
WireConnection;61;2;57;0
WireConnection;61;3;60;4
WireConnection;61;4;99;0
WireConnection;63;0;62;0
WireConnection;63;3;61;0
WireConnection;105;0;63;0
ASEEND*/
//CHKSM=E7FE5904D4DCB282859D78023304DBB4ECE607D2