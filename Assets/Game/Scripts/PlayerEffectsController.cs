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
    /// Mushroom trip state is not serialized; clear runtime state and post-process after loading a save.
    /// </summary>
    public void ResetMushroomTripAfterLoad()
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
