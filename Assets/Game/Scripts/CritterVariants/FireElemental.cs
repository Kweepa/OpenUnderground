using UnityEngine;

public enum EFireSoundEnvelope
{
    Alert,
    Flinch,
    Attack,
    ProjectileAttack,
    Die
}

[System.Serializable]
public struct FireSoundEnvelope
{
    public float attackTime;
    public float sustainVolume;
    public float sustainTime;
    public float sustainStartPitch;
    public float sustainEndPitch;
    public float releaseTime;
}

namespace Game.Scripts.CritterVariants
{
    public class FireElemental : Critter
    {
        [EnumNamedArray(typeof(EFireSoundEnvelope))]
        public FireSoundEnvelope[] envelopes;

        public float idleFireVolume = 0.05f;
        public float moveFireVolume = 0.15f;

        public AudioSource loopedFireSound;

        private float baseFireVolume;
        private const float basePitch = 1.0f;
        
        // Active envelope state tracking
        private EFireSoundEnvelope? activeEnvelope;
        private enum EnvelopePhase
        {
            Attack,
            Sustain,
            Release,
            None
        }
        private EnvelopePhase currentPhase = EnvelopePhase.None;
        private float phaseElapsedTime;
        private float phaseStartVolume;
        private float phaseStartPitch;
        
        private void StartFireSoundEnvelope(EFireSoundEnvelope envelope)
        {
            if (envelopes == null || envelopes.Length == 0)
                return;
                
            int envelopeIndex = (int)envelope;
            if (envelopeIndex < 0 || envelopeIndex >= envelopes.Length)
                return;
                
            activeEnvelope = envelope;
            phaseElapsedTime = 0.0f;
            
            FireSoundEnvelope env = envelopes[envelopeIndex];
            
            // Only start if any time is valid
            if (env.attackTime > 0.0f)
            {
                currentPhase = EnvelopePhase.Attack;
                phaseStartVolume = baseFireVolume;
                phaseStartPitch = basePitch;
            }
            else if (env.sustainTime > 0.0f)
            {
                currentPhase = EnvelopePhase.Sustain;
                phaseStartVolume = env.sustainVolume;
                phaseStartPitch = env.sustainStartPitch;
            }
            else if (env.releaseTime > 0.0f)
            {
                currentPhase = EnvelopePhase.Release;
                phaseStartVolume = env.sustainVolume;
                phaseStartPitch = env.sustainEndPitch;
            }
            else
            {
                activeEnvelope = null;
            }
        }

