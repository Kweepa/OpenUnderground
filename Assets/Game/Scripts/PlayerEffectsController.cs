using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

public class PlayerEffectsController : MonoBehaviour
{
    [Header("Effect Timings")]
    [Tooltip("How long it takes for the drunk intensity to catch up to the player's drunkenness level (in seconds).")]
    public float drunkInterpolationTime = 2.0f;

    [Tooltip("How long it takes for the mushroom to 'kick in' after being eaten.")]
    public float druggedDigestionTime = 3.0f;
    
    [Tooltip("How much time (in seconds) is removed from the digestion phase if a second mushroom is eaten.")]
    public float druggedDigestionSpeedUp = 1.5f;

    [Tooltip("How long it takes for the mushroom effect to fade in completely.")]
    public float druggedFadeInTime = 1.0f;
    
    [Tooltip("The total duration of the mushroom trip before it starts to fade out.")]
    public float druggedDuration = 30.0f;

    [Tooltip("How long it takes for the mushroom effect to fade out completely.")]
    public float druggedFadeOutTime = 20.0f;

    // --- State Machine for Drugged Effect ---
    private enum DruggedState
    {
        Inactive,
        Digesting,
        FadingIn,
        Holding,
        FadingOut
    }
    private DruggedState currentDruggedState = DruggedState.Inactive;
    private float druggedStateTimer = 0f;

    // --- Private references ---
    private PostProcessVolume volume;
    private DrunkEffect drunkEffect;
    private DruggedEffect druggedEffect;

    private float currentDrunkIntensity = 0f;
    private float drunkInterpolationSpeed;

    void Awake()
    {
        volume = GetComponentInChildren<PostProcessVolume>();
        if (volume == null)
        {
            Debug.LogError("PlayerEffectsController could not find a PostProcessVolume on any child objects!");
        }
    }

    void Start()
    {
        if (volume != null)
        {
            volume.profile.TryGetSettings<DrunkEffect>(out drunkEffect);
            volume.profile.TryGetSettings<DruggedEffect>(out druggedEffect);
        }
        drunkInterpolationSpeed = 1.0f / drunkInterpolationTime;
    }

    void Update()
    {
        HandleDrunkEffect();
        HandleDruggedEffect(); 
    }

    private void HandleDrunkEffect()
    {
        if (drunkEffect == null) return;
        bool isDrunk = PlayerData.sData.drunkenness > 0;
        drunkEffect.enabled.value = isDrunk;
        if (isDrunk)
        {
            float targetDrunkIntensity = PlayerData.sData.drunkenness / 30f;
            currentDrunkIntensity = Mathf.MoveTowards(currentDrunkIntensity, targetDrunkIntensity, drunkInterpolationSpeed * Time.deltaTime);
            drunkEffect.masterIntensity.value = currentDrunkIntensity;
        }
        else
        {
            currentDrunkIntensity = 0f;
            drunkEffect.masterIntensity.value = 0f;
        }
    }

    private void HandleDruggedEffect()
    {
        if (druggedEffect == null) return;

        if (currentDruggedState == DruggedState.Inactive)
        {
            druggedEffect.enabled.value = false;
            druggedEffect.masterIntensity.value = 0f;
            return;
        }

        // Set every frame rather than once, so that a trip put back by RestoreTrip() shows even
        // when the effect was not yet found at that moment.
        if (currentDruggedState != DruggedState.Digesting)
        {
            druggedEffect.enabled.value = true;
        }

        druggedStateTimer += Time.deltaTime;

        switch (currentDruggedState)
        {
            case DruggedState.Digesting:
                // Wait for the digestion time to pass
                if (druggedStateTimer >= druggedDigestionTime)
                {
                    // Digestion is over, start the visual effect
                    druggedEffect.enabled.value = true;
                    currentDruggedState = DruggedState.FadingIn;
                    druggedStateTimer = 0f;
                }
                break;
            
            case DruggedState.FadingIn:
                float fadeInIntensity = Mathf.Clamp01(druggedStateTimer / druggedFadeInTime);
                druggedEffect.masterIntensity.value = fadeInIntensity;
                if (druggedStateTimer >= druggedFadeInTime)
                {
                    currentDruggedState = DruggedState.Holding;
                    druggedStateTimer = 0f;
                }
                break;

            case DruggedState.Holding:
                druggedEffect.masterIntensity.value = 1f;
                float holdDuration = druggedDuration - druggedFadeInTime;
                if (druggedStateTimer >= holdDuration)
                {
                    currentDruggedState = DruggedState.FadingOut;
                    druggedStateTimer = 0f;
                }
                break;

            case DruggedState.FadingOut:
                float fadeOutIntensity = 1.0f - Mathf.Clamp01(druggedStateTimer / druggedFadeOutTime);
                druggedEffect.masterIntensity.value = fadeOutIntensity;
                if (druggedStateTimer >= druggedFadeOutTime)
                {
                    druggedEffect.enabled.value = false;
                    currentDruggedState = DruggedState.Inactive;
                    druggedStateTimer = 0f;
                }
                break;
        }
    }
    
