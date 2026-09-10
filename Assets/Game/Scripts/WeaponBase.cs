using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem.Controls;
using Random = UnityEngine.Random;

public class WeaponBase : UUObject
{
    protected enum EState
    {
        Reset,
        Hold,
        Prepare,
        Swing,
        PostSwing
    }

    public Vector3[] positions =
    {
        new(0.2f, -0.2f, 1.0f),
        new(0.2f, -0.2f, 1.0f),
        new(0.2f, -0.2f, 1.0f),
        new(0.2f, -0.2f, 1.0f),
        new(0.2f, -0.2f, 1.0f),
    };

    public Vector3[] rotations =
    {
        new(0.2f, -0.2f, 1.0f),
        new(0.2f, -0.2f, 1.0f),
        new(0.2f, -0.2f, 1.0f),
        new(0.2f, -0.2f, 1.0f),
        new(0.2f, -0.2f, 1.0f),
    };

    public float[] times =
    {
        0.5f,
        0.6f,
        0.2f,
        0.1f,
        0.5f,
    };

    public float attackTime = 0.1f;
    public float hitPause = 0.3f;

    public AudioClip swingSound;
    public AudioClip prepareSound;
    public ESkill skill = ESkill.Sword;

    public SphereCollider tipCollider;

    protected float prepareTime;
    protected EState state;
    private float swingTime;
    private bool hasAttacked;
    private float attackHit;
    private float resetTime;
    private float postSwingTime;
    private bool wasCancelled;
    private float lastSwingEndTime = -999f;

    private int enchantmentIndex;
    
    private readonly Collider[] cachedColliders = new Collider[32];

    private Vector3 position;
    private Vector3 rotation;
    //private bool previousLeftHanded;
    private Vector3 originalScale;

    private enum EAttack
    {
        Slash,
        Bash,
        Stab
    }

    // these are the most powerful attacks per weapon
    private static readonly EAttack[] attacks =
    {
        EAttack.Slash, EAttack.Slash, EAttack.Slash, // axes
        EAttack.Stab, EAttack.Stab, EAttack.Slash, EAttack.Slash, // swords
        EAttack.Bash, EAttack.Bash, EAttack.Bash, // maces
        EAttack.Slash, EAttack.Slash, EAttack.Slash, EAttack.Slash, EAttack.Bash, // jewelled etc
        EAttack.Stab // fist
    };

    public override void Initialize(ushort[] objData, byte[] critterData)
    {
        base.Initialize(objData, critterData);
        
        // Initialize originalScale from current transform scale (or Vector3.one if zero)
        originalScale = transform.localScale;
        if (originalScale.sqrMagnitude < 0.0001f)
        {
            originalScale = Vector3.one;
        }

        UpdateEnchantmentState();

        // identify Caliburn immediately
        if (type is EObjectType.ShinySword)
        {
            loreResult = Skills.ESkillTestResult.CriticalSuccess;
        }
    }

    protected virtual void ChangeState(EState newState)
    {
        state = newState;
        switch (state)
        {
        case EState.Reset:
            break;
        case EState.Prepare:
            prepareTime = 0.0f;
            break;
        case EState.Hold:
            WeaponChargeGem.sChargeGem.power = 0.0f;
            resetTime = 0.0f;
            hasAttacked = false;
            break;
        case EState.Swing:
            swingTime = 0.0f;
            Utils.PlayClip2d(swingSound);
            TutorialManager.NotifyAttack();
            break;
        case EState.PostSwing:
            postSwingTime = 0.0f;
            break;
        }
    }

