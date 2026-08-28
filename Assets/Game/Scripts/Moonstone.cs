using UnityEngine;

public class Moonstone : UUObject
{
    public override void TryInteract(UUObject originator, UUObject sender, EAction action)
    {
        base.TryInteract(originator, sender, action);
        if (action == EAction.Use)
        {
            PlayerData.sData.moonstoneDropped = false;
        }
    }

    public override void Update()
    {
        base.Update();

        if (!PlayerData.sData.moonstoneDropped)
        {
            Rigidbody rb = gameObject.GetComponent<Rigidbody>();
            if (rb != null && rb.IsSleeping())
            {
                PlayerData.sData.moonstoneDropped = true;
                PlayerData.sData.moonstoneDroppedLevel = LevelLoader.sLevelLoader.loadedLevel;
                PlayerData.sData.moonstoneDroppedPosition = rb.position;
            }
        }
    }
}
