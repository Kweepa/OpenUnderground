using System;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

// This attribute defines the settings for our effect
[Serializable]
[PostProcess(typeof(DruggedEffectRenderer), PostProcessEvent.AfterStack, "Custom/Drugged Effect")]
public sealed class DruggedEffect : PostProcessEffectSettings
{
    [Tooltip("A master control to turn the effect on and off.")]
    [Range(0f, 1f)]
    public FloatParameter masterIntensity = new FloatParameter { value = 0f };

    [Tooltip("Controls the magnitude of the screen distortion.")]
    [Range(0f, 1f)]
    public FloatParameter distortionIntensity = new FloatParameter { value = 0.05f };

    [Tooltip("Controls the speed of the distortion animation.")]
    [Range(0f, 1f)]
    public FloatParameter distortionSpeed = new FloatParameter { value = 0.3f };

    [Tooltip("Sets the scale of the noise used for distortion.")]
    [Range(1f, 10f)]
    public FloatParameter noiseScale = new FloatParameter { value = 4f };

    [Tooltip("Controls the intensity of the color shifting effect.")]
    [Range(0f, 1f)]
    public FloatParameter colorIntensity = new FloatParameter { value = 0.5f };

    [Tooltip("Controls how fast the hue rotates through the color spectrum.")]
    [Range(-2f, 2f)]
    public FloatParameter hueRotationSpeed = new FloatParameter { value = 0.15f };
    
    // Override the IsEnabledAndSupported method to check if the effect should be rendered
    public override bool IsEnabledAndSupported(PostProcessRenderContext context)
    {
        return enabled.value && masterIntensity.value > 0f;
    }
}

// This attribute links the renderer to the settings class
public sealed class DruggedEffectRenderer : PostProcessEffectRenderer<DruggedEffect>
{
    private Shader _shader;

    public override void Init()
    {
        // Tell Unity which shader to use
        _shader = Shader.Find("Hidden/Custom/DruggedEffectPPv2");
    }

    // This is where the magic happens
    public override void Render(PostProcessRenderContext context)
    {
        // Get a property sheet for our shader
        var sheet = context.propertySheets.Get(_shader);

        // Pass the settings from the Profile to the shader
        sheet.properties.SetFloat("_MasterIntensity", settings.masterIntensity);
        sheet.properties.SetFloat("_DistortionIntensity", settings.distortionIntensity);
        sheet.properties.SetFloat("_DistortionSpeed", settings.distortionSpeed);
        sheet.properties.SetFloat("_NoiseScale", settings.noiseScale);
        sheet.properties.SetFloat("_ColorIntensity", settings.colorIntensity);
        sheet.properties.SetFloat("_HueRotationSpeed", settings.hueRotationSpeed);

        // Apply the effect to the screen
        context.command.BlitFullscreenTriangle(context.source, context.destination, sheet, 0);
    }
}
