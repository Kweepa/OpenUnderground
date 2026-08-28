using UnityEngine;

/// <summary>
/// ScriptableObject asset containing critter sound configuration.
/// Allows easy editing of critter sounds without opening prefabs.
/// </summary>
[CreateAssetMenu(fileName = "CritterSoundData", menuName = "Game/Critter Sound Data", order = 1)]
public class CritterSoundData : ScriptableObject
{
    [PropertyLabelArray("soundType")]
    public CritterSoundCategory[] sounds = new CritterSoundCategory[0];
}

