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
                        // Same routine as a swing in the original (UW.EXE 0x259e7 and 0x2527e both
                        // call 0x24cb5), and the missile path picks a body part of its own before
                        // it gets there (0x25988), so a creature's armour soaks an arrow too.
                        damage = critter.AbsorbWithArmour(damage, transform.position.y);
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

    /// <summary>
    /// The Missile skill scales a physical missile's damage rather than adding to it, and the
    /// scale is a fraction of 256: 192 with no skill at all, eight more for every point of it.
    /// So an untrained shot does three quarters of the table's damage and a fully trained one
    /// a little over one and two thirds (UW.EXE 0x2b2ec-0x2b32b, read whole).
    /// </summary>
    private const int MissileScaleUntrained = 192;
    private const int MissileScalePerPoint = 8;
    private const int MissileScaleUnit = 256;

    /// <summary>
    /// Every shot the player takes rolls the skill at this difficulty, and only the two critical
    /// outcomes move the scale - by which the worst a trained archer can do is still better than
    /// the best an untrained one manages.
    /// </summary>
    private const int MissileRollDifficulty = 10;
    private const int MissileScaleCriticalFailure = -128;
    private const int MissileScaleCriticalSuccess = 192;

    /// <summary>
    /// The marker the missile table carries on the four physical projectiles. The original tests
    /// the row's third byte for exactly this before letting any of the character into the sum,
    /// which is how a fireball ends up owing nothing to the mage who cast it.
    /// </summary>
    private const int PhysicalMissileMarker = 0xC0;

    /// <summary>
    /// Nothing is ever rolled against a maximum lower than this: the routine that rolls the
    /// damage lifts one up to two before it starts (UW.EXE 0x24cd9). Melee shares that routine,
    /// but with this game's data only a scaled missile can arrive under two - a sling stone whose
    /// skill roll came out a critical failure below Missile 5 - so this is where it belongs. The
    /// unscaled rows are all five or more.
    /// </summary>
    private const int MinimumRolledMaximum = 2;

    /// <summary>
    /// The most damage this shot can do, before the roll: the missile table's own byte, scaled
    /// by the Missile skill where the row allows it.
    /// </summary>
    private int GetMaxDamage()
    {
        // Object types 16-23 are the eight rows of the missile table that carry a damage value;
        // 24-31 are the launchers, whose byte reads 3 and means nothing.
        if ((int)type < 16 || (int)type > 23)
        {
            return 0;
        }

        ObjectsData.MissileData row = DataLoader.sDataLoader.objectsData.missileStats[(int)type & 15];

        // Neither a creature's shot nor a spell takes anything from the player's skill. The
        // original decides both at once: it scales only when the shot is the player's and the
        // row carries the physical marker.
        if (projectileOwner != PlayerObject.Player.gameObject || row.marker != PhysicalMissileMarker)
        {
            return row.damage;
        }

        int missile = Skills.GetSkill(ESkill.Missile);
        int scale = MissileScaleUntrained + MissileScalePerPoint * missile;
        switch (Skills.GetResult(missile, MissileRollDifficulty))
        {
        case Skills.ESkillTestResult.CriticalFailure:
            scale += MissileScaleCriticalFailure;
            break;
        case Skills.ESkillTestResult.CriticalSuccess:
            scale += MissileScaleCriticalSuccess;
            break;
        }

        return Mathf.Max(MinimumRolledMaximum, row.damage * scale / MissileScaleUnit);
    }

    private int GetDamage()
    {
        int damage;

        if (type == EObjectType.Knife)
        {
            // A mage weapon this project added rather than the original: type 27 lands among the
            // launchers, which have no damage of their own, so it keeps its hand-written roll.
            damage = Random.Range(3, 6);
        }
        else
        {
            // The table byte is a maximum and the original rolls it down, the same way and with
            // the same helper melee damage uses - the two share the routine that finally takes
            // the hit points off (UW.EXE 0x2b2bd to 0x258cf to 0x24cb5, each read whole).
            damage = Utils.GetDamageRoll(GetMaxDamage());
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
