using System;

[Serializable]
public struct EObjectTypeEntry
{
    [FilterableEnumList]
    public EObjectType value;

    public EObjectTypeEntry(EObjectType val)
    {
        value = val;
    }

    public static implicit operator EObjectType(EObjectTypeEntry entry)
    {
        return entry.value;
    }

    public static implicit operator EObjectTypeEntry(EObjectType val)
    {
        return new EObjectTypeEntry(val);
    }
}
