using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

public class DamageFlash : MonoBehaviour
{
    private PostProcessVolume volume;
    private Vignette vignette;

    private float intensity;
    private float holdTime;

    public Color damageColor;
    public Color drowningColor;
    public Color lavaColor;
    public Color poisonColor;

    void Start()
    {
        volume = GetComponent<PostProcessVolume>();
        volume.profile.TryGetSettings(out vignette);
        vignette.enabled.Override(false);
    }

    public void Flash(float _intensity, EDamageType damageType)
    {
        holdTime = 0.15f;
        intensity = Mathf.Min(_intensity, 0.6f);
        vignette.enabled.Override(true);
        vignette.intensity.Override(intensity);
        Color col = damageColor;
        switch (damageType)
        {
        case EDamageType.Damage:
        case EDamageType.Direct:
            col = damageColor;
            break;
        case EDamageType.Drowning:
            col = drowningColor;
            break;
        case EDamageType.Lava:
            col = lavaColor;
            break;
        case EDamageType.Poison:
            col = poisonColor;
            break;
        }
        vignette.color.Override(col);
    }

    void Update()
    {
        if (holdTime > 0.0f)
        {
            holdTime -= Time.deltaTime;
        }
        else if (intensity > 0.0f)
        {
            vignette.intensity.Override(intensity);
            intensity -= Time.deltaTime;
            if (intensity <= 0.0f)
            {
                vignette.enabled.Override(false);
            }
        }
    }
}
