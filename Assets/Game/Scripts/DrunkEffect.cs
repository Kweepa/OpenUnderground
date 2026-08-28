using System;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

// This class defines the settings that appear in the Post-process Profile
[Serializable]
[PostProcess(typeof(DrunkEffectRenderer), PostProcessEvent.AfterStack, "Custom/Drunk Effect")]
public sealed class DrunkEffect : PostProcessEffectSettings
{
    [Tooltip("A master control to turn the effect on and off.")]
    [Range(0f, 1f)]
    public FloatParameter masterIntensity = new FloatParameter { value = 0f };

    [Tooltip("Controls the magnitude of the screen distortion.")]
    [Range(0f, 0.1f)]
    public FloatParameter distortionIntensity = new FloatParameter { value = 0.02f };

    [Tooltip("Controls the speed of the distortion animation.")]
    [Range(0f, 1f)]
    public FloatParameter distortionSpeed = new FloatParameter { value = 0.16f };

    [Tooltip("Sets the scale of the noise used for distortion.")]
    [Range(1f, 10f)]
    public FloatParameter noiseScale = new FloatParameter { value = 2.8f };

    [Tooltip("Controls the blend intensity of the 'double vision' effect.")]
    [Range(0f, 1f)]
    public FloatParameter drunkIntensity = new FloatParameter { value = 1f };

    [Tooltip("Multiplies the offset of the 'double vision' sample.")]
    [Range(0f, 3f)]
    public FloatParameter drunkOffsetMultiplier = new FloatParameter { value = 2.4f };

    public override bool IsEnabledAndSupported(PostProcessRenderContext context)
    {
        return enabled.value && masterIntensity.value > 0f;
    }
}

// This class contains the logic to apply the shader to the screen
public sealed class DrunkEffectRenderer : PostProcessEffectRenderer<DrunkEffect>
{
    private Shader _shader;

    public override void Init()
    {
        _shader = Shader.Find("Hidden/Custom/DrunkEffectPPv2");
    }

    public override void Render(PostProcessRenderContext context)
    {
        var sheet = context.propertySheets.Get(_shader);

        // Pass all the settings from the profile to the shader
        sheet.properties.SetFloat("_MasterIntensity", settings.masterIntensity);
        sheet.properties.SetFloat("_DistortionIntensity", settings.distortionIntensity);
        sheet.properties.SetFloat("_DistortionSpeed", settings.distortionSpeed);
        sheet.properties.SetFloat("_NoiseScale", settings.noiseScale);
        sheet.properties.SetFloat("_DrunkIntensity", settings.drunkIntensity);
        sheet.properties.SetFloat("_DrunkOffsetMultiplier", settings.drunkOffsetMultiplier);

        context.command.BlitFullscreenTriangle(context.source, context.destination, sheet, 0);
    }
}
