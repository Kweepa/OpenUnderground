using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Random = UnityEngine.Random;

[System.Serializable]
public class CritterSaveData : UUObjectSaveData
{
    public int hp;
    public int originalHp;
    public int goal;
    public int gtarg;
    public int critterLevel;
    public int talkedTo;
    public int attitude;
    public int yhome;
    public int xhome;
    public int heading;
    public int hunger;
    public int whoami;
    public int movementType;
    public int state;
    public float stateTime;
    public bool playerAlly;
    public List<Vector3> path;
    
    // Animator state
    public int animatorStateHash; // fullPathHash from AnimatorStateInfo
    public float animatorNormalizedTime;
    public float animatorSpeed;
    public string currentAnimation; // Bookkeeping variable for current animation name
    
    // Status effect timers
    public float poisonTime;
    public float timeToNextPoisonDamage;
    public float paralyzeTime;
    public float confusionTime;
    public float curseTime;
    public float damagedTimer;
    
    // State machine variables
    public bool actionDone;
    public bool deathProcessed;
    public bool remainsSpawned;
    public float wallRubTime;
    public float initialTurnAngle;
    
    // Attack lunge state
    public float mLungeTime;
    public Vector3 mLungeDirection;
    public Vector3 mLungeDestination;
    
    // Sprite animation state (for non-Animator critters)
    public float frameTime;
    public int frameIndex;
    
    // Loot
    public List<ObjectSaveData> loot;
    
    // Conversation state/memory between conversations
    public short[] charGlobals;
}

// Player detection mechanics
// stealth - move quietly
// conceal - hard to see
// invisibility - both stealth and conceal
// Detection based on line of sight and making sound (e.g., footsteps, swimming, attack sounds)
// I wonder if the armour type you wear should affect the sounds you make (at least as far as detection goes)
// If someone is attacked and calls for support, response should be to get closer, then reevaluate
// 

public enum EWhoAmI
{
    None,
    Corby,
    Shak,
    Goldthirst,
    Shanklick,
    Eyesnack,
    Marrowsuck,
    Ketchaval,
    Retichall,
    Vernix,
    Lanugo,
    Thorlson,
    DornaIronfist,
    Morlock,
    DrOwl,
    Sseetharee,
    Ishtass,
    SetharStrongarm,
    LakshiLongtooth,
    Hagbard,
    Gulik,
    Steeltoe,
    Golem,
    Judy,
    Prisoner,
    Door,
    Celaven,
    Garamon,
    Zak,
    Jaacar = 64,
    Eb,
    Drog,
    Bragit,
    Brawnclan = 88,
    Hewstone,
    Ironwit,
    Janus,
    Gazer = 110,
    Bandit = 112,
    HeadBandit,
    Issleek,
    Oradinar = 136,
    Linnet,
    Derek,
    Trisch,
    Ree,
    Feznor,
    Rodrick,
    Biden,
    Rawstag,
    Doris = 146,
    Kyle,
    Cecil,
    Meredith,
    Anjor = 161,
    Kneenibble,
    Delanrey = 184,
    Nilpont,
    Folina,
    Illomo,
    Gralwart,
    Shenilor,
    Bronus,
    Ranthru,
    Fyrgen,
    Louvnon,
    Dominus,
    Warren = 207,
    Cardon,
    Guard209,
    Naruto,
    Dantes,
    Kallistan,
    Fintor,
    Bolinard,
    Smonden,
    Jailor,
    Gurstang,
    Griffle,
    Guard219,
    Guard220,
    Imp,
    Guard222,
    
    Tyball = 231,
    Carasso,
    
    Count
}

[System.Serializable]
public struct CritterEncounterData
{
    public int level;
    public int objectIndex;
    public int originalHp;
}

public class Critter : UUObject
{
    private int slot;
    private int frameIndex;
    private float frameTime;
    protected CritterLoader.Critter crit;

    static readonly float kXScale = 0.04f;
    static readonly float kYScale = 0.048f;

    static readonly float kWakeUpRange = 50.0f;

    // Static cache for string -> enum conversion (zero allocations)
    private static readonly Dictionary<string, CritterSoundType> soundTypeCache;

    static Critter()
    {
        // Initialize sound type cache
        soundTypeCache = new Dictionary<string, CritterSoundType>();
        foreach (CritterSoundType soundType in System.Enum.GetValues(typeof(CritterSoundType)))
        {
            soundTypeCache[soundType.ToString()] = soundType;
        }
    }

    [Tooltip("ScriptableObject containing critter sounds. If assigned, this will be used instead of the sounds array above.")]
    public CritterSoundData soundData;

    public float meleeAttackRange = 4.0f;
    public float meleeAttackHysteresis = 1.0f;

    public List<EObjectType> likes = new();
    public List<EObjectType> dislikes = new();

    public short[] charGlobals;

    public Font debugFont;

    public CutscenePlayer tyballDeath;

    private float confusionTime;

    private float curseTime;
    private float curseMultiplier = 1.0f;

    private float bobTime;

    private float damagedTimer;

    private float theftDetectedTimer;

    public int range;

    private Renderer eyeGlowRenderer;
    private static readonly Color[] eyeColors = { Color.green, Color.yellow, Color.red };

    public bool removeTalker;

    public enum EState
    {
        Initialize,
        Enable,
        Crouch,
        Idle,
        Fidget,
        TurnToWander,
        Wander,
        Converse,
        TurnToApproach,
        Approach,
        CombatIdle,
        CombatTurn,
        Attack,
        ProjectileIdle,
        ProjectileAttack,
        TurnToFlee,
        Flee,
        Flinch,
        Die,
        Dead,
        Cleanup
    }

    public enum EGoal
    {
        Stand0,
        GoTo,
        Wander2,
        FollowTarget,
        Wander4,
        AttackTarget5,
        FleeTarget,
        Stand7,
        Wander8,
        AttackTarget9,
        AwaitConversation,
        Stand11,
        Stand12
    }

    public enum EAttitude
    {
        Hostile,
        Upset,
        Mellow,
        Friendly
    }

    public enum EMovementType
    {
        TwilightZone,
        Walking,
        Flying,
        Swimming,
        Creeping,
        Crawling
    }

    public EState state;
    protected float stateTime;
    private float wallRubTime;

    public int hp;
    public EGoal goal;
    public int gtarg;
    public int critterLevel;
    public int talkedTo;

    public EAttitude attitude;

    //public int height;
    public int yhome;
    public int xhome;
    public int heading;
    public int hunger;
    public EWhoAmI whoami;

    public float walkSpeed = 1.0f;
    public float confusedSpeed = 2.0f;
    public float runSpeed = 3.0f;
    public float fleeSpeed = 4.0f;

    public float flyerBobMagnitude = 0.5f;
    public float flyerBobSpeed = 1.0f;

    public int originalHp;

    public EMovementType movementType;

    private float flyerBobTime;
    private float mLungeTime;
    private float mLungeIdealRange = 1.0f;
    private Vector3 mLungeDestination;
    private Vector3 mLungeDirection;

    public Critter playerLastEngagedInCombat;
    public Critter attackTarget;

    public bool playerAlly;

    // Track whether alert sound has been played for current detection session
    private bool hasPlayedAlertForCurrentDetection;

    public float wanderTurnSpeed = 180.0f;
    public float approachTurnSpeed = 360.0f;
    public float combatTurnSpeed = 360.0f;
    public float combatTurnTurnSpeed = 120.0f;
    public float projectileTurnSpeed = 360.0f;
    public float fleeTurnTurnSpeed = 540.0f;
    public float fleeTurnSpeed = 180.0f;

    public float minCeilingDistance = 1.0f;

    [Tooltip("Game objects to hide when dead (e.g., helmet, non-lootable items) to avoid confusion with loot")]
    public GameObject[] nonLoot;

    private float initialTurnAngle;

    private static int ExtractBits(byte[] data, byte i, int b0, int b1)
    {
        int v = (data[i + 1] << 8) | data[i];
        return (v >> b0) & ((1 << (1 + b1 - b0)) - 1);
    }

    public override void Initialize(ushort[] objData, byte[] critterData)
    {
        base.Initialize(objData, critterData);

        // get all properties from critterData
        // some levels have critters that are never spawned, with object index >= 256
        // these critters won't have critterData, and can probably be deleted
        if (critterData != null)
        {
            hp = critterData[0];
            goal = (EGoal)(critterData[3] & 15);
            gtarg = ExtractBits(critterData, 3, 4, 11);
            critterLevel = critterData[5] & 15;
            talkedTo = critterData[6] & (1 << 5);
            attitude = (EAttitude)(critterData[6] >> 6);
            //height = ExtractBits(critterData, 7, 6, 12);
            yhome = ExtractBits(critterData, 14, 4, 9);
            xhome = ExtractBits(critterData, 14, 10, 15);
            heading = critterData[16] & 31;
            hunger = critterData[17] & 127;
            whoami = (EWhoAmI)critterData[18];

            movementType = (EMovementType)(stats.Category & 0xf);
        }

        originalHp = hp;

        // try to keep rodrick in the hall area
        range = (whoami == EWhoAmI.Rodrick) ? 15 : 20;
    }

    /// <summary>
    /// Adds <see cref="VisibilityGroup"/> when missing. Called from <see cref="Start"/>,
    /// <see cref="PostLoadInitialize"/>, and <see cref="PrepareVisibilityAfterTemplateClone"/>.
    /// </summary>
    private void EnsureVisibilityGroup()
    {
        cachedVisibilityGroup = GetComponent<VisibilityGroup>();
        if (cachedVisibilityGroup == null)
        {
            cachedVisibilityGroup = gameObject.AddComponent<VisibilityGroup>();
            cachedVisibilityGroup.Initialize();
        }
    }

    /// <summary>
    /// Call immediately after Instantiate from a scene template (<see cref="Trap.DoCreateObjectTrap"/>).
    /// Resets VisibilityGroup defaults so PVS-culled templates do not produce clones with all-false logical mesh state.
    /// </summary>
    public void PrepareVisibilityAfterTemplateClone()
    {
        EnsureVisibilityGroup();
        cachedVisibilityGroup.ReinitializeLogicalDefaultsAllVisible();
    }

    public void Start()
    {
        EnsureVisibilityGroup();
    }

    public override void PostLoadInitialize(bool restoredFromSave = false)
    {
        base.PostLoadInitialize(restoredFromSave);
        // look up the critter
        crit = CritterLoader.GetCritter(type);

        cachedCharacterController = GetComponent<CharacterController>();
        cachedAnimator = GetComponent<Animator>();
        EnsureVisibilityGroup();

        // Flush visibility state to ensure it's correctly synced after load
        // This ensures the first Update() frame will have correct visibility
        cachedVisibilityGroup.FlushVisibilityState();

        // Only set slot for critters without Animators (legacy sprite-based critters)
        if (cachedAnimator == null)
        {
            slot = 32; // idle
        }

        switch (movementType)
        {
        case EMovementType.TwilightZone:
            if (isTwilightZone)
            {
                // type/slot - character
                // 79/32 - bat
                // 79/40 - teeth
                // 79/80 - spiral
                // 79/128 - devil panther
                // 125/32 - lightning
                // 125/80 - fish              
                // 126/32 - eyeball
                // 126/80 - skull

                // these rules don't seem quite right, but
                // importantly they work for the lightning.
                int[] slots = { 32, 40, 80, 128 };
                slot = slots[((int)whoami) & 3];
                if (crit.slots[slot, 0] == null)
                {
                    slot = slots[((int)whoami) & 2];
                }
            }
            break;
        case EMovementType.Flying:
            switch (type)
            {
            case EObjectType.Imp:
            case EObjectType.Mongbat:
            case EObjectType.CaveBat:
            case EObjectType.VampireBat:
            case EObjectType.Tyball:
            case EObjectType.Gazer:
                // don't want the defaults
                break;
            default:
                cachedCharacterController.center = Vector3.zero;
                cachedCharacterController.height = 1.0f;
                break;
            }
            break;
        case EMovementType.Creeping:
        case EMovementType.Crawling:
            switch (type)
            {
            case EObjectType.Reaper: // reaper is crawling :O
                // don't want the defaults
                break;
            default:
                cachedCharacterController.center = 0.5f * Vector3.up;
                cachedCharacterController.height = 1.0f;
                break;
            }
            break;
        }

        if (cachedCharacterController != null)
        {
            if ((movementType is EMovementType.Crawling or EMovementType.Creeping or EMovementType.Walking && type != EObjectType.Wisp)
                || type == EObjectType.Tyball)
            {
                // steps are 0.75m
                cachedCharacterController.stepOffset = 0.8f;
                // recommendation is skin width is 10% of radius
                cachedCharacterController.skinWidth = 0.1f * cachedCharacterController.radius;
                Vector3 center = cachedCharacterController.center;
                center.y += cachedCharacterController.skinWidth;
                cachedCharacterController.center = center;

                // Ceiling layer exclusion - allows large stepOffset in tight vertical spaces
                int ceilingLayer = LayerMask.NameToLayer("Ceiling");
                if (ceilingLayer >= 0)
                {
                    cachedCharacterController.excludeLayers |= (LayerMask)(1 << LayerMask.NameToLayer("Ceiling"));
                }
            }
        }

        name += " " + GetGivenName();

        if (whoami == EWhoAmI.Tyball)
        {
            singularName = "mage";
        }

        eyeGlowRenderer = GetComponentsInChildren<SkinnedMeshRenderer>().FirstOrDefault(r => r.CompareTag("EyeGlow"));

        // Defer animator restore to LateUpdate so it runs after any Start() (e.g. BaseCritterSM) that would overwrite it
        if (restoredFromSave && cachedAnimator != null && savedAnimatorStateHash != 0)
        {
            pendingAnimatorRestore = true;
        }
    }

    private List<Vector3> path;
    protected CharacterController cachedCharacterController;
    protected Animator cachedAnimator;
    protected VisibilityGroup cachedVisibilityGroup;

    // Temporary storage for animator state during load
    private int savedAnimatorStateHash = 0;
    private float savedAnimatorNormalizedTime = 0f;
    private float savedAnimatorSpeed = 1f;
    private bool pendingAnimatorRestore = false;

    // Reusable arrays for step height checking to avoid allocations
    private readonly int[] fromHeights = new int[4];
    private readonly int[] toHeights = new int[4];

    // Maximum drop height in meters that the critter can safely drop down
    public float maxDropHeight = 2.0f;

    // Cached current tile - updated each frame in Update()
    private Tile cachedCurrentTile;

    Vector3 GetLookahead(float a)
    {
        Vector3 to = path[0] - transform.position;
        float d = to.magnitude;
        if (d > a)
        {
            return transform.position + a * to.normalized;
        }
        else if (path.Count < 2)
        {
            return path[0];
        }
        else
        {
            a -= d;
            to = path[1] - path[0];
            return path[0] + a * to.normalized;
        }
    }

