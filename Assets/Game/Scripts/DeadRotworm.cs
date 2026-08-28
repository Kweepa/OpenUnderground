using UnityEngine;

public class DeadRotworm : Food
{
    [SerializeField]
    private int foodValue = 24;

    public override EEquipAction Equip()
    {
        return EatSolidWithMessage(234, foodValue);
    }

    public override string GetUseText()
    {
        return "Eat";
    }
}
