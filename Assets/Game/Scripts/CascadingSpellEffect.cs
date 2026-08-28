using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class CascadingSpellEffectSaveData : LevelObjectSaveData
{
    public int currentState;
    public float stateTimer;
    public int ownerObjectIndex;  // Track owner for damage attribution
    public bool isPlayerOwned;
    public string particlePrefabName;  // "SheetLightning" or "FlameWind"
    public int minDamage;
    public int maxDamage;
    public Vector3 originPosition;
    public Quaternion originRotation;
    public Vector3[] wave1Positions;
    public Vector3[] wave2Positions;
    public Vector3[] wave3Positions;
}

public class CascadingSpellEffect : LevelObject
{
    public enum EState
    {
        Idle,
        Wave1,
        WaitForWave2,
        Wave2,
        WaitForWave3,
        Wave3,
        Cleanup1,
        Cleanup2,
        Cleanup3,
        Complete
    }

    private EState currentState = EState.Idle;
    private float stateTimer;
    
    private MonoBehaviour owner;
    private GameObject particlePrefab;
    private int minDamage;
    private int maxDamage;
    
    private Vector3 originPosition;
    private Quaternion originRotation;
    
    // Wave position arrays (in local space)
    private Vector3[] zerothWave;
    private Vector3[] firstWave;
    private Vector3[] secondWave;
    private Vector3[] thirdWave;
    
    // Spawned particles per wave
    private List<GameObject> wave1Particles;
    private List<GameObject> wave2Particles;
    private List<GameObject> wave3Particles;
    
    private readonly Collider[] cachedColliders = new Collider[32];
    
    private GameObject transformOwner; // For non-player casters
    private bool initialized = false;
    
    public void Initialize(MonoBehaviour ownerMono, GameObject particle, int min, int max)
    {
        owner = ownerMono;
        particlePrefab = particle;
        minDamage = min;
        maxDamage = max;
        
        // Note: Not marking as temporary so it can be saved
        // The spell completes in 4.5 seconds anyway
        
        // Set up transform
        Transform tx = owner.transform;
        if (owner != PlayerObject.Player)
        {
            transformOwner = new GameObject("CascadeTransform");
            transformOwner.transform.position = tx.position;
            transformOwner.transform.LookAt(PlayerObject.Player.transform.position);
            tx = transformOwner.transform;
        }
        
        originPosition = tx.position;
        originRotation = tx.rotation;
        
        // Define wave patterns (in local space)
        zerothWave = new Vector3[] { Vector3.zero };
        firstWave = new Vector3[] { new(-2.0f, 0.0f, 5.0f), new(2.0f, 0.0f, 5.0f) };
        secondWave = new Vector3[] { new(-4.0f, 0.0f, 9.0f), new(0.0f, 0.0f, 9.0f), new(4.0f, 0.0f, 9.0f) };
        thirdWave = new Vector3[] { new(-6.0f, 0.0f, 13.0f), new(-2.0f, 0.0f, 13.0f), new(2.0f, 0.0f, 13.0f), new(6.0f, 0.0f, 13.0f) };
        
        wave1Particles = new List<GameObject>();
        wave2Particles = new List<GameObject>();
        wave3Particles = new List<GameObject>();
        
        // Start the state machine
        currentState = EState.Wave1;
        stateTimer = 0f;
        
        initialized = true;
    }
    
    public void Stop()
    {
        // Jump to cleanup state immediately
        currentState = EState.Cleanup1;
        stateTimer = 0f;
    }
    
    public void Update()
    {
        if (!initialized || particlePrefab == null)
        {
            return;
        }
        
        stateTimer += Time.deltaTime;
        
        switch (currentState)
        {
            case EState.Idle:
                // Should not reach here normally
                break;
                
            case EState.Wave1:
                if (stateTimer >= 0.0f)
                {
                    SpawnWave(zerothWave, ref firstWave, wave1Particles);
                    currentState = EState.WaitForWave2;
                    stateTimer = 0f;
                }
                break;
                
            case EState.WaitForWave2:
                if (stateTimer >= 0.5f)
                {
                    currentState = EState.Wave2;
                    stateTimer = 0f;
                }
                break;
                
            case EState.Wave2:
                if (stateTimer >= 0.0f)
                {
                    SpawnWave(firstWave, ref secondWave, wave2Particles);
                    currentState = EState.WaitForWave3;
                    stateTimer = 0f;
                }
                break;
                
            case EState.WaitForWave3:
                if (stateTimer >= 0.5f)
                {
                    currentState = EState.Wave3;
                    stateTimer = 0f;
                }
                break;
                
            case EState.Wave3:
                if (stateTimer >= 0.0f)
                {
                    SpawnWave(secondWave, ref thirdWave, wave3Particles);
                    currentState = EState.Cleanup1;
                    stateTimer = 0f;
                }
                break;
                
            case EState.Cleanup1:
                if (stateTimer >= 1.0f)
                {
                    DestroyWaveParticles(wave1Particles);
                    currentState = EState.Cleanup2;
                    stateTimer = 0f;
                }
                break;
                
            case EState.Cleanup2:
                if (stateTimer >= 1.0f)
                {
                    DestroyWaveParticles(wave2Particles);
                    currentState = EState.Cleanup3;
                    stateTimer = 0f;
                }
                break;
                
            case EState.Cleanup3:
                if (stateTimer >= 1.0f)
                {
                    DestroyWaveParticles(wave3Particles);
                    currentState = EState.Complete;
                    stateTimer = 0f;
                }
                break;
                
            case EState.Complete:
                CleanupAndDestroy();
                break;
        }
    }
    
