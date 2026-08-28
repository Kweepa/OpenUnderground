Shader "Custom/BlackStencilOverlay" {
    Properties {
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _HeightFadeDistance ("Height Fade Distance", Range(0.001, 1.0)) = 1.0
    }
    SubShader {
        Tags { "Queue"="Geometry+1" }

        Pass {
            ZTest Equal
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float4 modelPos : TEXCOORD1;
            };

            sampler2D _MainTex;
            float _HeightFadeDistance;

            v2f vert (appdata v) {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.modelPos = v.vertex;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target {
                fixed4 texColor = tex2D(_MainTex, i.uv);
                
                if (texColor.r + texColor.g + texColor.b >= 0.01) {
                    discard;
                }
                
                // Calculate fade based on the Z-axis in model space.
                float heightFade = saturate(abs(i.modelPos.z) / _HeightFadeDistance);
                
                // Return pure black with an alpha that fades based on distance from the XY plane.
                return fixed4(0, 0, 0, heightFade);
            }
            ENDCG
        }
    }
}
