using UnityEngine;

namespace Game.Scripts.CritterVariants
{
    public class ShadowBeast : Critter
    {
        public float dissolveMin;
        public float dissolveMax;
        public float interiorMin;
        public float interiorMax;

        public SkinnedMeshRenderer bodyMesh;
        public SkinnedMeshRenderer eyesMesh;

        private float bodyFadeVal;
        private bool bodyFadeIn;

        private float eyeFadeVal;
        private bool eyeFadeIn;

        private float deathDissolve;
        private float deathDissolveSpeed;

        protected override void SetState(EState newState)
        {
            base.SetState(newState);
            switch (newState)
            {
            case EState.Idle:
            case EState.CombatIdle:
                eyeFadeIn = false;
                bodyFadeIn = false;
                break;
            case EState.Approach:
            case EState.Attack:
            case EState.CombatTurn:
                eyeFadeIn = true;
                bodyFadeIn = true;
                break;
            case EState.Die:
                eyeFadeIn = false;
                bodyFadeIn = true;
                break;
            }
        }

        protected override string[] GetAttackCandidateStates()
        {
            return new[] { "Attack_Left", "Attack_Right" };
        }

        public override void Update()
        {
            base.Update();

            // Handle standard body fade in/out
            bodyFadeVal = Mathf.MoveTowards(bodyFadeVal, bodyFadeIn ? 1.0f : 0.0f, Time.deltaTime);
            bodyMesh.material.SetFloat("_Dissolve", Mathf.Lerp(dissolveMin, dissolveMax, bodyFadeVal));
            bodyMesh.material.SetFloat("_InteriorSpread", Mathf.Lerp(interiorMin, interiorMax, bodyFadeVal));

            // Handle eye fade in/out
            eyeFadeVal = Mathf.MoveTowards(eyeFadeVal, eyeFadeIn ? 1.0f : 0.0f, Time.deltaTime);
            eyesMesh.material.SetFloat("_Dissolve", Mathf.Lerp(dissolveMin, dissolveMax, eyeFadeVal));

            // Handle death dissolve effect
            deathDissolve = Mathf.MoveTowards(deathDissolve, 1.0f, deathDissolveSpeed * Time.deltaTime);
            bodyMesh.material.SetFloat("_DeathDissolve", deathDissolve);
        }

        public void DeathDissolveEvent(float dissolveSpeed)
        {
            // Start the death dissolve process
            deathDissolveSpeed = dissolveSpeed;
        }
    }
}
