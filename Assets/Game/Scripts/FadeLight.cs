using UnityEngine;

public class FadeLight : MonoBehaviour
{
    public float fadeTime;

    private Light lightComponent;
    private float initialIntensity;
    private float totalTime;

    public void Start()
    {
        lightComponent = GetComponent<Light>();
        totalTime = fadeTime;
        initialIntensity = lightComponent.intensity;
    }

    public void Update()
    {
        fadeTime -= Time.deltaTime;
        if (fadeTime >= 0.0f)
        {
            lightComponent.intensity = fadeTime / totalTime * initialIntensity;
        }
        else
        {
            Destroy(this);
        }
    }
}
