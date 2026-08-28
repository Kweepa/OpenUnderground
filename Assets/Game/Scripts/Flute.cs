
public class Flute : UUObject
{
    public FluteGUI fluteGUI;

    public override EEquipAction Equip()
    {
        FluteGUI fg = Instantiate(fluteGUI);
        fg.instrumentType = type;
        return EEquipAction.Nothing;
    }

    public override string GetUseText()
    {
        return "Play";
    }
}
