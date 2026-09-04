
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

    protected override void GetQualityString(System.Text.StringBuilder sb)
    {
        int durability = DataLoader.sDataLoader.objectsData.armourStats[(int)type & 31].durability;
        if (durability == 255)
        {
            return;
        }
        base.GetQualityString(sb);
    }

    public override int GetDefence()
    {
        // armor rating depends on quality
        int defence = 1 + DataLoader.sDataLoader.objectsData.armourStats[(int)type & 31].protection * quality / 64;
        if (enchantmentType == EEnchantmentType.Protection)
        {
            defence += 1 + enchantmentIndex;
        }

        return defence;
    }

    /// <summary>
    /// The worn value minus a Protection enchantment the player has not pinned down yet. The flat
    /// point every piece gets and the table protection are visible on the item, so they always
    /// count; the enchantment is not, and only a critical Lore result names it (see
    /// <see cref="GetIdentifiedName"/>). Until then the panel shows the crown, not the crown of
    /// protection - while the bonus goes on working.
    /// </summary>
    public override int GetKnownDefence()
    {
        int defence = GetDefence();
        if (enchantmentType == EEnchantmentType.Protection
            && loreResult != Skills.ESkillTestResult.CriticalSuccess)
        {
            defence -= 1 + enchantmentIndex;
        }

        return defence;
    }

    public override int GetToughness()
    {
        int toughness = 0;
        if (enchantmentType == EEnchantmentType.Toughness)
        {
            toughness = 1 + enchantmentIndex;
        }
        return toughness;
    }

    public override void TryDamage(int unscaledDamage, Skills.ESkillTestResult result)
    {
        // Check durability first - if it's 255, armor is indestructible
        int durability = DataLoader.sDataLoader.objectsData.armourStats[(int)type & 31].durability;
        if (durability != 255)
        {
            int damage = result == Skills.ESkillTestResult.CriticalSuccess ? 2 * unscaledDamage : unscaledDamage;

            // note - "X of toughness" type armour is just more resistant to damage
            int damageToArmour = Random.Range(0, damage) - GetToughness() - durability / 4;
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