    bool TurnTo(Vector3 point, float turnSpeed, float doneAngle)
    {
        Vector3 direction = point - transform.position;
        direction.y = 0.0f;
        if (direction.sqrMagnitude > 0.0001f)
        {
            direction.Normalize();
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction),
                turnSpeed * Time.deltaTime);
            return Vector3.Angle(transform.forward, direction) < doneAngle;
        }

        return true;
    }

    /// <summary>
    /// Checks if a bridge is at approximately the correct height for the critter.
    /// Returns true if the bridge is at the critter's height (within stepOffset tolerance).
    /// </summary>
    private bool IsBridgeAtHeight(Bridge bridge, float critterHeight)
    {
        if (bridge == null)
            return false;

        float bridgeWorldHeight = bridge.z * yScale;
        float heightDifference = Mathf.Abs(bridgeWorldHeight - critterHeight);
        return heightDifference < cachedCharacterController.stepOffset;
    }

    /// <summary>
    /// Checks if a tile has a bridge at approximately the correct height for the critter.
    /// Returns true if a bridge is found at the critter's height (within stepOffset tolerance).
    /// </summary>
    private bool HasBridgeAtHeight(Tile tile, float critterHeight)
    {
        if (tile == null)
            return false;

        return IsBridgeAtHeight(tile.bridge, critterHeight) || IsBridgeAtHeight(tile.upperBridge, critterHeight);
    }

    private void UpdateLunge()
    {
        if (mLungeTime <= 0.0f)
            return;

        Vector3 targetPos = GetTargetFootPos();
        TurnTo(targetPos, combatTurnSpeed, 20.0f);

        // Check if we've reached the ideal location (plane crossing check)
        Vector3 fromDest = transform.position - mLungeDestination;
        float dotProduct = Vector3.Dot(fromDest, mLungeDirection);
        float distanceToPlayer = Vector3.Distance(transform.position, targetPos);

        if (dotProduct > 0.0f || distanceToPlayer < mLungeIdealRange)
        {
            // Reached ideal location or close enough to player, stop lunging
            mLungeTime = 0.0f;
            return;
        }

        mLungeTime -= Time.deltaTime;

        // Check tile one character radius ahead for dangerous terrain
        Vector3 checkAheadPos = transform.position + mLungeDirection * cachedCharacterController.radius;
        Tile checkAheadTile = LevelLoader.GetTile(Tile.GetTileX(checkAheadPos.x), Tile.GetTileY(checkAheadPos.z));
        bool safeTerrain = true;

        if (checkAheadTile != null && movementType != EMovementType.Flying)
        {
            ETerrainType terrainType = checkAheadTile.GetFloorTerrain();
            if (movementType == EMovementType.Swimming)
            {
                // Swimmers can only go through water
                safeTerrain = terrainType == ETerrainType.Water;
            }
            else
            {
                // Non-swimmers and non-flyers avoid water and lava
                safeTerrain = terrainType != ETerrainType.Water && terrainType != ETerrainType.Lava;

                // Check if there's a bridge at approximately the correct height
                // If so, mark as safe terrain even if the floor below is water or lava
                if (HasBridgeAtHeight(checkAheadTile, transform.position.y))
                {
                    safeTerrain = true;
                }
            }
        }

        // Check the final destination tile
        Tile finalTile = LevelLoader.GetTile(Tile.GetTileX(mLungeDestination.x), Tile.GetTileY(mLungeDestination.z));
        bool finalTileAcceptable = false;

        if (finalTile != null && cachedCurrentTile != null && finalTile.type != 0)
        {
            // Check if there's a bridge at approximately the correct height
            // Swimmers and flyers don't check bridges (swimmers stay in water, flyers ignore terrain)
            if (movementType != EMovementType.Flying && movementType != EMovementType.Swimming)
            {
                float critterHeight = transform.position.y;
                finalTileAcceptable = HasBridgeAtHeight(finalTile, critterHeight);
            }

            // If no bridge at right height, check floor height
            if (!finalTileAcceptable)
            {
                finalTileAcceptable = finalTile.floorHeight >= cachedCurrentTile.floorHeight - 2;
            }
        }

        if (finalTileAcceptable && safeTerrain)
        {
            Vector3 move = mLungeDirection * (runSpeed * 2.0f * Time.deltaTime);
            if (movementType != EMovementType.Flying)
            {
                move.y += Physics.gravity.y * Time.deltaTime;
            }

            // Apply overlap avoidance to prevent moving into other critters
            move = ApplyOverlapAvoidance(move, updateWallRubTime: false);

            cachedCharacterController.Move(move);
        }
    }

    private ObjectsData.CritterData stats => DataLoader.sDataLoader.objectsData.critterStats[(int)type & 63];

    // for now just use first attack value. not sure how we determine which, if there are options
    private int attackChanceToHit => stats.AttackChanceToHit[0];

    private int attackDamage => stats.AttackDamage[0];

    private int strength => (int)(stats.Strength * curseMultiplier);

    private int equipDamage => (int)(stats.EquipDamage * curseMultiplier);

    public int race => stats.Race;

    private int blood => stats.Blood;

    private int poison => stats.Poison;

    public int height => DataLoader.sDataLoader.comObjProps[(int)type].height;

    private bool canOpenDoors => DataLoader.sDataLoader.comObjProps[(int)type].canOpenDoors;

    private float detectionRange => Tile.xzScale * (type == EObjectType.SlasherOfVeils ? 99.0f : Mathf.Sqrt(stats.DetectionRange));

    private float theftDetectionRange => Tile.xzScale * Mathf.Sqrt(stats.TheftDetectionRange);

    private int baseDefence => stats.Defence;

    private int treasureProb => stats.treasureLoot >> 4;
    private int treasureStackProb => stats.treasureLoot & 15;

    private int foodProb => stats.foodLoot & 15;
    private EObjectType foodItem => EObjectType.PieceOfMeat + (stats.foodLoot >> 4);

    public override int GetDefence()
    {
        if (Magic.sMagic.IsSpellActive(Magic.ESpell.FreezeTime))
        {
            return 0;
        }
        return (int)(baseDefence * curseMultiplier);
    }

    private bool isTwilightZone => type is EObjectType.TwilightZoneA or EObjectType.TwilightZoneB or EObjectType.TwilightZoneC;

    public static bool canBeSummoned(EObjectType type)
    {
        return DataLoader.sDataLoader.comObjProps[(int)type].canBeSummoned;
    }

    public static ObjectsData.CritterData getStats(EObjectType type)
    {
        return DataLoader.sDataLoader.objectsData.critterStats[(int)type - 64];
    }

    private readonly Collider[] sphereOverlapCache = new Collider[12];

    protected Vector3 swimmerDesiredMotion;

    /// <summary>
    /// Applies overlap avoidance to prevent critters from moving into each other/separate critters.
    /// </summary>
    /// <param name="motion">The motion vector to adjust</param>
    /// <param name="updateWallRubTime">Whether to update wallRubTime when overlaps occur</param>
    /// <returns>The adjusted motion vector with overlap avoidance applied</returns>
    private Vector3 ApplyOverlapAvoidance(Vector3 motion, bool updateWallRubTime = true)
    {
        int numOverlaps = Physics.OverlapSphereNonAlloc(transform.position, 2.0f * cachedCharacterController.radius,
            sphereOverlapCache, 1 << LayerMask.NameToLayer("Characters"));

        for (int i = 0; i < numOverlaps; ++i)
        {
            Collider col = sphereOverlapCache[i];
            CharacterController cc = col as CharacterController;
            if (cc != null && cc != cachedCharacterController && cc.enabled)
            {
                Vector3 centerA = transform.TransformPoint(cachedCharacterController.center) + motion;
                Vector3 centerB = col.transform.TransformPoint(cc.center);
                Vector3 bToA = centerA - centerB;

                // For non-flyers, only consider horizontal overlap
                if (movementType != EMovementType.Flying)
                {
                    bToA.y = 0.0f;
                }

                float overlap = cachedCharacterController.radius + cc.radius - bToA.magnitude;
                if (overlap > 0.0f)
                {
                    bToA.Normalize();
                    motion += overlap * bToA;
                    if (updateWallRubTime)
                    {
                        wallRubTime += Time.deltaTime;
                    }

                    if (state is EState.Wander or EState.Approach or EState.Flee)
                    {
                        // if walking into someone, stop so we don't look ridiculous running on the spot
                        if (Vector3.Dot(bToA, transform.forward) > 0.0f)
                        {
                            SetState(EState.Idle);
                        }
                    }
                }
            }
        }

        return motion;
    }

    /// <summary>
    /// Detects doors ahead and returns a steering vector towards the door center.
    /// </summary>
    private Vector3 GetSteeringForDoors(Vector3 movementDir, float lookaheadDistance)
    {
        if (movementType == EMovementType.Flying)
        {
            return Vector3.zero; // Flyers don't need door steering
        }

        Vector3 currentPos = transform.position;

        // Check if we're approaching a door by looking at upcoming path points
        if (path == null || path.Count == 0)
        {
            return Vector3.zero;
        }

        Door door = cachedCurrentTile.door;
        if (door == null)
        {
            // Check the next path point and a bit ahead
            Vector3 nextPathPoint = path[0];
            Tile nextTile = LevelLoader.GetTile(Tile.GetTileX(nextPathPoint.x), Tile.GetTileY(nextPathPoint.z));
            if (nextTile != null)
            {
                door = nextTile.door;
            }
            if (door == null)
            {
                return Vector3.zero;
            }
        }

        // Only steer if door is open or can be opened
        if (!door.isOpen)
        {
            // Check if critter can open doors
            bool canOpen = DataLoader.sDataLoader.comObjProps[(int)type].canOpenDoors && !door.Locked(out _) && !door.spiked;
            if (!canOpen)
            {
                return Vector3.zero;
            }
        }

        Vector3 doorCenter = door.transform.position;
        doorCenter.y = currentPos.y; // Use current height for horizontal steering

        // If door is closed and openable, check if it would open towards the critter
        Vector3 steeringTarget = doorCenter;
        bool doorWillSwingIntoUs = false;
        if (!door.isOpen && door.type != EObjectType.Portcullis)
        {
            // Check if door would open towards the critter
            float openDirection = (door.flags & 2) == 0 ? -1.0f : 1.0f;
            Vector3 critterLocalPos = Utils.InverseTransformPointNoScale(door.transform, currentPos);

            // If critter is on the side the door will swing into (would open towards critter)
            if (critterLocalPos.z * openDirection < 0.0f)
            {
                doorWillSwingIntoUs = true;
                // Steer towards the side opposite the hinge
                // The hinge is on negative X, so steer towards positive X (right/red vector)
                steeringTarget = doorCenter + door.transform.right;
            }
        }

        // Check if we're close enough to the door to need steering
        Vector3 toTarget = steeringTarget - currentPos;
        toTarget.y = 0.0f;
        float distanceToTarget = toTarget.magnitude;

        // Only apply steering when within threshold (e.g., 2 meters)
        float steeringThreshold = 4.0f;
        if (distanceToTarget > steeringThreshold)
        {
            return Vector3.zero;
        }

        // also open door if we're in the correct location
        if (!door.isOpen)
        {
            if (!doorWillSwingIntoUs)
            {
                door.Open();
            }
            else
            {
                // check we're out of the way of the swinging door
                Vector3 offsetFromHinge = transform.position - door.hinge.transform.position;
                offsetFromHinge.y = 0.0f;
                if (offsetFromHinge.magnitude > 1.5f)
                {
                    door.Open();
                }
            }
        }

        // Calculate steering vector towards target
        if (distanceToTarget > 0.1f && distanceToTarget < steeringThreshold)
        {
            // Weight by distance - stronger steering when closer
            float weight = Mathf.Clamp01(1.0f - distanceToTarget / steeringThreshold);
            return toTarget.normalized * weight * 0.6f; // Scale down to avoid oversteering
        }

        return Vector3.zero;
    }

    bool MoveAlongPath(float moveSpeed, float doneDistance)
    {
        bool done = false;
        Vector3 off = path[0] - transform.position;
        if (movementType != EMovementType.Flying)
        {
            off.y = 0.0f;
        }
        else
        {
            // bob
            flyerBobTime += Time.deltaTime;
            off.y += flyerBobMagnitude * Mathf.Sin(flyerBobTime * flyerBobSpeed);
        }

        float distanceToNextPathPoint = Mathf.Sqrt(off.x * off.x + off.z * off.z);
        if (distanceToNextPathPoint < doneDistance)
        {
            path.RemoveAt(0);
            if (path.Count == 0)
            {
                done = true;
            }
        }
        else if (Time.deltaTime > 1e-7f)
        {
            float distance = Mathf.Min(moveSpeed * Time.deltaTime, off.magnitude);
            Vector3 motion = distance * off.normalized;

            // Apply steering corrections for doors
            float lookaheadDistance = Mathf.Min(off.magnitude, 2.0f);
            Vector3 doorSteering = GetSteeringForDoors(off.normalized, lookaheadDistance);

            // Blend steering correction into movement direction
            if (doorSteering.sqrMagnitude > 0.01f)
            {
                // Blend the steering correction with the original direction
                // Use a weighted average to avoid completely overriding path following
                Vector3 blendedDir = Vector3.Slerp(off.normalized, (off.normalized + doorSteering).normalized, 0.4f);
                motion = distance * blendedDir;
            }

            if (movementType != EMovementType.Flying)
            {
                // push down hard to keep on ground
                motion -= 3.0f * Time.deltaTime * Vector3.up;
            }

            // find nearby characters and prevent moving into them
            motion = ApplyOverlapAvoidance(motion, updateWallRubTime: true);

            if (movementType == EMovementType.Swimming)
            {
                swimmerDesiredMotion = motion;
            }
            else
            {
                Vector3 oldPos = transform.position;
                cachedCharacterController.Move(motion);
                Vector3 actualMovement = transform.position - oldPos;
                actualMovement.y = 0.0f;
                motion.y = 0.0f;
                if (actualMovement.magnitude < 0.5f * motion.magnitude)
                {
                    wallRubTime += Time.deltaTime;
                }
            }
        }

        return done;
    }

    private string currentAnimation = "Idle";

    protected virtual string[] GetAttackCandidateStates()
    {
        string[] candidateStates;
        switch (type)
        {
        case EObjectType.FleshSlug:
        case EObjectType.AcidSlug:
            candidateStates = new[] { "Attack_Bash" };
            break;
        case EObjectType.GiantRatBrown:
        case EObjectType.GiantRatGrey:
        case EObjectType.GiantSpider:
        case EObjectType.WolfSpider:
        case EObjectType.DreadSpider:
            candidateStates = new[] { "Attack_Bite", "Attack_Jump" };
            break;
        case EObjectType.GhoulA:
        case EObjectType.GhoulB:
        case EObjectType.DarkGhoul:
            candidateStates = new[] { "Attack_Left", "Attack_Right", "Attack_Both" };
            break;
        case EObjectType.Headless:
        case EObjectType.Gazer:
        case EObjectType.GreenLizardman:
        case EObjectType.GreyLizardman:
        case EObjectType.RedLizardman:
        case EObjectType.Troll:
        case EObjectType.FeralTroll:
        case EObjectType.GreatTroll:
        case EObjectType.Reaper:
        case EObjectType.EarthGolem:
        case EObjectType.StoneGolem:
        case EObjectType.MetalGolem:
        case EObjectType.ShadowBeast:
            candidateStates = new[] { "Attack_Left", "Attack_Right" };
            break;
        case EObjectType.Outcast:
        case EObjectType.FighterA:
        case EObjectType.FighterB:
        case EObjectType.FighterC:
        case EObjectType.FighterD:
        case EObjectType.FighterE:
        case EObjectType.GoblinA:
        case EObjectType.GoblinB:
        case EObjectType.GoblinC:
        case EObjectType.GoblinD:
        case EObjectType.GoblinE:
        case EObjectType.GoblinF:
        case EObjectType.Skeleton:
        case EObjectType.MountainmanA:
        case EObjectType.MountainmanB:
            candidateStates = new[] { "Attack_Bash_Prepare", "Attack_Slash_Prepare", "Attack_Thrust_Prepare" };
            break;
        case EObjectType.Lurker:
        case EObjectType.DeepLurker:
            candidateStates = new[] { "Attack_Bite", "Attack_SwipeL", "Attack_SwipeR" };
            break;
        case EObjectType.Imp:
        case EObjectType.Mongbat:
            candidateStates = new[] { "SlashAttack" };
            break;
        default:
            candidateStates = new[] { "Attack" };
            break;
        }
        return candidateStates;
    }

    protected virtual void ChangeAnimation(string animationName)
    {
        if (cachedAnimator != null && animationName != currentAnimation)
        {
            if (animationName == "Attack")
            {
                string[] candidateStates = GetAttackCandidateStates();
                string chosenState = candidateStates[Random.Range(0, candidateStates.Length)];
                cachedAnimator.CrossFadeInFixedTime(chosenState, 0.15f);
            }
            else
            {
                if (cachedAnimator.HasState(0, Animator.StringToHash(animationName)))
                {
                    cachedAnimator.CrossFadeInFixedTime(animationName, animationName == "Flinch" ? 0.05f : 0.15f);
                }
                else
                {
                    //Debug.LogWarning($"{name} has no animation {animationName}.");
                }
            }
            currentAnimation = animationName;
        }
    }

    private void PlayAttackSound()
    {
        PlaySoundByEnum(CritterSoundType.Attack);
    }

    protected virtual void PlayAlertSound()
    {
        PlaySoundByEnum(CritterSoundType.Alert);
        hasPlayedAlertForCurrentDetection = true;
    }

    /// <summary>
    /// Get the sounds array
    /// </summary>
    private CritterSoundCategory[] GetSounds()
    {
        if (soundData != null)
        {
            return soundData.sounds;
        }
        return null;
    }

    /// <summary>
    /// Play a sound of the specified type from this critter's sound library.
    /// Picks a random clip from the category.
    /// </summary>
    public void PlaySoundByEnum(CritterSoundType soundType)
    {
        if (GetSounds() != null)
        {
            // Find the sound category
            foreach (var category in GetSounds())
            {
                if (category.soundType == soundType)
                {
                    if (Random.value <= category.chance)
                    {
                        AudioClip clip = category.GetRandomClip();
                        if (clip != null)
                        {
                            AudioSource src = Utils.PlayClipOccluded(clip, transform.position, category.volume);
                            if (src != null)
                            {
                                float basePitch = category.pitch;
                                // Apply random pitch variation if configured
                                if (category.pitchVariation > 0f)
                                {
                                    basePitch += Random.Range(-category.pitchVariation, category.pitchVariation);
                                }
                                src.pitch = basePitch;
                            }
                        }
                    }
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Animation event callback to start the lunge forward during attack.
    /// </summary>
    /// <param name="idealRange">The ideal minimum distance from target (in meters)</param>
    public void OnLunge(float idealRange)
    {
        mLungeTime = 0.5f;
        mLungeIdealRange = idealRange;

        // Calculate lunge destination and direction
        Vector3 targetPos = GetTargetFootPos();

        // For flyers, target 2m above player feet
        if (movementType == EMovementType.Flying)
        {
            targetPos.y += 2.0f;
        }

        float dist = Vector3.Distance(transform.position, targetPos);

        if (dist > idealRange)
        {
            Vector3 dir = (targetPos - transform.position).normalized;

            // For non-flyers, remove vertical component from direction
            if (movementType != EMovementType.Flying)
            {
                dir.y = 0.0f;
                dir.Normalize();
            }

            // Calculate final destination (clamp to max 3m from start)
            float lungeDistance = Mathf.Min(dist - idealRange, 3.0f);
            mLungeDestination = transform.position + dir * lungeDistance;
            mLungeDirection = dir;
        }
        else
        {
            // Already at ideal range, no lunge needed
            mLungeTime = 0.0f;
        }
    }

    /// <summary>
    /// Play a sound by string name (for animation events).
    /// Uses cached dictionary for zero-allocation lookup.
    /// </summary>
    public void PlaySound(string soundTypeName)
    {
        if (soundTypeCache.TryGetValue(soundTypeName, out CritterSoundType soundType))
        {
            PlaySoundByEnum(soundType);
        }
        else
        {
            Debug.LogWarning($"Unknown sound type: {soundTypeName} for {name}");
        }
    }

    public void AttackEvent()
    {
        // Stop the lunge forward movement when attack event fires (with a little follow through)
        mLungeTime = Mathf.Min(mLungeTime, 0.15f);
        TryDamageTarget();
    }

    private void ProjectileEvent()
    {
        if (type == EObjectType.Tyball)
        {
            // choose from a few possibilities. for now launch sheet lightning to see
            Magic.sMagic.CastSheetLightning(this);
        }
        else
        {
            LaunchProjectile();
        }
    }

    protected bool actionDone;
    private bool deathProcessed;
    private bool remainsSpawned;

    private void EnsureDeathProcessedOnce()
    {
        if (deathProcessed)
        {
            return;
        }

        // Mark first to ensure idempotence even if any downstream work triggers reentry.
        deathProcessed = true;

        SpawnInventory();
        SpawnCorpseContents();
        SpawnRandomReplacement();
        BloodStain.TrySpawnDeathStain(transform.position, stats.Remains, blood);

        // TODO: reduce based on relative level of player and enemy
        // also maybe reduce based on max abyss level reached...?
        PlayerObject.AddXP(10 * stats.Exp);

        if (whoami == EWhoAmI.Gazer)
        {
            PlayerData.SetQuestFlag(EQuestFlag.GazerKilled, 1);
        }
        else if (whoami == EWhoAmI.Rodrick)
        {
            PlayerData.SetQuestFlag(EQuestFlag.RodrickKilled, 1);
        }
        else if (whoami == EWhoAmI.Tyball)
        {
            PlayerObject.Player.StartCoroutine(TyballDead(this));
        }
        else if (type == EObjectType.WolfSpider && LevelLoader.sLevelLoader.loadedLevel == 1)
        {
            PlayerData.sData.navreyKilled = true;
        }

        PlayerData.sData.SetDefeatedCritter(typeNum - 64, true);

        // Hide non-loot objects when dead to avoid confusion with loot
        if (nonLoot != null)
        {
            foreach (GameObject obj in nonLoot)
            {
                if (obj != null)
                {
                    cachedVisibilityGroup.SetGameObjectState(obj, false);
                }
            }
        }
    }

    private void EnsureRemainsSpawnedOnce()
    {
        if (remainsSpawned)
        {
            return;
        }

        remainsSpawned = true;
        SpawnRemains();
    }

    public void FinishedEvent()
    {
        // if we're crossfading out to a new state, don't want to interrupt that state
        if (!cachedAnimator.IsInTransition(0))
        {
            if (DebugPrintThis)
            {
                Debug.Log($"{name} {Time.realtimeSinceStartup:F3} {currentAnimation} finished");
            }
            actionDone = true;
        }
    }

    protected virtual void SetState(EState newState)
    {
        if (DebugPrintThis)
        {
            Debug.Log($"{name} {Time.realtimeSinceStartup:F3} state -> {newState}");
        }
        state = newState;
        frameTime = 0.0f;
        frameIndex = 0;
        actionDone = false;
        switch (state)
        {
        case EState.Crouch:
            ChangeAnimation("Crouched_Idle");
            break;
        case EState.Idle:
        case EState.Converse:
            if (!isTwilightZone)
            {
                slot = 32;
            }
            ChangeAnimation("Idle");
            stateTime = Random.Range(2.0f, 4.0f);
            break;
        case EState.Fidget:
            ChangeAnimation("Fidget");
            stateTime = Random.Range(2.0f, 4.0f);
            break;
        case EState.TurnToWander:
        case EState.TurnToApproach:
            slot = 128;
            ChangeAnimation("Walk");
            break;
        case EState.Wander:
            wallRubTime = 0.0f;
            if (confusionTime > 0.0f)
            {
                ChangeAnimation("Run");
            }
            break;
        case EState.Approach:
            slot = 7;
            ChangeAnimation("Run");
            wallRubTime = 0.0f;
            flyerBobTime = 0.0f;
            break;
        case EState.CombatIdle:
            slot = 0;
            stateTime = Random.Range(0.5f, 2.5f);
            ChangeAnimation("CombatIdle");
            NotifyRaceOfAttack();
            PlayerObject.Player.RegisterEncounteredCritter(type, levelIndex, objectIndex, originalHp);
            break;
        case EState.CombatTurn:
            ChangeAnimation(initialTurnAngle < 0.0f ? "Turn_L" : "Turn_R");
            stateTime = 1.0f;
            break;
        case EState.Attack:
            // slot is determined by caller
            {
            // for now choose one randomly from available options
            List<int> slots = new List<int>();
            // = bash, 2 = slash, 3 = thrust
            if (crit.frameCount[1] > 0) slots.Add(1);
            if (crit.frameCount[2] > 0) slots.Add(2);
            if (crit.frameCount[3] > 0) slots.Add(3);
            slot = slots.Count == 1 ? slots[0] : slots[Random.Range(0, slots.Count - 1)];
            }
            ChangeAnimation("Attack");
            stateTime = 1.0f; // one second is more time for the player to dodge
            mLungeTime = 0.0f; // Reset lunge timer - will be set by animation event
            mLungeDirection = Vector3.zero;
            mLungeDestination = Vector3.zero;
            Music.InCombat();
            break;
        case EState.ProjectileIdle:
            slot = 0;
            stateTime = Random.Range(0.5f, 1.5f);
            ChangeAnimation("Idle");
            PlayerObject.Player.RegisterEncounteredCritter(type, levelIndex, objectIndex, originalHp);
            break;
        case EState.ProjectileAttack:
            slot = crit.frameCount[5] > 0 ? 5 : 1;
            ChangeAnimation("ProjectileAttack");
            stateTime = 1.0f; // when the projectile is generated
            Music.InCombat();
            break;
        case EState.TurnToFlee:
            slot = 128;
            ChangeAnimation("Walk");
            break;
        case EState.Flinch:
            ChangeAnimation("Flinch");
            break;
        case EState.Flee:
            ChangeAnimation("Run");
            break;
        case EState.Die:
            slot = 12;
            ChangeAnimation("Death");
            stateTime = 0.0f;
            Music.EnemyDied();
            if (cachedCharacterController != null)
            {
                // prevent the dead character from stepping up onto spawned inventory
                cachedCharacterController.stepOffset = 0.0f;
            }
            EnsureDeathProcessedOnce();
            break;
        case EState.Dead:
            cachedCharacterController.enabled = false;
            stateTime = 0.0f;
            EnsureRemainsSpawnedOnce();
            // 3d critters wait until the body isn't being looked at to disappear
            if (cachedAnimator == null || type == EObjectType.Rotworm)
            {
                Utils.DestroyCritter(this);
            }
            break;
        }
    }

    private IEnumerator TyballDead(Critter tyball)
    {
        PlayerData.sData.SetTyballDead();

        PlayerObject.Player.fadeIn = false;

        while (PlayerObject.Player.fade < 1.0f)
        {
            PlayerObject.Player.fade += Time.unscaledDeltaTime;
            yield return null;
        }

        PlayerObject.Player.fade = 1.0f;

        CutscenePlayer soliloquy = Instantiate(tyballDeath);
        // ReSharper disable once LoopVariableIsNeverChangedInsideLoop
        while (soliloquy != null)
        {
            yield return null;
        }

        // Snap to floor height at current XZ (Tyball may still be airborne after flying combat).
        Vector3 pos = tyball.transform.position;
        Tile tile = LevelLoader.GetTile(pos);
        if (tile != null && tile.type != 0 && tyball.cachedCharacterController != null)
        {
            float floorY = tile.GetFloorY(pos.x, pos.z);
            CharacterController cc = tyball.cachedCharacterController;
            pos.y = floorY - cc.center.y + 0.5f * cc.height;
            tyball.transform.position = pos;
        }

        tyball.cachedAnimator.Play("Death_Land");

        while (PlayerObject.Player.fade > 0.0f)
        {
            PlayerObject.Player.fade -= Time.unscaledDeltaTime;
            yield return null;
        }

        PlayerObject.Player.fade = 0.0f;
    }

    public bool DebugThis;
    public bool DebugPrintThis;

    protected virtual Vector3 GetProjectileLaunchPosition(bool estimated)
    {
        float localHeight;
        switch (type)
        {
        case EObjectType.FireElemental:
            localHeight = 0.5f * cachedCharacterController.height;
            break;
        case EObjectType.AcidSlug:
            localHeight = 0.3f;
            break;
        default:
            localHeight = cachedCharacterController.height - cachedCharacterController.radius;
            break;
        }
        return transform.position + localHeight * Vector3.up + cachedCharacterController.radius * transform.forward;
    }

    private Vector3 GetMeleeAttackPos()
    {
        return transform.position + 0.5f * cachedCharacterController.height * Vector3.up;
    }

    private Vector3 GetTargetPos()
    {
        if (attackTarget != null && attackTarget.hp > 0)
        {
            return attackTarget.transform.TransformPoint(attackTarget.cachedCharacterController.center);
        }

        return PlayerObject.Player.mainCamera!.transform.position + 0.5f * Vector3.down;
    }

    private Vector3 GetTargetFootPos()
    {
        if (attackTarget != null && attackTarget.hp > 0)
        {
            return attackTarget.transform.TransformPoint(attackTarget.cachedCharacterController.center) + attackTarget.cachedCharacterController.height * Vector3.down;
        }

        return PlayerObject.Player.mainCamera!.transform.position + 1.5f * Vector3.down;
    }

    private bool CanDoProjectileAttack(float distanceOffset)
    {
        // some creatures are projectile only?
        bool hasProjectileWeapon = false;
        switch (type)
        {
        case EObjectType.AcidSlug:
        case EObjectType.GoblinC:
        case EObjectType.GoblinD:
        case EObjectType.GoblinE:
        case EObjectType.Imp:
        case EObjectType.Gazer:
        case EObjectType.FireElemental:
            hasProjectileWeapon = true;
            break;
        }

        if (hasProjectileWeapon)
        {
            // also needs to be close enough and have line of sight to the target
            if (GetDistanceToTarget() + distanceOffset < 12.0f)
            {
                Vector3 startPos = GetProjectileLaunchPosition(true);
                Vector3 endPos = GetTargetPos();
                int mask = LayerMasks.EnvironmentAndCeiling;
                if (!Physics.Raycast(startPos, endPos - startPos, out _, 10.0f, mask))
                {
                    return true;
                }
            }
        }

        return false;
    }

    protected virtual EObjectType GetProjectileType()
    {
        EObjectType projectileType = 0;

        switch (type)
        {
        case EObjectType.AcidSlug:
            projectileType = EObjectType.Acid;
            break;
        case EObjectType.GoblinC:
        case EObjectType.GoblinD:
        case EObjectType.GoblinE:
            projectileType = EObjectType.SlingStone;
            break;
        case EObjectType.Imp:
            projectileType = EObjectType.LightningBolt;
            break;
        case EObjectType.Gazer:
            projectileType = EObjectType.MagicMissile;
            break;
        case EObjectType.FireElemental:
            projectileType = EObjectType.Fireball;
            break;
        }

        return projectileType;
    }

    private bool DetectPlayer()
    {
        if (PlayerData.sData.dead)
        {
            return false;
        }

        if (PlayerObject.Player.noise > 0.0f)
        {
            return true;
        }

        if (damagedTimer > 0.0f)
        {
            return true;
        }

        if (!Magic.sMagic.IsSpellActive(Magic.ESpell.Conceal) && !Magic.sMagic.IsSpellActive(Magic.ESpell.Invisibility))
        {
            // check we're looking towards the player
            Vector3 playerPos = PlayerObject.Player.transform.position;
            Vector3 dir = (playerPos - transform.position).normalized;
            float dot = Vector3.Dot(dir, transform.forward);
            if (dot > 0.0f)
            {
                float distance = (playerPos - transform.position).magnitude;
                // check line of sight
                if (!Physics.Raycast(transform.position, dir, distance, LayerMasks.EnvironmentAndCeiling))
                {
                    return true;
                }
            }
        }

        if (whoami == EWhoAmI.Tyball && talkedTo != 0)
        {
            // keep attacking until the player gets out of the detection range
            return true;
        }

        // Slasher can always see the player
        if (LevelLoader.sLevelLoader.loadedLevel == 9)
        {
            return true;
        }

        return false;
    }

    private bool CheckIdleToCombat(float distanceToPlayer)
    {
        if (confusionTime > 0.0f)
        {
            return false;
        }

        if (playerAlly && attackTarget == null)
        {
            return false;
        }

        if (attackTarget == null)
        {
            if (theftDetectedTimer > 0.0f)
            {
                return true;
            }
            return distanceToPlayer < detectionRange && DetectPlayer();
        }

        return true;
    }

    private float GetDistanceToTarget()
    {
        return (GetTargetPos() - transform.position).magnitude;
    }

    private bool IsGolemWithShieldOfValor()
    {
        // golem on level 6
        if (whoami == EWhoAmI.Golem)
        {
            foreach (UUObject obj in loot)
            {
                if (obj.type == EObjectType.ShinyShield)
                {
                    return true;
                }
            }
        }
        return false;
    }

    private bool IsViewToTargetClear()
    {
        Vector3 targetPos = GetTargetPos();
        Vector3 center = transform.position + cachedCharacterController.center;
        Vector3 direction = targetPos - center;
        float distance = direction.magnitude;
        return !Physics.SphereCast(center, 0.3f, direction / distance, out _, distance, LayerMasks.EnvironmentAndCeiling);
    }

    /// <summary>
    /// Checks if the critter should be hidden because the player is on the opposite side of a closed door.
    /// </summary>
    /// <returns>True if the critter should be hidden due to door occlusion, false otherwise</returns>
    private bool IsOccludedByDoor()
    {
        Door door = cachedCurrentTile.door;
        if (door == null)
        {
            Vector3 off = PlayerObject.Player.mainCamera.transform.position - transform.position;
            int dx = 0;
            int dy = 0;
            if (Mathf.Abs(off.x) > Mathf.Abs(off.z))
            {
                dx = (int)Mathf.Sign(off.x);
            }
            else
            {
                dy = (int)Mathf.Sign(off.z);
            }
            // check the tile next to the critter in the direction of the player
            door = LevelLoader.GetTile(cachedCurrentTile.x + dx, cachedCurrentTile.y + dy).door;
            if (door == null)
            {
                return false;
            }
        }

        // Don't occlude portcullises - they have gaps you can see through
        if (door.type is EObjectType.Portcullis or EObjectType.OpenPortcullis)
        {
            return false;
        }

        // Check if door is sufficiently closed (handle opening/closing states)
        if (door.OpenAmount > 0.01f) // Door must be mostly closed to occlude
        {
            return false;
        }

        // Convert positions to door-local space (ignoring scale)
        float critterLocalZ = Utils.InverseTransformPointNoScale(door.cachedDoorCollider.transform, transform.position).z;
        float playerLocalZ = Utils.InverseTransformPointNoScale(door.cachedDoorCollider.transform, PlayerObject.Player.mainCamera.transform.position).z;

        // Check if on opposite sides (opposite signs of z component)
        if (critterLocalZ * playerLocalZ > 0.0f)
        {
            return false; // show critter - player on same side
        }

        // occluded
        return true;
    }

    /// <summary>
    /// Checks if critter is trapped behind an open door and closes it if so.
    /// </summary>
    private void TryCloseDoorIfTrapped()
    {
        if (cachedCurrentTile == null || cachedCurrentTile.door == null)
        {
            return;
        }

        Door door = cachedCurrentTile.door;

        // Only check if door is fully open
        if (!door.isOpen || door.OpenAmount <= 0.99f || door.type is EObjectType.Portcullis)
        {
            return;
        }

        // Check if critter is close to the door
        Vector3 doorPos = door.cachedDoorCollider.transform.position;
        Vector3 critterPos = transform.position;
        Vector3 doorToCritter = critterPos - doorPos;
        doorToCritter.y = 0.0f; // Check distance in XZ plane only
        float distanceToDoor = doorToCritter.magnitude;

        if (distanceToDoor > 1.0f) // Not close enough
        {
            return;
        }

        // Check that critter is on the opening side (the side the door opens toward)
        float openDirection = (door.flags & 2) == 0 ? -1.0f : 1.0f;
        Vector3 critterLocalPos = Utils.InverseTransformPointNoScale(door.cachedDoorCollider.transform, critterPos);

        // Critter must be on the opening side (critterLocalPos.z * openDirection < 0)
        if (critterLocalPos.z * openDirection > 0.0f)
        {
            return; // Critter is not on the opening side
        }

        // Check that there's a wall next to the door on the opening side
        // Raycast from center of door collider outward along door local z (opening direction)
        Vector3 doorColliderCenter = door.cachedDoorCollider.transform.TransformPoint(door.cachedDoorCollider.center);
        Vector3 doorLocalZ = door.cachedDoorCollider.transform.forward; // Forward is +z in local space
        Vector3 raycastDirection = openDirection < 0.0f ? doorLocalZ : -doorLocalZ;
        Vector3 raycastOrigin = doorColliderCenter + 0.5f * Vector3.up; // Raise origin to avoid grazing floor

        int envLayerMask = LayerMasks.EnvironmentAndCeiling;
        float checkDistance = 2.0f; // Check within 2m for wall

        if (Physics.Raycast(raycastOrigin, raycastDirection, out RaycastHit hit, checkDistance, envLayerMask))
        {
            // Wall detected next to door on opening side - critter is trapped
            door.Close();
        }
    }

    public override void Update()
    {
        if (DebugThis)
        {
            DebugThis = false;
        }

        base.Update();

        // Update cached current tile
        cachedCurrentTile = LevelLoader.GetTile(transform.position);

        // Check PVS visibility and update renderer visibility
        if (LevelLoader.GetLevel().pvs.IsVisible(cachedCurrentTile) && !IsOccludedByDoor())
        {
            cachedVisibilityGroup.Show();
        }
        else
        {
            cachedVisibilityGroup.Hide();
        }

        Vector3 off = transform.position - PlayerObject.Player.mainCamera!.transform.position;
        float distanceToPlayer = off.sqrMagnitude;
        if (distanceToPlayer > kWakeUpRange * kWakeUpRange && LevelLoader.sLevelLoader.loadedLevel != 9)
        {
            return;
        }
        distanceToPlayer = Mathf.Sqrt(distanceToPlayer);

        if (IsGolemWithShieldOfValor() && hp < 5)
        {
            attitude = EAttitude.Mellow;
            hp = Mathf.Max(1, hp);

            // stop being poisoned
            poisonTime = 0.0f;

            // find incoming missiles and destroy them
            int layerMask = 1 << LayerMask.NameToLayer("Ignore Raycast"); // projectiles are marked this

            int numOverlaps = Physics.OverlapSphereNonAlloc(transform.position, 12.0f, sphereOverlapCache, layerMask);
            for (int i = 0; i < numOverlaps; ++i)
            {
                Collider col = sphereOverlapCache[i];
                Projectile p = col.transform.root.GetComponent<Projectile>();
                if (p != null)
                {
                    Utils.DestroyItem(p);
                }
            }

            Magic.sMagic.StopDamageWaves();

            // restart the conversation after the fight
            TryStartConversation();
        }

        if (whoami is EWhoAmI.Rodrick or EWhoAmI.Tyball && talkedTo == 0)
        {
            if (distanceToPlayer < 6.0f && Vector3.Dot(off / distanceToPlayer, PlayerObject.Player.mainCamera.transform.forward) > Mathf.Cos(Mathf.PI / 4))
            {
                if (distanceToPlayer < 0.5f
                    || !Physics.Raycast(PlayerObject.Player.mainCamera.transform.position, off / distanceToPlayer, distanceToPlayer - 0.5f, LayerMasks.EnvironmentAndCeiling))
                {
                    // start conversation
                    TryStartConversation();
                }
            }
        }

        if (state != EState.Die && state != EState.Dead && state != EState.Cleanup)
        {
            if (poisonTime > 0.0f)
            {
                poisonTime -= Time.deltaTime;
                timeToNextPoisonDamage -= Time.deltaTime;
                if (timeToNextPoisonDamage < 0.0f)
                {
                    timeToNextPoisonDamage = 5.0f;
                    TryDamage(Random.Range(2, 4), Skills.ESkillTestResult.Success);
                }
            }
            if (hp <= 0 && !isTwilightZone && !IsGolemWithShieldOfValor())
            {
                SetState(EState.Die);
            }

            if (attackTarget != null && attackTarget.hp <= 0)
            {
                attackTarget = null;
            }

            if (confusionTime > 0.0f)
            {
                confusionTime -= Time.deltaTime;
            }

            if (curseTime > 0.0f)
            {
                curseTime -= Time.deltaTime;
                if (curseTime <= 0.0f)
                {
                    curseTime = 0.0f;
                    curseMultiplier = 1.0f;
                }
            }

            if (damagedTimer > 0.0f)
            {
                damagedTimer -= Time.deltaTime;
            }

            if (theftDetectedTimer > 0.0f)
            {
                theftDetectedTimer -= Time.deltaTime;
            }
        }

        if (paralyzeTime > 0.0f)
        {
            paralyzeTime -= Time.deltaTime;
            if (cachedAnimator != null)
            {
                cachedAnimator.speed = 0.0f;
            }
        }
        else if (Magic.sMagic.IsSpellActive(Magic.ESpell.FreezeTime) && state != EState.Die && state != EState.Dead)
        {
            // do nothing
            if (cachedAnimator != null)
            {
                cachedAnimator.speed = 0.0f;
            }
        }
        else
        {
            if (cachedAnimator != null)
            {
                cachedAnimator.speed = 1.0f;
            }
            switch (state)
            {
            case EState.Initialize:
                SpawnLoot();
                if (type == EObjectType.Imp && LevelLoader.sLevelLoader.loadedLevel == 7 && goal == EGoal.Stand7 && attitude == EAttitude.Mellow)
                {
                    SetState(EState.Crouch);
                }
                else
                {
                    SetState(EState.Idle);
                }
                break;
            case EState.Enable:
                if (type == EObjectType.Imp && LevelLoader.sLevelLoader.loadedLevel == 7 && goal == EGoal.Stand7 && attitude == EAttitude.Mellow)
                {
                    SetState(EState.Crouch);
                    if (cachedAnimator != null)
                    {
                        // force the animator change
                        cachedAnimator.CrossFadeInFixedTime("Crouched_Idle", 0.01f);
                    }
                }
                else
                {
                    SetState(EState.Idle);
                }
                break;
            case EState.Crouch:
                if (attitude == EAttitude.Hostile)
                {
                    SetState(EState.Idle);
                }
                break;
            case EState.Idle:
            case EState.Fidget:
                TryCloseDoorIfTrapped();
                if (isTwilightZone)
                {
                    // just idle
                    AdvanceAnim(0.8f);
                }
                else if (goal == EGoal.AwaitConversation && distanceToPlayer < 4.0f && DetectPlayer())
                {
                    // start a conversation
                    TryInteract(null, null, EAction.Use);
                }
                else if ((attitude == EAttitude.Hostile || goal == EGoal.AttackTarget5) && CheckIdleToCombat(distanceToPlayer))
                {
                    // Play alert sound if this is the first time detecting the player while hostile in this detection session
                    if (attackTarget == null && attitude == EAttitude.Hostile && !hasPlayedAlertForCurrentDetection)
                    {
                        PlayAlertSound();
                    }

                    GetPath(out path, transform.position, GetTargetFootPos());
                    if (path.Count > 0 && path.Count < detectionRange / Tile.xzScale)
                    {
                        SetState(EState.TurnToApproach);
                    }
                    else
                    {
                        SetState(EState.CombatIdle);
                    }
                }
                else if (AdvanceAnim(0.8f) || confusionTime > 0.0f) // removed check for upset (want to see fidgets)
                {
                    if (goal != EGoal.Stand7 && goal != EGoal.Stand0 && goal != EGoal.Stand11 && goal != EGoal.Stand12 && goal != EGoal.AwaitConversation)
                    {
                        // try a wander
                        Tile target = FindWanderPoint();
                        Vector3 targetCenter = AdjustWanderPoint(target);

                        GetPath(out path, transform.position, targetCenter);
                        if (path.Count > 0 && path.Count < detectionRange / Tile.xzScale)
                        {
                            //path[path.Count - 1] += new Vector3( Random.Range( -1.0f, 1.0f ), 0.0f, Random.Range( -1.0f, 1.0f ) );
                            SetState(EState.TurnToWander);
                        }
                    }
                }
                else
                {
                    stateTime -= Time.deltaTime;
                    bool fidgetEnd = state == EState.Fidget && ((cachedAnimator == null && stateTime < 0.0f) || (cachedAnimator != null && actionDone));
                    bool idleEnd = state == EState.Idle && stateTime < 0.0f;
                    if (fidgetEnd)
                    {
                        SetState(EState.Idle);
                    }
                    else if (idleEnd)
                    {
                        if (cachedAnimator != null && cachedAnimator.HasState(0, Animator.StringToHash("Fidget")))
                        {
                            SetState(EState.Fidget);
                        }
                    }
                }

                // Reset alert flag when hostile critter loses sight of player in Idle/Fidget state
                if (attitude == EAttitude.Hostile && !DetectPlayer())
                {
                    hasPlayedAlertForCurrentDetection = false;
                }

                break;
            case EState.TurnToWander:
                AdvanceAnim(0.3f);
                if (TurnTo(GetLookahead(0.5f), wanderTurnSpeed, 20.0f))
                {
                    SetState(EState.Wander);
                }
                break;
            case EState.Wander:
                TryCloseDoorIfTrapped();
                // Interrupt wander if hostile and can see player (confused critters keep wandering erratically).
                // Player allies without a target yet should keep wandering instead of snapping to Idle.
                if (attitude == EAttitude.Hostile && DetectPlayer() && confusionTime <= 0.0f
                    && !(playerAlly && attackTarget == null))
                {
                    SetState(EState.Idle);
                    break;
                }

                // Reset alert flag when hostile critter loses sight of player in Wander state
                if (attitude == EAttitude.Hostile && !DetectPlayer())
                {
                    hasPlayedAlertForCurrentDetection = false;
                }

                // Interrupt wander if theft was detected
                if (theftDetectedTimer > 0.0f && confusionTime <= 0.0f)
                {
                    SetState(EState.Idle);
                    break;
                }

                AdvanceAnim(confusionTime > 0.0f ? 0.5f : 0.3f);
                TurnTo(GetLookahead(0.5f), wanderTurnSpeed, 20.0f);
                if (MoveAlongPath(confusionTime > 0.0f ? confusedSpeed : walkSpeed, 0.5f * cachedCharacterController.radius)
                    || wallRubTime > 1.0f)
                {
                    if (cachedAnimator != null
                        && cachedAnimator.HasState(0, Animator.StringToHash("Fidget"))
                        && Random.value < 0.5f)
                    {
                        SetState(EState.Fidget);
                    }
                    else
                    {
                        SetState(EState.Idle);
                    }
                }
                break;
            case EState.Converse:
                {
                Vector3 toPlayer = PlayerObject.Player.mainCamera.transform.position - transform.position;
                toPlayer.y = 0.0f;
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(toPlayer),
                    540.0f * Time.deltaTime);
                frameTime -= Time.deltaTime;
                if (frameTime < 0.0f)
                {
                    switch (frameIndex)
                    {
                    case 0:
                    case 2:
                        frameIndex = Random.value > 0.5f ? 1 : 3;
                        frameTime = Random.Range(3.0f, 4.0f);
                        break;
                    case 1:
                    case 3:
                        frameIndex = Random.value > 0.5f ? 0 : 2;
                        frameTime = Random.Range(0.5f, 0.8f);
                        break;
                    }
                }

                if (Conversations.runningConversation == null)
                {
                    SetState(EState.Idle);
                }
                }
                break;
            case EState.TurnToApproach:
                AdvanceAnim(0.5f);
                if (TurnTo(GetLookahead(0.5f), approachTurnSpeed, 30.0f))
                {
                    SetState(EState.Approach);
                }
                break;
            case EState.Approach:
                TryCloseDoorIfTrapped();
                AdvanceAnim(0.5f);
                TurnTo(GetLookahead(0.5f), approachTurnSpeed, 30.0f);
                if (MoveAlongPath(runSpeed, 0.3f))
                {
                    // idle will repath
                    SetState(EState.Idle);
                }
                // see if we're close enough to the target to attack
                else if (GetDistanceToTarget() < meleeAttackRange && IsViewToTargetClear())
                {
                    Music.InCombat();
                    SetState(EState.CombatIdle);
                }
                else if (wallRubTime > 1.0f)
                {
                    SetState(EState.CombatIdle);
                }
                else if (CanDoProjectileAttack(0.0f) && IsViewToTargetClear())
                {
                    SetState(EState.ProjectileIdle);
                }
                break;
            case EState.CombatIdle:
                TryCloseDoorIfTrapped();
                if (cachedAnimator == null)
                {
                    TurnTo(GetTargetFootPos(), combatTurnSpeed, 30.0f);
                }
                else
                {
                    initialTurnAngle = Vector3.Angle(transform.forward, GetTargetFootPos() - transform.position);
                    if (Mathf.Abs(initialTurnAngle) > 20.0f)
                    {
                        SetState(EState.CombatTurn);
                        break;
                    }
                }

                stateTime -= Time.deltaTime;
                if (stateTime < 0.0f)
                {
                    CheckTransitionToAttack();
                }
                break;
            case EState.CombatTurn:
                TurnTo(GetTargetFootPos(), combatTurnTurnSpeed, 30.0f);
                if (movementType == EMovementType.Flying
                    || cachedAnimator == null
                    || !cachedAnimator.HasState(0, Animator.StringToHash("Turn_L")))
                {
                    stateTime -= Time.deltaTime;
                }
                if (stateTime < 0.0f || actionDone)
                {
                    CheckTransitionToAttack();
                }
                break;
            case EState.Attack:
                TryCloseDoorIfTrapped();
                {
                // Lunge forward if triggered by animation event
                UpdateLunge();
                }

                if (cachedAnimator == null)
                {
                    if (stateTime > 0.0f)
                    {
                        stateTime -= Time.deltaTime;
                        if (stateTime <= 0.0f)
                        {
                            // try to damage player
                            TryDamageTarget();
                        }
                    }
                }

                if ((cachedAnimator != null && actionDone)
                    || (cachedAnimator == null && AdvanceAnim(0.3f)))
                {
                    SetState(EState.CombatIdle);
                }

                break;

            case EState.ProjectileIdle:
                TurnTo(PlayerObject.Player.transform.position, projectileTurnSpeed, 30.0f);

                stateTime -= Time.deltaTime;
                if (AdvanceAnim(0.3f) && stateTime < 0.0f)
                {
                    // add some hysteresis
                    if (CanDoProjectileAttack(-2.0f))
                    {
                        SetState(EState.ProjectileAttack);
                    }
                    else
                    {
                        // idle will get us closer
                        SetState(EState.Idle);
                    }
                }
                break;
            case EState.ProjectileAttack:
                TurnTo(PlayerObject.Player.transform.position, projectileTurnSpeed, 30.0f);
                if (cachedAnimator == null)
                {
                    if (stateTime > 0.0f)
                    {
                        stateTime -= Time.deltaTime;
                        if (stateTime <= 0.0f)
                        {
                            ProjectileEvent();
                        }
                    }

                    if (AdvanceAnim(0.3f))
                    {
                        SetState(EState.CombatIdle);
                    }
                }
                else if (actionDone)
                {
                    SetState(EState.CombatIdle);
                }
                break;

            case EState.TurnToFlee:
                AdvanceAnim(0.3f);
                if (TurnTo(GetLookahead(0.5f), fleeTurnTurnSpeed, 20.0f))
                {
                    SetState(EState.Flee);
                }
                break;
            case EState.Flee:
                TryCloseDoorIfTrapped();
                AdvanceAnim(0.2f);
                TurnTo(GetLookahead(0.5f), fleeTurnSpeed, 20.0f);
                if (MoveAlongPath(fleeSpeed, 0.3f))
                {
                    SetState(EState.Idle);
                }
                if (wallRubTime > 1.0f)
                {
                    SetState(EState.Idle);
                }
                break;
            case EState.Flinch:
                if (hp > 0 && actionDone)
                {
                    SetState(EState.CombatIdle);
                }
                break;
            case EState.Die:
                stateTime += Time.deltaTime;
                if (cachedAnimator == null)
                {
                    if (AdvanceAnim(0.3f))
                    {
                        SetState(EState.Dead);
                        frameIndex = crit.frameCount[12] - 1;
                    }
                }
                else if (actionDone)
                {
                    SetState(EState.Dead);
                }
                break;
            case EState.Dead:
                stateTime += Time.deltaTime; // Accumulate time for periodic visibility check

                // Check visibility periodically (every 0.5 seconds)
                if (stateTime >= 0.5f)
                {
                    stateTime = 0.0f; // Reset timer for next check

                    // Get camera frustum planes
                    Camera mainCam = PlayerObject.Player.mainCamera;
                    if (mainCam != null)
                    {
                        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(mainCam);

                        // Create bounds from a sphere around the character
                        // Use a reasonable radius (e.g., 1.5 units) to account for mesh extent
                        float sphereRadius = 1.5f;
                        Bounds sphereBounds = new Bounds(transform.position, Vector3.one * sphereRadius * 2);

                        // Test if bounds are outside frustum
                        // TestPlanesAABB returns false if bounds are completely outside
                        if (!GeometryUtility.TestPlanesAABB(planes, sphereBounds))
                        {
                            // Character is completely outside camera view, safe to delete
                            if (gameObject.activeSelf)
                            {
                                gameObject.SetActive(false);
                            }
                            // make sure it doesn't come back (eg during level transition)
                            activeInLevel = false;
                            Utils.DestroyCritter(this);
                        }
                    }
                }
                break;
            case EState.Cleanup:
                EnsureDeathProcessedOnce();
                EnsureRemainsSpawnedOnce();
                activeInLevel = false;
                Utils.DestroyCritter(this);
                return;
            }
        }

        if (cachedRenderer != null && cachedAnimator == null && type != EObjectType.Wisp)
        {
            // idle or walking
            int slotOffset = 0;
            if (slot == 32 || slot == 128)
            {
                if (state == EState.Converse)
                {
                    slotOffset = 5;
                }
                else
                {
                    // get the relative angle.
                    Vector3 cameraForward = transform.position - PlayerObject.Player.mainCamera.transform.position;
                    cameraForward.y = 0.0f;
                    Vector3 critterForward = transform.forward;
                    critterForward.y = 0.0f;
                    float dot = Vector3.Dot(cameraForward, critterForward);
                    float cross = Vector3.Cross(cameraForward, critterForward).y;
                    float relativeAngle = Mathf.Atan2(cross, dot);
                    slotOffset = Mathf.RoundToInt(8.0f + 4.0f * relativeAngle / Mathf.PI) % 8;
                }
            }

            CritterLoader.Frame frame = crit.slots[slot + slotOffset, frameIndex];
            if (frame == null && slotOffset > 0)
            {
                // find the max value, then remap the frame index
                for (int i = crit.frameCount[slot] - 1; i >= 0; --i)
                {
                    if (crit.slots[slot + slotOffset, i] != null)
                    {
                        int newFrameIndex = frameIndex * crit.frameCount[slot] / (i + 1);
                        frame = crit.slots[slot + slotOffset, newFrameIndex];
                        break;
                    }
                }
            }

            if (frame != null)
            {
                cachedRenderer.material = frame.mat;
                Texture2D tex = frame.mat.mainTexture as Texture2D;
                // make sure the pixels are square
                cachedRenderer.transform.localScale = new Vector3(kXScale * tex.width, kYScale * tex.height, 1.0f);
                // face the camera
                cachedRenderer.transform.rotation = PlayerObject.Player.mainCamera.transform.rotation;
                // reposition so the hotspot is in the correct place.
                float xOff = kXScale * (0.5f * tex.width - frame.hotspotX);
                float yOff = kYScale * (0.5f * tex.height - frame.hotspotY);

                if (state != EState.Die && state != EState.Dead && movementType == EMovementType.Flying)
                {
                    // bob
                    bobTime += Time.deltaTime;
                    if (bobTime > 2.0f * Mathf.PI)
                    {
                        bobTime -= 2.0f * Mathf.PI;
                    }
                    yOff += 0.2f * Mathf.Sin(2.0f * bobTime);
                }

                cachedRenderer.transform.localPosition = new Vector3(-xOff, -yOff, 0.0f);
            }
        }
    }


    public void OnEnable()
    {
        switch (state)
        {
        case EState.Die:
        case EState.Dead:
            // Cannot destroy immediately here: level re-enable iterates a linked list that can be locked.
            // Defer cleanup to the state's Update handler.
            SetState(EState.Cleanup);
            break;
        case EState.Initialize:
            break;
        default:
            SetState(EState.Enable);
            break;
        }
    }

    private void CheckTransitionToAttack()
    {
        if (playerAlly && attackTarget == null)
        {
            SetState(EState.Idle);
            return;
        }

        if (attackTarget == null && !DetectPlayer())
        {
            SetState(EState.Idle);
        }
        else
        {
            float distanceToTarget = GetDistanceToTarget();
            if (distanceToTarget < meleeAttackRange + meleeAttackHysteresis)
            {
                SetState(EState.Attack);
            }
            else if (CanDoProjectileAttack(0.0f))
            {
                SetState(EState.ProjectileIdle);
            }
            else
            {
                // idle will get us closer
                SetState(EState.Idle);
            }
        }
    }

    protected virtual void LaunchProjectile()
    {
        EObjectType projectileType = GetProjectileType();
        if (projectileType != 0)
        {
            UUObject proj = LevelLoader.CreateObjectOfType(projectileType);
            proj.PostLoadInitialize();
            Vector3 startPos = GetProjectileLaunchPosition(false);
            Vector3 endPos = PlayerObject.Player.mainCamera!.transform.position + 0.5f * Vector3.down;
            proj.gameObject.transform.position = startPos;
            LevelLoader.AddToWorld(proj);
            Rigidbody rb = proj.gameObject.GetComponent<Rigidbody>();
            if (rb != null)
            {
                float projectileSpeed = 14.0f;

                if (rb.useGravity)
                {
                    // calculate velocity to hit player
                    // about 10m/s horizontally
                    Vector3 vel = endPos - startPos;
                    vel.y = 0.0f;
                    float xzDist = vel.magnitude;
                    vel = projectileSpeed * vel / xzDist;
                    float t = xzDist / projectileSpeed;
                    float g = Physics.gravity.y;
                    float s = endPos.y - startPos.y;
                    // s = ut + 1/2 at^2;
                    vel.y = s / t - 0.5f * g * t;
                    rb.linearVelocity = vel;
                }
                else
                {
                    rb.linearVelocity = projectileSpeed * (endPos - startPos).normalized;
                    Debug.DrawLine(startPos, endPos, Color.green, 5.0f);
                }
                proj.gameObject.transform.forward = rb.linearVelocity.normalized;
            }

            Projectile projectile = proj as Projectile;
            if (projectile != null)
            {
                projectile.doDamage = true;
                projectile.projectileOwner = gameObject;
                projectile.createdByCursedEntity = curseTime > 0.0f;
            }
        }
    }

    private Tile FindWanderPoint()
    {
        // start at the home tile and wander randomly for a square or two
        // until we reach a spot a reasonable distance from the critter
        // don't go through doors, over bridges, or up or down stairs (but slopes are ok)

        const int kMinWander = 3;
        int wander = 0;
        Tile home = LevelLoader.GetTile(xhome, yhome);
        Tile cur = home;
        Tile exclude = LevelLoader.GetTile(GetTileX(), GetTileY()); // exclude the current tile
        while (wander++ < kMinWander)
        {
            List<Tile> neighbs = GetValidNeighbours(cur, home, 3, exclude);
            if (neighbs.Count > 0)
            {
                cur = neighbs[Random.Range(0, neighbs.Count)];
            }
        }

        return cur;
    }

    protected virtual Vector3 AdjustWanderPoint(Tile target)
    {
        return target.GetCenter();
    }

    private Tile FindFleePoint()
    {
        // try calculating a flee to 3 points and see which is furthest from the player
        float bestDist = 0.0f;
        Tile bestTile = null;

        Vector3 playerToMe = (transform.position - PlayerObject.Player.transform.position).normalized;
        playerToMe.y = 0.0f;
        Vector3 left = Vector3.Cross(playerToMe, Vector3.up);
        Tile source = LevelLoader.GetTile(GetTileX(), GetTileY());

        Tile fleeTile = LevelLoader.GetClosestTile(transform.position + 12.0f * playerToMe);
        GetTilePath(out List<Tile> fleeTiles, source, fleeTile);
        if (fleeTiles.Count > 1)
        {
            Tile lastTile = fleeTiles[^1];
            bestTile = lastTile;
            bestDist = (lastTile.GetCenter() - PlayerObject.Player.transform.position).magnitude;
        }

        fleeTile = LevelLoader.GetClosestTile(transform.position + 10.0f * playerToMe + 8.0f * left);
        GetTilePath(out fleeTiles, source, fleeTile);
        if (fleeTiles.Count > 1)
        {
            Tile lastTile = fleeTiles[^1];
            float dist = (lastTile.GetCenter() - PlayerObject.Player.transform.position).magnitude;
            if (dist > bestDist)
            {
                bestTile = lastTile;
                bestDist = (lastTile.GetCenter() - PlayerObject.Player.transform.position).magnitude;
            }
        }

        fleeTile = LevelLoader.GetClosestTile(transform.position + 10.0f * playerToMe - 8.0f * left);
        GetTilePath(out fleeTiles, source, fleeTile);
        if (fleeTiles.Count > 1)
        {
            Tile lastTile = fleeTiles[^1];
            float dist = (lastTile.GetCenter() - PlayerObject.Player.transform.position).magnitude;
            if (dist > bestDist)
            {
                bestTile = lastTile;
            }
        }

        return bestTile;
    }

    private static ulong sTag;

    private bool GetTilePath(out List<Tile> tilePath, Tile source, Tile target)
    {
        ++sTag;

        tilePath = new List<Tile>();
        var openSet = new HashSet<Tile>();

        Tile home = LevelLoader.GetTile(xhome, yhome);
        Tile cur = source;
        openSet.Add(cur);
        cur.g = 0.0f;
        cur.h = cur.HeuristicTo(target);
        cur.openTag = sTag;
        cur.parent = null;

        Tile closest = cur;

        while (openSet.Count > 0)
        {
            // find the best in the open list
            float leastCost = Mathf.Infinity;
            cur = null;
            foreach (Tile t in openSet)
            {
                if (t.g + t.h < leastCost)
                {
                    leastCost = t.g + t.h;
                    cur = t;
                }
            }

            if (cur == target)
            {
                openSet.Clear();
            }
            else if (cur != null)
            {
                if (cur.h < closest.h)
                {
                    closest = cur;
                }

                openSet.Remove(cur);
                cur.closedTag = sTag;
                List<Tile> neighbs = GetValidNeighbours(cur, home, range, null);
                foreach (Tile t in neighbs)
                {
                    if (t.closedTag != sTag)
                    {
                        float newG = cur.g + cur.HeuristicTo(t);
                        if (t.openTag != sTag)
                        {
                            t.g = newG;
                            t.h = t.HeuristicTo(target);
                            t.parent = cur;
                            openSet.Add(t);
                        }
                        else if (newG < t.g)
                        {
                            t.g = newG;
                            t.openTag = sTag;
                            t.parent = cur;
                        }
                    }
                }
            }
        }

        bool found = false;
        if (cur == target)
        {
            found = true;
        }
        else
        {
            cur = closest;
        }

        while (cur != null)
        {
            tilePath.Add(cur);
            cur = cur.parent;
        }

        tilePath.Reverse();

        return found;
    }

    private void GetPath(out List<Vector3> vecPath, Vector3 start, Vector3 end)
    {
        Tile source = LevelLoader.GetTile(Tile.GetTileX(start.x), Tile.GetTileY(start.z));
        Tile target = LevelLoader.GetTile(Tile.GetTileX(end.x), Tile.GetTileY(end.z));

        vecPath = new List<Vector3>();

        List<Tile> tiles;
        bool found = false;

        if (type != EObjectType.Tyball)
        {
            found = GetTilePath(out tiles, source, target);
        }
        else
        {
            // try walking and flying, and choose flying if it gets us closer
            movementType = EMovementType.Flying;
            bool flightFound = GetTilePath(out List<Tile> flyingTiles, source, target);
            movementType = EMovementType.Walking;
            bool walkFound = GetTilePath(out tiles, source, target);

            if (flightFound && walkFound)
            {
                if ((flyingTiles[^1] == tiles[^1] && flyingTiles.Count < tiles.Count)
                    || (flyingTiles[^1].GetCenter() - end).sqrMagnitude < (tiles[^1].GetCenter() - end).sqrMagnitude)
                {
                    tiles = flyingTiles;
                    movementType = EMovementType.Flying;
                    found = true;
                }
            }
            else if (flightFound)
            {
                tiles = flyingTiles;
                movementType = EMovementType.Flying;
                found = true;
            }
            else
            {
                found = walkFound;
            }
        }

        for (int i = 1; i < tiles.Count; ++i)
        {
            Vector3 center = tiles[i].GetCenter();

            // add some randomness to try to prevent creatures getting stuck on one another
            Vector2 r = cachedCharacterController.radius * Random.insideUnitCircle;
            center.x += r.x;
            center.z += r.y;

            if (movementType == EMovementType.Flying)
            {
                // move up off the ground. the path will get shrinkwrapped in the post process.
                center.y = center.y + 1.5f;
            }

            vecPath.Add(center);
        }

        if (found)
        {
            if (vecPath.Count > 0)
            {
                vecPath.RemoveAt(vecPath.Count - 1);
            }

            if (movementType != EMovementType.Flying)
            {
                end.y = target.floorHeight * Tile.yScale;
            }

            vecPath.Add(end);
        }

        if (found && movementType == EMovementType.Flying)
        {
            PostProcessPath(start, vecPath);
        }

        // Debug draw the final path for 5 seconds (including current position at start)
        if (found && vecPath.Count >= 1)
        {
            Debug.DrawLine(start, vecPath[0], Color.cyan, 5.0f);
            for (int i = 0; i < vecPath.Count - 1; ++i)
            {
                Debug.DrawLine(vecPath[i], vecPath[i + 1], Color.cyan, 5.0f);
            }
        }
    }

    private void PostProcessPath(Vector3 start, List<Vector3> vecPath)
    {
        // Post-process flyer paths: smooth Y and add clearance nodes.
        // can't make this a virtual function because Tyball changes movementType on the fly
        if (cachedCharacterController != null && vecPath.Count >= 2)
        {
            // 1) Smooth Y using linear interpolation from first to last node:
            //    Y_k = Y_start + (k / (N - 1)) * (Y_end - Y_start), for k in [0, N-1]
            int count = vecPath.Count;
            float yStart = vecPath[0].y;
            float yEnd = vecPath[count - 1].y;
            if (count > 1)
            {
                for (int k = 0; k < count; ++k)
                {
                    float t = (count == 1) ? 0.0f : (float)k / (count - 1);
                    float y = Mathf.Lerp(yStart, yEnd, t);
                    Vector3 v = vecPath[k];
                    v.y = y;
                    vecPath[k] = v;
                }
            }

            // 2) Add clearance nodes when entering a new tile whose floor is high relative to current path height.
            // Prepend current position so we can add a clearance node between start and first path point.
            vecPath.Insert(0, start);

            float h = cachedCharacterController.height;
            List<Vector3> processedPath = new List<Vector3>(vecPath.Count * 2);

            for (int j = 0; j < vecPath.Count - 1; ++j)
            {
                Vector3 current = vecPath[j];
                Vector3 next = vecPath[j + 1];
                processedPath.Add(current);

                // Determine tiles for current and next points.
                Tile currentTile = LevelLoader.GetTile(Tile.GetTileX(current.x), Tile.GetTileY(current.z));
                Tile nextTile = LevelLoader.GetTile(Tile.GetTileX(next.x), Tile.GetTileY(next.z));

                if (currentTile == null || nextTile == null)
                {
                    continue;
                }

                // If path height is below edge height + 2*h, insert a clearance node.
                float crossingEdgeY = Mathf.Max(currentTile.floorHeight, nextTile.floorHeight) * Tile.yScale + 2.0f * h;
                float crossingPathY = (current.y + next.y) / 2.0f;

                if (crossingPathY < crossingEdgeY)
                {
                    // Position the clearance node on the edge between the two tiles (midpoint of centers in XZ),
                    // at a height of newFloorY + 2*h.
                    Vector3 currentCenter = currentTile.GetCenter();
                    Vector3 nextCenter = nextTile.GetCenter();
                    Vector3 edgePos = 0.5f * (currentCenter + nextCenter);
                    edgePos.y = crossingEdgeY;

                    processedPath.Add(edgePos);
                }
            }

            // Add the final point.
            processedPath.Add(vecPath[vecPath.Count - 1]);

            vecPath = processedPath;

            // 3) Raise central points: for each triplet (A, B, C), if B is lower than the midpoint of A and C, raise B.
            bool smoothed = true;
            for (int j = 0; j < 5 && smoothed; ++j)
            {
                smoothed = false;
                for (int i = 1; i < vecPath.Count - 1; ++i)
                {
                    float midpointY = (vecPath[i - 1].y + vecPath[i + 1].y) * 0.5f;
                    if (vecPath[i].y < midpointY)
                    {
                        Vector3 v = vecPath[i];
                        v.y = midpointY;
                        vecPath[i] = v;
                        smoothed = true;
                    }
                }
            }

            // Remove the current position we prepended (path should not include start).
            vecPath.RemoveAt(0);

            for (int i = 0; i < vecPath.Count; ++i)
            {
                Vector3 v = vecPath[i];
                v.y = Mathf.Min(v.y, 12.0f - minCeilingDistance);
                vecPath[i] = v;
            }
        }
    }

    private bool CanTraverseTerrain(Tile t)
    {
        ETerrainType terrainType = t.GetFloorTerrain();
        switch (movementType)
        {
        case EMovementType.Flying:
            return true;
        case EMovementType.Swimming:
            if (terrainType != ETerrainType.Water)
            {
                return false;
            }
            if (t.bridge == null)
            {
                return true;
            }
            else
            {
                // make sure there isn't a low bridge
                int bridgeHeight = t.bridge.z / 8; // in 0-15 units 
                return bridgeHeight > t.floorHeight;
            }
        default:
            // Slasher of Veils shouldn't try shortcutting corners
            return terrainType != ETerrainType.Water && (LevelLoader.sLevelLoader.loadedLevel != 9 || t.floorTexture != 9);
        }
    }

    /// <summary>
    /// Checks if a critter can step from one tile to another based on corner heights along the shared edge.
    /// Uses character controller stepOffset to determine if the step is climbable.
    /// </summary>
    private bool CanStepBetweenTiles(Tile from, Tile to, Tile.EDirection dir)
    {
        if (from == null || to == null)
        {
            return false;
        }

        // Get corner heights for both tiles
        // Using member arrays to avoid allocations

        // Calculate direction offsets for GetFloorHeights
        int dx = to.x - from.x;
        int dy = to.y - from.y;

        if (from.movingPlatform != null)
        {
            fromHeights[0] = fromHeights[1] = fromHeights[2] = fromHeights[3] = from.movingPlatform.h;
        }
        else
        {
            // we're "on" this tile so don't need a direction (dx,dy)
            LevelLoader.sLevelLoader.GetFloorHeights(fromHeights, from.x, from.y, 0, 0, null);
        }
        if (to.movingPlatform != null)
        {
            toHeights[0] = toHeights[1] = toHeights[2] = toHeights[3] = to.movingPlatform.h;
        }
        else
        {
            // going to this tile, we want to pass the direction to the tile
            LevelLoader.sLevelLoader.GetFloorHeights(toHeights, to.x, to.y, dx, dy, null);
        }

        // Map direction to edge corners
        int cornerDiff1, cornerDiff2;

        // Corner mapping: h[0]=SW, h[1]=SE, h[2]=NW, h[3]=NE
        switch (dir)
        {
        case Tile.EDirection.North:
            // Moving north: check from NW,NE vs to SW,SE
            cornerDiff1 = toHeights[0] - fromHeights[2];
            cornerDiff2 = toHeights[1] - fromHeights[3];
            break;
        case Tile.EDirection.East:
            // Moving east: check from NE,SE vs to NW,SW
            cornerDiff1 = toHeights[2] - fromHeights[3];
            cornerDiff2 = toHeights[0] - fromHeights[1];
            break;
        case Tile.EDirection.South:
            // Moving south: check from SW,SE vs to NW,NE
            cornerDiff1 = toHeights[2] - fromHeights[0];
            cornerDiff2 = toHeights[3] - fromHeights[1];
            break;
        case Tile.EDirection.West:
            // Moving west: check from NW,SW vs to NE,SE
            cornerDiff1 = toHeights[3] - fromHeights[2];
            cornerDiff2 = toHeights[1] - fromHeights[0];
            break;
        case Tile.EDirection.NorthEast:
            // Diagonal NE: check from NE vs to SW
            cornerDiff1 = cornerDiff2 = toHeights[0] - fromHeights[3];
            break;
        case Tile.EDirection.SouthEast:
            // Diagonal SE: check from SE vs to NW
            cornerDiff1 = cornerDiff2 = toHeights[2] - fromHeights[1];
            break;
        case Tile.EDirection.SouthWest:
            // Diagonal SW: check from SW vs to NE
            cornerDiff1 = cornerDiff2 = toHeights[3] - fromHeights[0];
            break;
        case Tile.EDirection.NorthWest:
            // Diagonal NW: check from NW vs to SE
            cornerDiff1 = cornerDiff2 = toHeights[1] - fromHeights[2];
            break;
        default:
            return false;
        }

        // Calculate maximum step height at each end of the edge
        float maxStepUp = Tile.yScale * Mathf.Max(cornerDiff1, cornerDiff2);

        // Calculate maximum drop height at each end of the edge
        float maxDrop = Tile.yScale * Mathf.Max(-cornerDiff1, -cornerDiff2);

        // Both ends must be within stepOffset for steps up, and maxDropHeight for drops down
        // Fleeing or confused characters can drop by maxDropHeight, others only by stepOffset
        float stepOffset = cachedCharacterController.stepOffset;
        bool canDropFar = state == EState.TurnToFlee || confusionTime > 0.0f;
        float allowedDropHeight = canDropFar ? maxDropHeight : stepOffset;
        return maxStepUp <= stepOffset && maxDrop <= allowedDropHeight;
    }

    private List<Tile> GetValidNeighbours(Tile t, Tile home, int maxRange, Tile exclude)
    {
        List<Tile> neighbs = new List<Tile>();
        int[] xo = { 0, 1, 0, -1, 1, 1, -1, -1 };
        int[] yo = { 1, 0, -1, 0, 1, -1, -1, 1 };
        for (Tile.EDirection dir = Tile.EDirection.North; dir <= Tile.EDirection.NorthWest; ++dir)
        {
            if (!t.DirectionBlocked(dir))
            {
                int d = (int)dir;
                int px = xo[d];
                int py = yo[d];
                int nx = px + t.x;
                int ny = py + t.y;
                if (nx is >= 0 and <= 63 && ny is >= 0 and <= 63)
                {
                    Tile tn = LevelLoader.GetTile(nx, ny);
                    if (tn != exclude
                        && tn.type != 0 // wall
                        && (tn.door == null || tn.door.isOpen || (canOpenDoors && !tn.door.spiked && !tn.door.Locked(out _)))
                        && !tn.OppositeDirectionBlocked(dir)
                        && (movementType == EMovementType.Flying || CanStepBetweenTiles(t, tn, dir))
                        && CanTraverseTerrain(tn))
                    {
                        int dx = tn.x - home.x;
                        int dy = tn.y - home.y;
                        if (dir >= Tile.EDirection.NorthEast)
                        {
                            // diagonal, so check the step heights for the intermediate squares
                            Tile t1 = LevelLoader.GetTile(nx, t.y);
                            Tile t2 = LevelLoader.GetTile(t.x, ny);

                            // Determine directions for intermediate tiles
                            Tile.EDirection dir1 = (nx > t.x) ? Tile.EDirection.East : Tile.EDirection.West;
                            Tile.EDirection dir2 = (ny > t.y) ? Tile.EDirection.North : Tile.EDirection.South;

                            if (t1 == null || t2 == null
                                || !CanStepBetweenTiles(t, t1, dir1)
                                || !CanStepBetweenTiles(t, t2, dir2))
                            {
                                continue;
                            }
                        }

                        if (dx * dx + dy * dy <= maxRange * maxRange)
                        {
                            neighbs.Add(tn);
                        }
                    }
                }
            }
        }

        return neighbs;
    }

    public bool TryStartConversation()
    {
        bool gotResponse = false;
        bool talkToRodrick = whoami is EWhoAmI.Rodrick;
        bool talkToTyball = whoami is EWhoAmI.Tyball && talkedTo == 0;
        if (hp > 0 && !Magic.sMagic.IsSpellActive(Magic.ESpell.FreezeTime)
            && (attitude != EAttitude.Hostile || talkToRodrick || talkToTyball))
        {
            if (Conversations.StartConversation(this))
            {
                gotResponse = true;
                if (!talkToRodrick && !talkToTyball)
                {
                    SetState(EState.Converse);
                    frameIndex = 1;
                    frameTime = Random.Range(3.0f, 4.0f);
                }
            }
        }
        return gotResponse;
    }

    public override void TryInteract(UUObject originator, UUObject sender, EAction action)
    {
        if (!TryStartConversation())
        {
            Messages.Add(7, 1); // you get no response
        }
    }

    private bool AdvanceAnim(float frameDuration)
    {
        bool animOver = false;
        frameTime += Time.deltaTime;
        if (frameTime > frameDuration)
        {
            frameTime = 0.0f;
            ++frameIndex;

            if (frameIndex == crit.frameCount[slot] || crit.slots[slot, frameIndex] == null)
            {
                animOver = true;
                frameIndex = 0;
            }
        }

        return animOver;
    }

    static void fixScale(Transform t)
    {
        for (int i = 0; i < t.childCount; ++i)
        {
            Transform c = t.GetChild(i);
            c.localScale = Vector3.one;
            fixScale(c);
        }
    }

    protected void LateUpdate()
    {
        // Apply deferred animator restore after all Start() callbacks have run
        if (pendingAnimatorRestore && cachedAnimator != null && savedAnimatorStateHash != 0)
        {
            cachedAnimator.Play(savedAnimatorStateHash, 0, savedAnimatorNormalizedTime);
            cachedAnimator.speed = savedAnimatorSpeed;
            pendingAnimatorRestore = false;
        }

        // giant rats - bad animations require fixing
        if (type is EObjectType.GiantRatBrown or EObjectType.GiantRatGrey)
        {
            fixScale(gameObject.transform);
        }
    }

    public override bool IsDamageable()
    {
        return hp > 0;
    }

    private void NotifyRaceOfAttack()
    {
        // max distance is < 4 tiles

        int numOverlaps = Physics.OverlapSphereNonAlloc(transform.position, 4.0f * xzScale,
            sphereOverlapCache, 1 << LayerMask.NameToLayer("Characters"));

        for (int i = 0; i < numOverlaps; ++i)
        {
            Collider col = sphereOverlapCache[i];
            if (col is CharacterController)
            {
                Critter critter = col.transform.root.GetComponent<Critter>();

                if (critter.race == race && critter != this)
                {
                    critter.attitude = (EAttitude)Mathf.Max(0, (int)(critter.attitude - 1));
                }
            }
        }
    }

    public override void TryDamage(int damage, Skills.ESkillTestResult result)
    {
        // Easy mode: increase damage done to enemies by 3/2
        if (PlayerData.sData.easy)
        {
            damage = damage * 3 / 2;
        }
        hp -= damage;

        CritterHealthDisplay.CritterDamaged(hp / (float)originalHp);

        EParticleType particleType = Utils.MapRemainsToParticleType(stats.Remains, blood);
        Utils.CreateSplats(this, particleType, damage);

        // make more hostile
        if (attitude > 0)
        {
            --attitude;

            if (attitude == EAttitude.Hostile)
            {
                PlayAlertSound();
            }
        }

        bool flinching = false;
        if (result == Skills.ESkillTestResult.CriticalSuccess && hp > 0)
        {
            if (cachedAnimator != null && cachedAnimator.HasState(0, Animator.StringToHash("Flinch")))
            {
                SetState(EState.Flinch);
                flinching = true;
            }
        }

        if (!flinching && state is EState.Wander or EState.TurnToWander or EState.Approach or EState.TurnToApproach)
        {
            SetState(EState.Idle);
        }

        if (attitude == EAttitude.Hostile || goal == EGoal.AttackTarget5)
        {
            Music.InCombat();
            NotifyRaceOfAttack();

            // maybe flee
            if (result == Skills.ESkillTestResult.CriticalSuccess
                && hp < originalHp / 3.0f
                && PlayerData.sData.charLevel >= critterLevel
                && Random.value < 0.5f)
            {
                CreateFear();
            }
        }

        if (whoami is not EWhoAmI.Tyball and not EWhoAmI.Golem and not EWhoAmI.Rodrick)
        {
            PlayerData.sData.pacifistStopped = true;
        }

        if (eyeGlowRenderer != null)
        {
            float fractionalHealthRemaining = (float)hp / originalHp;
            int index = Math.Clamp((int)(3.0f * (1.0f - fractionalHealthRemaining)), 0, 2);
            Color color = eyeColors[index];
            // set the eye emission color
            foreach (Material m in eyeGlowRenderer.materials)
            {
                m.SetColor(emissionColorPropertyId, color);
            }
        }

        damagedTimer += 5.0f;
    }

    public void ReactToMissedAttack()
    {
        // Check if facing player
        Vector3 playerPos = PlayerObject.Player.transform.position;
        Vector3 dir = (playerPos - transform.position).normalized;
        float dot = Vector3.Dot(dir, transform.forward);

        if (dot > 0.0f) // Facing player
        {
            // Make more hostile
            if (attitude > 0)
            {
                --attitude;
                if (attitude == EAttitude.Hostile)
                {
                    PlayAlertSound();
                    Music.InCombat();
                }
            }
            damagedTimer += 2.0f; // Shorter than actual damage
        }
    }

    public void CreateFear()
    {
        Tile fleeTile = FindFleePoint();
        if (fleeTile != null)
        {
            SetState(EState.TurnToFlee);
            Vector3 targetCenter = fleeTile.GetCenter();
            if (movementType == EMovementType.Flying)
            {
                targetCenter.y += 1.5f;
            }

            GetPath(out path, transform.position, targetCenter);
            if (path.Count > 0)
            {
                //path[path.Count - 1] += new Vector3( Random.Range( -1.0f, 1.0f ), 0.0f, Random.Range( -1.0f, 1.0f ) );
                SetState(EState.TurnToFlee);
            }
        }
    }

    private float poisonTime;
    private float timeToNextPoisonDamage;

    public void Poison()
    {
        // some creatures are immune to poison (those that do poison damage)
        if (poison == 0)
        {
            poisonTime = 30.0f;
            timeToNextPoisonDamage = 5.0f;
        }
    }

    protected override void StolenFrom(UUObject item, int ownerRace)
    {
        if (!Magic.sMagic.IsSpellActive(Magic.ESpell.FreezeTime))
        {
            if (Vector3.Distance(item.transform.position, transform.position) < theftDetectionRange)
            {
                if (race == ownerRace)
                {
                    // Set timer to interrupt wander state
                    theftDetectedTimer = 0.5f;

                    if (attitude != EAttitude.Hostile)
                    {
                        string preHostilityName = GetLookName();
                        --attitude;
                        string msg = $"{preHostilityName}{StringLoader.GetString(1, 225 + (int)attitude)}";
                        msg = char.ToUpper(msg[0]) + msg.Substring(1);
                        Messages.Add(msg);

                        if (attitude == EAttitude.Hostile)
                        {
                            PlayAlertSound();
                        }
                    }
                }
            }
        }
    }

    int CalcFlankingBonus()
    {
        // base on where attacking the player from
        // should be 0-3
        return 0;
    }

    private static int GetCriticalDamage(int damage)
    {
        //based on disassembly:
        //seems to be a 50:50 chance for a crit which gives double damage. (48+(rng 0-30)) >>5
        return damage * Random.Range(1, 3);
    }

    void TryDamageTarget()
    {
        if (GetDistanceToTarget() > meleeAttackRange + meleeAttackHysteresis)
        {
            // miss
            return;
        }

        Vector3 meleeAttackPos = GetMeleeAttackPos();
        Vector3 dir = GetTargetPos() - meleeAttackPos;
        float checkDist = dir.magnitude;
        if (Physics.Raycast(GetMeleeAttackPos(), dir / checkDist, checkDist, LayerMasks.EnvironmentAndCeiling))
        {
            // miss
            return;
        }

        int flankingbonus = CalcFlankingBonus();
        int attackScore = attackChanceToHit + (equipDamage / 2) + Random.Range(7, 12) + flankingbonus; //+Maybe Npc Level
        // + unknownbonus (stored in critterdata)

        int defenceScore = attackTarget == null ? PlayerObject.Player.GetDefence() : attackTarget.GetDefence();

        Skills.ESkillTestResult result = Skills.GetResult(attackScore, defenceScore);
        if (type == EObjectType.SlasherOfVeils && result != Skills.ESkillTestResult.CriticalSuccess)
        {
            result = Skills.ESkillTestResult.Success;
        }

        int damage = Mathf.Max(2, attackDamage + strength / 5);
        if (result == Skills.ESkillTestResult.CriticalSuccess)
        {
            damage = GetCriticalDamage(damage);
        }

        damage = Utils.GetDamageRoll(damage);

        // Comment from Hank's UW exporter.
        // TODO: damage for NPCS is scaled based on a lookup table and a property in their mobile data.
        // This is similar to the player attack charge. (values stored in segment_60 in UW2 exe)
        // Lookup appears to be based on the value in the object at 0xF ( bits 12 to 15)
        // For now just scale it randomly
        if (damage > 2)
        {
            damage = (short)Random.Range(2, damage + 1);
        }

        switch (result)
        {
        case Skills.ESkillTestResult.CriticalFailure:
        case Skills.ESkillTestResult.Failure:
            // play sound
            break;
        default:
            if (attackTarget == null)
            {
                bool poisoning = false;
                // apply poison
                int poisonStat = poison;
                if (poisonStat > 0)
                {
                    int poisonProtection = Magic.sMagic.IsSpellActive(Magic.ESpell.PoisonResistance) ? 10 : 0;
                    Skills.ESkillTestResult poisonResult = Skills.GetResult(poisonStat, PlayerData.sData.strength / 2 + poisonProtection);
                    if (poisonResult >= Skills.ESkillTestResult.Success)
                    {
                        int appliedPoison = Utils.GetDamageRoll(poisonStat);
                        PlayerObject.AddPoison(appliedPoison);
                        poisoning = true;
                    }
                }
                PlayerObject.Player.Damage(result, damage, poisoning ? EDamageType.Poison : EDamageType.Damage);
            }
            else
            {
                attackTarget.TryDamage(damage, result);
            }
            break;
        }
    }

    void SpawnRemains()
    {
        if (stats.Remains != 0)
        {
            UUObject remains = null;
            switch (stats.Remains)
            {
            case 1:
                remains = LevelLoader.CreateObjectOfType(EObjectType.DeadRotworm);
                break;
            }

            if (remains != null)
            {
                remains.PostLoadInitialize();
                remains.WorldInitialize(transform.position);
                // snap to the same spot as the dying critter
                remains.transform.position = transform.position;
                remains.transform.eulerAngles = transform.eulerAngles;
            }
        }
    }

    public List<UUObject> loot = new List<UUObject>();

    private void SpawnLoot()
    {
        if (link != 0)
        {
            int next = link;
            while (next != 0 && loot.Count < 7)
            {
                // follow loot from here
                UUObject obj = LevelLoader.GetObj(next);
                if (obj != null)
                {
                    obj.PostLoadInitialize(restoredFromSave: false);
                    loot.Add(obj);
                    next = obj.chainIndex;
                }
                else
                {
                    break;
                }
            }
            link = 0; // prevent loot being inventory and getting double spawned when the critter dies
        }
        else
        {
            foreach (var lootType in stats.Loot)
            {
                if (lootType == 0 && type is not EObjectType.MountainmanA and not EObjectType.MountainmanB)
                {
                    Debug.Log("Loot type 0", this);
                }
                UUObject lootObj = LevelLoader.CreateObjectOfType(lootType);
                lootObj.quality = Random.Range(1, 41);
                lootObj.levelIndex = levelIndex;
                lootObj.originalLevel = levelIndex;
                lootObj.PostLoadInitialize();
                loot.Add(lootObj);
            }
        }

        SpawnTreasure();
        SpawnFood();

        // Ensure containers in loot have their contents filled
        FillLootContainerContents();
    }

    private void FillLootContainerContents()
    {
        foreach (var lootObj in loot)
        {
            if (lootObj != null && lootObj.getClass == UUObject.EClass.Containers)
            {
                LevelLoader.FillContainer(lootObj);
            }
        }
    }

    private void SpawnFood()
    {
        if (Random.Range(0, 15) < foodProb)
        {
            UUObject food = LevelLoader.CreateObjectOfType(foodItem);
            food.quality = Random.Range(20, 41);
            food.PostLoadInitialize();
            loot.Add(food);
        }
    }

    private void SpawnTreasure()
    {
        if (Random.Range(0, 16) < treasureProb)
        {
            // heavily weight coins, but make other treasure more likely on later levels
            int levelBias = 3 * LevelLoader.sLevelLoader.loadedLevel;
            int treasureType = 160 + Mathf.Max(0, Random.Range(0, 37 - levelBias) - (30 - levelBias));
            int value = DataLoader.sDataLoader.comObjProps[treasureType].monetaryValue;
            // spawn less of the valuable stuff
            if (value >= 12)
            {
                value = 188 + 8 * value;
            }
            else if (value >= 8)
            {
                value = 236 + 4 * value;
            }
            else if (value >= 4)
            {
                value = 252 + 2 * value;
            }
            int quant = 0;
            if (treasureStackProb < value)
            {
                if (4 * treasureStackProb < Random.Range(0, value))
                {
                    quant = 1;
                }
            }
            else
            {
                // nD4
                int numRolls = 2 * (4 * treasureStackProb / value);
                quant = Utils.DiceRoll(4, numRolls) / 4;
            }

            if (quant > 0)
            {
                //Create obj
                UUObject lootObj = LevelLoader.CreateObjectOfType((EObjectType)treasureType);
                lootObj.quantity = quant;
                lootObj.PostLoadInitialize();
                loot.Add(lootObj);
            }
        }
    }

    private void SpawnCorpseContents()
    {
        foreach (var lootObj in loot)
        {
            PositionPossessedObject(lootObj);
            if (lootObj.type is EObjectType.BoneA or EObjectType.BoneB or EObjectType.SkullA or EObjectType.SkullB or EObjectType.PileOfBonesA or EObjectType.PileOfBonesB)
            {
                lootObj.ownerIndex = typeNum - 64;
            }
            // Mark keys spawned from Tyball as invulnerable to lava (using quality = 255 as marker)
            if (whoami == EWhoAmI.Tyball && lootObj is Key)
            {
                lootObj.quality = 255;
            }
        }
    }

    void SpawnRandomReplacement()
    {
        // iterate the active create object traps
        Trap[] allTraps = FindObjectsByType<Trap>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        List<Trap> spawnTraps = allTraps.Where(
            trap => trap.type == EObjectType.CreateObjectTrap
                && trap.link > 0 && trap.link < 256 // critters in top 256
                && LevelLoader.GetLevel().objects[trap.link] != null
                && trap.flags == 0
                && !LevelLoader.GetLevel().pvs.IsVisible(trap.initialTile)).ToList();
        if (spawnTraps.Count > 0)
        {
            Trap chosenTrap = spawnTraps[Random.Range(0, spawnTraps.Count)];
            Critter critter = chosenTrap.DoCreateObjectTrap(chosenTrap) as Critter;
            if (critter != null && critter.movementType == EMovementType.Flying)
            {
                critter.transform.position += 2.0f * Vector3.up;
            }
        }
    }

    public void Conversation_setup_to_barter()
    {
        // first scan through loot and pick what is possible
        // then randomly choose from the good stuff
        bool firstWeapon = false;
        List<UUObject> goodStuff = new List<UUObject>();
        foreach (UUObject lootObj in loot)
        {
            if (lootObj.getClass == EClass.Weapons && !firstWeapon)
            {
                firstWeapon = true;
                continue;
            }
            if (DataLoader.sDataLoader.comObjProps[(int)lootObj.type].monetaryValue > 0
                && (whoami != EWhoAmI.Zak || lootObj.getClass == EClass.LampsAndWands)) // make sure Zak trades the taper
            {
                goodStuff.Add(lootObj);
            }
        }

        for (int i = 0; i < 4 && goodStuff.Count > 0; ++i)
        {
            int s = Inventory.npcTradeIndices[i];
            int r = Random.Range(0, goodStuff.Count);
            Inventory.sInv.tradeSlots[s] = goodStuff[r];
            loot.Remove(goodStuff[r]);
            goodStuff.RemoveAt(r);
        }

        Inventory.ShowPanel();
    }

    public int Conversation_do_decline()
    {
        EmptyBarterTray();
        return 0;
    }

    private int tradePatience;
    private int tradeLastOfferValue;

    // these are in the order of the arguments to do_offer
    public enum ETradeResult
    {
        Undef,
        What, // nothing to trade
        Tired, // patience ran out
        Bad, // bad deal
        No, // don't like deal
        Yes // deal done
    }

    public ETradeResult TryTrade()
    {
        ETradeResult result = ETradeResult.Undef;

        if (!Inventory.sInv.DealPossible())
        {
            return ETradeResult.What;
        }

        int relativeValue = Inventory.sInv.GetRelativeValueOfTrade(true);

        // just add in Charm. it's pretty useless otherwise so might as well give a big bonus here.
        relativeValue -= Skills.GetSkill(ESkill.Charm);

        // same as do_judgement
        int appraiseSkill = stats.TradeAppraisal;
        int appraiseVariance = (30 - appraiseSkill) / 3;
        relativeValue += Random.Range(-appraiseVariance, appraiseVariance + 1);
        int deal = relativeValue / 5;
        if (deal < 0) // good+
        {
            result = ETradeResult.Yes;
        }

        // use stats.TradeThreshold here somehow
        bool tookDeal = false;
        if (deal == 0 || deal == 1) // even or poor
        {
            // maybe take the deal
            tookDeal = Random.value < 0.5f;
            result = tookDeal ? ETradeResult.Yes : ETradeResult.No;
        }
        else if (deal > 1)
        {
            result = ETradeResult.Bad;
        }

        if (!tookDeal)
        {
            if (++tradePatience >= stats.TradePatience)
            {
                result = ETradeResult.Tired;
            }
        }

        if (result == ETradeResult.Yes)
        {
            // swap the items
            List<int> itemsToTake = new List<int>();
            foreach (int i in Inventory.playerTradeIndices)
            {
                if (Inventory.sInv.tradeSlotSelected[i])
                {
                    itemsToTake.Add(i);
                }
            }

            List<int> itemsToGive = new List<int>();
            foreach (int i in Inventory.npcTradeIndices)
            {
                if (Inventory.sInv.tradeSlotSelected[i])
                {
                    itemsToGive.Add(i);
                }
            }

            // take the items first so there's more room to give
            foreach (int i in itemsToTake)
            {
                loot.Add(Inventory.sInv.tradeSlots[i]);
                Inventory.sInv.tradeSlots[i] = null;
                Inventory.sInv.tradeSlotSelected[i] = false;
            }

            // now give. if space put in trade tray, otherwise put in player inventory
            foreach (int i in itemsToGive)
            {
                bool given = false;
                foreach (int d in Inventory.playerTradeIndices)
                {
                    if (Inventory.sInv.tradeSlots[d] == null)
                    {
                        Inventory.sInv.tradeSlots[d] = Inventory.sInv.tradeSlots[i];
                        given = true;
                        break;
                    }
                }

                if (!given)
                {
                    Inventory.Add(Inventory.sInv.tradeSlots[i]);
                }

                Inventory.sInv.tradeSlots[i] = null;
                Inventory.sInv.tradeSlotSelected[i] = false;
            }

            EmptyBarterTray();
        }

        return result;
    }

    public void StealBarterItems()
    {
        List<int> itemsToGive = new List<int>();
        foreach (int i in Inventory.npcTradeIndices)
        {
            if (Inventory.sInv.tradeSlotSelected[i])
            {
                itemsToGive.Add(i);
            }
        }

        // now give. if space put in trade tray, otherwise put in player inventory
        foreach (int i in itemsToGive)
        {
            bool given = false;
            foreach (int d in Inventory.playerTradeIndices)
            {
                if (Inventory.sInv.tradeSlots[d] == null)
                {
                    Inventory.sInv.tradeSlots[d] = Inventory.sInv.tradeSlots[i];
                    given = true;
                    break;
                }
            }

            if (!given)
            {
                Inventory.Add(Inventory.sInv.tradeSlots[i]);
            }

            Inventory.sInv.tradeSlots[i] = null;
            Inventory.sInv.tradeSlotSelected[i] = false;
        }
    }

    private void EmptyBarterTray()
    {
        // move anything left in the barter window back to the npc
        foreach (int i in Inventory.npcTradeIndices)
        {
            if (Inventory.sInv.tradeSlots[i] != null)
            {
                loot.Add(Inventory.sInv.tradeSlots[i]);
            }
            Inventory.sInv.tradeSlots[i] = null;
            Inventory.sInv.tradeSlotSelected[i] = false;
        }
    }

    public void StartConversation()
    {
        tradePatience = 0;
    }

    public void EndConversation()
    {
        talkedTo = 1;
        EmptyBarterTray();

        if (removeTalker)
        {
            LevelLoader.worldObj.Remove(this);
            gameObject.SetActive(false);
        }
    }

    private string GetGivenName()
    {
        string givenName = singularName;
        Conversations.TryGetGivenName(ref givenName, (int)whoami);

        return givenName;
    }

    public override string GetLookName()
    {
        string attitudeString = StringLoader.GetString(5, 96 + (int)attitude);
        string lookName = (Utils.IsVowel(attitudeString[0]) ? "an " : "a ") + attitudeString + " ";

        if (whoami == EWhoAmI.Warren)
        {
            // special case spectre named Warren
            lookName += StringLoader.GetString(7, 223);
        }
        else
        {
            string givenName = GetGivenName();
            if (givenName != "" && givenName != name)
            {
                // check for bandit, prisoner, &c.
                if (char.IsUpper(givenName[0]))
                {
                    lookName += singularName + " named " + givenName;
                }
                else
                {
                    lookName += givenName;
                }
            }
            else
            {
                lookName += singularName;
            }
        }

        return lookName;
    }

    public override float GetInteractionDistance()
    {
        return 7.5f;
    }

    private float paralyzeTime;

    public void TryParalyze()
    {
        paralyzeTime = 15.0f;
    }

    public void TryAlly()
    {
        if (playerLastEngagedInCombat != null)
        {
            attackTarget = playerLastEngagedInCombat;
            attitude = EAttitude.Hostile;
        }
        else if (attitude == EAttitude.Hostile)
        {
            // at least stop them attacking the player
            // don't want to make them friendly in case that allows for conversations
            attitude = EAttitude.Upset;
        }
    }

    public void TryConfuse()
    {
        confusionTime = 30.0f;
        if (hp > 0)
        {
            state = EState.Wander;
        }
    }

    public void TryCurse(float _curseTime = 30.0f)
    {
        curseTime = _curseTime;
        curseMultiplier = 0.5f;
    }

    private static readonly int emissionColorPropertyId = Shader.PropertyToID("_EmissionColor");

    protected void Footstep(string boneName)
    {
        Transform t = transform;
        if (boneName.Length > 0)
        {
            t = Utils.FindDeepChild(t, boneName);
        }

        foreach (var category in GetSounds())
        {
            if (category.soundType == CritterSoundType.Footstep)
            {
                AudioClip clip = category.GetRandomClip();
                if (clip != null)
                {
                    Utils.PlayClipOccluded(clip, t.position, 10.0f, AudioRolloffMode.Linear, category.volume);
                }
                break;
            }
        }
    }

    protected void OnGUI()
    {
        GUI.depth = (int)EGUIDepth.CritterDebug;
#if UNITY_EDITOR && false
        if (PlayerObject.Player.controlsDisabled == 0)
        {
            if ((PlayerObject.Player.mainCamera.transform.position - transform.position).sqrMagnitude < 150.0f)
            {
                DebugGUI.DrawTextOnGUI(transform.position, $"{state.ToString()}; {attitude.ToString()}; {currentAnimation}",
                    debugFont);

                if (path != null && path.Count > 0)
                {
                    DebugGUI.DrawTextOnGUI(GetLookahead(0.5f), "e", debugFont);
                    DebugGUI.DrawTextOnGUI(path[0], "o", debugFont);
                }
            }
        }
#endif
    }

    protected override void PopulateSaveData(LevelObjectSaveData data)
    {
        base.PopulateSaveData(data);

        if (data is CritterSaveData critterData)
        {
            critterData.hp = hp;
            critterData.originalHp = originalHp;
            critterData.goal = (int)goal;
            critterData.gtarg = gtarg;
            critterData.critterLevel = critterLevel;
            critterData.talkedTo = talkedTo;
            critterData.attitude = (int)attitude;
            critterData.yhome = yhome;
            critterData.xhome = xhome;
            critterData.heading = heading;
            critterData.hunger = hunger;
            critterData.whoami = (int)whoami;
            critterData.movementType = (int)movementType;
            critterData.state = (int)state;
            critterData.stateTime = stateTime;
            critterData.playerAlly = playerAlly;
            critterData.path = path != null ? new List<Vector3>(path) : null;

            // Save animator state - when in transition save target state so we don't persist the source (e.g. Idle during Idle->Walk)
            if (cachedAnimator != null)
            {
                AnimatorStateInfo stateInfo = cachedAnimator.IsInTransition(0)
                    ? cachedAnimator.GetNextAnimatorStateInfo(0)
                    : cachedAnimator.GetCurrentAnimatorStateInfo(0);
                critterData.animatorStateHash = stateInfo.fullPathHash;
                critterData.animatorNormalizedTime = stateInfo.normalizedTime % 1.0f; // Keep within 0-1
                critterData.animatorSpeed = cachedAnimator.speed;
                critterData.currentAnimation = currentAnimation; // Save currentAnimation verbatim
            }
            else
            {
                critterData.animatorStateHash = 0;
                critterData.animatorNormalizedTime = 0f;
                critterData.animatorSpeed = 1f;
                critterData.currentAnimation = currentAnimation ?? "Idle";
            }

            // Save status effect timers
            critterData.poisonTime = poisonTime;
            critterData.timeToNextPoisonDamage = timeToNextPoisonDamage;
            critterData.paralyzeTime = paralyzeTime;
            critterData.confusionTime = confusionTime;
            critterData.curseTime = curseTime;
            critterData.damagedTimer = damagedTimer;

            // Save state machine variables
            critterData.actionDone = actionDone;
            critterData.deathProcessed = deathProcessed;
            critterData.remainsSpawned = remainsSpawned;
            critterData.wallRubTime = wallRubTime;
            critterData.initialTurnAngle = initialTurnAngle;

            // Save attack lunge state
            critterData.mLungeTime = mLungeTime;
            critterData.mLungeDirection = mLungeDirection;
            critterData.mLungeDestination = mLungeDestination;

            // Save sprite animation state
            critterData.frameTime = frameTime;
            critterData.frameIndex = frameIndex;

            // Save loot
            if (loot != null && loot.Count > 0)
            {
                critterData.loot = new List<ObjectSaveData>();
                foreach (UUObject lootObj in loot)
                {
                    if (lootObj != null)
                    {
                        critterData.loot.Add(lootObj.SaveToData());
                    }
                }
            }

            // Save conversation state
            critterData.charGlobals = charGlobals != null ? (short[])charGlobals.Clone() : null;
        }
    }

    protected override void RestoreFromSaveData(LevelObjectSaveData data)
    {
        base.RestoreFromSaveData(data);

        if (data is CritterSaveData critterData)
        {
            hp = critterData.hp;
            originalHp = critterData.originalHp;
            goal = (EGoal)critterData.goal;
            gtarg = critterData.gtarg;
            critterLevel = critterData.critterLevel;
            talkedTo = critterData.talkedTo;
            attitude = (EAttitude)critterData.attitude;
            yhome = critterData.yhome;
            xhome = critterData.xhome;
            heading = critterData.heading;
            hunger = critterData.hunger;
            whoami = (EWhoAmI)critterData.whoami;
            movementType = (EMovementType)critterData.movementType;
            state = (EState)critterData.state;
            stateTime = critterData.stateTime;
            playerAlly = critterData.playerAlly;
            path = critterData.path != null ? new List<Vector3>(critterData.path) : null;

            // Store animator state for restoration in PostLoadInitialize
            savedAnimatorStateHash = critterData.animatorStateHash;
            savedAnimatorNormalizedTime = critterData.animatorNormalizedTime;
            savedAnimatorSpeed = critterData.animatorSpeed;
            currentAnimation = critterData.currentAnimation ?? "Idle"; // Restore currentAnimation verbatim

            // Restore status effect timers
            poisonTime = critterData.poisonTime;
            timeToNextPoisonDamage = critterData.timeToNextPoisonDamage;
            paralyzeTime = critterData.paralyzeTime;
            confusionTime = critterData.confusionTime;
            curseTime = critterData.curseTime;
            curseMultiplier = curseTime > 0.0f ? 0.5f : 1.0f;
            damagedTimer = critterData.damagedTimer;

            // Restore state machine variables
            actionDone = critterData.actionDone;
            deathProcessed = critterData.deathProcessed;
            remainsSpawned = critterData.remainsSpawned;
            wallRubTime = critterData.wallRubTime;
            initialTurnAngle = critterData.initialTurnAngle;

            // Restore attack lunge state
            mLungeTime = critterData.mLungeTime;
            mLungeDirection = critterData.mLungeDirection;
            mLungeDestination = critterData.mLungeDestination;

            // Restore sprite animation state
            frameTime = critterData.frameTime;
            frameIndex = critterData.frameIndex;

            // Restore loot
            if (critterData.loot != null && critterData.loot.Count > 0)
            {
                loot = new List<UUObject>();
                foreach (ObjectSaveData lootData in critterData.loot)
                {
                    if (lootData.objectType == 0 && type is not EObjectType.MountainmanA and not EObjectType.MountainmanB)
                    {
                        Debug.Log($"Hand axe in loot for {name} {objectIndex}");
                    }
                    LevelObject lootObj = SaveGameManager.sInstance.CreateObjectFromSaveData(lootData);
                    if (lootObj is UUObject uuLootObj)
                    {
                        loot.Add(uuLootObj);
                        // Initialize the loot item but keep it inactive (not positioned)
                        // Loot will be positioned when critter dies via SpawnCorpseContents()
                        uuLootObj.PostLoadInitialize(restoredFromSave: true);
                        uuLootObj.gameObject.SetActive(false);
                    }
                }

                // Ensure containers in restored loot have their contents filled
                FillLootContainerContents();
            }

            // Restore conversation state
            charGlobals = critterData.charGlobals != null ? (short[])critterData.charGlobals.Clone() : null;
        }
    }

    public override ObjectSaveData SaveToData()
    {
        CritterSaveData data = new CritterSaveData();
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

        CritterSaveData data = JsonUtility.FromJson<CritterSaveData>(objData.jsonData);
        RestoreFromSaveData(data);
    }
}
