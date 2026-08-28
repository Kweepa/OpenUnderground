using UnityEngine;

/// <summary>
/// Rotates an object in an unsettling, unpredictable way.
/// Combines smooth, Perlin-noise-driven rotation with sudden random "jumps".
/// </summary>
public class UnsettlingSpinner : MonoBehaviour
{
    [Header("Spin Axis")]
    [Tooltip("The axis around which the object will rotate (in local space).")]
    public Vector3 rotationAxis = Vector3.up;

    [Header("Smooth Rotation")]
    [Tooltip("The maximum speed (degrees per second) of the 'smooth' rotation component.")]
    public float maxSmoothSpeed = 200f;

    [Tooltip("How quickly the smooth rotation speed changes. Higher values = more erratic.")]
    public float noiseFrequency = 0.4f;

    [Header("Jump Rotation")]
    [Tooltip("The chance per second that a 'jump' rotation will occur (e.g., 0.5 = 50% chance per second).")]
    public float jumpChancePerSecond = 0.5f;

    [Tooltip("The minimum angle (in degrees) for a random jump.")]
    public float minJumpAngle = 30f;

    [Tooltip("The maximum angle (in degrees) for a random jump.")]
    public float maxJumpAngle = 90f;

    // A private offset for the Perlin noise to ensure 
    // different objects don't spin in sync.
    private float noiseOffset;

    void Start()
    {
        // Initialize with a random offset for the noise function.
        // This is crucial so that multiple objects with this script
        // don't all spin in perfect synchronization.
        noiseOffset = Random.Range(0f, 1000f);
    }

    void Update()
    {
        // --- 1. Smooth Rotation ---
        
        // Use Perlin noise to get a value that changes smoothly over time.
        // Time.time * noiseFrequency = how "fast" we move through the noise
        // noiseOffset = our unique starting point in the noise
        float noiseValue = Mathf.PerlinNoise(Time.time * noiseFrequency, noiseOffset);

        // Calculate the current speed and apply the rotation.
        float currentSmoothSpeed = noiseValue * maxSmoothSpeed;
        transform.Rotate(rotationAxis, currentSmoothSpeed * Time.deltaTime);

        // --- 2. Jump Rotation ---

        // Check if a jump should occur this frame.
        // We multiply by Time.deltaTime to make the probability 
        // independent of the frame rate.
        if (Random.Range(0f, 1f) < jumpChancePerSecond * Time.deltaTime)
        {
            // A jump is triggered!
            float jumpAmount = Random.Range(minJumpAngle, maxJumpAngle);

            // Add a 50% chance for the jump to be in the negative direction
            // to make it even more unpredictable.
            if (Random.value < 0.5f)
            {
                jumpAmount *= -1f;
            }

            // Apply the jump rotation instantly.
            transform.Rotate(rotationAxis, jumpAmount);
        }
    }
}
