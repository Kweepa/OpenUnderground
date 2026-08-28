using UnityEngine;

public class Treasure : UUObject
{
    protected override int GetQualityOffset()
    {
        if (type <= EObjectType.Emerald)
        {
            return 36;
        }
        return 42;
    }
}