    private void SpawnWave(Vector3[] waveA, ref Vector3[] waveB, List<GameObject> particles)
    {
        Matrix4x4 transform = Matrix4x4.TRS(originPosition, originRotation, Vector3.one);
        
        for (int i = 0; i < waveB.Length; ++i)
        {
            Vector3 localPos = waveB[i] + RandomInCircle();
            Vector3 worldPos = transform.MultiplyPoint3x4(localPos);
            
            float bestDist = 0.0f;
            Vector3 bestPos = worldPos;
            
            for (int j = 0; j < waveA.Length; ++j)
            {
                Vector3 startPos = j == 0 && waveA.Length == 1 ? originPosition : transform.MultiplyPoint3x4(waveA[j]);
                Vector3 dir = worldPos - startPos;
                float dist = dir.magnitude;
                int layerMask = LayerMasks.EnvironmentAndCeiling;
                
                if (Physics.SphereCast(startPos, 0.5f, dir.normalized, out RaycastHit hit, dist, layerMask))
                {
                    if (hit.distance > bestDist)
                    {
                        bestPos = startPos + hit.distance / dist * dir;
                        bestDist = hit.distance;
                    }
                }
                else
                {
                    bestPos = worldPos;
                    break;
                }
            }
            
            waveB[i] = originRotation * Quaternion.Inverse(originRotation) * (bestPos - originPosition);
            GameObject particle = Instantiate(particlePrefab, bestPos, Quaternion.identity);
            particles.Add(particle);
            
            // Deal damage
            DealDamageAtPosition(bestPos);
        }
    }
    
    private void DealDamageAtPosition(Vector3 position)
    {
        // If owner is null or is the player, damage enemies
        if (owner == null || owner == PlayerObject.Player)
        {
            List<Critter> hitList = new();
            int count = Physics.OverlapSphereNonAlloc(position, 6.0f, cachedColliders, 1 << LayerMask.NameToLayer("Characters"));
            for (int j = 0; j < count; j++)
            {
                Critter critter = cachedColliders[j].transform.root.GetComponent<Critter>();
                if (critter != null && !hitList.Contains(critter))
                {
                    hitList.Add(critter);
                    critter.TryDamage(Random.Range(minDamage, maxDamage), Skills.ESkillTestResult.Success);
                }
            }
        }
        else
        {
            // Enemy cast - damage player
            Vector3 playerPos = PlayerObject.Player.transform.position;
            Vector3 off = playerPos - position;
            if (off.magnitude < 6.0f)
            {
                if (!Physics.Raycast(position, off.normalized, 6.0f, LayerMasks.EnvironmentAndCeiling))
                {
                    int damage = Random.Range(minDamage, maxDamage);
                    if (Magic.sMagic != null && Magic.sMagic.IsSpellActive(Magic.ESpell.ResistBlows))
                    {
                        damage /= 2;
                    }
                    PlayerObject.Player.Damage(Skills.ESkillTestResult.Success, damage, EDamageType.Damage);
                }
            }
        }
    }
    
    private Vector3 RandomInCircle()
    {
        Vector3 r = Random.insideUnitSphere;
        r.y = 0.0f;
        return r;
    }
    
    private void DestroyWaveParticles(List<GameObject> particles)
    {
        if (particles != null)
        {
            foreach (GameObject go in particles)
            {
                if (go != null)
                {
                    Destroy(go);
                }
            }
            particles.Clear();
        }
    }
    
    private void CleanupAndDestroy()
    {
        DestroyWaveParticles(wave1Particles);
        DestroyWaveParticles(wave2Particles);
        DestroyWaveParticles(wave3Particles);
        
        if (transformOwner != null)
        {
            Destroy(transformOwner);
        }
        
        LevelLoader.worldObj.Remove(this);
        Destroy(gameObject);
    }
    
