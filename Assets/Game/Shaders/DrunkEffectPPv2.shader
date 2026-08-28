Shader "Hidden/Custom/DrunkEffectPPv2"
{
    HLSLINCLUDE

        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"

        TEXTURE2D_SAMPLER2D(_MainTex, sampler_MainTex);

        // Properties controlled by the C# script
        float _MasterIntensity;
        float _DistortionIntensity;
        float _DistortionSpeed;
        float _NoiseScale;
        float _DrunkIntensity;
        float _DrunkOffsetMultiplier;

        // Noise function from your original shader
        float3 mod289(float3 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
        float2 mod289(float2 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
        float3 permute(float3 x) { return mod289(((x*34.0)+1.0)*x); }
        float snoise(float2 v){const float4 C=float4(0.211324865405187,0.366025403784439,-0.577350269189626,0.024390243902439);float2 i=floor(v+dot(v,C.yy));float2 x0=v-i+dot(i,C.xx);float2 i1;i1=(x0.x>x0.y)?float2(1.0,0.0):float2(0.0,1.0);float4 x12=x0.xyxy+C.xxzz;x12.xy-=i1;i=mod289(i);float3 p=permute(permute(i.y+float3(0.0,i1.y,1.0))+i.x+float3(0.0,i1.x,1.0));float3 m=max(0.5-float3(dot(x0,x0),dot(x12.xy,x12.xy),dot(x12.zw,x12.zw)),0.0);m=m*m;m=m*m;float3 x=2.0*frac(p*C.www)-1.0;float3 h=abs(x)-0.5;float3 ox=floor(x+0.5);float3 a0=x-ox;m*=1.79284291400159-0.85373472095314*(a0*a0+h*h);float3 g;g.x=a0.x*x0.x+h.x*x0.y;g.yz=a0.yz*x12.xz+h.yz*x12.yw;return 130.0*dot(m,g);}

        float4 Frag(VaryingsDefault i) : SV_Target
        {
            float2 noise_uv = i.texcoord * _NoiseScale;

            [cite_start]// Main "wobble" effect
            float2 motion_main = float2(_Time.y * _DistortionSpeed, _Time.y * _DistortionSpeed);
            float noise_main = snoise(noise_uv + motion_main);
            float2 distortedUV_main = i.texcoord + noise_main * _DistortionIntensity * _MasterIntensity;
            float4 main_color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, distortedUV_main);

            [cite_start]// Secondary "drunk" or "double vision" sample
            float2 motion_drunk = float2(_Time.y * _DistortionSpeed + 123.45, _Time.y * _DistortionSpeed - 54.32);
            float noise_drunk = snoise(noise_uv + motion_drunk);
            float2 drunk_offset = noise_drunk * _DistortionIntensity * _DrunkOffsetMultiplier;
            float4 drunk_color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord + drunk_offset);
            
            [cite_start]// Blend between the main and drunk samples using another noise pattern
            float blend_noise = snoise(noise_uv * 0.4 + motion_main.yx);
            float blend_amount = ((blend_noise + 1) * 0.5) * _DrunkIntensity * _MasterIntensity;
            float4 final_color = lerp(main_color, drunk_color, blend_amount);

            return final_color;
        }

    ENDHLSL

    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment Frag
            ENDHLSL
        }
    }
}
