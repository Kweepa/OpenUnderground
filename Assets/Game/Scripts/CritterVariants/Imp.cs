using UnityEngine;

namespace Game.Scripts.CritterVariants
{
    public class Imp : Flyer
    {
        // override just to add particle effects when firing projectile

        public GameObject electricEffect;
        public GameObject leftHand;
        public GameObject rightHand;

        private bool effectsActive;

        private void EffectsEvent(int active)
        {
            if (electricEffect != null)
            {
                effectsActive = active > 0;
                cachedVisibilityGroup.SetGameObjectState(electricEffect, effectsActive);
            }
        }

        protected override void SetState(EState newState)
        {
            if (state == EState.ProjectileAttack)
            {
                // turn off effects
                EffectsEvent(0);
            }
            
            base.SetState(newState);
        }

        protected override Vector3 GetProjectileLaunchPosition(bool estimate)
        {
            if (estimate)
            {
                return transform.position + 0.8f * Vector3.up;
            }
            return 0.5f * (leftHand.transform.position + rightHand.transform.position);
        }

        public override void Update()
        {
            base.Update();

            if (electricEffect != null && leftHand != null && rightHand != null && effectsActive)
            {
                electricEffect.transform.position = 0.5f * (leftHand.transform.position + rightHand.transform.position);
            }
        }
    }
}
