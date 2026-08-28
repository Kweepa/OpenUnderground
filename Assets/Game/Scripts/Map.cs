
public class Map : UUObject
{
    public bool popUpMap;

    protected override int GetQualityOffset()
    {
        return 12; // the same as scrolls
    }

    public override EEquipAction Equip()
    {
        popUpMap = true;
        return EEquipAction.Nothing;
    }

    public override string GetUseText()
    {
        return "Open";
    }
}
