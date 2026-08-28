using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(ParticleSystem))]
public class WispParticleBlobs : MonoBehaviour
{
    ParticleSystem ps;
    ParticleSystem.Particle[] particles;

    struct BlobData
    {
        public Vector3 axis;
        public Vector3 driftAxis;
        public Vector3 startDir;
        public float currentAngle;
        public float orbitRadius;
        public float orbitSpeed;
        
        // Size & Throb
        public float baseSizeMin;
        public float baseSizeMax;
        public float pulsePhase;
        public float pulseSpeed;
        public float sizeScalar; // <--- NEW: Stores the random system start size
    }
    BlobData[] blobData;

    [Header("Configuration")]
    [Range(1, 20)] public int orbCount = 6;
    
    [Header("Orbit Movement")]
    public float minOrbitRadius = 0.4f;
    public float maxOrbitRadius = 0.8f;
    public float orbitSpeed = 2.0f;
    public float axisDriftSpeed = 20.0f;

    [Header("Size Throb")]
    public float minSize = 0.1f;
    public float maxSize = 0.3f;
    public float minPulseSpeed = 2.0f;
    public float maxPulseSpeed = 3.0f;

    [Header("Visuals")]
    public bool useSystemColor = true;
    public Color customColor = Color.cyan;

    bool needsReset = false;

    void Start()
    {
        Initialize();
    }

    void OnValidate()
    {
        needsReset = true;
    }

    void Initialize()
    {
        ps = GetComponent<ParticleSystem>();
        
        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = orbCount;

        ResetBlobs();
        needsReset = false;
    }

    void ResetBlobs()
    {
        if (ps == null) ps = GetComponent<ParticleSystem>();

        ps.Clear();
        
        if (particles == null || particles.Length != orbCount)
        {
            particles = new ParticleSystem.Particle[orbCount];
            blobData = new BlobData[orbCount];
        }

        // Emit using the System's settings (applies Random Start Size here)
        ps.Emit(orbCount);
        ps.GetParticles(particles);

        for (int i = 0; i < orbCount; i++)
        {
            particles[i].startLifetime = float.MaxValue;
            particles[i].remainingLifetime = float.MaxValue;

            // --- NEW: Capture the size assigned by the Particle System ---
            // If you set Start Size to "Random Between Constants" (e.g., 0.5 to 1.5),
            // this value will be unique for every blob.
            blobData[i].sizeScalar = particles[i].startSize;

            blobData[i].orbitRadius = Random.Range(minOrbitRadius, maxOrbitRadius);
            blobData[i].axis = Random.onUnitSphere;
            blobData[i].driftAxis = Random.onUnitSphere;
            
            blobData[i].startDir = Vector3.Cross(blobData[i].axis, Vector3.up).normalized;
            if (blobData[i].startDir == Vector3.zero) blobData[i].startDir = Vector3.right;
            
            blobData[i].orbitSpeed = orbitSpeed * (Random.value > 0.5f ? 1 : -1);
            blobData[i].currentAngle = Random.Range(0f, 360f);

            // Throb ranges (still randomized slightly for variety)
            blobData[i].baseSizeMin = minSize;
            blobData[i].baseSizeMax = maxSize;
            blobData[i].pulsePhase = Random.Range(0f, Mathf.PI * 2);
            blobData[i].pulseSpeed = Random.Range(minPulseSpeed, maxPulseSpeed);
        }

        ps.SetParticles(particles, orbCount);
    }

    void LateUpdate()
    {
        if (ps == null) return;

        if (ps.particleCount == 0 || needsReset)
        {
            Initialize();
        }

        int count = ps.GetParticles(particles);
        Color targetColor = useSystemColor ? ps.main.startColor.color : customColor;

        for (int i = 0; i < count; i++)
        {
            float dt = Time.deltaTime;

            // Orbit
            blobData[i].currentAngle += blobData[i].orbitSpeed * dt;
            
            Quaternion driftRot = Quaternion.AngleAxis(axisDriftSpeed * dt, blobData[i].driftAxis);
            blobData[i].axis = driftRot * blobData[i].axis;

            Quaternion orbitRot = Quaternion.AngleAxis(blobData[i].currentAngle * Mathf.Rad2Deg, blobData[i].axis);
            Vector3 pos = orbitRot * (blobData[i].startDir * blobData[i].orbitRadius);
            
            particles[i].position = pos;

            // Throb
            float sineWave = Mathf.Sin((Time.time * blobData[i].pulseSpeed) + blobData[i].pulsePhase); 
            float t = (sineWave + 1f) / 2f; 
            
            float throbSize = Mathf.Lerp(blobData[i].baseSizeMin, blobData[i].baseSizeMax, t);
            
            // --- APPLY MULTIPLIER ---
            // Combine the throb animation with the System's random start size
            particles[i].startSize = throbSize * blobData[i].sizeScalar;
            
            // Color
            particles[i].startColor = targetColor;
        }

        ps.SetParticles(particles, count);
    }
}
