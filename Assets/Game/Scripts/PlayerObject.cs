using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;
using System.Runtime.CompilerServices;
using Game.Scripts.CritterVariants;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

public class PlayerObject : MonoBehaviour
{
    public MonoBehaviour[] itemsToSpawn;

    public float jumpVelocity = 5.0f;
    public float leapVelocity = 8.0f;
    public float leapMaxHeight = 5.0f;
    public float leapUpApex = 0.7f;
    public float leapUpLipOvershoot = 1.5f;
    public float groundSpeed = 6.0f;
    public float encumberedSpeed = 1.0f;
    public float combatSpeed = 3.0f;
    [Tooltip("Sprint speed multiplier (1.5 = 50% faster).")]
    public float sprintSpeedMultiplier = 1.5f;
    [Tooltip("Stamina: 0 = empty, 1 = full. Drains over 10s when sprinting, recovers over 20s when not.")]
    [Range(0f, 1f)]
    public float stamina = 1.0f;
    private const float staminaDrainTime = 10f;
    private const float staminaRecoverTime = 20f;
    private const float staminaDepletedRecoverDelay = 2f;
    /// <summary>Seconds remaining before stamina can recover after depleting to 0. Zero or less means not in delay. Saved so delay survives save/load.</summary>
    public float staminaRecoverDelayRemaining;
    public AudioClip pickupClip;

    public AudioClip[] footsteps;
    public AudioClip[] splashes;
    public AudioClip[] waterWalkSteps;
    public AudioClip[] lavaWalkSteps;
    public AudioClip[] landingSounds;
    public AudioClip[] jumpSounds;
    public AudioClip[] grunts;
    public AudioClip[] maleGrunts;

    public Font font;

    public EndGame endGame;

    public CharacterController cachedCharacterController;

    public Camera mainCamera;
    
    private readonly Collider[] cachedColliders = new Collider[16];

    // bitfield
    public EControlMask controlsDisabled;

    public bool controlsActive => controlsDisabled == 0;

    public float swimBob = 0.1f;
    public float swimSway = 3.0f;
    public float flightBobSpeed = 2.0f;
    public float flightBobScale = 0.001f;

    public float cameraOffset = 0.85f;
    [Tooltip("Walk camera bob amplitude (world units). Lowest point aligns with footstep.")]
    public float cameraBobAmplitude = 0.05f;
    [Tooltip("How fast the camera bob returns to rest when stopping (units/sec).")]
    public float cameraBobReturnSpeed = 0.4f;

    [Tooltip("Mouse look smoothing time (seconds). 0 = raw input. Gamepad is unaffected.")]
    [SerializeField] private float mouseLookSmoothingTime = 0.04f;

    public float footstepMultiplier = 0.07f;
    public float swimstepMultiplier = 0.1f;

    private Vector3 flightVelocity;
    private float flightBobTime;

    private float leapMoveSpeed;

    public static PlayerObject Player { get; private set; }

    public class ClassData
    {
        public byte minStrength;
        public byte minIntellect;
        public byte minDexterity;
        public byte pointsRemaining;
        public List<byte[]> skills = new ();
    }

    public static ClassData[] classData = new ClassData[Enum.GetNames(typeof(EPlayerClass)).Length];

    // might not be the first place you would normally reach these levels at
    private static readonly Vector3[] levelStarts =
    {
        Vector3.zero,
        new(98, 10, 7),
        new(23, 10, 115), // level 2
        //new(184, 4, 14), // level 2 shak
        new(127, 10, 184), // level 3
        //new(76, 10, 66), // level 3 prisoner
        new(125, 7, 7),
        new(120, 3, 80),
        new(130, 10, 83),
        new(58, 10, 40),
        new(180, 10, 7),
        new(81, 5, 72)
    };

    public void DebugJumpToLevelStart()
    {
        transform.position = levelStarts[LevelLoader.sLevelLoader.loadedLevel];
        if (LevelLoader.sLevelLoader.loadedLevel == 9)
        {
            transform.rotation = Quaternion.AngleAxis(90.0f, Vector3.up);
        }
    }

    public void SetLastEngagedInCombat(Critter critter)
    {
        // find nearby enemies and set this on them
        int layerMask = 1 << LayerMask.NameToLayer("Characters");
        int count = Physics.OverlapSphereNonAlloc(transform.position, 10.0f, cachedColliders, layerMask);
        for (int i = 0; i < count; i++)
        {
            Collider col = cachedColliders[i];
            if (col is CharacterController)
            {
                Critter otherCritter = col.transform.root.gameObject.GetComponent<Critter>();
                if (otherCritter != critter)
                {
                    otherCritter.playerLastEngagedInCombat = critter;
                    if (otherCritter.playerAlly)
                    {
                        // immediately attack this
                        otherCritter.attackTarget = critter;
                    }
                }
            }
        }
    }

    /// <summary>
    /// What a monster's attack roll is made against. Armour is deliberately not part of it: the
    /// original keeps this number as the Defense skill plus half the skill of the weapon in hand
    /// (UW.EXE 0x7e535 and 0x7e5f8) and spends armour on the damage instead, in
    /// <see cref="AbsorbWithArmour"/>. Adding armour here made a plate-armoured player untouchable
    /// rather than merely hard to hurt.
    /// </summary>
    public int GetDefence()
    {
        int defenceScore = Skills.GetSkill(ESkill.Defense);
        defenceScore += Magic.sMagic.GetSpellArmourScore();
        Weapon weapon = Inventory.sInv.invSlotContents[(int)(PlayerData.sData.leftHanded ? EInvSlot.LeftHand : EInvSlot.RightHand)] as Weapon;
        defenceScore += Skills.GetSkill(weapon != null ? weapon.skill : ESkill.Unarmed) / 2;

        if (Magic.sMagic.IsSpellActive(Magic.ESpell.Cursed))
        {
            defenceScore /= 2;
        }

        return defenceScore;
    }

    /// <summary>
    /// Takes the protection covering the body part a blow arriving at <paramref name="strikeHeight"/>
    /// lands on off the damage, floored at zero (UW.EXE 0x24e14). This is what armour does in the
    /// original: it does not stop blows from landing, it stops them from hurting. Every blow that
    /// reaches the damage routine goes through it, a swing and an arrow alike, because melee and
    /// missiles share that routine (UW.EXE 0x2527e and 0x259e7).
    /// </summary>
    public int AbsorbWithArmour(int damage, float strikeHeight)
    {
        return Math.Max(0, damage - Inventory.sInv.GetArmourByBodyPart()[(int)PickBodyPart(strikeHeight)]);
    }

    /// <summary>
    /// Which body part a blow arriving at <paramref name="strikeHeight"/> lands on. The original
    /// compares that height with the vertical extent of what it is hitting (UW.EXE 0x2441a): under
    /// the feet is legs, over the head is head, and in between the split leans low or high
    /// depending on which of the two is taller. So a rotworm goes for the legs and an imp for the
    /// head, and the armour that answers is the one covering that part.
    /// </summary>
    public EBodyPart PickBodyPart(float strikeHeight)
    {
        CharacterController body = cachedCharacterController;
        if (body == null)
        {
            return EBodyPart.Torso;
        }

        float middle = transform.TransformPoint(body.center).y;
        float foot = middle - 0.5f * body.height;
        float head = middle + 0.5f * body.height;

        if (strikeHeight < foot)
        {
            return EBodyPart.Legs;
        }

        if (strikeHeight > head)
        {
            return EBodyPart.Head;
        }

        if (strikeHeight < middle)
        {
            if (Random.Range(0, 2) == 0)
            {
                return EBodyPart.Legs;
            }
        }
        else if (Random.Range(0, 3) == 0)
        {
            return EBodyPart.Head;
        }

        return Random.Range(0, 3) == 0 ? EBodyPart.Arms : EBodyPart.Torso;
    }

