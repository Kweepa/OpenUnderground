using UnityEngine;

namespace Game.Scripts.CritterVariants
{
    public class Reaper : Critter
    {
        // prefab to spawn when this guy dies - destructable
        public GameObject deathReaper;
        
        protected override void SetState(EState newState)
        {
            base.SetState(newState);

            switch (newState)
            {
            case EState.Die:
                gameObject.SetActive(false);
                PlaySoundByEnum(CritterSoundType.Death);
                ParticleSpawner.SpawnParticle(EParticleType.ReaperDust, transform.position);
                if (deathReaper != null)
                {
                    Instantiate(deathReaper, gameObject.transform.position, gameObject.transform.rotation);
                }

                // As the golem's (Golem.SetState()): it falls apart into pieces of its own and leaves no
                // body, so it goes now, leaving its wood chips under them; switched off, it never reached
                // Dead and they came only when the player came back to the level.
                CleanUpBody();
                break;
            }
        }
    }
}
