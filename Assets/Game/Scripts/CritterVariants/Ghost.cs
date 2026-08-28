using UnityEngine;

namespace Game.Scripts.CritterVariants
{
    public class Ghost : Critter
    {
        public SkinnedMeshRenderer body;
        public Material[] skinMaterials;
        public AudioSource hoverHum;
        private float hoverHumBaseVolume = 1f;
        
        private float eyeColorTime;
        private Color eyeColorSource;
        private Color eyeColorTarget;
        private float eyeColorSpeed;

        private float deathDissolve;
        private float deathDissolveSpeed;
        private static readonly int eyeColorPropertyId = Shader.PropertyToID("_Eye_Color");
        private static readonly int dissolvePropertyId = Shader.PropertyToID("_Dissolve");

        public override void PostLoadInitialize(bool restoredFromSave = false)
        {
            base.PostLoadInitialize(restoredFromSave);

            if (body != null)
            {
                switch (type)
                {
                case EObjectType.GhostA:
                    body.material = skinMaterials[0];
                    break;
                case EObjectType.GhostB:
                    body.material = skinMaterials[1];
                    break;
                case EObjectType.GhostC:
                    body.material = skinMaterials[2];
                    break;
                case EObjectType.DireGhost:
                    body.material = skinMaterials[3];
                    break;
                }

                body.material.SetColor(eyeColorPropertyId, Color.black);
            }

            if (hoverHum != null)
            {
                hoverHumBaseVolume = hoverHum.volume;
            }
        }

        protected override void SetState(EState newState)
        {
            base.SetState(newState);
            
            switch (newState)
            {
            case EState.Attack:
                if (body != null)
                {
                    body.material.SetColor(eyeColorPropertyId, Color.black);
                }
                break;
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

        public override void Update()
        {
            base.Update();

            if (hoverHum != null && hoverHum.isPlaying)
            {
                hoverHum.volume = hoverHumBaseVolume * PlayerInput.EffectsVolume;
            }

            if (body != null)
            {
                if (eyeColorSpeed > 0.0f && eyeColorTime < 1.0f)
                {
                    eyeColorTime = Mathf.Clamp01(eyeColorTime + Time.deltaTime * eyeColorSpeed);
                    Color c = Color.Lerp(eyeColorSource, eyeColorTarget, eyeColorTime);
                    body.material.SetColor(eyeColorPropertyId, c);
                }

                if (deathDissolveSpeed > 0.0f && deathDissolve > -1.0f)
                {
                    deathDissolve = Mathf.Clamp(deathDissolve - Time.deltaTime * deathDissolveSpeed, -1.0f, 2.0f);
                    body.material.SetFloat(dissolvePropertyId, 2.0f * deathDissolve);
                }
            }
        }

        public void BrightenEyesEvent()
        {
            eyeColorSource = Color.black;
            eyeColorTarget = Color.yellow;
            eyeColorSpeed = 1.0f;
            eyeColorTime = 0.0f;
        }

        public void DarkenEyesEvent()
        {
            eyeColorSource = Color.yellow;
            eyeColorTarget = Color.black;
            eyeColorSpeed = 3.0f;
            eyeColorTime = 0.0f;
        }

        public void DeathFadeEvent()
        {
            deathDissolve = 0.5f;
            deathDissolveSpeed = 0.5f;
        }
    }
}
