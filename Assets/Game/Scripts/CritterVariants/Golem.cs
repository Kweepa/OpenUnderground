using System.Runtime.CompilerServices;
using UnityEngine;

namespace Game.Scripts.CritterVariants
{
    public class Golem : Critter
    {
        // prefab to spawn when this guy dies - destructable
        public GameObject deathGolem;

        [FilterableEnumList]
        public EParticleType deathDustType;

        protected override void SetState(EState newState)
        {
            base.SetState(newState);

            switch (newState)
            {
            case EState.Die:
                gameObject.SetActive(false);
                PlaySoundByEnum(CritterSoundType.Death);
                ParticleSpawner.SpawnParticle(deathDustType, transform.position);
                if (deathGolem != null)
                {
                    Instantiate(deathGolem, gameObject.transform.position, gameObject.transform.rotation);
                }
                break;
            }
        }
    }
}