    private UUObject FindObjectToDamage(float maxDistance)
    {
        UUObject best = null;
        float bestCost = 0.0f;

        // doors are marked Environment so things collide with them. need to gather them anyway
        int mask = (1 << LayerMask.NameToLayer("Characters")) | (1 << LayerMask.NameToLayer("Objects")) | LayerMasks.EnvironmentAndCeiling;

        Vector3 cameraPos = PlayerObject.Player.mainCamera.transform.position;
        List<UUObject> alreadyChecked = new List<UUObject>();
        int count = Physics.OverlapSphereNonAlloc(
                     cameraPos + 0.5f * maxDistance * PlayerObject.Player.mainCamera.transform.forward, 0.5f * maxDistance, cachedColliders, mask);
        for (int i = 0; i < count; i++)
        {
            Collider col = cachedColliders[i];
            UUObject obj = col.transform.root.gameObject.GetComponent<UUObject>();
            if (obj != null && obj.IsDamageable() && !alreadyChecked.Contains(obj))
            {
                alreadyChecked.Add(obj);

                Vector3 objPos;
                CharacterController cc = obj.GetComponentInChildren<CharacterController>();
                if (cc != null)
                {
                    objPos = obj.gameObject.transform.TransformPoint(cc.center);
                }
                else
                {
                    objPos = obj.cachedRenderer != null ? obj.cachedRenderer.bounds.center : obj.transform.position;
                }
                Vector3 direction = objPos - cameraPos;
                float distance = direction.magnitude;

                Rigidbody rb = obj.GetComponentInChildren<Rigidbody>();
                if (rb == null || rb.linearVelocity.magnitude < 1.0f)
                {
                    direction /= distance;
                    // include objects so that you can't hit hidden doors through decals
                    int envLayerMask = LayerMasks.EnvironmentAndCeiling | 1 << LayerMask.NameToLayer("Objects");
                    bool hit = Physics.Raycast(cameraPos, direction, out RaycastHit castHit, distance, envLayerMask);
                    if (!hit || castHit.transform.root.gameObject == obj.gameObject)
                    {
                        float dot = Vector3.Dot(direction, PlayerObject.Player.mainCamera.transform.forward);
                        float cost = Mathf.Pow(dot, 1.0f) * (20.0f - distance);
                        if (cost > bestCost)
                        {
                            best = obj;
                            bestCost = cost;
                        }
                    }
                }
            }
        }
        return best;
    }

