
public class Cup : UUObject
{
    public override void Initialize(ushort[] objData, byte[] critterData)
    {
        base.Initialize(objData, critterData);

        // immediately identify
        loreResult = Skills.ESkillTestResult.CriticalSuccess;
    }

    protected override string GetIdentifiedName(string baseName)
    {
        return StringLoader.GetString(1, 267);
    }
}
