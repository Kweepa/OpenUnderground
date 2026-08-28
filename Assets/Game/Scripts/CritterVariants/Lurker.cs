using UnityEngine;

namespace Game.Scripts.CritterVariants
{
    public class Lurker : Critter
    {
        public GameObject leftArm;
        public GameObject rightArm;
        public float scale = 0.5f;
        public MeshRenderer waterRipple;
        public GameObject jaw;

        private float deadTime;
        private float splashTime;

        public override void PostLoadInitialize(bool restoredFromSave = false)
        {
            base.PostLoadInitialize(restoredFromSave);
            
            transform.localScale = new Vector3(scale, scale, scale);
        }

        public void StartSwipeEvent(int arm)
        {
            GameObject armObj = arm == 0 ? leftArm : rightArm;
            cachedVisibilityGroup.SetGameObjectState(armObj, true);
        }

        public void StopSwipeEvent(int arm)
        {
            GameObject armObj = arm == 0 ? leftArm : rightArm;
            cachedVisibilityGroup.SetGameObjectState(armObj, false);
        }

        protected override void SetState(EState newState)
        {
            if (state == EState.Die && newState == EState.Dead)
            {
                ParticleSpawner.SpawnParticle(EParticleType.LurkerBubbles, transform.position);
            }
            if (state == EState.Attack)
            {
                cachedVisibilityGroup.SetGameObjectState(leftArm, false);
                cachedVisibilityGroup.SetGameObjectState(rightArm, false);
            }
            base.SetState(newState);
        }

        private Vector3 swimmerVelocity;
        private static readonly int clipPropertyId = Shader.PropertyToID("_Clip");

        public override void Update()
        {
            swimmerDesiredMotion = Vector3.zero;

            base.Update();

            if (Time.deltaTime > 1e-7f && cachedAnimator.speed > 0)
            {
                swimmerVelocity = Utils.DampedApproach(swimmerVelocity, swimmerDesiredMotion / Time.deltaTime, 1.0f);
                if (cachedCharacterController.enabled)
                {
                    cachedCharacterController.Move(swimmerVelocity * Time.deltaTime);
                }
                
                if (waterRipple != null)
                {
                    float rippleOpacity = swimmerVelocity.magnitude / 2.0f;

                    if (state == EState.Attack || state == EState.Flinch)
                    {
                        splashTime += Time.deltaTime;
                    }
                    else
                    {
                        splashTime -= Time.deltaTime;
                    }
                    splashTime = Mathf.Clamp01(splashTime);
                    rippleOpacity += splashTime;
                    rippleOpacity = Mathf.Clamp01(rippleOpacity);

                    if (state == EState.Die || state == EState.Dead)
                    {
                        deadTime += Time.deltaTime;
                        deadTime = Mathf.Clamp01(deadTime);
                    }

                    float rippleTarget = Mathf.Lerp(0.75f, 1.0f, deadTime);
                    rippleOpacity = Mathf.Lerp(rippleTarget, 0.45f, rippleOpacity);

                    waterRipple.material.SetFloat(clipPropertyId, rippleOpacity);

                    if (jaw != null)
                    {
                        Vector3 pos = jaw.transform.position;
                        pos.y = transform.position.y + 0.01f;
                        waterRipple.transform.position = pos;
                    }
                }
            }
        }
    }
}
