using UnityEngine;

/// <summary>
/// A category of sounds for a specific critter action.
/// Contains multiple clips for randomization.
/// </summary>
[System.Serializable]
public class CritterSoundCategory
{
    public CritterSoundType soundType;
    public AudioClip[] clips = new AudioClip[0];
    [Range(0f, 2f)]
    public float volume = 1.0f;
    [Range(0.5f, 2.0f)]
    public float pitch = 1.0f;
    [Tooltip("Random pitch variation range. When playing sounds, pitch will be randomly varied by up to this amount in either direction.")]
    [Range(0f, 1f)]
    public float pitchVariation = 0f;
    [Range(0.0f, 1.0f)]
    public float chance = 1.0f;
    
    /// <summary>
    /// Get a random clip from this category.
    /// Returns null if no clips available.
    /// </summary>
    public AudioClip GetRandomClip()
    {
        if (clips == null || clips.Length == 0)
            return null;
        
        return clips[Random.Range(0, clips.Length)];
    }
}
