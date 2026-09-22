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
        UpdateEnchantmentState();
        if (isLinked)
        {
            link = 0;
        }
    }

    protected override void UpdateEnchantmentState()
    {
        // The linked potion keeps its own number even after the link is cleared, because
        // isLinked still says what it is.
        if (isLinked)
        {
            SetEnchantment(277);
            return;
        }

        // Only when special really holds an enchantment. Every potion in the level data does. The
        // one that does not is the potion that rolls its enchantment when it spawns, in a save
        // written before that roll was recorded: there special is 0, and deriving from it would
        // quietly turn the potion into a different one. For that potion the number comes back
        // from the name it was saved with, looked up in the spell list first. Mana Boost and
        // Restore Mana are not spells, so only when the spell list has no such name is the
        // name looked up among the potions the debris can roll, in the strings file in use and
        // then in English. Without a number those two would do nothing, because
        // UseEnchantedScrollOrPotion() knows them by number only.
        int enchantmentNumber = Enchantment.PotionNumberFrom(special);
        if (enchantmentNumber == Enchantment.None)
        {
            enchantmentNumber = Magic.FindSpellNumberByName(enchantmentName);
        }
        if (enchantmentNumber == Enchantment.None)
        {
            enchantmentNumber = DebrisLootTables.FindPotionEnchantmentNumber(enchantmentName);
        }
        if (enchantmentNumber != Enchantment.None)
        {
            SetEnchantment(enchantmentNumber);
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

        if (enchantmentNumber == 277) // Poison
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