    protected override void PopulateSaveData(LevelObjectSaveData data)
    {
        base.PopulateSaveData(data);
        
        if (data is CascadingSpellEffectSaveData effectData)
        {
            effectData.currentState = (int)currentState;
            effectData.stateTimer = stateTimer;
            effectData.isPlayerOwned = (owner == PlayerObject.Player);
            
            // Store owner reference
            if (!effectData.isPlayerOwned && owner is UUObject uuOwner)
            {
                effectData.ownerObjectIndex = uuOwner.objectIndex;
            }
            
            // Store particle prefab name
            if (particlePrefab == Magic.sMagic.sheetLightningParticle)
            {
                effectData.particlePrefabName = "SheetLightning";
            }
            else if (particlePrefab == Magic.sMagic.flameWindParticle)
            {
                effectData.particlePrefabName = "FlameWind";
            }
            
            effectData.minDamage = minDamage;
            effectData.maxDamage = maxDamage;
            effectData.originPosition = originPosition;
            effectData.originRotation = originRotation;
            
            // Save wave positions
            effectData.wave1Positions = firstWave != null ? (Vector3[])firstWave.Clone() : null;
            effectData.wave2Positions = secondWave != null ? (Vector3[])secondWave.Clone() : null;
            effectData.wave3Positions = thirdWave != null ? (Vector3[])thirdWave.Clone() : null;
        }
    }
    
    protected override void RestoreFromSaveData(LevelObjectSaveData data)
    {
        base.RestoreFromSaveData(data);
        
        if (data is CascadingSpellEffectSaveData effectData)
        {
            currentState = (EState)effectData.currentState;
            stateTimer = effectData.stateTimer;
            
            // Restore owner reference
            if (effectData.isPlayerOwned)
            {
                owner = PlayerObject.Player;
            }
            else if (effectData.ownerObjectIndex > 0)
            {
                UUObject ownerObj = LevelLoader.GetObj(effectData.ownerObjectIndex);
                if (ownerObj != null)
                {
                    owner = ownerObj.GetComponent<MonoBehaviour>();
                }
            }
            
            // Restore particle prefab
            if (effectData.particlePrefabName == "SheetLightning")
            {
                particlePrefab = Magic.sMagic.sheetLightningParticle;
            }
            else if (effectData.particlePrefabName == "FlameWind")
            {
                particlePrefab = Magic.sMagic.flameWindParticle;
            }
            
            minDamage = effectData.minDamage;
            maxDamage = effectData.maxDamage;
            originPosition = effectData.originPosition;
            originRotation = effectData.originRotation;
            
            // Restore wave positions
            firstWave = effectData.wave1Positions ?? new Vector3[] { new(-2.0f, 0.0f, 5.0f), new(2.0f, 0.0f, 5.0f) };
            secondWave = effectData.wave2Positions ?? new Vector3[] { new(-4.0f, 0.0f, 9.0f), new(0.0f, 0.0f, 9.0f), new(4.0f, 0.0f, 9.0f) };
            thirdWave = effectData.wave3Positions ?? new Vector3[] { new(-6.0f, 0.0f, 13.0f), new(-2.0f, 0.0f, 13.0f), new(2.0f, 0.0f, 13.0f), new(6.0f, 0.0f, 13.0f) };
            zerothWave = new Vector3[] { Vector3.zero };
            
            wave1Particles = new List<GameObject>();
            wave2Particles = new List<GameObject>();
            wave3Particles = new List<GameObject>();
            
            // Re-spawn particles for already-spawned waves based on current state
            Matrix4x4 transform = Matrix4x4.TRS(originPosition, originRotation, Vector3.one);
            
            if ((int)currentState >= (int)EState.WaitForWave2)
            {
                // Wave 1 was already spawned
                foreach (Vector3 localPos in firstWave)
                {
                    Vector3 worldPos = transform.MultiplyPoint3x4(localPos);
                    GameObject particle = Instantiate(particlePrefab, worldPos, Quaternion.identity);
                    wave1Particles.Add(particle);
                }
            }
            
            if ((int)currentState >= (int)EState.WaitForWave3)
            {
                // Wave 2 was already spawned
                foreach (Vector3 localPos in secondWave)
                {
                    Vector3 worldPos = transform.MultiplyPoint3x4(localPos);
                    GameObject particle = Instantiate(particlePrefab, worldPos, Quaternion.identity);
                    wave2Particles.Add(particle);
                }
            }
            
            if ((int)currentState >= (int)EState.Cleanup1)
            {
                // Wave 3 was already spawned
                foreach (Vector3 localPos in thirdWave)
                {
                    Vector3 worldPos = transform.MultiplyPoint3x4(localPos);
                    GameObject particle = Instantiate(particlePrefab, worldPos, Quaternion.identity);
                    wave3Particles.Add(particle);
                }
            }
            
            initialized = true;
        }
    }
    
    public override ObjectSaveData SaveToData()
    {
        CascadingSpellEffectSaveData data = new CascadingSpellEffectSaveData();
        PopulateSaveData(data);
        
        return new ObjectSaveData
        {
            objectTypeName = GetType().Name,
            objectType = 0,
            objectIndex = objectIndex,
            level = levelIndex,
            jsonData = JsonUtility.ToJson(data)
        };
    }
    
    public override void LoadFromData(ObjectSaveData objData)
    {
        if (objData == null || string.IsNullOrEmpty(objData.jsonData))
            return;
        
        CascadingSpellEffectSaveData data = JsonUtility.FromJson<CascadingSpellEffectSaveData>(objData.jsonData);
        RestoreFromSaveData(data);
    }
}


