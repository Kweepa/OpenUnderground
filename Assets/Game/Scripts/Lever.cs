using UnityEngine;

public class Lever : SwitchBase
{
    public Vector3 eulerOff;
    public Vector3 eulerOn;

    protected override void SetSwitchChildPosition()
    {
        switchChild.localEulerAngles = (textureIndex & 8) > 0 ? eulerOn : eulerOff;
    }
}
