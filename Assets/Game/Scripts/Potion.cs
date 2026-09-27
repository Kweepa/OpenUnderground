using UnityEngine;

// potions generally use the magic spells, except for Mana boost and restore for obvious reasons

public class Potion : UUObject
{
    public AudioClip quaff;

    public override void Initialize(ushort[] objData, byte[] critterData)
    {
        base.Initialize(objData, critterData);
        
        UpdateEnchantmentState();
    }

    protected override void UpdateEnchantmentState()
    {
        // A potion whose quantity bit is clear has no enchantment: its link is a list, and the
        // one potion in the game that has one holds a poison trap there and nothing else (UW.EXE
        // 0x38b5c looks for an enchantment object in the list and finds none). The trap is the
        // whole potion: drunk, it goes off once; disarmed or set off, the potion is a dud. A save
        // written when the remake still made it a Poison potion comes back as a dud too, because
        // the link was cleared then; its saved name is dropped here.
        if (isLinked)
        {
            // this.: the method declares a local of the same name further down.
            this.enchantmentNumber = Enchantment.None;
            enchantmentName = "";
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

    /// <summary>
    /// The trapped potion once its trap is gone - disarmed, set off, or drunk: it does nothing, and
    /// says so by its name.
    /// </summary>
    /// <remarks>
    /// Ours, the user's request of 27 September 2026. The original leaves it a red potion that
    /// only prints "You quaff the potion in one gulp." (UW.EXE 0x374ee), which nothing tells the
    /// player. The state is only reached by dealing with the trap, so the name gives nothing away.
    /// </remarks>
    public bool IsUseless => isLinked && link == 0;

    public override string GetLookName()
    {
        if (!IsUseless)
        {
            return base.GetLookName();
        }

        string name = singularName;
        singularName = "useless potion";
        try
        {
            return base.GetLookName();
        }
        finally
        {
            singularName = name;
        }
    }

    protected override string GetIdentifiedName(string baseName)
    {
        return string.IsNullOrEmpty(enchantmentName) ? baseName : baseName + " of " + enchantmentName;
    }
    
    public override EEquipAction Equip()
    {
        // assume potions can't be stacked for now

        Utils.PlayClip2d(quaff);
        Messages.Add(1, 240); // quaff

        if (isLinked)
        {
            // The trapped potion: after the message, the use goes down its list and sets off a
            // trap hung straight on it, which is then deleted (UW.EXE 0x374ee at 0x37945,
            // 0x385d6). With no trap left there is nothing else to it.
            TrapSearch.SetOffOnUse(this);
        }
        else if (enchantmentNumber == 277) // Poison
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
