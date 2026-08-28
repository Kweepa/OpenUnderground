using UnityEngine;

public class Arial : UUObject
{
    public static Arial sArial;

    public void OnEnable()
    {
        sArial = this;
    }

    public void OnDisable()
    {
        sArial = null;
    }

    public override string GetLookName()
    {
        return StringLoader.GetString(10, 143);
    }

    public override void TryInteract(UUObject originator, UUObject sender, EAction action)
    {
        Messages.Add(7, 1);
    }
}
