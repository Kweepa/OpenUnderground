// Mage.cs

using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Game.Scripts.CritterVariants
{
    public enum ERobeColor
    {
        Red,
        Blue,
        Yellow,
        Fancy
    }

    public enum ESkinColor
    {
        Light,
        Medium,
        Dark
    }

    public enum EFacialHair
    {
        None,
        Mustache,
        Beard
    }

    public enum EFringe
    {
        None,
        Present
    }
    
    public enum EMageHairColour
    {
        LightGrey,
        DarkGrey,
        Black,
        Golden
    }

    public enum EMageEyeColour
    {
        Blue,
        Brown,
        Yellow,
        Red
    }

    [Serializable]
    public struct MageCustomization
    {
        public string name;
        [FilterableEnumList]
        public EWhoAmI whoami;
        public ERobeColor robeColor;
        public ESkinColor skinColor;
        public EFacialHair facialHair;
        public EFringe fringe;
        public EMageHairColour hairColor;
        public EMageEyeColour eyeColor;
        public bool hasGlasses;
    }

    public class Mage : Caster
    {
        // GameObjects for different parts of the mage model
        public GameObject robes;
        public GameObject[] skin;
        public GameObject eyebrows;
        public GameObject eyes;
        public GameObject glasses;
        public GameObject fringe;
        public GameObject[] mustache;
        public GameObject beard;
        public GameObject staff; // A mage's weapon

        // Material arrays to be assigned in the Unity Editor
        public Material[] robeMats;
        public Material[] skinMats;
        public Material[] hairMats;
        public Material[] eyeMats;
        public Material[] eyebrowMats;

        // Array of pre-defined customizations
        public MageCustomization[] customizations;

        // Track spawned critters to limit spawns
        private System.Collections.Generic.List<Critter> spawnedCritters = new ();

        protected override string[] GetAttackCandidateStates()
        {
            if (type == EObjectType.Tyball && movementType == EMovementType.Flying)
            {
                return new[] { "Cast_Fly", "Throw_Knife_Fly" };
            }
            else if (type is EObjectType.MageB or EObjectType.MageF)
            {
                return new[] { "Cast", "Thrust" };
            }

            return new[] { "Cast1", "Cast2", "Throw_Knife" };
        }

        protected override void ChangeAnimation(string animationName)
        {
            if (type == EObjectType.Tyball && movementType == EMovementType.Flying && animationName != "Attack")
            {
                base.ChangeAnimation(animationName + "_Fly");
                return;
            }
            base.ChangeAnimation(animationName);
        }

        /// <summary>
        /// Sets the material for a specific GameObject's renderer.
        /// </summary>
        private void SetPartMaterial(GameObject part, Material mat)
        {
            if (part != null)
            {
                SkinnedMeshRenderer skinned = part.GetComponent<SkinnedMeshRenderer>();
                if (skinned != null)
                {
                    skinned.material = mat;
                }
            }
        }

        /// <summary>
        /// Applies the full set of customizations to the mage model.
        /// </summary>
        private void Customize(MageCustomization cust)
        {
            // Set materials for parts that are always visible
            SetPartMaterial(robes, robeMats[(int)cust.robeColor]);
            foreach (GameObject go in skin)
            {
                SetPartMaterial(go, skinMats[(int)cust.skinColor]);
            }
            SetPartMaterial(eyes, eyeMats[(int)cust.eyeColor]);

            // Determine hair material and apply to all hair-related parts
            Material hairMat = hairMats[(int)cust.hairColor];
            SetPartMaterial(fringe, hairMat);
            SetPartMaterial(mustache[0], hairMat);
            SetPartMaterial(mustache[1], hairMat);
            SetPartMaterial(beard, hairMat);
            SetPartMaterial(eyebrows, eyebrowMats[(int)cust.hairColor]);

            // Enable/disable optional parts
            if (fringe != null)
            {
                cachedVisibilityGroup.SetGameObjectState(fringe, cust.fringe == EFringe.Present);
            }

            if (mustache[0] != null && mustache[1] != null)
            {
                cachedVisibilityGroup.SetGameObjectState(mustache[0], cust.facialHair == EFacialHair.Beard);
                cachedVisibilityGroup.SetGameObjectState(mustache[1], cust.facialHair == EFacialHair.Mustache);
            }

            if (beard != null)
            {
                cachedVisibilityGroup.SetGameObjectState(beard, cust.facialHair == EFacialHair.Beard);
            }

            // Set staff visibility once. It will not change based on state.
            if (staff != null)
            {
                cachedVisibilityGroup.SetGameObjectState(staff, type is EObjectType.MageB or EObjectType.MageF);
            }

            if (glasses != null)
            {
                cachedVisibilityGroup.SetGameObjectState(glasses, cust.hasGlasses);
            }
        }

        /// <summary>
        /// Initializes the critter's appearance after it has been loaded.
        /// </summary>
        public override void PostLoadInitialize(bool restoredFromSave = false)
        {
            base.PostLoadInitialize(restoredFromSave);

            bool foundSpecific = false;
            if (whoami != EWhoAmI.None)
            {
                // Look for a pre-defined customization matching this critter's identity
                foreach (MageCustomization cust in customizations)
                {
                    if (cust.whoami == whoami)
                    {
                        foundSpecific = true;
                        Customize(cust);
                        break;
                    }
                }
            }

            // If no specific customization was found, create a random one
            if (!foundSpecific)
            {
                // Generate deterministic seed from level and objectIndex for consistent appearance
                int level = originalLevel != 0 ? originalLevel : levelIndex;
                int deterministicSeed = (level * 1024) + objectIndex;
                Random.InitState(deterministicSeed);
                
                MageCustomization cust;

                cust.name = "Random";
                cust.whoami = whoami;
                cust.robeColor = (ERobeColor)Random.Range(0, 3); // don't randomly choose the fancy robe
                cust.skinColor = Utils.RandomEnum<ESkinColor>();
                cust.facialHair = Random.value < 0.9f ? EFacialHair.Beard : EFacialHair.None;
                cust.fringe = Random.value < 0.8f ? EFringe.None : EFringe.Present;
                cust.hairColor = Utils.RandomEnum<EMageHairColour>();
                cust.eyeColor = Random.value < 0.5f ? EMageEyeColour.Blue : EMageEyeColour.Brown;
                cust.hasGlasses = Random.value < 0.2f;

                Customize(cust);
            }
        }

        protected override void SetState(EState newState)
        {
            base.SetState(newState);

            switch (newState)
            {
            case EState.Die:
                if (staff != null)
                {
                    // Hide staff when dead to avoid confusion with loot
                    cachedVisibilityGroup.SetGameObjectState(staff, false);
                }
                break;
            }
        }

        protected override void LaunchProjectile()
        {
            // On level 4, mages sometimes spawn monsters instead of launching projectiles
            if (LevelLoader.sLevelLoader != null && LevelLoader.sLevelLoader.loadedLevel == 4)
            {
                // 30% chance to spawn a monster instead of launching a projectile
                if (Random.value < 0.3f)
                {
                    // Clean up dead critters from the list
                    spawnedCritters.RemoveAll(critter => critter == null || critter.hp <= 0 || critter.state == EState.Die || critter.state == EState.Dead);

                    // Only spawn if we have less than 3 living spawns
                    if (spawnedCritters.Count < 3)
                    {
                        // for debugging to check sounds even when there are no encountered critters
                        Utils.PlayClipOccluded(Magic.sMagic.castSpellSound[(int)Magic.ESpell.SummonMonster], transform.position);
                        Critter spawnedMonster = SpawnMonster();
                        if (spawnedMonster != null)
                        {
                            return; // Don't launch projectile when spawning monster
                        }
                    }
                }
            }

            base.LaunchProjectile();
        }

        private Critter SpawnMonster()
        {
            // Find a spot near the mage to spawn the monster
            Vector3 spawnPos = transform.position + transform.forward * 2.0f;
            spawnPos.y = transform.position.y;

            // Raycast down to find ground
            int layerMask = LayerMasks.EnvironmentOnly;
            if (Physics.Raycast(spawnPos + Vector3.up * 2.0f, Vector3.down, out RaycastHit hit, 5.0f, LayerMasks.EnvironmentOnly))
            {
                spawnPos = hit.point + Vector3.up * 0.1f;
            }

            // Choose which monster to spawn (gazer less often - 10% chance, others 22.5% each)
            EObjectType monsterType;
            float roll = Random.value;
            if (roll < 0.1f)
            {
                monsterType = EObjectType.Gazer;
            }
            else if (roll < 0.325f)
            {
                monsterType = EObjectType.GiantRatGrey;
            }
            else if (roll < 0.55f)
            {
                monsterType = EObjectType.Skeleton;
            }
            else if (roll < 0.775f)
            {
                monsterType = EObjectType.Mongbat;
            }
            else
            {
                monsterType = EObjectType.DreadSpider;
            }

            // Get encounter data for this monster type
            CritterEncounterData? encounterData = PlayerObject.Player.GetEncounteredCritterData(monsterType);
            if (encounterData != null)
            {
                // Create dummy objData (unlink it)
                ushort[] objData = { (ushort)((int)monsterType | (1 << 15)), 0, 40, 0 };
                // Read critterData from the level where this critter was encountered
                byte[] critterData = LevelLoader.sLevelLoader.ReadCritterDataFromLevel(encounterData.Value.level, encounterData.Value.objectIndex);
                
                if (critterData != null)
                {
                    Critter spawnedMonster = LevelLoader.sLevelLoader.CreateObjectOfType(objData, critterData) as Critter;
                    if (spawnedMonster != null)
                    {
                        spawnedMonster.transform.position = spawnPos;
                        // Calculate sub-tile coordinates (0-7) from world position
                        spawnedMonster.x = Tile.GetSubTileX(spawnPos.x);
                        spawnedMonster.y = Tile.GetSubTileY(spawnPos.z);
                        LevelLoader.AddToWorld(spawnedMonster);
                        spawnedMonster.WorldInitialize();
                        spawnedMonster.PostLoadInitialize();
                        spawnedMonster.transform.position = spawnPos; // Reposition after WorldInitialize snaps to tile corner
                        
                        // Make it hostile to the player
                        spawnedMonster.attitude = Critter.EAttitude.Hostile;
                        spawnedMonster.attackTarget = PlayerObject.Player.GetComponent<Critter>();

                        // Track this spawned critter
                        spawnedCritters.Add(spawnedMonster);

                        // Spawn particle effect (same as CastSummonMonster)
                        ParticleSpawner.SpawnParticle(EParticleType.MagicCreateFood, spawnPos);
                    }
                    return spawnedMonster;
                }
            }
            return null;
        }

        public override void Update()
        {
            base.Update();

            if (type != EObjectType.Tyball)
            {
                switch (state)
                {
                case EState.Die:
                    {
                        float t = Mathf.Min(stateTime, 1.0f);
                        // s-curve
                        float ease = 3.0f * t * t - 2.0f * t * t * t;
                        float yScale = 1.0f - 0.9f * ease;
                        transform.localScale = new Vector3(1.0f, yScale, 1.0f);
                    }
                    break;
                }
            }
            else
            {
                // force Tyball to be hostile, despite the conversation changing it to Upset
                attitude = EAttitude.Hostile;
            }
        }
    }
}
