using UnityEngine;

public class Bedroll : UUObject
{
    // The dreams of the sleep, which Sleeping plays: they hang here, on the bedroll's prefab.
    public CutscenePlayer[] dreams;

    public override EEquipAction Equip()
    {
        Sleeping.TryOnBedroll();
        return EEquipAction.Nothing;
    }

    public override string GetUseText()
    {
        return "Sleep";
    }
}
