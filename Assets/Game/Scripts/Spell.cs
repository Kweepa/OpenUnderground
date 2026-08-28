
public class Spell : UUObject
{
    public override void TryInteract(UUObject originator, UUObject sender, EAction action)
    {
        if (!isLinked)
        {
            int major = (special & 0x1ff) >> 6;
            int minor = (special & 0x3f);
            Messages.Add(StringLoader.GetString(6, 16 * major + minor));
            Messages.Add(StringLoader.GetString(6, special & 0xff));
            //Magic.sMagic.TryCast((int)spell);
        }
        else
        {
            TryChainLinkedInteraction(action);
        }
    }
}
