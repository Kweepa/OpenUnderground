using UnityEngine;

// potions generally use the magic spells, except for Mana boost and restore for obvious reasons

public class Potion : UUObject
{
    public AudioClip quaff;

    public override void Initialize(ushort[] objData, byte[] critterData)
    {
        base.Initialize(objData, critterData);
        
        // there is only one potion that's linked to a damage trap
        // the whole damage trap + what to do with the leftover potion suggests that instead we should
        // just rewrite it as a normal potion
        if (isLinked)
        {
            enchantmentName = StringLoader.GetString(6, 277);
            link = 0;
        }
        else
        {
            int potionIndex = special & 0x3f;
            enchantmentName = StringLoader.GetString(6, 256 + potionIndex);
        }
    }

    protected override string GetIdentifiedName(string baseName)
    {
        return baseName + " of " + enchantmentName;
    }
    
    public override EEquipAction Equip()
    {
        // assume potions can't be stacked for now

        Utils.PlayClip2d(quaff);
        Messages.Add(1, 240); // quaff

        if (enchantmentName == StringLoader.GetString(6, 277))
        {
            if (!Magic.sMagic.IsSpellActive(Magic.ESpell.PoisonResistance))
            {
                int appliedPoison = Utils.GetDamageRoll(15);
                PlayerObject.AddPoison(appliedPoison);
            }
        }
        else
        {
            UseEnchantedScrollOrPotion(true);
        }

        return EEquipAction.Consume;
    }

    public override string GetUseText()
    {
        return "Drink";
    }
}
