using UnityEngine;

/// <summary>
/// Component that adjusts the volume of a sibling AudioSource based on PVS (Potentially Visible Set) visibility.
/// When the object's tile is visible according to PVS, volume is set to full volume.
/// When not visible, volume is reduced to a lower level.
/// Only works for static objects (eg campfires, fountains, shrines)
/// </summary>
public class PVSAudioVolume : MonoBehaviour
{
    [Tooltip("The AudioSource to control. If null, will search for one on the same GameObject.")]
    public AudioSource audioSource;
    
    [Tooltip("Volume when visible via PVS (0.0 to 1.0)")]
    [Range(0f, 1f)]
    public float visibleVolume = 1.0f;
    
    [Tooltip("Volume when not visible via PVS (0.0 to 1.0)")]
    [Range(0f, 1f)]
    public float hiddenVolume = 0.2f;
    
    [Tooltip("Speed at which volume transitions (volume units per second)")]
    public float transitionSpeed = 1.0f;

    private UUObject uuObj;

    private void Awake()
    {
        // Find the AudioSource on the same gameobject
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        // Get UUObject on the root gameobject to access initialTile
        uuObj = transform.root.GetComponent<UUObject>();
    }

    private void Update()
    {
        if (audioSource == null || uuObj == null || uuObj.initialTile == null)
        {
            return;
        }

        // Check PVS visibility
        bool isVisible = LevelLoader.GetLevel().pvs.IsVisible(uuObj.initialTile);
        float targetVolume = (isVisible ? visibleVolume : hiddenVolume) * PlayerInput.EffectsVolume;

        // Smoothly transition volume
        audioSource.volume = Mathf.MoveTowards(audioSource.volume, targetVolume, transitionSpeed * Time.deltaTime);
    }
}