    public override void Update()
    {
        base.Update();
        
        // Detect handedness change and reset position/rotation
        // this breaks the positioning of objects in barrels, so don't do it
        #if false
        bool currentLeftHanded = PlayerData.sData.leftHanded;
        if (previousLeftHanded != currentLeftHanded)
        {
            // Reset to base position/rotation for new handedness
            position = positions[0];
            rotation = rotations[0];
            if (currentLeftHanded)
            {
                position.x = -position.x;
                rotation.y = -rotation.y;
            }
            // Flip scale: weapons flip for left-handed, fist (left-handed model) flips for right-handed
            Vector3 currentScale = originalScale.sqrMagnitude > 0.0001f ? originalScale : Vector3.one;
            bool shouldFlip = (type == EObjectType.Fist) ? !currentLeftHanded : currentLeftHanded;
            if (shouldFlip)
            {
                currentScale.x = -Mathf.Abs(currentScale.x);
            }
            else
            {
                currentScale.x = Mathf.Abs(currentScale.x);
            }
            transform.localScale = currentScale;
            previousLeftHanded = currentLeftHanded;
        }
        #endif

        EInvSlot slot = PlayerData.sData.leftHanded ? EInvSlot.LeftHand : EInvSlot.RightHand;
        bool hold = Inventory.sInv.invSlotContents[(int)slot] == this || (type == EObjectType.Fist && Inventory.sInv.invSlotContents[(int)slot] == null);

        // Keep gamepad vs mouse/keyboard controls separate at the callsite level.
        // PlayerObject refreshes this too, but weapons can update independently.
        GameInput.RefreshLastActiveDevice();

        bool usingKeyboardMouse = GameInput.LastActiveDevice == GameInputDevice.MouseKeyboard;
        bool panelBlocksWeapon = usingKeyboardMouse
            && (PlayerPanelState.IsEffectivelyExploringInventory || PlayerPanelState.IsEffectivelyExploringMagic
                || (PlayerPanelState.ArePanelsAvailable
                    && StatsPanel.sStatsPanel != null
                    && StatsPanel.sStatsPanel.ShouldDismissWithEscape()));
        bool cancelHeld = usingKeyboardMouse ? IsCancelHeldMouseKeyboard() : IsCancelHeldGamepad();
        bool cancelPressed = usingKeyboardMouse ? IsCancelPressedMouseKeyboard() : IsCancelPressedGamepad();
        bool attackPressed = usingKeyboardMouse ? IsAttackPressedMouseKeyboard() : IsAttackPressedGamepad();
        bool attackHeld = usingKeyboardMouse ? IsAttackHeldMouseKeyboard() : IsAttackHeldGamepad();

        if (wasCancelled)
        {
            if (!cancelHeld && attackPressed && !panelBlocksWeapon)
            {
                wasCancelled = false;
            }
        }
        bool pressingTrigger = !wasCancelled && attackHeld && !panelBlocksWeapon;
        
        switch (state)
        {
        case EState.Reset:
            SetPosition(hold);
            if (!cancelHeld)
            {
                if (hold && (resetTime < 3.0f || pressingTrigger) && !PlayerObject.Player.isInWater)
                {
                    ChangeState(EState.Hold);
                }
            }
            break;
        case EState.Hold:
            SetPosition(hold);
            if (!hold || PlayerObject.Player.isInWater)
            {
                ChangeState(EState.Reset);
            }
            else if (pressingTrigger)
            {
                ChangeState(EState.Prepare);
            }
            else
            {
                resetTime += Time.deltaTime;
                if (resetTime >= 3.0f)
                {
                    ChangeState(EState.Reset);
                }
            }
            break;
        case EState.Prepare:
            SetPosition(hold);
            if (!hold || PlayerObject.Player.isInWater)
            {
                ChangeState(EState.Reset);
            }
            else if (attackHeld && !panelBlocksWeapon)
            {
                bool wasPrepared = prepareTime > 0.5f;
                float chargeSpeed = GetChargeSpeed();
                prepareTime += Time.deltaTime * chargeSpeed;
                if (!wasPrepared && prepareTime > 0.5f)
                {
                    Utils.PlayClip2d(prepareSound);
                }
                WeaponChargeGem.sChargeGem.power = Mathf.Clamp(prepareTime - 0.5f, 0.0f, 1.0f);

                if (cancelPressed)
                {
                    wasCancelled = true;
                    ChangeState(EState.Reset);
                }
            }
            else
            {
                if (prepareTime > 0.5f)
                {
                    ChangeState(EState.Swing);
                }
                else
                {
                    ChangeState(EState.Reset);
                }
            }
            break;
        case EState.Swing:
            if (hasAttacked && attackHit > 0.0f)
            {
                attackHit -= Time.deltaTime;
            }
            else
            {
                swingTime += Time.deltaTime;
            }
            if (!hasAttacked && swingTime > attackTime)
            {
                attackHit = Attack() ? hitPause : 0.0f;
                hasAttacked = true;
            }
            // this needs to go before updating swingTime so that the swing follows the correct curve
            SetPosition(hold);
            if (swingTime > times[3])
            {
                ChangeState(EState.PostSwing);
            }
            break;
        case EState.PostSwing:
            SetPosition(hold);
            postSwingTime += Time.deltaTime;
            if (postSwingTime > times[4])
            {
                lastSwingEndTime = Time.time;
                ChangeState(EState.Hold);
            }
            break;
        }

        ApplyLoweredWeaponVisuals();
    }

    private static bool IsCancelPressedMouseKeyboard()
    {
        return GameInput.CurrentKeyboard?.xKey.wasPressedThisFrame ?? false;
    }

    private static bool IsCancelHeldMouseKeyboard()
    {
        return GameInput.CurrentKeyboard?.xKey.isPressed ?? false;
    }

    private static bool IsAttackPressedMouseKeyboard()
    {
        return GameInput.CurrentMouse?.rightButton.wasPressedThisFrame ?? false;
    }

    private static bool IsAttackHeldMouseKeyboard()
    {
        return GameInput.CurrentMouse?.rightButton.isPressed ?? false;
    }

