using UnityEngine;

public class RotwormStew : Food
{
    [SerializeField]
    private int foodValue = 64;

    public override EEquipAction Equip()
    {
        return EatSolidWithMessage(235, foodValue);
    }

    public override string GetUseText()
    {
        return "Eat";
    }
}