        private void UpdateFireSoundVolume()
        {
            if (loopedFireSound == null)
                return;
                
            float targetVolume = baseFireVolume;
            float targetPitch = basePitch;
            
            // Process active envelope if one is active
            if (activeEnvelope.HasValue && envelopes != null && envelopes.Length > 0)
            {
                int envelopeIndex = (int)activeEnvelope.Value;
                if (envelopeIndex >= 0 && envelopeIndex < envelopes.Length)
                {
                    FireSoundEnvelope env = envelopes[envelopeIndex];
                    
                    phaseElapsedTime += Time.deltaTime;
                    
                    switch (currentPhase)
                    {
                    case EnvelopePhase.Attack:
                        {
                            float t = Mathf.Clamp01(phaseElapsedTime / env.attackTime);
                            targetVolume = Mathf.Lerp(phaseStartVolume, env.sustainVolume, t);
                            // Pitch stays at base during attack phase
                            targetPitch = basePitch;
                            
                            if (t >= 1.0f)
                            {
                                // Transition to sustain phase
                                if (env.sustainTime > 0.0f)
                                {
                                    currentPhase = EnvelopePhase.Sustain;
                                    phaseElapsedTime = 0.0f;
                                    phaseStartPitch = env.sustainStartPitch;
                                }
                                else if (env.releaseTime > 0.0f)
                                {
                                    // No sustain, go directly to release
                                    currentPhase = EnvelopePhase.Release;
                                    phaseElapsedTime = 0.0f;
                                    phaseStartVolume = env.sustainVolume;
                                    phaseStartPitch = env.sustainEndPitch;
                                }
                                else
                                {
                                    // No release either, envelope complete
                                    activeEnvelope = null;
                                    targetVolume = baseFireVolume;
                                    targetPitch = basePitch;
                                }
                            }
                        }
                        break;
                        
                    case EnvelopePhase.Sustain:
                        targetVolume = env.sustainVolume;
                        
                        // Lerp pitch from start to end during sustain
                        if (env.sustainTime > 0.0f)
                        {
                            float t = Mathf.Clamp01(phaseElapsedTime / env.sustainTime);
                            targetPitch = Mathf.Lerp(env.sustainStartPitch, env.sustainEndPitch, t);
                        }
                        else
                        {
                            targetPitch = env.sustainStartPitch;
                        }
                        
                        if (phaseElapsedTime >= env.sustainTime)
                        {
                            // Transition to release phase
                            if (env.releaseTime > 0.0f)
                            {
                                currentPhase = EnvelopePhase.Release;
                                phaseElapsedTime = 0.0f;
                                phaseStartVolume = env.sustainVolume;
                                phaseStartPitch = env.sustainEndPitch;
                            }
                            else
                            {
                                // No release, envelope complete
                                activeEnvelope = null;
                                targetVolume = baseFireVolume;
                                targetPitch = basePitch;
                            }
                        }
                        break;
                        
                    case EnvelopePhase.Release:
                        {
                            float t = Mathf.Clamp01(phaseElapsedTime / env.releaseTime);
                            targetVolume = Mathf.Lerp(phaseStartVolume, baseFireVolume, t);
                            // Lerp pitch back to base (1.0) during release
                            targetPitch = Mathf.Lerp(phaseStartPitch, basePitch, t);
                            
                            if (t >= 1.0f)
                            {
                                // Release complete, envelope finished
                                activeEnvelope = null;
                                currentPhase = EnvelopePhase.None;
                                targetPitch = basePitch;
                            }
                        }
                        break;
                    }
                }
            }
            
            loopedFireSound.volume = targetVolume * PlayerInput.EffectsVolume;
            loopedFireSound.pitch = targetPitch;
        }

        protected override void SetState(EState newState)
        {
            base.SetState(newState);

            baseFireVolume = idleFireVolume;

            switch (newState)
            {
            case EState.Die:
                baseFireVolume = 0.0f;
                StartFireSoundEnvelope(EFireSoundEnvelope.Die);
                break;
            case EState.Dead:
                baseFireVolume = 0.0f;
                break;
            case EState.Flinch:
                StartFireSoundEnvelope(EFireSoundEnvelope.Flinch);
                break;
            case EState.Attack:
                StartFireSoundEnvelope(EFireSoundEnvelope.Attack);
                break;
            case EState.ProjectileAttack:
                StartFireSoundEnvelope(EFireSoundEnvelope.ProjectileAttack);
                break;
            case EState.Approach:
            case EState.Flee:
            case EState.Wander:
                baseFireVolume = moveFireVolume;
                break;
            }
        }

        public override void Update()
        {
            base.Update();

            UpdateFireSoundVolume();
        }

        public override void PostLoadInitialize(bool restoredFromSave = false)
        {
            base.PostLoadInitialize(restoredFromSave);

            transform.localScale = new Vector3(1.0f, 1.5f, 1.5f);
            
            // Initialize base fire volume based on current state
            baseFireVolume = idleFireVolume;
        }

        protected override void PlayAlertSound()
        {
            base.PlayAlertSound();
            
            // Increase the volume of the flames (attack, sustain, release)
            StartFireSoundEnvelope(EFireSoundEnvelope.Alert);
        }
        
        private void DisableEmittersEvent()
        {
            ParticleSystem[] systems = GetComponentsInChildren<ParticleSystem>();

            foreach (ParticleSystem system in GetComponentsInChildren<ParticleSystem>())
            {
                if (system.isEmitting)
                {
                    // get a proxy and modify it
                    var emission = system.emission;
                    emission.enabled = false;
                }
            }
        }
    }
}
