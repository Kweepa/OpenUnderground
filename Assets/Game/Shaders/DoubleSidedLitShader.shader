// Lit (PBR), double-sided shader.
// Reacts to scene lighting on both front and back faces.

Shader "Custom/LitDoubleSided"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        [Gamma] _Metallic ("Metallic", Range(0.0, 1.0)) = 0.0
        _Glossiness ("Smoothness", Range(0.0, 1.0)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        // Renders both front and back faces of a mesh.
        Cull Off

        CGPROGRAM
        // This line declares it as a PBR surface shader using the "Standard" lighting model.
        #pragma surface surf Standard fullforwardshadows

        #pragma target 3.0

        sampler2D _MainTex;

        struct Input
        {
            float2 uv_MainTex;
            // VFACE gives a positive value for front-faces and negative for back-faces.
            half facing : VFACE;
        };

        half _Glossiness;
        half _Metallic;
        fixed4 _Color;

        // The Surface Function: defines the visual properties of the material per-pixel.
        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            // Albedo comes from a texture tinted by color
            fixed4 c = tex2D (_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb;
            
            // Set the Metallic and Smoothness properties
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Alpha = c.a;

            // --- FIX FOR DOUBLE-SIDED LIGHTING ---
            // Flip the normal on back-faces so they are lit correctly.
            o.Normal = o.Normal * IN.facing;
        }
        ENDCG
    }
    FallBack "Diffuse"
}