    private static ButtonControl GetAttackTriggerGamepad()
    {
        var gp = GameInput.CurrentGamepad;
        if (gp == null || PlayerData.sData == null)
            return null;
        return PlayerData.sData.leftHanded ? gp.leftTrigger : gp.rightTrigger;
    }

    private static bool IsAttackPressedGamepad()
    {
        return GetAttackTriggerGamepad()?.wasPressedThisFrame ?? false;
    }

    private static bool IsAttackHeldGamepad()
    {
        return GetAttackTriggerGamepad()?.isPressed ?? false;
    }

    private static bool IsCancelPressedGamepad()
    {
        return GameInput.CurrentGamepad?.bButton.wasPressedThisFrame ?? false;
    }

    private static bool IsCancelHeldGamepad()
    {
        return GameInput.CurrentGamepad?.bButton.isPressed ?? false;
    }

    /// <summary>Direct child transform excluded from lowered-hide toggling (e.g. ranged projectile).</summary>
    protected virtual Transform GetLoweredHideExemptChild()
    {
        return null;
    }

    private void ApplyLoweredWeaponVisuals()
    {
        Transform exempt = GetLoweredHideExemptChild();
        // Hide only once the lowered slide has finished (DampedApproach), not on the first Reset frame.
        bool hideLowered = state == EState.Reset && IsAtLoweredRestPose();
        bool show = !hideLowered;
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (exempt != null && child == exempt)
            {
                continue;
            }

            if (child.gameObject.activeSelf != show)
            {
                child.gameObject.SetActive(show);
            }
        }
    }

    /// <summary>Undo first-person lowered hiding so inventory/world meshes are visible after unequip.</summary>
    private void RestoreWeaponVisualChildrenForUnequip()
    {
        Transform exempt = GetLoweredHideExemptChild();
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            bool wantActive = exempt == null || child != exempt;
            if (child.gameObject.activeSelf != wantActive)
            {
                child.gameObject.SetActive(wantActive);
            }
        }
    }

    private bool IsAtLoweredRestPose()
    {
        const float posEpsilon = 0.01f;
        return (position - positions[0]).sqrMagnitude < posEpsilon * posEpsilon;
    }

    private void SetPosition(bool hold)
    {
        if (hold)
        {
            int stateIndex = (int)state;
            float time = times[stateIndex];
            if (state == EState.Swing)
            {
                float t = Mathf.Min(swingTime / times[3], 1.0f);
                position = Vector3.Lerp(positions[stateIndex - 1], positions[stateIndex], t * t);
                rotation = Vector3.Lerp(rotations[stateIndex - 1], rotations[stateIndex], t * t);
            }
            else
            {
                position = Utils.DampedApproach(position, positions[stateIndex], time);
                rotation = Utils.DampedApproach(rotation, rotations[stateIndex], time);
            }
            
            // Flip for left-handed players
            Vector3 finalPosition = position;
            Vector3 finalRotation = rotation;
            if (PlayerData.sData.leftHanded)
            {
                finalPosition.x = -finalPosition.x;
                finalRotation.y = -finalRotation.y;
            }
            
            transform.SetLocalPositionAndRotation(finalPosition, Quaternion.Euler(finalRotation));

            // cast a sphere from the player's center to the tip of the weapon,
            // then pull back the weapon along this line to keep out of the environment
            Vector3 centerToTip = 0.6f * transform.forward;
            Vector3 tip = transform.position + centerToTip;
            float radius = 0.1f;
            if (tipCollider != null)
            {
                centerToTip = tipCollider.transform.position - transform.position;
                tip = tipCollider.transform.position;
                radius = tipCollider.radius;
            }
            Vector3 start = PlayerObject.Player.transform.position;
            float dist = (tip - start).magnitude;
            Vector3 dir = (tip - start) / dist;

            RaycastHit hit;
            int layerMask = LayerMasks.EnvironmentAndCeiling;
            if (Physics.SphereCast(start, radius, dir, out hit, dist, layerMask))
            {
                transform.position = start + hit.distance * dir - centerToTip;
            }
        }
    }

    private float GetChargeSpeed()
    {
        ObjectsData.MeleeData meleeData = DataLoader.sDataLoader.objectsData.weaponStats[(int)type & 15];
        
        // WeaponSpeed ranges from 5-30 (higher = faster)
        float weaponSpeedNormalized = (meleeData.WeaponSpeed - 5.0f) / 25.0f; // 0.0 to 1.0
        
        // Skills range from 0-30 (higher = faster)
        float attackSkillNormalized = Skills.GetSkill(ESkill.Attack) / 30.0f; // 0.0 to 1.0
        float dexterityNormalized = PlayerData.sData.dexterity / 30.0f; // 0.0 to 1.0
        float weaponSkillNormalized = Skills.GetSkill(skill) / 30.0f; // 0.0 to 1.0
        
        // Base speed: 0.5x to 2.0x multiplier based on weapon speed
        // Skill bonus: up to 1.5x additional multiplier from skills
        float baseSpeed = 0.5f + weaponSpeedNormalized * 1.5f;
        float skillBonus = (attackSkillNormalized + dexterityNormalized + weaponSkillNormalized) / 3.0f * 1.5f;
        
        float chargeSpeed = baseSpeed + skillBonus;
        
        // Clamp chargeSpeed so time to reach prepareTime = 1.0 ranges from 0.1s to 5s
        // time = 1.0 / chargeSpeed, so chargeSpeed must be between 0.2 and 10.0
        return Mathf.Clamp(chargeSpeed, 0.2f, 10.0f);
    }

    /// <summary>True when the player is preparing, swinging, in post-swing, or within 2 seconds of finishing a swing (sprint blocked).</summary>
    public bool IsSprintBlocked()
    {
        if (state == EState.Prepare || state == EState.Swing || state == EState.PostSwing)
            return true;
        return Time.time - lastSwingEndTime < 2.0f;
    }

    public int GetMaxDamage()
    {
        ObjectsData.MeleeData meleeData = DataLoader.sDataLoader.objectsData.weaponStats[(int)type & 15];
        int weaponDamage = 0;
        switch (attacks[(int)type & 15])
        {
        case EAttack.Slash:
            weaponDamage = meleeData.Slash;
            break;
        case EAttack.Bash:
            weaponDamage = meleeData.Bash;
            break;
        case EAttack.Stab:
            weaponDamage = meleeData.Stab;
            break;
        }
        int playerDamage = PlayerData.sData.strength / 9;
        int weaponBonusDamage = 0;
        if (isEnchanted && !string.IsNullOrEmpty(enchantmentName) && enchantmentIndex >= 8) // damage enchantment
        {
            weaponBonusDamage = 1 + enchantmentIndex - 8;
        }

        int maxDamage = weaponDamage + playerDamage + weaponBonusDamage;

        if (Magic.sMagic.IsSpellActive(Magic.ESpell.Cursed))
        {
            maxDamage /= 2;
        }
        
        return maxDamage;
    }

    public override void TryDamage(int damage, Skills.ESkillTestResult result)
    {
        // Check durability first - if it's 255, weapon is indestructible
        ObjectsData.MeleeData meleeData = DataLoader.sDataLoader.objectsData.weaponStats[(int)type & 15];
        int durability = meleeData.Durability;
        if (durability != 255)
        {
            // Calculate damage to weapon - depends on durability
            int damageToWeapon = Random.Range(0, damage) * 8 / durability;
            if (damageToWeapon > 0)
            {
                quality -= damageToWeapon;
                if (quality <= 0)
                {
                    Messages.Add($"Your {singularName} was destroyed.");
                    Unequip();
                    Inventory.sInv.DestroyEquippedItem(this);
                }
                else
                {
                    Messages.Add($"Your {singularName} was damaged.");
                }
            }
        }
    }

    protected virtual bool Attack()
    {
        UUObject obj = FindObjectToDamage(3.0f);
        if (obj != null)
        {
            int hitChance = Skills.GetSkill(ESkill.Attack) / 2
                            + Skills.GetSkill(skill)
                            + PlayerData.sData.dexterity / 7;
            // Easy mode: add 5 to damage check
            if (PlayerData.sData.easy)
            {
                hitChance += 5;
            }
            if (isEnchanted && !string.IsNullOrEmpty(enchantmentName) && enchantmentIndex < 8) // an accuracy enchantment
            {
                hitChance += 1 + enchantmentIndex;
            }
            Skills.ESkillTestResult res = Skills.GetResult(hitChance, obj.GetDefence());
            switch (res)
            {
            case Skills.ESkillTestResult.CriticalFailure:
                // damage weapon
                if (type == EObjectType.Fist)
                {
                    int weaponDamage = Random.Range(1, 4);
                    int finalHp = PlayerData.sData.hp - weaponDamage;
                    if (finalHp <= 0)
                    {
                        // make sure punching can't kill you
                        weaponDamage = PlayerData.sData.hp - 1;
                    }
                    if (weaponDamage > 0)
                    {
                        PlayerObject.Player.Damage(res, weaponDamage, EDamageType.Direct);
                    }
                }
                else
                {
                    TryDamage(5, res);
                }
                break;
            case Skills.ESkillTestResult.Failure:
                if (obj is Critter critterMiss)
                {
                    critterMiss.ReactToMissedAttack();
                }
                break;
            case Skills.ESkillTestResult.Success:
            case Skills.ESkillTestResult.CriticalSuccess:
                {
                    ObjectsData.MeleeData meleeData =  DataLoader.sDataLoader.objectsData.weaponStats[(int)type & 15];
                    int maxDamage = GetMaxDamage();
                    int rolledDamage = Utils.GetDamageRoll(maxDamage);
                    int prepTime = (int)(100.0f * Mathf.Clamp(prepareTime - 0.5f, 0.0f, 1.0f));
                    int scaledForDamage = meleeData.MinCharge +
                                          (meleeData.MaxCharge - meleeData.MinCharge) * prepTime / 100;
                    int damage = scaledForDamage * rolledDamage / 128;

                    if (Cheats.sCheats.boostDamageFromPlayer)
                    {
                        damage *= 10;
                    }

                    //Debug.Log($"Damage: max {maxDamage}; roll {rolledDamage}; prep {prepTime}; scale {scaledForDamage}; dam {damage}");

                    Critter critter = obj as Critter;
                    if (critter != null)
                    {
                        // The creature's own armour eats the blow, the same way the player's eats
                        // one coming the other way: the original runs both through one routine and
                        // subtracts the protection covering wherever the blow landed
                        // (UW.EXE 0x24dc1, 0x24e14).
                        damage = critter.AbsorbWithArmour(damage, PlayerObject.Player.GetSwingHeight());
                    }

                    obj.TryDamage(damage, res);
                
                    PlayerObject.Rumble(0.05f, 0.4f, 0.2f);
                
                    if (critter != null)
                    {
                        PlayerObject.Player.SetLastEngagedInCombat(critter);
                        
                        // If attack succeeded but did zero damage, ensure enemy reacts if facing player
                        if (damage == 0)
                        {
                            critter.ReactToMissedAttack();
                        }
                    }
                }
                break;
            }
            if (obj is Critter)
            {
                Critter c = obj as Critter;
                if (c.attitude == Critter.EAttitude.Hostile)
                {
                    Music.InCombat();
                }
            }

            return res >= Skills.ESkillTestResult.Success;
        }
        else
        {
            // Check for wall hit when no damageable object is found
            Vector3 cameraPos = PlayerObject.Player.mainCamera.transform.position;
            Vector3 forward = PlayerObject.Player.mainCamera.transform.forward;
            float maxDistance = 1.5f;
            int wallLayerMask = LayerMasks.EnvironmentAndCeiling;
            
            if (Physics.Raycast(cameraPos, forward, out RaycastHit wallHit, maxDistance, wallLayerMask))
            {
                // Pull impact point out of wall by 0.1m so spark doesn't intersect and sound isn't occluded
                Vector3 sparkPosition = wallHit.point + wallHit.normal * 0.15f;
                
                if (ParticleSpawner.sParticleSpawner != null)
                {
                    GameObject particle = ParticleSpawner.SpawnParticle(EParticleType.SparkSplat, sparkPosition);
                    if (particle == null)
                    {
                        Utils.CreateFallbackSplat(sparkPosition, SplatType.Spark);
                    }
                }
                else
                {
                    Utils.CreateFallbackSplat(sparkPosition, SplatType.Spark);
                }
                
                // Play impact sound
                if (DataLoader.sDataLoader != null && DataLoader.sDataLoader.impact != null)
                {
                    Utils.PlayClipOccluded(DataLoader.sDataLoader.impact, sparkPosition);
                }
            }
        }
        return false;
    }

    public override EEquipAction Equip()
    {
        enabled = true;
        gameObject.SetActive(true);
        
        Rigidbody rb = gameObject.GetComponentInChildren<Rigidbody>();
        if (rb)
        {
            rb.isKinematic = true;
        }

        Collider col = gameObject.GetComponentInChildren<Collider>();
        if (col != null)
        {
            col.enabled = false;
        }

        transform.SetParent(PlayerObject.Player.mainCamera.transform, false);
        position = positions[0];
        rotation = rotations[0];
        
        // Store original scale before any modifications (if not already set)
        if (originalScale.sqrMagnitude < 0.0001f)
        {
            originalScale = transform.localScale;
            if (originalScale.sqrMagnitude < 0.0001f)
            {
                originalScale = Vector3.one;
            }
        }
        
        // Initialize handedness tracking
        //previousLeftHanded = PlayerData.sData.leftHanded;
        
        // Flip for left-handed players
        if (PlayerData.sData.leftHanded)
        {
            position.x = -position.x;
            rotation.y = -rotation.y;
        }
        
        transform.SetLocalPositionAndRotation(position, Quaternion.Euler(rotation));
        
        // Flip scale: weapons flip for left-handed, fist (left-handed model) flips for right-handed
        Vector3 scale = originalScale;
        bool shouldFlip = (type == EObjectType.Fist) ? !PlayerData.sData.leftHanded : PlayerData.sData.leftHanded;
        if (shouldFlip)
        {
            scale.x = -Mathf.Abs(scale.x);
        }
        else
        {
            scale.x = Mathf.Abs(scale.x);
        }
        transform.localScale = scale;

        // show it for a bit
        resetTime = 0.0f;
        if (PlayerData.sData.xp == 0)
        {
            // unless it's the start of the game
            resetTime = 5.0f;
        }

        TutorialManager.NotifyWeaponEquipped(this);
        
        return EEquipAction.Equip;
    }

    public override void Unequip()
    {
        RestoreWeaponVisualChildrenForUnequip();

        Collider col = gameObject.GetComponentInChildren<Collider>();
        if (col != null)
        {
            col.enabled = true;
        }

        ChangeState(EState.Reset);
        enabled = false;
        gameObject.SetActive(false);
        gameObject.transform.SetParent(null);
    }
    
    protected override int GetQualityOffset()
    {
        return 6;
    }

    protected override void GetQualityString(System.Text.StringBuilder sb)
    {
        ObjectsData.MeleeData meleeData = DataLoader.sDataLoader.objectsData.weaponStats[(int)type & 15];
        int durability = meleeData.Durability;
        if (durability == 255)
        {
            return;
        }
        base.GetQualityString(sb);
    }

    public override bool IsRepairable()
    {
        return true;
    }

    protected override string GetIdentifiedName(string baseName)
    {
        if (type == EObjectType.ShinySword)
        {
            return StringLoader.GetString(1, 268);
        }

        return base.GetIdentifiedName(baseName);
    }

    public override string GetUseText()
    {
        return "Equip";
    }

    protected override void RestoreFromSaveData(LevelObjectSaveData data)
    {
        base.RestoreFromSaveData(data);
        UpdateEnchantmentState();
    }

    private void UpdateEnchantmentState()
    {
        if (!isLinked && isEnchanted)
        {
            int spellIndex = special - 704;
            if (spellIndex is >= 0 and < 16)
            {
                enchantmentName = StringLoader.GetString(6, 448 + spellIndex);
                enchantmentIndex = spellIndex;
            }
        }
    }
}
