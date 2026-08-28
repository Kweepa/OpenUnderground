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
                break;
            }
        }
    }
}
