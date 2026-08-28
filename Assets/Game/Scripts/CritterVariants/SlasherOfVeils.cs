using UnityEngine;

namespace Game.Scripts.CritterVariants
{
    public class SlasherOfVeils : Critter
    {
        [Tooltip("Magic Containment effect hierarchy to enable on level 8")]
        public GameObject magicContainmentHierarchy;

        private float timeOfNextSlasherHit;

        public override void Initialize(ushort[] objData, byte[] critterData)
        {
            base.Initialize(objData, critterData);
            
            // chase the player
            if (LevelLoader.sLevelLoader.loadedLevel == 9)
            {
                attitude = EAttitude.Hostile;

                // don't give up
                range = 50;
            }
            gameObject.transform.root.gameObject.tag = "Slasher";
        }

        public override string GetLookName()
        {
            return StringLoader.GetString(7, 264);
        }

        protected override void SetState(EState newState)
        {
            base.SetState(newState);

            switch (newState)
            {
            case EState.Approach:
                if (LevelLoader.sLevelLoader.loadedLevel == 9)
                {
                    PlaySoundByEnum(CritterSoundType.Alert);
                }
                break;
            case EState.CombatIdle:
                // attack immediately
                stateTime = 0.0f;
                break;
            }
        }
        
        /// <summary>
        /// Stops all active MagicContainment effects spawned by SlasherOfVeils instances.
        /// Called when the last talisman is thrown into the lava.
        /// </summary>
        public static void StopAllMagicContainmentEffects()
        {
            // Find all SlasherOfVeils instances in the scene (including inactive ones)
            SlasherOfVeils[] allSlashers = FindObjectsByType<SlasherOfVeils>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            
            foreach (SlasherOfVeils slasher in allSlashers)
            {
                if (slasher != null && slasher.magicContainmentHierarchy != null)
                {
                    // Stop all particle systems in the hierarchy
                    ParticleSystem[] particleSystems = slasher.magicContainmentHierarchy.GetComponentsInChildren<ParticleSystem>();
                    foreach (ParticleSystem ps in particleSystems)
                    {
                        if (ps != null && ps.isEmitting)
                        {
                            var emission = ps.emission;
                            emission.enabled = false;
                        }
                    }
                    
                    // Disable the hierarchy
                    slasher.magicContainmentHierarchy.SetActive(false);
                }
            }
        }

        public override void Update()
        {
            base.Update();

            if (LevelLoader.sLevelLoader != null)
            {
                // Enable MagicContainment effect hierarchy on level 8
                if (LevelLoader.sLevelLoader.loadedLevel == 8)
                {
                    if (magicContainmentHierarchy != null && !magicContainmentHierarchy.activeSelf)
                    {
                        magicContainmentHierarchy.SetActive(true);
                    }
                }

                if (LevelLoader.sLevelLoader.loadedLevel == 9 && !PlayerData.sData.enteredGreenMoongate)
                {
                    Music.InCombat();
            
                    // do 1 damage to the player every so often - until drained
                    if (Time.time - timeOfNextSlasherHit > 0.0f)
                    {
                        timeOfNextSlasherHit = Time.time + Random.Range(1.0f, 4.0f);
                        int damageToApply = PlayerData.sData.hp > 5 ? 1 : 0;
                        PlayerObject.Player.Damage(Skills.ESkillTestResult.CriticalSuccess, damageToApply, EDamageType.Damage);
                    }
                }
            }
        }
    }
}
