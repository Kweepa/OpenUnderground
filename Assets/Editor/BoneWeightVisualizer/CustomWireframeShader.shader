// Custom/Wireframe Shader
// This shader uses barycentric coordinates to draw a wireframe over a mesh.
Shader "Custom/Wireframe"
{
    Properties
    {
        _WireColor ("Wire Color", Color) = (0.1, 0.1, 0.1, 0.8)
        _Thickness ("Thickness", Range(1, 10)) = 1.5
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 100

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off // Show wireframe on both sides of a polygon

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma geometry geo
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2g
            {
                float4 pos : SV_POSITION;
            };
            
            struct g2f
            {
                float4 pos : SV_POSITION;
                float3 barycentric : TEXCOORD0;
            };

            float _Thickness;
            fixed4 _WireColor;

            v2g vert(appdata v)
            {
                v2g o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            [maxvertexcount(3)]
            void geo(triangle v2g i[3], inout TriangleStream<g2f> stream)
            {
                float3 p0 = i[0].pos.xyz / i[0].pos.w;
                float3 p1 = i[1].pos.xyz / i[1].pos.w;
                float3 p2 = i[2].pos.xyz / i[2].pos.w;

                g2f o;

                o.pos = i[0].pos;
                o.barycentric = float3(1, 0, 0);
                stream.Append(o);

                o.pos = i[1].pos;
                o.barycentric = float3(0, 1, 0);
                stream.Append(o);

                o.pos = i[2].pos;
                o.barycentric = float3(0, 0, 1);
                stream.Append(o);
            }

            fixed4 frag(g2f i) : SV_Target
            {
                float edge = min(i.barycentric.x, min(i.barycentric.y, i.barycentric.z));
                float width = fwidth(edge) * _Thickness;
                float alpha = smoothstep(0, width, edge);

                if (alpha > 0.99) discard; // Discard center fragments

                return fixed4(_WireColor.rgb, _WireColor.a * (1.0 - alpha));
            }
            ENDCG
        }
    }
}
