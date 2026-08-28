Shader "Hidden/Custom/DruggedEffectPPv2"
{
    HLSLINCLUDE

        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"

        // The screen texture is passed in as _MainTex by the framework
        TEXTURE2D_SAMPLER2D(_MainTex, sampler_MainTex);

        // Define your properties here. They will be set by the C# script.
        float _MasterIntensity;
        float _DistortionIntensity;
        float _DistortionSpeed;
        float _NoiseScale;
        float _ColorIntensity;
        float _HueRotationSpeed;

        // -- Your Noise and HSV Functions (Unchanged) --
        float3 mod289(float3 x) { return x - floor(x * (1.0/289.0)) * 289.0; }
        float2 mod289(float2 x) { return x - floor(x * (1.0/289.0)) * 289.0; }
        float3 permute(float3 x) { return mod289(((x*34.0)+1.0)*x); }
        float snoise(float2 v){const float4 C=float4(0.211324865405187,0.366025403784439,-0.577350269189626,0.024390243902439);float2 i=floor(v+dot(v,C.yy));float2 x0=v-i+dot(i,C.xx);float2 i1;i1=(x0.x>x0.y)?float2(1.0,0.0):float2(0.0,1.0);float4 x12=x0.xyxy+C.xxzz;x12.xy-=i1;i=mod289(i);float3 p=permute(permute(i.y+float3(0.0,i1.y,1.0))+i.x+float3(0.0,i1.x,1.0));float3 m=max(0.5-float3(dot(x0,x0),dot(x12.xy,x12.xy),dot(x12.zw,x12.zw)),0.0);m=m*m;m=m*m;float3 x=2.0*frac(p*C.www)-1.0;float3 h=abs(x)-0.5;float3 ox=floor(x+0.5);float3 a0=x-ox;m*=1.79284291400159-0.85373472095314*(a0*a0+h*h);float3 g;g.x=a0.x*x0.x+h.x*x0.y;g.yz=a0.yz*x12.xz+h.yz*x12.yw;return 130.0*dot(m,g);}
        float3 rgb2hsv(float3 c){float4 K=float4(0.0,-1.0/3.0,2.0/3.0,-1.0);float4 p=lerp(float4(c.bg,K.wz),float4(c.gb,K.xy),step(c.b,c.g));float4 q=lerp(float4(p.xyw,c.r),float4(c.r,p.yzx),step(p.x,c.r));float d=q.x-min(q.w,q.y);float e=1.0e-10;return float3(abs(q.z+(q.w-q.y)/(6.0*d+e)),d/(q.x+e),q.x);}
        float3 hsv2rgb(float3 c){float4 K=float4(1.0,2.0/3.0,1.0/3.0,3.0);float3 p=abs(frac(c.xxx+K.xyz)*6.0-K.www);return c.z*lerp(K.xxx,clamp(p-K.xxx,0.0,1.0),c.y);}
        
        // The fragment shader remains mostly the same
        float4 Frag(VaryingsDefault i) : SV_Target
        {
            // 1. First, we calculate the noise and the distorted UV coordinates.
            float2 motion = float2(sin(0.9f * _Time.y * _DistortionSpeed), sin(1.1f * _Time.y * _DistortionSpeed));
            float2 noise_uv = i.texcoord * _NoiseScale;
            float noise_low_detail = snoise(noise_uv * 0.5 + motion);
            float noise_high_detail = snoise(noise_uv * 2.0 + motion);
            float2 distortedUV = i.texcoord + (noise_low_detail + noise_high_detail * 0.25) * _DistortionIntensity * _MasterIntensity;
            
            // 2. NOW, we sample the screen texture using those distorted coordinates.
            float4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, distortedUV);

            // 3. The rest of the color processing logic remains the same.
            float3 hsv = rgb2hsv(col.rgb);
            hsv.x = frac((noise_low_detail + 1) * 0.5 + _Time.y * _HueRotationSpeed + hsv.x);
            hsv.y = 1.0;
            
            col.rgb = lerp(col.rgb, hsv2rgb(hsv), _ColorIntensity * _MasterIntensity);
            
            return col;
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
