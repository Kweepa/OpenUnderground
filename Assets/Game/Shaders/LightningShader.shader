// Unity Shader for Built-in Render Pipeline
// CORRECTED VERSION: Replicates Particles/Standard Unlit with proper distance-based fading.
Shader "Custom/LightningShader"
{
    Properties
    {
        _MainTex ("Particle Texture", 2D) = "white" {}
        
        [HDR] _Color ("Emissive Tint", Color) = (1,1,1,1)

        _FadeStartDistance ("Fade Start Distance", Float) = 20.0
        _FadeEndDistance ("Fade End Distance", Float) = 50.0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 100

        Pass
        {
            // Additive blending
            Blend SrcAlpha One
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float fade : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _FadeStartDistance;
            float _FadeEndDistance;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv.xy = TRANSFORM_TEX(v.uv.xy, _MainTex);
                o.uv.zw = v.uv.zw;
                o.color = v.color;

                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                float dist = distance(worldPos, _WorldSpaceCameraPos.xyz);
                float fadeRange = max(0.001, _FadeEndDistance - _FadeStartDistance);
                float fadeFactor = saturate((dist - _FadeStartDistance) / fadeRange);
                o.fade = 1.0 - fadeFactor;
                
                return o;
            }

            // --- FRAGMENT SHADER (Corrected Logic) ---
            fixed4 frag (v2f i) : SV_Target
            {
                // 1. Sample the particle texture
                fixed4 texColor = tex2D(_MainTex, i.uv.xy);

                // 2. Combine all colors to get the final emissive color
                // This is the full brightness of the particle.
                fixed4 finalColor = texColor * _Color * i.color;
                
                // 3. FIX: Apply the distance fade ONLY to the alpha.
                // The alpha acts as the intensity control for the additive blend.
                finalColor.a *= i.fade;
                
                return finalColor;
            }
            ENDCG
        }
    }
}
