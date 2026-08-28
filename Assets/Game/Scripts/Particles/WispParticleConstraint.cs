using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(ParticleSystem))]
public class WispConstraint : MonoBehaviour
{
    ParticleSystem ps;
    ParticleSystem.Particle[] particles;

    [Header("Settings")]
    public float sphereRadius = 0.5f; 
    public float wanderStrength = 2.0f; 
    public float returnStrength = 5.0f; 
    public float maxSpeed = 2.0f; 

    void Start()
    {
        ps = GetComponent<ParticleSystem>();
    }

    void LateUpdate()
    {
        InitializeIfNeeded();

        int numParticlesAlive = ps.GetParticles(particles);

        // 1. Detect Coordinate Space
        // If World: We pull particles toward the object's current world position.
        // If Local: We pull particles toward (0,0,0).
        bool isWorldSpace = ps.main.simulationSpace == ParticleSystemSimulationSpace.World;
        Vector3 centerPos = isWorldSpace ? transform.position : Vector3.zero;

        for (int i = 0; i < numParticlesAlive; i++)
        {
            Vector3 position = particles[i].position;

            // 2. Calculate Vector from Particle to Center
            Vector3 offset = position - centerPos;
            float distance = offset.magnitude;

            // 3. Wander Force
            Vector3 wanderForce = Random.onUnitSphere * wanderStrength;

            // 4. Return Force (The Tether)
            // Pulls towards 'centerPos'. Strength increases with distance.
            Vector3 returnDir = -offset.normalized;
            Vector3 returnForce = returnDir * (distance / sphereRadius) * returnStrength;

            // 5. Apply & Clamp
            Vector3 newVelocity = particles[i].velocity + (wanderForce + returnForce) * Time.deltaTime;

            if (newVelocity.magnitude > maxSpeed)
            {
                newVelocity = newVelocity.normalized * maxSpeed;
            }

            particles[i].velocity = newVelocity;
        }

        ps.SetParticles(particles, numParticlesAlive);
    }

    void InitializeIfNeeded()
    {
        if (ps == null) ps = GetComponent<ParticleSystem>();

        if (particles == null || particles.Length < ps.main.maxParticles)
        {
            particles = new ParticleSystem.Particle[ps.main.maxParticles];
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, sphereRadius);
    }
}
