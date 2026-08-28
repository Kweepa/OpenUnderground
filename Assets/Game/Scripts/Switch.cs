using UnityEngine;

public class Switch : SwitchBase
{
    public int localAxis;
    public float posOn;
    public float posOff;

    protected override void SetSwitchChildPosition()
    {
        Vector3 pos = switchChild.localPosition;
        float m = (textureIndex & 8) > 0 ? posOn : posOff;
        switch (localAxis)
        {
        case 0:
            pos.x = m;
            break;
        case 1:
            pos.y = m;
            break;
        case 2:
            pos.z = m;
            break;
        }
        switchChild.localPosition = pos;
    }
}
