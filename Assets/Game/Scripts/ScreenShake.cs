using UnityEngine;

public class ScreenShake : MonoBehaviour
{
    public float shakeDuration = 0.5f;
    public float shakeMagnitude = 5.0f;
    public float shakeFrequency = 10.0f;

    private float shakeTimer;
    private float lastShakeTime;

    // Smoothing variables
    private float currentRoll;
    private float targetRoll;

    private void Start()
    {
        lastShakeTime = -shakeFrequency;
    }

    public void Shake()
    {
        shakeTimer = shakeDuration;
        currentRoll = 0.0f; // Start from zero roll
    }

    private void Update()
    {
        if (shakeTimer > 0)
        {
            // Update target roll at the specified frequency
            if (Time.time - lastShakeTime >= 1.0f / shakeFrequency)
            {
                targetRoll = Random.Range(-1.0f, 1.0f) * shakeMagnitude; // New random target
                lastShakeTime = Time.time;
            }

            // Smoothly interpolate towards the target roll
            currentRoll = Mathf.Lerp(currentRoll, targetRoll, Time.deltaTime * shakeFrequency * 2.0f); // Adjust the 2f for speed

            transform.localRotation = Quaternion.Euler(0.0f, 0.0f, currentRoll);

            shakeTimer -= Time.deltaTime;
        }
        else
        {
            transform.localRotation = Quaternion.identity;
            currentRoll = 0; // Reset roll for next shake
        }
    }
}
