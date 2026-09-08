using UnityEngine;
using Random = UnityEngine.Random;

[System.Serializable]
public class ProjectileSaveData : UUObjectSaveData
{
    // Projectile-specific state
    public bool doDamage;
    public bool isPlayerProjectile;  // For collision layers and skill checks
}

public class Projectile : UUObject
{
    public bool doDamage = true;
    public GameObject hitEffect;
    public float hitEffectScale;

    public GameObject projectileOwner;
    public bool createdByCursedEntity = false;
    
    [Tooltip("Radius of the projectile capsule for collision detection")]
    public float collisionRadius = 0.1f;
    
    [Tooltip("Half-length of the projectile capsule (from center to end)")]
    public float collisionHalfLength = 0.3f;

    public Vector3 oldPos = Vector3.zero;
    private Collider[] overlapCache = new Collider[8];

    public override void PostLoadInitialize(bool restoredFromSave = false)
    {
        base.PostLoadInitialize(restoredFromSave);
        // projectiles have no meaningful quality variance; normalize so stacks always match.
        quality = 63;
    }

    private void HitSomething()
    {
        // depending on missile skill you can keep more weapon projectiles
        // think of it like being better at finding and recovering them
        
        Skills.ESkillTestResult result = Skills.ESkillTestResult.Failure;

        if (type is EObjectType.SlingStone or EObjectType.Arrow or EObjectType.CrossbowBolt)
        {
            if (projectileOwner == PlayerObject.Player.gameObject)
            {
                result = Skills.GetResult(Skills.GetSkill(ESkill.Missile), 0);
            }
            else
            {
                result = Random.value < 0.7f ? Skills.ESkillTestResult.Failure : Skills.ESkillTestResult.Success;
            }
        }
        
        Rigidbody rb = transform.root.GetComponentInChildren<Rigidbody>();

        if (hitEffect != null)
        {
            Vector3 offset = Vector3.zero;
            if (rb != null)
            {
                offset = hitEffectScale * rb.linearVelocity.normalized;
            }
            GameObject eff = Instantiate(hitEffect, transform.position - offset, transform.rotation);
            eff.transform.localScale = new Vector3(hitEffectScale, hitEffectScale, hitEffectScale);
        }

        if (result < Skills.ESkillTestResult.Success)
        {
            Utils.DestroyItem(this);
        }
        else
        {
            if (rb != null)
            {
                // Stop the projectile (bounce a bit)
                rb.linearVelocity *= Random.Range(-0.1f, 0.0f);
                rb.useGravity = true; // for arrows/bolts so they fall and rest
            }
        }
    }

    public override void Update()
    {
        base.Update();

        // Check for collisions using overlap detection
        if (doDamage && oldPos != Vector3.zero)
        {
            // Calculate point0 (back of projectile last frame) and point1 (front of projectile this frame)
            Vector3 point0 = oldPos - transform.forward * collisionHalfLength;
            Vector3 point1 = transform.position + transform.forward * collisionHalfLength;
            
            Debug.DrawLine(point0, point1, Color.red, 5.0f);
            
            // First check for character/player collisions using capsule overlap
            // player shouldn't collide with player
            int characterLayerMask = 1 << LayerMask.NameToLayer("Characters");
            if (projectileOwner != PlayerObject.Player.gameObject)
            {
                characterLayerMask |= 1 << LayerMask.NameToLayer("Player");
            }
            
            int numOverlaps = Physics.OverlapCapsuleNonAlloc(point0, point1, collisionRadius, overlapCache, characterLayerMask);
            
            for (int i = 0; i < numOverlaps; i++)
            {
                Collider col = overlapCache[i];
                if (col != null)
                {
                    // CharacterController components ARE returned by OverlapCapsuleNonAlloc!
                    CharacterController cc = col as CharacterController;
                    if (cc != null && cc.enabled && cc.gameObject != projectileOwner && cc.gameObject.activeSelf)
                    {
                        HitSomething(cc.transform);
                        oldPos = transform.position;
                        return; // Exit early after hitting a character
                    }
                }
            }
            
            // Then check for environment collisions using sphere overlap
            int environmentLayerMask = LayerMasks.EnvironmentAndCeiling;
            numOverlaps = Physics.OverlapCapsuleNonAlloc(point0, point1, collisionRadius, overlapCache, environmentLayerMask);
            
            for (int i = 0; i < numOverlaps; i++)
            {
                Collider col = overlapCache[i];
                if (col != null)
                {
                    Door door = col.GetComponentInParent<Door>();
                    if (door != null && projectileOwner == PlayerObject.Player.gameObject)
                    {
                        HitSomething(door.transform);
                        oldPos = transform.position;
                        return;
                    }

                    // Hit environment - stop projectile
                    DrawAxes(transform.position, Color.red, 5.0f);
                    doDamage = false;
                    HitSomething();
                    break;
                }
            }
        }
        
        oldPos = transform.position;
    }

    private void DrawAxes(Vector3 pos, Color col, float time)
    {
        Debug.DrawLine(pos - Vector3.up, pos + Vector3.up, col, time);
        Debug.DrawLine(pos - Vector3.left, pos + Vector3.left, col, time);
        Debug.DrawLine(pos - Vector3.back, pos + Vector3.back, col, time);
    }

