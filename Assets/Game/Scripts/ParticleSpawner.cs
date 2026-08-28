using UnityEngine;

public class ParticleSpawner : MonoBehaviour
{
    public static ParticleSpawner sParticleSpawner;

    [EnumNamedArray(typeof(EParticleType))]
    public GameObject[] particleBlueprints;

    protected void Awake()
    {
        // Singleton pattern - destroy duplicate ParticleSpawner instances
        if (sParticleSpawner != null && sParticleSpawner != this)
        {
            Debug.LogWarning("Duplicate ParticleSpawner detected and destroyed");
            Destroy(gameObject);
            return;
        }
        
        sParticleSpawner = this;
        DontDestroyOnLoad(gameObject);
    }

    public static GameObject SpawnParticle(EParticleType particleType, Vector3 position, Quaternion rotation = default)
    {
        // default quaternion is identity
        return sParticleSpawner.SpawnParticleInternal(particleType, position, rotation);
    }

    private GameObject SpawnParticleInternal(EParticleType particleType, Vector3 position, Quaternion rotation)
    {

        int typeIndex = (int)particleType;
        if (particleBlueprints == null || typeIndex < 0 || typeIndex >= particleBlueprints.Length)
        {
            Debug.LogWarning($"Invalid particle type index {typeIndex} for EParticleType.{particleType}");
            return null;
        }

        GameObject blueprint = particleBlueprints[typeIndex];
        if (blueprint == null)
        {
            // this is expected for many particles during development
            return null;
        }

        GameObject instance = Instantiate(blueprint, position, rotation);
        return instance;
    }
}
