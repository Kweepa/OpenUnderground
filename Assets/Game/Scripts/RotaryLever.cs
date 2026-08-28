using UnityEngine;

public class RotaryLever : UUObject
{
    public AudioClip switchClip;
    public Transform pivot;

    public override void PostLoadInitialize(bool restoredFromSave = false)
    {
        // Call base to load names and other UUObject initialization
        base.PostLoadInitialize(restoredFromSave);
        
        SnapToWallUsingTileBoundaries();

        pivot.transform.localEulerAngles = new Vector3(0, 0, -45 * flags);
    }

    public override void TryInteract(UUObject originator, UUObject sender, EAction action)
    {
        UUObject obj = LevelLoader.GetObj(link);
        if (obj != null)
        {
            flags = (flags + 1) % 8;
            pivot.transform.localEulerAngles = new Vector3(0, 0, -45 * flags);
            
            obj.TryInteract(originator, this, EAction.Use);
            Utils.PlayClip(switchClip, transform.position);
        }
        else
        {
            Debug.LogFormat("Rotary lever '{0}' refers to non-existent object {1}", name, link);
        }
    }
}
