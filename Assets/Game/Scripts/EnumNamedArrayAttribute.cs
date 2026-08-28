using UnityEngine;
using System;

// mark up an array and it will, along with the EnumNamedArrayDrawer, label each element with the enum
public class EnumNamedArrayAttribute : PropertyAttribute
{
    public Type TargetEnum;
    public int offset;

    public EnumNamedArrayAttribute(Type targetEnum, int _offset = 0)
    {
        TargetEnum = targetEnum;
        offset = _offset;
    }
}