    private void HitSomething(Transform root)
    {
        if (doDamage)
        {
            if (root.gameObject != projectileOwner)
            {
                int damage = GetDamage();

                PlayerObject player = root.GetComponent<PlayerObject>();
                if (player != null)
                {
                    if (!Magic.sMagic.IsSpellActive(Magic.ESpell.MissileProtection))
                    {
                        if (type == EObjectType.Fireball && Magic.sMagic.IsSpellActive(Magic.ESpell.Flameproof))
                        {
                            damage = 0;
                        }

                        // Missiles and melee share the damage routine in the original (UW.EXE
                        // 0x259e7 and 0x2527e), so armour soaks an arrow exactly as it soaks a
                        // swing, by the protection covering wherever it struck.
                        damage = player.AbsorbWithArmour(damage, transform.position.y);
                        if (damage > 0)
                        {
                            player.Damage(Skills.ESkillTestResult.Success, damage, EDamageType.Damage);
                        }
                    }
                }
                else
                {
                    Critter critter = root.GetComponent<Critter>();
                    if (critter != null)
                    {
                        critter.TryDamage(damage, Skills.ESkillTestResult.Success);
                        PlayerObject.Player.SetLastEngagedInCombat(critter);
                    }
                    else if (projectileOwner == PlayerObject.Player.gameObject)
                    {
                        Door door = root.GetComponentInParent<Door>();
                        if (door != null)
                        {
                            door.TryDamage(damage, Skills.ESkillTestResult.Success);
                        }
                    }
                }

                doDamage = false;
                HitSomething();
            }
        }
    }

    private int GetDamageSkillBoost()
    {
        // The original adds nothing per character level, here or in melee: the chain that
        // works out a projectile's damage - UW.EXE 0x2b2bd, 0x258cf and 0x24cb5, each read
        // whole - never reads the level, the byte at P1[0x3d].
        int damage = 0;
        switch (type)
        {
        case EObjectType.MagicMissile:
            damage += Skills.GetSkill(ESkill.Casting) / 6;
            break;
        case EObjectType.LightningBolt:
            damage += Skills.GetSkill(ESkill.Casting) / 5;
            break;
        case EObjectType.Fireball:
            damage += Skills.GetSkill(ESkill.Casting) / 4;
            break;
        case EObjectType.SlingStone:
            damage += Skills.GetSkill(ESkill.Missile) / 6;
            break;
        case EObjectType.Arrow:
            damage += Skills.GetSkill(ESkill.Missile) / 5;
            break;
        case EObjectType.CrossbowBolt:
            damage += Skills.GetSkill(ESkill.Missile) / 4;
            break;
        }

        return damage;
    }

    private int GetBaseDamage()
    {
        int damage = 0;
        switch (type)
        {
        case EObjectType.Acid:
            damage = Random.Range(1, 3);
            break;
        case EObjectType.MagicMissile:
            damage = Random.Range(2, 4);
            break;
        case EObjectType.LightningBolt:
            damage = Random.Range(4, 7);
            break;
        case EObjectType.Fireball:
            damage = Random.Range(6, 9);
            break;
        case EObjectType.Knife: // mage weapon
            damage = Random.Range(3, 6);
            break;
        case EObjectType.SlingStone:
            damage = Random.Range(2, 4);
            break;
        case EObjectType.Arrow:
            damage = Random.Range(3, 5);
            break;
        case EObjectType.CrossbowBolt:
            damage = Random.Range(4, 7);
            break;
        }

        return damage;
    }

    private int GetDamage()
    {
        int damage = GetBaseDamage();
        if (projectileOwner == PlayerObject.Player.gameObject)
        {
            damage += GetDamageSkillBoost();
        }
        
        if (createdByCursedEntity)
        {
            damage /= 2;
        }
        
        return damage;
    }

    protected override void PopulateSaveData(LevelObjectSaveData data)
    {
        base.PopulateSaveData(data);
        
        if (data is ProjectileSaveData projData)
        {
            projData.doDamage = doDamage;
            projData.isPlayerProjectile = (projectileOwner == PlayerObject.Player?.gameObject);
        }
    }

    protected override void RestoreFromSaveData(LevelObjectSaveData data)
    {
        base.RestoreFromSaveData(data);
        
        if (data is ProjectileSaveData projData)
        {
            doDamage = projData.doDamage;
            
            // Set owner to player or null (enemy projectiles have no specific owner on reload)
            projectileOwner = projData.isPlayerProjectile ? PlayerObject.Player.gameObject : null;
        }
    }

    public override ObjectSaveData SaveToData()
    {
        ProjectileSaveData data = new ProjectileSaveData();
        PopulateSaveData(data);
        
        return new ObjectSaveData
        {
            objectTypeName = GetType().Name,
            objectType = (int)type,
            objectIndex = objectIndex,
            level = levelIndex,
            jsonData = JsonUtility.ToJson(data)
        };
    }

    public override void LoadFromData(ObjectSaveData objData)
    {
        if (objData == null || string.IsNullOrEmpty(objData.jsonData))
            return;
        
        ProjectileSaveData data = JsonUtility.FromJson<ProjectileSaveData>(objData.jsonData);
        RestoreFromSaveData(data);
    }
}