    public void StartMushroomTrip()
    {
        if (druggedEffect == null)
        {
            Debug.LogWarning("Drugged effect is not assigned on the Post-Process Profile.");
            return;
        }

        switch (currentDruggedState)
        {
            case DruggedState.Digesting:
                // Speed up the remaining digestion time.
                druggedStateTimer += druggedDigestionSpeedUp;
                break;

            case DruggedState.FadingIn:
                // Currently fading in, so do nothing and let it continue.
                break;

            case DruggedState.Holding:
                // Already at peak intensity, so just reset the hold timer to extend the trip.
                druggedStateTimer = 0f;
                break;

            case DruggedState.FadingOut:
                // The effect is fading out, so we reverse it and start fading back in.
                float currentIntensity = druggedEffect.masterIntensity.value;
                currentDruggedState = DruggedState.FadingIn;
                druggedStateTimer = currentIntensity * druggedFadeInTime;
                break;
            
            case DruggedState.Inactive:
                // The effect is off, so start the new 'Digesting' phase.
                currentDruggedState = DruggedState.Digesting;
                druggedStateTimer = 0f;
                break;
        }
    }

    /// <summary>
    /// Starts the trip with no digestion, for the Hallucination enchantment of a potion or a
    /// scroll. The original sets its counter to 3 world ticks, 42 to 63 seconds (UW.EXE 0x34619,
    /// 0x2ad4d); the fade in, the full trip and the fade out here come to about 50.
    /// </summary>
    public void StartHallucination()
    {
        if (druggedEffect == null)
        {
            Debug.LogWarning("Drugged effect is not assigned on the Post-Process Profile.");
            return;
        }

        switch (currentDruggedState)
        {
            case DruggedState.FadingIn:
                // Already on its way to full intensity.
                break;

            case DruggedState.Holding:
                // At full intensity: the counter goes back to 3, so the trip starts over.
                druggedStateTimer = 0f;
                break;

            case DruggedState.FadingOut:
                // Fade back in from where the effect is now.
                currentDruggedState = DruggedState.FadingIn;
                druggedStateTimer = druggedEffect.masterIntensity.value * druggedFadeInTime;
                break;

            default:
                // Inactive, or a mushroom still digesting: the effect starts now.
                druggedEffect.enabled.value = true;
                currentDruggedState = DruggedState.FadingIn;
                druggedStateTimer = 0f;
                break;
        }
    }

    /// <summary>The trip's stage, 0 for none, and the seconds spent in it: what a save keeps.</summary>
    public int TripStage => (int)currentDruggedState;
    public float TripTimer => druggedStateTimer;

    /// <summary>
    /// Puts back the trip a save was written with, at the same stage and second, so that it ends
    /// when it would have. The original saves its hallucination counter in PLAYER.DAT and shows the
    /// effect again after a load (UW.EXE 0x788a9, at 0x7899b). Stage 0, which a save without a trip
    /// or an older save gives, ends the one running.
    /// </summary>
    public void RestoreTrip(int stage, float timer)
    {
        EndMushroomTrip();
        if (stage > (int)DruggedState.Inactive && stage <= (int)DruggedState.FadingOut)
        {
            currentDruggedState = (DruggedState)stage;
            druggedStateTimer = timer;
        }
    }

    /// <summary>
    /// Ends the mushroom trip at every sleep, which clears the original's hallucination counter
    /// (UW.EXE 0x8205d), and before a load puts back the one saved.
    /// </summary>
    public void EndMushroomTrip()
    {
        currentDruggedState = DruggedState.Inactive;
        druggedStateTimer = 0f;
        if (druggedEffect != null)
        {
            druggedEffect.enabled.value = false;
            druggedEffect.masterIntensity.value = 0f;
        }
    }
}
