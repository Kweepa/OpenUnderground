using UnityEngine;

namespace Game.Scripts.CritterVariants
{
    public class Skeleton : Critter
    {
        protected override void SetState(EState newState)
        {
            base.SetState(newState);

            switch (newState)
            {
            case EState.Die:
                if (ParticleSpawner.sParticleSpawner != null)
                {
                    ParticleSpawner.SpawnParticle(EParticleType.SkeletonDust, transform.position);
                }
                break;
            }
        }
    }
}
