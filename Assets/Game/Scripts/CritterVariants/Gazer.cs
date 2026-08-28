using UnityEngine;

namespace Game.Scripts.CritterVariants
{
    public class Gazer : Flyer
    {
        private float eyeColorTime = 1.0f;
        public SkinnedMeshRenderer eyeMesh;
        public AudioSource hoverHum;
        private float hoverHumBaseVolume = 1f;
        private static readonly int colorPropertyId = Shader.PropertyToID("_Color");

        public override void PostLoadInitialize(bool restoredFromSave = false)
        {
            base.PostLoadInitialize(restoredFromSave);
            
            if (hoverHum != null)
            {
                hoverHumBaseVolume = hoverHum.volume;
            }

            eyeMesh.material.SetColor(colorPropertyId, Color.green);

            movementType = EMovementType.Flying;
        }

        protected override void SetState(EState newState)
        {
            switch (state)
            {
            case EState.ProjectileAttack:
                TurnEyeGreen();
                break;
            }

            base.SetState(newState);

            switch (newState)
            {
            case EState.Idle:
                if (hoverHum != null)
                {
                    hoverHum.Play();
                }
                break;
            case EState.Dead:
                if (hoverHum != null)
                {
                    hoverHum.Stop();
                }
                break;
            }
        }

        protected override Vector3 GetProjectileLaunchPosition(bool estimated)
        {
            // don't call the base
            return eyeMesh.bounds.center;
        }

        public override void Update()
        {
            base.Update();

            if (hoverHum != null && hoverHum.isPlaying)
            {
                hoverHum.volume = hoverHumBaseVolume * PlayerInput.EffectsVolume;
            }

            if (eyeColorTime < 1.0f)
            {
                eyeColorTime = Mathf.Clamp01(eyeColorTime + Time.deltaTime);
                Color c = eyeColorTime < 0.5f
                    ? Color.Lerp(Color.green, Color.yellow, 2.0f * eyeColorTime)
                    : Color.Lerp(Color.yellow, Color.red, 2.0f * (eyeColorTime - 0.5f));
                eyeMesh.material.SetColor(colorPropertyId, c);
            }
        }

        public void TurnEyeRed()
        {
            eyeColorTime = 0.0f;
        }

        public void TurnEyeGreen()
        {
            eyeMesh.material.SetColor(colorPropertyId, Color.green);
            eyeColorTime = 1.0f;
        }
    }
}
