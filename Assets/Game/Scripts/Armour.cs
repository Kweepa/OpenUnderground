
using UnityEngine;

enum EEnchantmentType
{
    None,
    Normal,
    Protection,
    Toughness
}

[System.Serializable]
public class ArmourSaveData : UUObjectSaveData
{
    public int enchantmentType; // EEnchantmentType as int
    public int enchantmentIndex;
}

public class Armour : UUObject
{
    private EEnchantmentType enchantmentType;
    private int enchantmentIndex;

    public override int GetQualityIndex()
    {
        if (type is EObjectType.ShinyShield or EObjectType.IronRing)
        {
            return 5;
        }
        return base.GetQualityIndex();
    }

    protected override int GetQualityOffset()
    {
        if (type >= EObjectType.TowerShield)
        {
            return 30;
        }
        return 6;
    }

    public override int GetDurability()
    {
        return DataLoader.sDataLoader.objectsData.armourStats[(int)type & 31].durability;
    }

    protected override void GetQualityString(System.Text.StringBuilder sb)
    {
        if (GetDurability() == 255)
        {
            return;
        }
        base.GetQualityString(sb);
    }

    public override int GetDefence()
    {
        // armor rating depends on quality
        // A Protection enchantment is deliberately not part of this. The original spends it on the
        // attack roll instead of on the damage - see GetMagicProtection() - and the loop that fills
        // the armour bytes reads the table value alone (UW.EXE 0x7e47f).
        // A Toughness enchantment is not part of it either. That one does soak damage, but the
        // original spreads it over the body parts by a rule of its own, so it is spread by
        // Inventory.GetToughnessByBodyPart() rather than carried on the piece's armour value.
        return 1 + DataLoader.sDataLoader.objectsData.armourStats[(int)type & 31].protection * quality / 64;
    }

    /// <summary>
    /// A "of Toughness" piece soaks damage on the part it covers. Same effect 12 as
    /// <see cref="GetMagicProtection"/> with bit 3 of the parameter set, which sends the value to
    /// the player's armour bytes instead (UW.EXE 0x7e19c, 0x7e1d4). Specials 712-719 give parameter
    /// 8-15, so <c>(parameter &amp; 7) + 1</c> is again the enchantment index plus one.
    /// </summary>
    public override int GetToughness()
    {
        if (enchantmentType == EEnchantmentType.Toughness)
        {
            return 1 + enchantmentIndex;
        }

        return 0;
    }

    /// <summary>
    /// Nothing until a critical Lore result names the enchantment, the same rule
    /// <see cref="GetKnownMagicProtection"/> follows.
    /// </summary>
    public override int GetKnownToughness()
    {
        return loreResult == Skills.ESkillTestResult.CriticalSuccess ? GetToughness() : 0;
    }

    /// <summary>
    /// A "of Protection" piece makes the part it covers harder to hit rather than soaking damage
    /// there. The original decodes the enchantment to effect 12, parameter <c>special &amp; 15</c>
    /// (UW.EXE 0x38cb6), and effect 12 adds <c>(parameter &amp; 7) + 1</c> to the byte of the part
    /// the piece covers (UW.EXE 0x7e1bf). For specials 704-711 that is the enchantment index plus
    /// one, the same 1..8 the panel used to add to the armour.
    /// </summary>
    public override int GetMagicProtection()
    {
        if (enchantmentType == EEnchantmentType.Protection)
        {
            return 1 + enchantmentIndex;
        }

        return 0;
    }

    /// <summary>
    /// Nothing until a critical Lore result names the enchantment, on the same rule the armour
    /// number follows in <see cref="GetKnownDefence"/>.
    /// </summary>
    public override int GetKnownMagicProtection()
    {
        return loreResult == Skills.ESkillTestResult.CriticalSuccess ? GetMagicProtection() : 0;
    }

    /// <summary>
    /// The armour number of the piece itself is entirely visible on it - the flat point every piece
    /// gets plus the table protection scaled by quality - so there is nothing to hide here. What an
    /// unidentified enchantment hides lives in <see cref="GetKnownToughness"/> and
    /// <see cref="GetKnownMagicProtection"/>, next to the enchantments themselves.
    /// </summary>
    public override int GetKnownDefence()
    {
        return GetDefence();
    }

