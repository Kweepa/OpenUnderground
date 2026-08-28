Shader "Custom/RetroFlameMasked"
{
    Properties
    {
        [Header(Base Textures)]
        _MainTex ("Main Texture Green Key", 2D) = "white" {}
        
        [Header(Green Key Settings)]
        _KeyThreshold ("Green Threshold", Range(0.0, 1.0)) = 0.8
        
        [Header(Gradient Range Settings)]
        _GradientMinY ("Gradient Bottom Y", Range(0.0, 1.0)) = 0.3
        _GradientMaxY ("Gradient Top Y", Range(0.0, 1.0)) = 0.7

        [Header(Layer One Static Background)]
        _BgColorBottom ("Background Bottom White", Color) = (1.0, 1.0, 1.0, 1)
        _BgColorTop ("Background Top Cyan", Color) = (0.0, 0.7, 1.0, 1)
        // NEW CONTROL: Replaces gradient curve for better control over white band
        _BgWhitePoint ("White Point Position", Range(0.0, 0.95)) = 0.4

        [Header(Layer Two Animated Flames)]
        // Bottom is now Red, Top is now Yellow
        _FlameColorBottom ("Flame Bottom Red", Color) = (0.9, 0.1, 0.0, 1)  // Deep Red
        _FlameColorTop ("Flame Top Yellow", Color) = (1.0, 0.9, 0.0, 1)     // Bright Yellow
        
        [Header(Flame Shape Settings)]
        _FlameHeight ("Flame Height", Range(0.5, 2.5)) = 1.2
        _FlameSoftness ("Flame Softness", Range(0.1, 2.0)) = 0.8
        _FlameMix ("Flame Color Mix", Range(0.5, 3.0)) = 1.0 

        [Header(Animation Parameters)]
        _NoiseTex ("Noise Texture", 2D) = "white" {}
        _ScrollSpeedX ("Scroll Speed X", Range(-2.0, 2.0)) = 0.1
        _ScrollSpeedY ("Scroll Speed Y", Range(-2.0, 5.0)) = 2.0
        _NoiseScale ("Noise Scale", Range(0.1, 5.0)) = 1.5
        _FlameDistortion ("Distortion Amount", Range(0.0, 1.0)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 100
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _NoiseTex;
            float4 _NoiseTex_ST;

            // Background Params
            fixed4 _BgColorBottom;
            fixed4 _BgColorTop;
            float _BgWhitePoint; // New variable

            // Flame Colors
            fixed4 _FlameColorBottom;
            fixed4 _FlameColorTop;

            float _KeyThreshold;
            float _GradientMinY;
            float _GradientMaxY;

            float _ScrollSpeedX;
            float _ScrollSpeedY;
            float _NoiseScale;
            float _FlameDistortion;
            float _FlameHeight;
            float _FlameSoftness;
            float _FlameMix;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // 1. Green Screen Check
                fixed4 mainCol = tex2D(_MainTex, i.uv);
                float isGreen = step(_KeyThreshold, mainCol.g) * step(mainCol.r, 0.5) * step(mainCol.b, 0.5);
                if (isGreen < 0.1) return mainCol;

                // --- SHADER LOGIC ---

                // 2. Normalize UV.y
                float range = max(0.001, _GradientMaxY - _GradientMinY);
                float localV = saturate((i.uv.y - _GradientMinY) / range);

                // 3. LAYER 1: STATIC BACKGROUND (White -> Cyan Split Gradient)
                // Calculate where we are relative to the white point and the top
                float transitionRange = max(0.001, 1.0 - _BgWhitePoint);
                // Determine progress through the transition zone (0 means at white point, 1 means at top)
                float bgProgress = saturate((localV - _BgWhitePoint) / transitionRange);
                // Use smoothstep for a nice fade from white to cyan
                float bgBlend = smoothstep(0.0, 1.0, bgProgress);
                
                fixed4 staticBackground = lerp(_BgColorBottom, _BgColorTop, bgBlend);

                // 4. Calculate Noise
                float2 timeOffset = float2(_ScrollSpeedX, _ScrollSpeedY) * _Time.y;
                float2 noiseUV = (i.uv * _NoiseScale) - timeOffset;
                float noise1 = tex2D(_NoiseTex, noiseUV).r;
                float noise2 = tex2D(_NoiseTex, noiseUV * 1.5 + float2(timeOffset.y * 0.5, 0)).r;
                float combinedNoise = (noise1 + noise2) * 0.5;

                // 5. Define Flame Intensity
                // 1.0 at bottom (high intensity), 0.0 at top (low intensity)
                float flameIntensity = ((1.0 - localV) * _FlameHeight) - (combinedNoise * _FlameDistortion);
                
                // 6. LAYER 2: FLAME COLOR (Red Bottom -> Yellow Top)
                float colorMix = saturate(flameIntensity * _FlameMix);
                fixed4 flameColor = lerp(_FlameColorTop, _FlameColorBottom, colorMix);

                // 7. Calculate Soft Alpha Blend (Fade flame over background)
                float flameAlpha = smoothstep(0.0, _FlameSoftness, flameIntensity);

                // 8. FINAL COMPOSITE
                fixed4 finalColor = lerp(staticBackground, flameColor, flameAlpha);

                finalColor.a = mainCol.a; 
                return finalColor;
            }
            ENDCG
        }
    }
}
