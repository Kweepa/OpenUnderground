using System;
using UnityEngine;

public class CrystalBall : UUObject
{
    public AudioClip clip;
    public GameObject particleEffectHierarchy;
    public GameObject explosionPrefab;
    
    public void OnEnable()
    {
        // Enable particle effect hierarchy for crystal ball on level 7
        if (LevelLoader.sLevelLoader.loadedLevel == 7 && objectIndex == 820)
        {
            if (particleEffectHierarchy != null)
            {
                particleEffectHierarchy.SetActive(true);
            }
        }
    }

    public override void TryInteract(UUObject originator, UUObject sender, EAction action)
    {
        if (action == EAction.Use)
        {
            if (LevelLoader.sLevelLoader.loadedLevel == 1)
            {
                #if false
                Utils.PlayClip2d(clip, false);
                #endif
                Messages.Add(StringLoader.GetString(9, 1));
            }
            else
            {
                // redirect to look
                TryChainInteraction(EAction.Look);
            }
        }
    }
}