    public override void TryDamage(int unscaledDamage, Skills.ESkillTestResult result)
    {
        // Check durability first - if it's 255, armor is indestructible
        int durability = GetDurability();
        if (durability != 255)
        {
            int damage = result == Skills.ESkillTestResult.CriticalSuccess ? 2 * unscaledDamage : unscaledDamage;

            // A Toughness enchantment no longer slows this down. It soaks damage instead - see
            // GetToughness(). The original does wear equipment, in three places all inside
            // UW.EXE 0x24ac9, and none of them looks at the enchantment: a critical hit taken
            // wears a worn piece by a flat 2d4 (0x24c35 rolls it, 0x24c3f spends it) with the slot
            // taken from the body part struck, and the other two work the same way on the weapon.
            // Nor could they: the only six places that decode an item's enchantment are the swing
            // (0x25406), two item-use paths (0x37ca4, 0x384e4), two that build its name (0x7bd5a,
            // 0x7bdbc) and the equipment recalculation (0x7e729).
            int damageToArmour = Random.Range(0, damage) - durability / 4;
            if (damageToArmour > 0)
            {
                quality -= damageToArmour; 
                // for armor, this rule is good
                string wasSlashWere = singularName.EndsWith('s') ? "were" : "was"; 
                if (quality <= 0)
                {
                    Messages.Add($"Your {singularName} {wasSlashWere} destroyed.");
                    Inventory.sInv.DestroyEquippedItem(this);
                }
                else
                {
                    Messages.Add($"Your {singularName} {wasSlashWere} damaged.");
                }
            }
        }
    }

    public override void Initialize(ushort[] objData, byte[] critterData)
    {
        base.Initialize(objData, critterData);
    
        if (!isLinked && isEnchanted)
        {
            if (special is >= 704 and < 712)
            {
                enchantmentIndex = special - 704;
                enchantmentName = StringLoader.GetString(6, 464 + enchantmentIndex);
                enchantmentType = EEnchantmentType.Protection;
            }
            else if (special is >= 712 and < 720)
            {
                enchantmentIndex = special - 712;
                enchantmentName = StringLoader.GetString(6, 472 + enchantmentIndex);
                enchantmentType = EEnchantmentType.Toughness;
            }
            else if (special >= 512)
            {
                enchantmentIndex = special - 512;
                enchantmentName = StringLoader.GetString(6, enchantmentIndex);
                enchantmentType = EEnchantmentType.Normal;
            }

            name += " of " + enchantmentName;
        }

        // identify Shield of Valor and Ring of Humility immediately
        if (type is EObjectType.ShinyShield or EObjectType.IronRing)
        {
            loreResult = Skills.ESkillTestResult.CriticalSuccess;
        }
    }
    
    public override EEquipAction Equip()
    {
        if (isEnchanted)
        {
            if (!isLinked)
            {
                if (enchantmentIndex == 212) // maze navigation
                {
                    // find the blocks to recolor
                    LevelLoader.sLevelLoader.ShowMazePath(true);
                }
                else if (enchantmentType == EEnchantmentType.Normal)
                {
                    EquipEnchantedItem();
                }
            }
            else
            {
                TryInteract(this, this, EAction.Trigger);
            }
        }

        return EEquipAction.Equip;
    }

    public override void Unequip()
    {
        if (isEnchanted && !isLinked)
        {
            if (enchantmentIndex == 212) // maze navigation
            {
                // find the blocks to recolor
                LevelLoader.sLevelLoader.ShowMazePath(false);
            }
            else if (enchantmentType == EEnchantmentType.Normal)
            {
                UnequipEnchantedItem();
            }
        }
    }

    public override bool IsRepairable()
    {
        if (type is EObjectType.ShinyShield or EObjectType.IronRing)
        {
            return false;
        }
        return true;
    }

    protected override string GetIdentifiedName(string baseName)
    {
        if (type == EObjectType.ShinyShield)
        {
            return StringLoader.GetString(1, 266);
        }
        else if (type == EObjectType.IronRing)
        {
            return StringLoader.GetString(1, 269);
        }

        return base.GetIdentifiedName(baseName);
    }

    public override string GetUseText()
    {
        return "Equip";
    }

    protected override void PopulateSaveData(LevelObjectSaveData data)
    {
        base.PopulateSaveData(data);
        
        if (data is ArmourSaveData armourData)
        {
            armourData.enchantmentType = (int)enchantmentType;
            armourData.enchantmentIndex = enchantmentIndex;
        }
    }

    protected override void RestoreFromSaveData(LevelObjectSaveData data)
    {
        base.RestoreFromSaveData(data);
        
        if (data is ArmourSaveData armourData)
        {
            enchantmentType = (EEnchantmentType)armourData.enchantmentType;
            enchantmentIndex = armourData.enchantmentIndex;
        }
    }

    public override ObjectSaveData SaveToData()
    {
        ArmourSaveData data = new ArmourSaveData();
        PopulateSaveData(data);
        
        return new ObjectSaveData
        {
            objectName = name,
            objectTypeName = GetType().Name,
            objectType = (int)type,
            objectIndex = objectIndex,
            level = levelIndex,
            originalLevel = originalLevel,
            jsonData = JsonUtility.ToJson(data)
        };
    }

    public override void LoadFromData(ObjectSaveData objData)
    {
        if (objData == null || string.IsNullOrEmpty(objData.jsonData))
            return;
        
        ArmourSaveData data = JsonUtility.FromJson<ArmourSaveData>(objData.jsonData);
        if (data == null)
        {
            Debug.LogError($"Failed to deserialize JSON for armour objectIndex={objData.objectIndex}, jsonData={objData.jsonData}");
            return;
        }
        RestoreFromSaveData(data);
    }
}