    public static void LoadSkills()
    {
        Stream stream = new Stream("../Data/skills.dat");

        for (int i = 0; i < classData.Length; ++i)
        {
            classData[i] = new ClassData();
            classData[i].minStrength = stream.GetByte();
            classData[i].minDexterity = stream.GetByte();
            classData[i].minIntellect = stream.GetByte();
            classData[i].pointsRemaining = stream.GetByte();
        }

        for (int i = 0; i < classData.Length; ++i)
        {
            for (int j = 0; j < 5; ++j)
            {
                byte l = stream.GetByte();
                classData[i].skills.Add(stream.GetByteArray(l));
            }
        }
    }

    protected void Start()
    {
        // Singleton pattern - destroy duplicate Player instances
        if (Player != null && Player != this)
        {
            Debug.LogWarning("Duplicate PlayerObject detected and destroyed");
            Destroy(gameObject);
            return;
        }
        
        Player = this;

        if (GetComponent<PlayerPanelInput>() == null)
            gameObject.AddComponent<PlayerPanelInput>();

        // try to update at the rate of the monitor to prevent tearing
        QualitySettings.vSyncCount = 1;
        // this is ignored when using vSyncCount
        Application.targetFrameRate = 60;

        // If loadedLevel is 0 (not yet set), default to level 1 starting position
        int levelIndex = LevelLoader.sLevelLoader.loadedLevel;
        if (levelIndex == 0)
        {
            levelIndex = 1;
        }
        transform.position = levelStarts[levelIndex];
        if (levelIndex == 9)
        {
            transform.rotation = Quaternion.AngleAxis(90.0f, Vector3.up);
        }

        cameraYPos = transform.position.y + cameraOffset;

        foreach (MonoBehaviour mb in itemsToSpawn)
        {
            Instantiate(mb);
        }

        Camera cam = GetComponentInChildren<Camera>();
        if (cam != null)
        {
            cam.depthTextureMode = DepthTextureMode.Depth;
        }

        cachedCharacterController = gameObject.GetComponent<CharacterController>();
        
        // Initialize wasGrounded to current state
        wasGrounded = cachedCharacterController.isGrounded;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private float cameraLookUp;
    private float smoothedMousePitchDelta;
    private float smoothedMouseYawDelta;
    private float yVelocity;
    private float previousYVelocity;
    private Vector3 cachedMoveDirection;
    private int lastFootstep = -1;
    private int lastGrunt = -1;
    
    private float timeBetweenSteps;
    public bool wasGrounded;
    public bool isInWater;
    private float cameraYPos;
    private float cameraBobOffset;
    private float cameraBobBlend;
    private const float footstepStride = 3.0f;
    private bool touchingWater;
    private bool touchingLava;

    public float noise;

    private void PlayFootstep(float volume)
    {
        AudioClip[] steps;
        
        // Priority: lava walk > water walk > swimming > normal footsteps
        bool walkingOnLava = touchingLava;
        bool walkingOnWater = touchingWater && Magic.sMagic.IsSpellActive(Magic.ESpell.WaterWalk);
        
        if (walkingOnLava && lavaWalkSteps != null && lavaWalkSteps.Length > 0)
        {
            steps = lavaWalkSteps;
            volume *= footstepMultiplier;
        }
        else if (walkingOnWater && waterWalkSteps != null && waterWalkSteps.Length > 0)
        {
            steps = waterWalkSteps;
            volume *= footstepMultiplier;
        }
        else if (isInWater)
        {
            steps = splashes;
            volume *= swimstepMultiplier; // tone down splashes
        }
        else
        {
            steps = footsteps;
            volume *= footstepMultiplier; // tone down footsteps
        }
        
        if (steps != null && steps.Length > 0)
        {
            int i = Random.Range(0, steps.Length);
            if (i == lastFootstep)
            {
                i = (i + 1) % steps.Length;
            }

            lastFootstep = i;

            bool isStealthy = Magic.sMagic.IsSpellActive(Magic.ESpell.Stealth) || Magic.sMagic.IsSpellActive(Magic.ESpell.Invisibility);
            Skills.ESkillTestResult result = Skills.GetResult(Skills.GetSkill(ESkill.Sneak), 10);
            
            Utils.PlayClip(steps[i], transform.position - Vector3.up, isStealthy ? 0.5f * volume : volume);

            if (!isStealthy && result < Skills.ESkillTestResult.Success)
            {
                noise = Mathf.Max(noise, volume);
            }
        }

        if (touchingWater && Magic.sMagic.IsSpellActive(Magic.ESpell.WaterWalk))
        {
            ++PlayerData.sData.waterWalkSteps;
        }
        else if (touchingLava && (Magic.sMagic.IsSpellActive(Magic.ESpell.Flameproof) || Inventory.sInv.Wearing(EObjectType.DragonskinBoots)))
        {
            ++PlayerData.sData.lavaWalkSteps;
        }
    }

    private void PlayLandingSound(float impactVelocity)
    {
        // Fall back to footstep if landing sounds aren't available
        if (landingSounds == null || landingSounds.Length == 0)
        {
            PlayFootstep(1.0f);
            return;
        }

        // Convert impact velocity to positive value (yVelocity is negative when falling)
        float velocity = Mathf.Abs(impactVelocity);
        
        // Fuzzy selection: determine which sound to use based on velocity
        // Thresholds: low (6-9), medium (9-12), high (>12)
        int soundIndex;
        float random = Random.value;
        
        if (velocity < 9.0f)
        {
            // Low velocity: mostly sound[0], sometimes sound[1]
            soundIndex = random < 0.8f ? 0 : 1;
        }
        else if (velocity < 12.0f)
        {
            // Medium velocity: mostly sound[1], sometimes sound[0] or sound[2]
            if (random < 0.2f)
                soundIndex = 0;
            else if (random < 0.8f)
                soundIndex = 1;
            else
                soundIndex = Mathf.Min(2, landingSounds.Length - 1);
        }
        else
        {
            // High velocity: mostly sound[2], sometimes sound[1]
            soundIndex = random < 0.2f ? 1 : Mathf.Min(2, landingSounds.Length - 1);
        }
        
        // Clamp to valid array index
        soundIndex = Mathf.Clamp(soundIndex, 0, landingSounds.Length - 1);
        
        AudioClip clip = landingSounds[soundIndex];
        if (clip == null)
        {
            PlayFootstep(1.0f);
            return;
        }
        
        // Calculate volume based on velocity (0.3 to 1.0 range)
        float volume = Mathf.Clamp(0.3f + (velocity / 20.0f) * 0.7f, 0.3f, 1.0f);
        
        // Calculate pitch based on velocity (0.8 to 1.2 range, higher pitch for higher velocity)
        float pitch = Mathf.Clamp(0.8f + (velocity / 20.0f) * 0.4f, 0.8f, 1.2f);
        
        // Use Utils.PlayClip with volume and pitch
        Vector3 pos = transform.position - Vector3.up;
        Utils.PlayClip(clip, pos, volume, pitch);
    }

    private void PlayJumpSound()
    {
        if (Random.value < 0.5f)
        {
            int i = PlayerData.sData.female ? 0 : 1;
            if (jumpSounds != null && jumpSounds.Length > i)
            {
                AudioClip clip = jumpSounds[i];
                if (clip != null)
                {
                    Vector3 pos = transform.position;
                    Utils.PlayClip(clip, pos, 0.25f, Random.Range(0.9f, 1.1f));
                }
            }
        }
    }

    private void PlayDamageGrunt(float volume)
    {
        AudioClip[] g = PlayerData.sData.female ? grunts : maleGrunts;
        if (g.Length > 0)
        {
            int i = Random.Range(0, g.Length);
            if (i == lastGrunt)
            {
                i = (i + 1) % g.Length;
            }

            lastGrunt = i;

            Utils.PlayClip2d(g[i], volume);
        }
    }

    private float lastGoodFloorCheckTime;
    private bool lastFloorCheckHit;

    public static void Rumble(float low, float high, float time)
    {
        RumbleManager rumble = Player.GetComponent<RumbleManager>();
        if (rumble != null)
        {
            rumble.Rumble(low, high, time);
        }
    }

    private static void RumbleStop()
    {
        RumbleManager rumble = Player.GetComponent<RumbleManager>();
        if (rumble != null)
        {
            rumble.Stop();
        }
    }

    private void UpdateJumpIndicator(Vector2 moveControl, float moveSpeed)
    {
        // jump indicator
        if (cachedCharacterController.isGrounded && moveControl.y > 0.7f && Mathf.Abs(moveControl.x) < 0.4f)
        {
            // look ahead 300ms
            // (reaction time about 200ms for a predicted event + motor spin up time + drop off time)
            float lookAheadTime = 0.3f;
            Vector3 center = transform.position;
            Vector3 forward = center + lookAheadTime * moveSpeed * cachedMoveDirection;
            int mask = LayerMasks.EnvironmentAndCeiling;
            if (!Physics.Raycast(center, cachedMoveDirection, lookAheadTime * moveSpeed, LayerMasks.EnvironmentOnly))
            {
                if (Physics.Raycast(forward, Vector3.down, 2.0f, LayerMasks.EnvironmentOnly))
                {
                    lastGoodFloorCheckTime = Time.time;
                    lastFloorCheckHit = true;
                }
                else
                {
                    if (lastGoodFloorCheckTime > Time.time - 0.1f && lastFloorCheckHit)
                    {
                        // rumble
                        Rumble(0.0f, 1.0f, 0.1f);
                    }
                    lastFloorCheckHit = false;
                }
            }
            else
            {
                lastFloorCheckHit = false;
            }
        }
    }

    private bool ShouldAutoJump(Vector2 moveControl, float moveSpeed)
    {
        if (cachedCharacterController.isGrounded
            && moveControl.y > 0.7f
            && Mathf.Abs(moveControl.x) < 0.4f
            && PlayerInput.JumpHeld())
        {
            float lookAheadTime = 0.05f;
            Vector3 center = transform.position;
            Vector3 forward = center + lookAheadTime * moveSpeed * cachedMoveDirection;
            int mask = LayerMasks.EnvironmentAndCeiling;
            if (!Physics.Raycast(center, cachedMoveDirection, lookAheadTime * moveSpeed, LayerMasks.EnvironmentOnly))
            {
                if (!Physics.Raycast(forward, Vector3.down, 2.0f, LayerMasks.EnvironmentOnly))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private bool TouchingTerrain(ETerrainType terrainType, Tile t)
    {
        // need to be close so not standing on a bridge
        // falling
        // touching a mostly upward facing floor
        // and the actual ray cast floor is close (so that must be what we're touching)
        bool touchingTerrain = false;
        // falling, and touching something
        if (t.GetFloorTerrain() == terrainType && yVelocity < 0.0f && hasGoodContact)
        {
            if (transform.position.y < t.floorHeight + 3.0f)
            {
                // close to the floor
                touchingTerrain = true;
            }
            else if (t.movingPlatform != null && transform.position.y - 1.2f < t.movingPlatform.transform.position.y)
            {
                // or close to the top of a moving platform (which would have the same texture)
                touchingTerrain = true;
            }
        }
        if (touchingTerrain)
        {
            // check there's not a bridge between us and the terrain
            touchingTerrain = Physics.Raycast(transform.position, Vector3.down, out RaycastHit bridgeCheck, 1.2f,
                LayerMasks.EnvironmentOnly);
            if (touchingTerrain && bridgeCheck.transform.root.gameObject.GetComponent<Bridge>() != null)
            {
                touchingTerrain = false;
            }
        }
        return touchingTerrain;
    }

    private bool IsWeaponBlockingSprint()
    {
        EInvSlot slot = PlayerData.sData.leftHanded ? EInvSlot.LeftHand : EInvSlot.RightHand;
        WeaponBase weapon = Inventory.sInv.invSlotContents[(int)slot] as WeaponBase;
        if (weapon == null) weapon = Inventory.sInv.fist;
        return weapon != null && weapon.IsSprintBlocked();
    }

    private void NormalMovement()
    {
        float gravity = 9.81f;
        if (yVelocity < 0.0f && Magic.sMagic.IsSpellActive(Magic.ESpell.SlowFall))
        {
            gravity = 2.5f;
        }
        yVelocity -= gravity * Time.deltaTime;

        // Track velocity before checking for landing
        previousYVelocity = yVelocity;

        float moveSpeed = remainingCarryWeight < 0
            ? encumberedSpeed
            : IsWeaponBlockingSprint() ? combatSpeed : groundSpeed;

        if (isInWater)
        {
            moveSpeed /= 2.0f;
            timeInWater += Time.deltaTime;
            if (timeInWater > 30.0f + 2.0f * Skills.GetSkill(ESkill.Swimming))
            {
                timeToNextWaterDamage -= Time.deltaTime;
                if (timeToNextWaterDamage < 0.0f)
                {
                    timeToNextWaterDamage = 10.0f;
                    Damage(Skills.ESkillTestResult.Success, Random.Range(3, 5), EDamageType.Drowning);
                }
            }
        }
        else
        {
            timeInWater = 0.0f;
        }

        if (!cachedCharacterController.isGrounded && leapMoveSpeed > 0.0f)
        {
            moveSpeed = leapMoveSpeed;
        }

        if (Magic.sMagic.IsSpellActive(Magic.ESpell.Speed))
        {
            moveSpeed *= 2.0f;
        }

        // While magic/stats/inventory panels are up, keyboard arrows stay for panel UI; WASD (and gamepad stick) still move.
        bool allowMove = controlsActive || (controlsDisabled & ~(EControlMask.Magic | EControlMask.Inventory)) == 0;
        bool usingKeyboardMove = false;
        Vector2 moveControl = allowMove ? ReadMoveVectorConsideringPanels(out usingKeyboardMove) : Vector2.zero;
        
        // Sprint: left stick click + moving forward only, when not in combat/weapon busy and have stamina
        const float sprintForwardThreshold = 0.5f;
        bool stickPressedGamepad = GameInput.CurrentGamepad?.leftStickButton.isPressed ?? false;
        bool shiftPressedKeyboard = GameInput.CurrentKeyboard?.leftShiftKey.isPressed ?? false;
        bool sprintModifierHeld = usingKeyboardMove ? shiftPressedKeyboard : stickPressedGamepad;

        bool wantsSprint = sprintModifierHeld
                          && moveControl.y > sprintForwardThreshold
                          && stamina > 0f
                          && cachedCharacterController.isGrounded
                          && !isInWater
                          && !Music.IsInCombat()
                          && !IsWeaponBlockingSprint();
        if (wantsSprint)
        {
            moveSpeed *= sprintSpeedMultiplier;
            stamina -= Time.deltaTime / staminaDrainTime;
            if (stamina <= 0f)
            {
                stamina = 0f;
                staminaRecoverDelayRemaining = staminaDepletedRecoverDelay;
            }
        }
        else if (!sprintModifierHeld)
        {
            // Recover only when left stick is released, and after delay countdown if stamina had just hit 0
            if (staminaRecoverDelayRemaining > 0f)
                staminaRecoverDelayRemaining -= Time.deltaTime;
            if (staminaRecoverDelayRemaining <= 0f)
            {
                stamina += Time.deltaTime / staminaRecoverTime;
                stamina = Mathf.Min(stamina, 1.0f);
                if (stamina >= 1f)
                    staminaRecoverDelayRemaining = 0f;
            }
        }
        
        if (cachedCharacterController.isGrounded)
        {
            // left/right at 75%, backward at 50% of forward speed
            cachedMoveDirection = 0.75f * Utils.DeadZone(moveControl.x) * transform.right +
                                  (moveControl.y > 0.0f ? 1.0f : 0.5f) * Utils.DeadZone(moveControl.y) * transform.forward;
            
            UpdateJumpIndicator(moveControl, moveSpeed);
        }
        
        cachedCharacterController.Move(cachedMoveDirection * (moveSpeed * Time.deltaTime) + (Time.deltaTime * yVelocity) * Vector3.up);

        Tile t = LevelLoader.GetTile((int)(transform.position.x / Tile.xzScale),
            (int)(transform.position.z / Tile.xzScale));
        
        touchingWater = TouchingTerrain(ETerrainType.Water, t);
        isInWater = !Magic.sMagic.IsSpellActive(Magic.ESpell.WaterWalk) && touchingWater;

        // flameproof?
        touchingLava = TouchingTerrain(ETerrainType.Lava, t);
        bool shouldBeBurned = !Magic.sMagic.IsSpellActive(Magic.ESpell.Flameproof) && !Inventory.sInv.Wearing(EObjectType.DragonskinBoots) && touchingLava; 

        if (shouldBeBurned)
        {
            // damage every so often
            timeToNextLavaDamage -= Time.deltaTime;
            if (timeToNextLavaDamage <= 0.0f)
            {
                timeToNextLavaDamage += Random.Range(2.0f, 3.0f);
                Skills.ESkillTestResult result = Skills.GetResult(4, 0); // small chance of damaging armor 
                Damage(result, Random.Range(1, 3), EDamageType.Lava);
            }
        }
        else
        {
            timeToNextLavaDamage = 3.0f;
        }

        // damage if off the main path
        if (LevelLoader.sLevelLoader.loadedLevel == 9 && t.floorTexture == 9)
        {
            timeToNextVoidDamage -= Time.deltaTime;
            if (timeToNextVoidDamage <= 0.0f)
            {
                timeToNextVoidDamage += Random.Range(0.2f, 0.4f);
                Damage(Skills.ESkillTestResult.CriticalSuccess, Random.Range(1, 3), EDamageType.Damage);
            }
        }

        hasGoodContact = false;
        
        // Only check for landing when time is running to prevent false landing sounds after pause
        bool currentGrounded = cachedCharacterController.isGrounded;
        if (Time.timeScale > 0.0f && currentGrounded)
        {
            if (!wasGrounded)
            {
                // land
                
                // Capture impact velocity before it gets reset (yVelocity is negative when falling)
                float impactVelocity = yVelocity;
                float impactSpeed = Mathf.Abs(impactVelocity);
                
                // don't play landed sound when starting the game
                if (PlayerData.sData.playTime > 1.0)
                {
                    // Only play landing sound if not in water (water walking is OK)
                    if (!isInWater)
                    {
                        // Below threshold speed, use normal footstep instead of landing sound
                        const float landingSoundThreshold = 6.0f;
                        if (impactSpeed >= landingSoundThreshold)
                        {
                            PlayLandingSound(impactVelocity);
                        }
                        else
                        {
                            PlayFootstep(1.0f);
                        }
                    }
                    else
                    {
                        // In water, let splash sounds handle it
                        PlayFootstep(1.0f);
                    }
                }
                timeBetweenSteps = 0.0f;

                leapMoveSpeed = 0.0f;

                if (shouldBeBurned)
                {
                    Damage(Skills.ESkillTestResult.Success, Random.Range(2, 4), EDamageType.Lava);
                }
                else
                {
                    // try landing damage
                    if (!isInWater && yVelocity < -10.0f)
                    {
                        int speed = (int) -yVelocity - 10;
                        Skills.ESkillTestResult result = Skills.GetResult(Skills.GetSkill(ESkill.Acrobat), 3 * speed);
                        if (result < Skills.ESkillTestResult.Success)
                        {
                            // flip the result for the damage
                            Skills.ESkillTestResult damageResult = (Skills.ESkillTestResult)(3 - (int)result);
                            Player.Damage(damageResult, 2 * speed, EDamageType.Direct);
                        }
                    }
                }
            }
            // push into ground to keep isGrounded accurate when walking down a slope
            yVelocity = -3.0f;
            bool jumped = false;
            if (allowMove && !isInWater)
            {
                if (PlayerInput.AutoJump)
                {
                    // Auto-jump: wait for release (or leap at the detected edge while held).
                    if (IsJumpReleasedSeparated() || ShouldAutoJump(moveControl, moveSpeed))
                    {
                        jumped = true;
                    }
                }
                else if (IsJumpPressedSeparated())
                {
                    jumped = true;
                }
            }
            if (jumped)
            {
                yVelocity = jumpVelocity;
                if (Magic.sMagic.IsSpellActive(Magic.ESpell.Leap) && remainingCarryWeight >= 0)
                {
                    float speed = cachedMoveDirection.magnitude;
                    bool foundCalculatedSpeed = false;
                    if (speed > 0.1f)
                    {
                        // look for a wall directly in front
                        Vector3 center = transform.position;
                        Vector3 forward = cachedMoveDirection.normalized;
                        int mask = LayerMasks.EnvironmentAndCeiling;
                        if (Physics.Raycast(center, forward, out RaycastHit hit, 12.0f * speed, mask))
                        {
                            // now depending on the wall normal, find the tile on the other side
                            Vector3 pos = hit.point - leapUpLipOvershoot * hit.normal;
                            Tile tile = LevelLoader.GetTile(pos);
                            if (tile.type != 0)
                            {
                                float tileY = tile.GetFloorY(pos.x, pos.z);
                                if (tileY > pos.y)
                                {
                                    pos.y = tileY + cachedCharacterController.height / 2.0f;
                                    if (pos.y < center.y + leapMaxHeight)
                                    {
                                        Vector3 launchVelocity = CalculateLeapUpVelocity(center, pos, leapUpApex);
                                        cachedMoveDirection = launchVelocity;
                                        cachedMoveDirection.y = 0.0f;
                                        yVelocity = launchVelocity.y;
                                        foundCalculatedSpeed = true;
                                        leapMoveSpeed = Mathf.Sqrt(launchVelocity.x * launchVelocity.x + launchVelocity.z * launchVelocity.z);
                                    }
                                }
                            }
                        }
                    }
                    if (!foundCalculatedSpeed)
                    {
                        yVelocity = leapVelocity;
                    }
                }
                PlayJumpSound();
                PlayFootstep(1.0f);
            }
            else
            {
                float moveMagnitude = cachedMoveDirection.magnitude;
                timeBetweenSteps += Time.deltaTime * moveMagnitude * moveSpeed;
                if (timeBetweenSteps > footstepStride)
                {
                    float stepVolume = (wantsSprint ? 0.65f : 0.5f) * moveMagnitude;
                    PlayFootstep(stepVolume);
                    timeBetweenSteps = 0.0f;
                }
            }
        }
        
        // Update wasGrounded at the end of the frame when time is running
        // This ensures it persists correctly across pause/resume cycles
        if (Time.timeScale > 0.0f)
        {
            wasGrounded = currentGrounded;
        }
        
        flightVelocity = Vector3.zero;
        flightBobTime = 0.0f;
    }

    private void Flight()
    {
        bool allowMove = controlsActive || (controlsDisabled & ~(EControlMask.Magic | EControlMask.Inventory)) == 0;
        Vector2 moveControl = allowMove ? ReadMoveVectorConsideringPanels(out _) : Vector2.zero;
        Vector3 desiredFlightVelocity = Utils.DeadZone(moveControl.x) * mainCamera.transform.right
                                        + Utils.DeadZone(moveControl.y) * mainCamera.transform.forward;
        if (!Magic.sMagic.IsSpellActive(Magic.ESpell.Fly))
        {
            // limit the xz movement for levitation
            desiredFlightVelocity.x *= 0.2f;
            desiredFlightVelocity.z *= 0.2f;
        }
        flightVelocity = Utils.DampedApproach(flightVelocity, desiredFlightVelocity, 1.0f);

        flightBobTime += flightBobSpeed * Time.deltaTime;
        flightVelocity += flightBobScale * Mathf.Sign(Mathf.Sin(flightBobTime)) * Vector3.up;

        cachedCharacterController.Move(flightVelocity * (groundSpeed * Time.deltaTime));

        cachedMoveDirection = flightVelocity;
    }
    
    private Vector3 CalculateLeapUpVelocity(Vector3 startPoint, Vector3 targetPoint, float maxApex)
    {
        float g = -Physics.gravity.y;
        
        float apexHeight = Mathf.Max(startPoint.y, targetPoint.y) + maxApex;
        
        float riseHeight = apexHeight - startPoint.y;
        float verticalVelocity = Mathf.Sqrt(2.0f * g * riseHeight);

        // Calculate time to reach the apex and time to fall from apex to target.
        float timeToApex = verticalVelocity / g;
        float fallHeight = apexHeight - targetPoint.y;
        float timeToFall = Mathf.Sqrt(2.0f * fallHeight / g);
        float totalTime = timeToApex + timeToFall;
        
        Vector3 displacement = targetPoint - startPoint;
        Vector3 launchVelocity = displacement / totalTime;
        launchVelocity.y = verticalVelocity;

        return launchVelocity;
    }
    
    protected void Update()
    {
        // Guard against no level loaded yet
        if (LevelLoader.sLevelLoader.loadedLevel == 0 
            || LevelLoader.GetLevel() == null)
        {
            return;
        }
        
        // Establish which device is currently driving controls (used to keep gamepad vs kb/m siloed).
        GameInput.RefreshLastActiveDevice();

        noise = Mathf.MoveTowards(noise, 0.0f, Time.deltaTime);

        if (PlayerData.sData.dead)
        {
            return;
        }

        if (fadeIn)
        {
            fade = Mathf.Max(0.0f, fade - Time.unscaledDeltaTime);
            if (fade == 0.0f)
            {
                fadeIn = false;
            }
        }

        timeToNextLightSourceDecay -= Time.deltaTime;
        if (timeToNextLightSourceDecay < 0.0f)
        {
            timeToNextLightSourceDecay += 20.0f;
            LightSource.DecayLights();
        }
        
        if (PlayerData.sData != null)
        {
            remainingCarryWeight = 2 * PlayerData.sData.strength - (int)Inventory.sInv.GetInventoryWeight();

            if (PlayerData.sData.poison > 0)
            {
                // poison wears off quicker
                bool havePoisonProtection = Magic.sMagic.IsSpellActive(Magic.ESpell.PoisonResistance);
                timeToDecrementPoison -= (havePoisonProtection ? 3.0f : 1.0f) * Time.deltaTime;
                if (timeToDecrementPoison <= 0.0f)
                {
                    --PlayerData.sData.poison;
                    timeToDecrementPoison += Random.Range(25.0f, 35.0f);
                }
            }
            
            if (PlayerData.sData.drunkenness > 0)
            {
                // Drunkenness wears off over time
                timeToDecrementDrunkenness -= Time.deltaTime;
                if (timeToDecrementDrunkenness <= 0.0f)
                {
                    --PlayerData.sData.drunkenness;
                    // Reset the timer for the next point to wear off
                    timeToDecrementDrunkenness += Random.Range(2.0f, 5.0f);
                }
            }

            if (PlayerData.sData.poison > 0)
            {
                // damage applied less often
                bool havePoisonProtection = Magic.sMagic.IsSpellActive(Magic.ESpell.PoisonResistance);
                timeToNextPoisonDamage -= Time.deltaTime / (havePoisonProtection ? 3.0f : 1.0f);
                if (timeToNextPoisonDamage <= 0.0f)
                {
                    int damage = Utils.GetDamageRoll(PlayerData.sData.poison);
                    Damage(Skills.ESkillTestResult.Success, damage, EDamageType.Poison);
                    timeToNextPoisonDamage += Random.Range(15.0f, 20.0f);
                }
            }

            timeToNextHunger -= Time.deltaTime;
            if (timeToNextHunger < 0.0f)
            {
                timeToNextHunger = 60.0f;
                PlayerData.sData.hunger = Mathf.Min(PlayerData.sData.hunger + 1, 255);
            }

            if (PlayerData.sData.hunger >= 224) // starving
            {
                timeToNextHungerDamage -= Time.deltaTime;
                if (timeToNextHungerDamage < 0.0f)
                {
                    Messages.Add(1, 17);
                    Damage(Skills.ESkillTestResult.Success, Utils.GetDamageRoll(3), EDamageType.Direct);
                    timeToNextHungerDamage = 60.0f;
                }
            }

            timeToNextFatigue -= Time.deltaTime;
            if (timeToNextFatigue < 0.0f)
            {
                timeToNextFatigue = 300.0f;
                PlayerData.sData.fatigue = Mathf.Min(PlayerData.sData.fatigue + 1, 30);
            }

            if (PlayerData.sData.fatigue >= 27) // fatigued
            {
                timeToNextFatigueDamage -= Time.deltaTime;
                if (timeToNextFatigueDamage < 0.0f)
                {
                    // You are fatigued.
                    Messages.Add($"{StringLoader.GetString(1, 91)}{StringLoader.GetString(1, 113)}.");
                    timeToNextFatigueDamage = 90.0f;
                    Damage(Skills.ESkillTestResult.Success, Utils.GetDamageRoll(3), EDamageType.Direct);
                }
            }

            PlayerData.sData.gameTime += Time.deltaTime;
            PlayerData.sData.playTime += Time.deltaTime;
        }

        if (cachedCharacterController != null)
        {
            bool flying = Magic.sMagic.IsSpellActive(Magic.ESpell.Fly) || Magic.sMagic.IsSpellActive(Magic.ESpell.Levitate);
            if (flying && remainingCarryWeight >= 0)
            {
                // NormalMovement() doesn't run, so water flags stay stale; force dry camera while airborne.
                isInWater = false;
                Flight();
            }
            else
            {
                NormalMovement();
            }

            GameObject cam = gameObject.transform.GetChild(0).gameObject;

            if (controlsActive || (controlsDisabled & ~(EControlMask.Inventory | EControlMask.Magic)) == 0)
            {
                float pitch = 0f;
                float yaw = 0f;
                if (GameInput.LastActiveDevice == GameInputDevice.MouseKeyboard)
                {
                    ReadLookMouseKeyboard(out pitch, out yaw);
                    ApplyMouseLookSmoothing(ref pitch, ref yaw);
                }
                else
                {
                    ResetMouseLookSmoothing();
                    ReadLookGamepad(out pitch, out yaw);
                }

                float invertMultiplier = PlayerPrefs.GetInt("Options_InvertLook", 0) == 1 ? -1.0f : 1.0f;
                cameraLookUp -= pitch * invertMultiplier;
                cameraLookUp = Mathf.Clamp(cameraLookUp, -89.0f, 89.0f);
                cam.transform.localEulerAngles = Vector3.right * cameraLookUp;

                transform.Rotate(Vector3.up * yaw);
            }
            else
            {
                ResetMouseLookSmoothing();
            }

            float cameraYTarget = transform.position.y;
            if (isInWater)
            {
                cameraYTarget += -0.2f + swimBob * Mathf.Sin(Time.time);
                cam.transform.localEulerAngles = new Vector3(cam.transform.localEulerAngles.x, 0.0f,
                    swimSway * Mathf.Sin(2.0f * Time.time));
            }
            else
            {
                cameraYTarget += cameraOffset;
            }

            // Walk bob: Abs(Cos) troughs at footstep. Blend weight eases in/out so start/stop don't snap.
            bool bobbing = !isInWater
                && !flying
                && cachedCharacterController.isGrounded
                && cachedMoveDirection.sqrMagnitude > 0.0001f;
            float bobBlendTarget = bobbing ? 1.0f : 0.0f;
            float bobBlendSpeed = cameraBobReturnSpeed / Mathf.Max(cameraBobAmplitude, 0.0001f);
            cameraBobBlend = Mathf.MoveTowards(cameraBobBlend, bobBlendTarget, bobBlendSpeed * Time.deltaTime);
            if (cameraBobBlend > 0.0f)
            {
                float phase = timeBetweenSteps / footstepStride;
                cameraBobOffset = cameraBobBlend * -cameraBobAmplitude * Mathf.Abs(Mathf.Cos(phase * Mathf.PI));
            }
            else
            {
                cameraBobOffset = 0.0f;
            }

            cameraYPos = Utils.DampedApproach(cameraYPos, cameraYTarget, 0.3f);
            cam.transform.localPosition = new Vector3(0.0f, cameraYPos - transform.position.y + cameraBobOffset, 0.0f);
        }

        Vector3 cameraPos = Camera.main.transform.position;
        LevelLoader.GetLevel().pvs.CacheVisibilityFrom(new Vector2Int(Tile.GetTileX(cameraPos.x), Tile.GetTileY(cameraPos.z)));
    }

    public int remainingCarryWeight;
    private float timeToDecrementPoison;
    private float timeToDecrementDrunkenness;
    private float timeToNextPoisonDamage;

    private float timeToNextLavaDamage;
    private float timeToNextVoidDamage;

    private float timeToNextHunger = 60.0f;
    private float timeToNextFatigue = 300.0f;
    private float timeToNextHungerDamage;
    private float timeToNextFatigueDamage;

    private float timeInWater;
    private float timeToNextWaterDamage;

    private float timeToNextLightSourceDecay = 20.0f;

    private static bool PlayerPanelsWantWasdOnlyMovement()
    {
        // Magic/inventory use arrow keys for UI; stats does not — full keyboard move is fine when stats alone is up.
        return PlayerPanelState.IsEffectivelyExploringInventory
            || PlayerPanelState.IsEffectivelyExploringMagic;
    }

    private Vector2 ReadMoveVectorConsideringPanels(out bool usingKeyboardMove)
    {
        if (!PlayerPanelsWantWasdOnlyMovement())
        {
            return ReadMoveVectorSeparated(out usingKeyboardMove);
        }

        if (GameInput.LastActiveDevice == GameInputDevice.Gamepad)
        {
            usingKeyboardMove = false;
            return ReadMoveVectorGamepad();
        }

        usingKeyboardMove = false;
        Vector2 wasd = ReadMoveVectorWasdOnly(out bool wasdHas);
        if (wasdHas)
        {
            usingKeyboardMove = true;
        }

        return wasd;
    }

    /// <summary>
    /// True when move input qualifies to walk-dismiss a panel: any keyboard movement,
    /// gamepad stick at half deflection or more in any direction, or sprint.
    /// </summary>
    public bool IsPanelWalkDismissMovement(out bool isSprinting)
    {
        isSprinting = false;

        bool allowMove = controlsActive || (controlsDisabled & ~(EControlMask.Magic | EControlMask.Inventory)) == 0;
        if (!allowMove)
        {
            return false;
        }

        Vector2 moveControl = ReadMoveVectorConsideringPanels(out bool usingKeyboardMove);
        if (moveControl.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        const float sprintForwardThreshold = 0.5f;
        bool stickPressedGamepad = GameInput.CurrentGamepad?.leftStickButton.isPressed ?? false;
        bool shiftPressedKeyboard = GameInput.CurrentKeyboard?.leftShiftKey.isPressed ?? false;
        bool sprintModifierHeld = usingKeyboardMove ? shiftPressedKeyboard : stickPressedGamepad;

        bool wantsSprint = sprintModifierHeld
                          && moveControl.y > sprintForwardThreshold
                          && stamina > 0f
                          && cachedCharacterController.isGrounded
                          && !isInWater
                          && !Music.IsInCombat()
                          && !IsWeaponBlockingSprint();
        if (wantsSprint)
        {
            isSprinting = true;
            return true;
        }

        if (usingKeyboardMove)
        {
            return true;
        }

        const float halfStickThreshold = 0.5f;
        return moveControl.magnitude >= halfStickThreshold;
    }

    private Vector2 ReadMoveVectorSeparated(out bool usingKeyboard)
    {
        usingKeyboard = false;

        Vector2 kb = ReadMoveVectorMouseKeyboard(out bool kbHas);
        if (kbHas)
        {
            usingKeyboard = true;
            return kb;
        }

        return ReadMoveVectorGamepad();
    }

    private static Vector2 ReadMoveVectorWasdOnly(out bool hasInput)
    {
        hasInput = false;
        Keyboard kb = GameInput.CurrentKeyboard;
        if (kb == null)
        {
            return Vector2.zero;
        }

        float x = 0f, y = 0f;
        if (kb.aKey.isPressed)
        {
            x -= 1f;
        }
        if (kb.dKey.isPressed)
        {
            x += 1f;
        }
        if (kb.wKey.isPressed)
        {
            y += 1f;
        }
        if (kb.sKey.isPressed)
        {
            y -= 1f;
        }

        if (x == 0f && y == 0f)
        {
            return Vector2.zero;
        }

        hasInput = true;
        Vector2 v = new Vector2(x, y);
        if (v.sqrMagnitude > 1f)
        {
            v.Normalize();
        }

        return v;
    }

    private static Vector2 ReadMoveVectorGamepad()
    {
        return GameInput.CurrentGamepad != null ? GameInput.CurrentGamepad.leftStick.ReadValue() : Vector2.zero;
    }

    private static Vector2 ReadMoveVectorMouseKeyboard(out bool hasInput)
    {
        hasInput = false;
        Keyboard kb = GameInput.CurrentKeyboard;
        if (kb == null)
            return Vector2.zero;

        float x = 0f, y = 0f;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) x -= 1f;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) x += 1f;
        if (kb.wKey.isPressed || kb.upArrowKey.isPressed) y += 1f;
        if (kb.sKey.isPressed || kb.downArrowKey.isPressed) y -= 1f;

        if (x == 0f && y == 0f)
            return Vector2.zero;

        hasInput = true;
        Vector2 v = new Vector2(x, y);
        if (v.sqrMagnitude > 1f)
            v.Normalize();
        return v;
    }

    private static bool IsJumpPressedSeparated()
    {
        bool kbJump = GameInput.CurrentKeyboard?.spaceKey.wasPressedThisFrame ?? false;
        bool gpJump = !PlayerPanelsWantWasdOnlyMovement()
            && (GameInput.CurrentGamepad?.aButton.wasPressedThisFrame ?? false);
        return gpJump || kbJump;
    }

    private static bool IsJumpReleasedSeparated()
    {
        bool kbJump = GameInput.CurrentKeyboard?.spaceKey.wasReleasedThisFrame ?? false;
        bool gpJump = !PlayerPanelsWantWasdOnlyMovement()
            && (GameInput.CurrentGamepad?.aButton.wasReleasedThisFrame ?? false);
        return gpJump || kbJump;
    }

    private static bool IsJumpHeldSeparated()
    {
        bool kbJumpHeld = GameInput.CurrentKeyboard?.spaceKey.isPressed ?? false;
        bool gpJumpHeld = !PlayerPanelsWantWasdOnlyMovement()
            && (GameInput.CurrentGamepad?.aButton.isPressed ?? false);
        return gpJumpHeld || kbJumpHeld;
    }

    private void ResetMouseLookSmoothing()
    {
        smoothedMousePitchDelta = 0f;
        smoothedMouseYawDelta = 0f;
    }

    private void ApplyMouseLookSmoothing(ref float pitch, ref float yaw)
    {
        if (mouseLookSmoothingTime <= 0f || (pitch == 0f && yaw == 0f))
        {
            ResetMouseLookSmoothing();
            return;
        }

        smoothedMousePitchDelta = Utils.DampedApproach(smoothedMousePitchDelta, pitch, mouseLookSmoothingTime);
        smoothedMouseYawDelta = Utils.DampedApproach(smoothedMouseYawDelta, yaw, mouseLookSmoothingTime);
        pitch = smoothedMousePitchDelta;
        yaw = smoothedMouseYawDelta;
    }

    private static void ReadLookGamepad(out float pitchDelta, out float yawDelta)
    {
        Vector2 stick = GameInput.CurrentGamepad?.rightStick.ReadValue() ?? Vector2.zero;
        pitchDelta = 90.0f * Time.deltaTime * Utils.DeadZone(stick.y);
        yawDelta = Utils.DeadZone(stick.x) * 180.0f * Time.deltaTime;
    }

    private static void ReadLookMouseKeyboard(out float pitchDelta, out float yawDelta)
    {
        pitchDelta = 0f;
        yawDelta = 0f;

        if (GameInput.CurrentMouse == null)
            return;
        if (Cursor.lockState != CursorLockMode.Locked)
            return;

        if (Interaction.sInt != null && Interaction.sInt.ShouldSuppressMouseLookWhileLeftHeld())
        {
            return;
        }

        float sens = PlayerInput.MouseLookSpeed * 0.12f;
        Vector2 delta = GameInput.CurrentMouse.delta.ReadValue();
        pitchDelta = delta.y * sens;
        yawDelta = delta.x * sens;
    }

    public void Damage(Skills.ESkillTestResult result, int damage, EDamageType damageType)
    {
        if (!PlayerData.sData.dead && !Cheats.sCheats.invincible)
        {
            // Easy mode: reduce damage taken by player to 2/3
            int actualDamage = PlayerData.sData.easy ? (damage * 2 / 3) : damage;
            PlayerData.sData.hp -= actualDamage;
            if (PlayerData.sData.hp < 0)
            {
                PlayerData.sData.hp = 0;
                PlayerData.sData.dead = true;
                Music.Dead();

                bool allowSaplingRebirth = PlayerData.sData.saplingPlanted && LevelLoader.sLevelLoader.loadedLevel != 9;
                StartCoroutine(allowSaplingRebirth ? PlayRebirthCutscene() : PlayDeathCutscene());
            }

            DamageFlash flash = mainCamera.GetComponentInChildren<DamageFlash>();
            if (flash != null)
            {
                flash.Flash(damage / 4.0f + (result == Skills.ESkillTestResult.CriticalSuccess ? 0.5f : 0.0f), damageType);
            }

            Rumble(0.1f, 0.2f, 0.3f);

            PlayDamageGrunt(Math.Min(0.75f + damage / 8.0f, 1.0f));

            // try to damage armour
            if (damageType is not EDamageType.Drowning and not EDamageType.Poison and not EDamageType.Direct)
            {
                UUObject armour = Inventory.sInv.GetRandomArmourPiece();
                if (armour != null)
                {
                    armour.TryDamage(damage, result);
                }
            }
            
            GetComponentInChildren<ScreenShake>().Shake();
        }
    }

    public void RestoreHealth(int health)
    {
        if (!PlayerData.sData.dead && PlayerData.sData.hp < PlayerData.sData.vitality)
        {
            PlayerData.sData.hp = Mathf.Min(PlayerData.sData.hp + health, PlayerData.sData.vitality);

            Rumble(0.1f, 0.2f, 0.3f);
        }
    }

    public static void DisableControls(EControlMask mask, bool disable)
    {
        // Ensure only a single bit is set in the mask
        // Check: mask != 0 and (mask & (mask - 1)) == 0
        // This ensures mask is a power of 2 (exactly one bit set)
        if (mask == 0 || ((int)mask & ((int)mask - 1)) != 0)
        {
            Debug.LogError($"DisableControls called with invalid mask: {mask}. Only a single control mask bit should be set.");
            return;
        }
        
        if (Player != null)
        {
            if (disable)
            {
                Player.controlsDisabled |= mask;
            }
            else
            {
                Player.controlsDisabled &= ~mask;
            }
        }
    }
    
    

    public void TeleportTo(Vector3 pos, Quaternion rot)
    {
        PlayerObject.Player.gameObject.transform.SetPositionAndRotation(pos, rot);
        // don't smooth eye pos
        cameraYPos = transform.position.y + cameraOffset;
        cameraBobOffset = 0.0f;
        cameraBobBlend = 0.0f;
    }

    public float fade = 1.0f; // start faded out
    public bool fadeIn = true;

    private void OnGUI()
    {
        GUI.depth = (int)EGUIDepth.PlayerFade;
#if false
       Tile t =
           LevelLoader.GetTile((int)(transform.position.x / Tile.xzScale), (int)(transform.position.z / Tile.xzScale));
       GUI.Label(new Rect(100, 100, 100, 20), $"{t.floorTexture}");
       GUI.Label(new Rect(100, 120, 100, 20), $"{t.GetFloorTerrain()}");
#endif
#if false
        foreach (Outcast oc in FindObjectsByType<Outcast>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (oc.levelIndex == 3 && oc.objectIndex == 254)
            {
                GUI.Label(Screen.safeArea, $"Zack loot count {oc.loot.Count}");
            }
        }
#endif

        if (PlayerData.sData.dead)
        {
            Utils.DrawFade(1.0f);
        }
        else
        {
            if (fade > 0)
            {
                Utils.DrawFade(fade);
            }
        }
    }

    private bool hasGoodContact;

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        hasGoodContact |= hit.normal.y > 0.8f;
    }

    // watching some YouTube videos, it seems this is how it works
    private static readonly int[] levelUp =
    {
        50, 100, 150, 200, 300, 400, 600, 800, 1200, 1600, 2400, 3200, 4800, 6400, 9600
    };

    private static int GetNewLevel(int newXp)
    {
        int level = 1;
        for (int i = 0; i < levelUp.Length; ++i)
        {
            if (newXp > levelUp[i])
            {
                level = 2 + i;
            }
        }

        return level;
    }

    public static void AddXP(int xpToAdd)
    {
        if (PlayerData.sData.charLevel < 16)
        {
            PlayerData.sData.xp += xpToAdd;
            ApplyXpProgress();
        }
    }

    // Awards skill points for newly crossed 300-display-XP tiers and character levels.
    // Uses a high-water XP tier so sapling XP penalties do not re-award the same tiers.
    /// <summary>
    /// The maximum hit points: level * Strength / 5 + 30, the multiply before the divide, as
    /// the original works it out from scratch whenever the level changes (UW.EXE 0x81305,
    /// reached from the level-up routine 0x8138a, both read whole).
    /// </summary>
    public static int GetMaxHitPoints()
    {
        return PlayerData.sData.charLevel * PlayerData.sData.strength / 5 + 30;
    }

    public static void ApplyXpProgress()
    {
        int displayXp = PlayerData.sData.xp / 20;

        int newTier = displayXp / 300;
        if (newTier > PlayerData.sData.skillPointsXpTier)
        {
            PlayerData.sData.skillPoints += newTier - PlayerData.sData.skillPointsXpTier;
            PlayerData.sData.skillPointsXpTier = newTier;
        }

        int newLevel = GetNewLevel(displayXp);
        if (newLevel > PlayerData.sData.charLevel)
        {
            int levelsGained = newLevel - PlayerData.sData.charLevel;
            PlayerData.sData.charLevel = newLevel;
            Messages.Add($"{StringLoader.GetString(1, 147)}{PlayerData.sData.charLevel}.");
            int previousMax = PlayerData.sData.vitality;
            PlayerData.sData.vitality = GetMaxHitPoints();
            // The original raises the ceiling and leaves the current total alone, but the
            // flask is drawn as 13 * hp / vitality, so that would empty two of its thirteen
            // segments at every level, even at full health. Kept as it was: what the ceiling
            // gains, the current total gains.
            PlayerData.sData.hp += Mathf.Max(0, PlayerData.sData.vitality - previousMax);
            PlayerData.sData.skillPoints += levelsGained;
            Music.LevelUp();
        }
    }

    public static void AddPoison(int poison)
    {
        // poison less effective
        bool havePoisonProtection = Magic.sMagic.IsSpellActive(Magic.ESpell.PoisonResistance);
        int appliedPoison = poison / (havePoisonProtection ? 3 : 1);
        PlayerData.sData.poison = Math.Min(PlayerData.sData.poison + appliedPoison, 15);
        Player.timeToDecrementPoison = Random.Range(25.0f, 35.0f);
        Player.timeToNextPoisonDamage = Random.Range(15.0f, 20.0f);
    }

    public void GoToEtherealVoid()
    {
        StartCoroutine(EnterMoonGate());
    }

    private IEnumerator EnterMoonGate()
    {
        // find and remove slasher of veils
        GameObject[] slashers = GameObject.FindGameObjectsWithTag("Slasher");
        Vector3 slasherPosition = Vector3.zero;
        if (slashers.Length > 0)
        {
            slasherPosition = slashers[0].transform.position;
            slashers[0].gameObject.SetActive(false);
        }

        Vector3 forward = mainCamera.transform.position - slasherPosition;
        forward.y = 0.0f;
        forward.Normalize();
        Quaternion rotation = Quaternion.LookRotation(forward);
        float forwardYaw = Mathf.Atan2(-forward.x, -forward.z) * Mathf.Rad2Deg;

        UUObject moongate = LevelLoader.CreateObjectOfType(EObjectType.Moongate);
        moongate.special = 708; // blue gate
        moongate.PostLoadInitialize();
        moongate.WorldInitialize(slasherPosition);
        moongate.gameObject.SetActive(true);
        moongate.transform.SetPositionAndRotation(slasherPosition, rotation);

        GameObject cameraObject = new GameObject();
        Camera c = cameraObject.AddComponent<Camera>();
        
        c.clearFlags = CameraClearFlags.SolidColor;
        c.backgroundColor = Color.black;
        Camera old = mainCamera;
        c.transform.SetPositionAndRotation(old.transform.position, old.transform.rotation);
        old.tag = "Untagged";
        c.tag = "MainCamera";
        float yaw = old.transform.eulerAngles.y;
        float pitch = old.transform.eulerAngles.x;
        Vector3 pos;
        DisableControls(EControlMask.EnterMoongate, true);

        yield return new WaitForSeconds(1.0f);

        float time = 2.0f;
        while (time > 0.0f)
        {
            yield return null;

            time -= Time.deltaTime;

            pos = Vector3.Lerp(old.transform.position, slasherPosition + Vector3.up + 0.1f * moongate.transform.forward, 1.0f - time / 2.0f);

            yaw = Mathf.MoveTowardsAngle(yaw, forwardYaw, 0.5f);
            pitch = Mathf.MoveTowardsAngle(pitch, 0.0f, 0.5f);
            float roll = time * 360.0f / 2.0f;
            c.transform.SetPositionAndRotation(pos, Quaternion.Euler(pitch, yaw, roll));
            
            moongate.transform.rotation *= Quaternion.AngleAxis(360.0f * Time.deltaTime, Vector3.up);
        }
        
        // return to normal player camera
        c.tag = "Untagged";
        old.tag = "MainCamera";
        Player.fade = 1.0f;
        
        // wait a frame before destroying camera
        yield return null;
        
        Destroy(cameraObject);

        LevelLoader.sLevelLoader.ChangeLevel(9, 26, 24);

        Player.fadeIn = true;
        
        DisableControls(EControlMask.EnterMoongate, false);
    }

    public Vector3 GetFootPos()
    {
        return transform.position + cachedCharacterController.height * Vector3.down;
    }

    public CutscenePlayer deathCutscene;
    public CutscenePlayer rebirthCutscene;

    private IEnumerator PlayDeathCutscene()
    {
        RumbleStop();

        // little pause to let the death music kick in
        yield return new WaitForSeconds(0.1f);

        CutscenePlayer cut = Instantiate(deathCutscene);
        while (cut != null)
        {
            yield return null;
        }
        // go back to main menu
        SceneManager.LoadScene("World");
    }

    private IEnumerator PlayRebirthCutscene()
    {
        RumbleStop();

        // little pause to let the death music kick in
        yield return new WaitForSeconds(0.1f);

        CutscenePlayer cut = Instantiate(rebirthCutscene);
        while (cut != null)
        {
            yield return null;
        }

        // lose 20% xp
        PlayerData.sData.xp *= 8;
        PlayerData.sData.xp /= 10;

        PlayerData.sData.dead = false;
        PlayerData.sData.hp = PlayerData.sData.vitality;
        PlayerData.sData.poison = 0;
        
        // remove all spells and regenerate mana
        Magic.sMagic.StopAllSpells();
        PlayerData.sData.mana = PlayerData.sData.maxMana;

        LevelLoader.sLevelLoader.ChangeLevel(PlayerData.sData.saplingPlantedLevel, PlayerData.sData.saplingPlantedPosition + Vector3.up);
    }

    public CritterEncounterData?[] encounteredCritterData = new CritterEncounterData?[64];
    
    public void RegisterEncounteredCritter(EObjectType type, int level, int objectIndex, int originalHp)
    {
        int typeIndex = (int)type - 64;
        if (encounteredCritterData[typeIndex] == null
            || encounteredCritterData[typeIndex].Value.originalHp < originalHp)
        {
            encounteredCritterData[typeIndex] = new CritterEncounterData
            {
                level = level,
                objectIndex = objectIndex,
                originalHp = originalHp
            };
        }
    }

    public EObjectType GetRandomEncounteredCritter()
    {
        List<int> encountered = new List<int>();
        for (int i = 64; i < 121; ++i) // everything up to and including fire elemental
        {
            Critter.EMovementType movementType = (Critter.EMovementType) Critter.getStats((EObjectType)i).Category; 
            if (Critter.canBeSummoned((EObjectType)i)
                && movementType != Critter.EMovementType.Swimming
                && encounteredCritterData[i - 64] != null)
            {
                encountered.Add(i);
            }
        }

        if (encountered.Count > 0)
        {
            return (EObjectType)encountered[Random.Range(0, encountered.Count)];
        }

        return 0;
    }

    public CritterEncounterData? GetEncounteredCritterData(EObjectType type)
    {
        int typeIndex = (int)type - 64;
        return encounteredCritterData[typeIndex];
    }
}
