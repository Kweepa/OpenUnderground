using UnityEngine;

namespace Game.Scripts.CritterVariants
{
    public class Caster : Critter
    {
        // base class of Mage and Sorceress to add particle effects when firing projectile

        public GameObject electricEffect;
        public GameObject leftHand;
        public GameObject rightHand;
        public GameObject knife;

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
            switch (state)
            {
            case EState.ProjectileAttack:
                // turn off effects
                EffectsEvent(0);
                break;
            case EState.Attack:
                // hide knife
                ShowKnifeEvent(0);
                break;
            }

            base.SetState(newState);
        }

        protected override Vector3 GetProjectileLaunchPosition(bool estimate)
        {
            if (estimate)
            {
                return transform.position + 0.5f * Vector3.up;
            }
            switch (projectileType)
            {
            case 0:
                return rightHand.transform.position;
            case 1:
            case 2:
                return 0.5f * (leftHand.transform.position + rightHand.transform.position);
            case 3:
                return knife.transform.position;
            }
            return transform.position + 0.5f * Vector3.up;
        }

        public override void Update()
        {
            base.Update();

            if (electricEffect != null && leftHand != null && rightHand != null && effectsActive)
            {
                electricEffect.transform.position = 0.5f * (leftHand.transform.position + rightHand.transform.position);
            }
        }

        private int projectileType;

        private void ProjectileTypeEvent(int _projectileType)
        {
            projectileType = _projectileType;
        }

        protected override EObjectType GetProjectileType()
        {
            switch (projectileType)
            {
            case 1:
                // one and two handed fireball
                return EObjectType.Fireball;
            case 2:
                return EObjectType.LightningBolt;
            case 3:
                return EObjectType.Knife;
            }

            return 0;
        }

        protected override void LaunchProjectile()
        {
            Magic.ESpell spell = Magic.ESpell.Curse;
            switch (GetProjectileType())
            {
            case EObjectType.Fireball:
                spell = Magic.ESpell.Fireball;
                break;
            case EObjectType.LightningBolt:
                spell = Magic.ESpell.Lightning;
                break;
            }

            if (spell != Magic.ESpell.Curse)
            {
                Utils.PlayClipOccluded(Magic.sMagic.castSpellSound[(int)spell], transform.position);
            }
            
            base.LaunchProjectile();
        }

        private void ShowKnifeEvent(int active)
        {
            cachedVisibilityGroup.SetGameObjectState(knife, active > 0);
        }

        private void ThrowKnifeEvent()
        {
            cachedVisibilityGroup.SetGameObjectState(knife, false);

            LaunchProjectile();
        }
    }
}
