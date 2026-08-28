using System;
using UnityEngine;

[System.Serializable]
public class WandSaveData : UUObjectSaveData
{
    public int charges;
    public int spellQuality;
}

public class Wand : UUObject
{
    public AudioClip snap;
    public AudioClip frogReset;
    private int charges;
    /// <summary>Charge capacity from the linked spell's quality. Wand quality is item condition.</summary>
    private int spellQuality;
    /// <summary>True when loading a pre-Wand save (e.g. sceptre as Treasure) that omitted charges.</summary>
    private bool recoverChargesFromLinkedSpell;

    public int ChargesRemaining => charges;

    private bool IsBrokenWand => type is >= EObjectType.BrokenWandA and <= EObjectType.BrokenWandD;

    private bool IsSpentSceptre => type == EObjectType.Sceptre && charges <= 0;

    private bool BehavesAsBroken => IsBrokenWand || IsSpentSceptre;

    private void ClearMagicalState()
    {
        enchantmentName = null;
        isEnchanted = false;
        loreResult = Skills.ESkillTestResult.Failure;
        loreResultLoreLevel = -1;
    }

    public override void Initialize(ushort[] objData, byte[] critterData)
    {
        base.Initialize(objData, critterData);

        if (!isLinked && !IsBrokenWand)
        {
            // TODO: investigate and fix this
            int spellIndex = special - 368;
            if (spellIndex >= 0)
            {
                enchantmentName = StringLoader.GetString(6, spellIndex);

                name = $"{name} of {enchantmentName} ({special})";
            }
            else
            {
                //Debug.LogFormat(gameObject, $"Wand on level {LevelLoader.sLevelLoader.loadedLevel} has special {special}");
            }
        }
    }

    public override void PostLoadInitialize(bool restoredFromSave = false)
    {
        base.PostLoadInitialize(restoredFromSave);

        if (IsBrokenWand)
        {
            ClearMagicalState();
            return;
        }

        if (isLinked)
        {
            UUObject obj = GetLinkedSpell();
            if (obj != null && obj.type == EObjectType.Spell)
            {
                int spellIndex = obj.special;
                if (spellIndex >= 512)
                {
                    if (spellIndex < 576)
                    {
                        enchantmentName = StringLoader.GetString(6, spellIndex - 256);
                    }
                    else
                    {
                        enchantmentName = StringLoader.GetString(6, spellIndex - 368);
                    }

                    if (!restoredFromSave || recoverChargesFromLinkedSpell)
                    {
                        ApplyChargesFromLinkedSpell(obj);
                    }
                    else if (spellQuality <= 0)
                    {
                        spellQuality = obj.quality;
                    }

                    name = $"{name} of {enchantmentName} with {charges} full charges";
                }
            }

            if (String.IsNullOrEmpty(enchantmentName))
            {
                enchantmentName = "(Linked)";
            }
        }

        if (IsSpentSceptre && !recoverChargesFromLinkedSpell)
        {
            ClearMagicalState();
        }
    }

    /// <summary>
    /// Retry charge recovery after world objects[] is populated (inventory may PLI before spells exist).
    /// </summary>
    public void TryFinishLegacyChargeRecovery()
    {
        if (!recoverChargesFromLinkedSpell || IsBrokenWand || !isLinked)
        {
            return;
        }

        UUObject obj = GetLinkedSpell();
        if (obj != null && obj.type == EObjectType.Spell && obj.special >= 512)
        {
            ApplyChargesFromLinkedSpell(obj);
        }

        if (IsSpentSceptre)
        {
            ClearMagicalState();
        }
    }

    private UUObject GetLinkedSpell()
    {
        if (link == 0)
        {
            return null;
        }

        UUObject obj = null;
        if (originalLevel > 0)
        {
            obj = LevelLoader.GetObj(link, originalLevel);
        }
        if (obj == null && levelIndex > 0)
        {
            obj = LevelLoader.GetObj(link, levelIndex);
        }
        if (obj == null)
        {
            obj = LevelLoader.GetObj(link);
        }
        return obj;
    }

    private void ApplyChargesFromLinkedSpell(UUObject spell)
    {
        charges = spell.quality;
        spellQuality = spell.quality;
        recoverChargesFromLinkedSpell = false;
    }
    
    public override string GetUseText()
    {
        if (BehavesAsBroken)
        {
            return null;
        }

        return "Use";
    }

    public override EEquipAction Equip()
    {
        if (enchantmentName == StringLoader.GetString(6, Magic.StringIndexTheFrog))
        {
            // reset bullfrog puzzle
            if (LevelLoader.sLevelLoader.loadedLevel == 4)
            {
                for (int tx = 48; tx <= 57; ++tx)
                {
                    for (int ty = 48; ty <= 57; ++ty)
                    {
                        Tile t = LevelLoader.GetTile(tx, ty);
                        if (t.movingPlatform != null)
                        {
                            t.movingPlatform.Reset();
                        }
                    }
                }
            }
            //Utils.PlayClip2d(frogReset, false);
            Messages.Add(1, 193); // reset activated
        }
        else if (charges > 0)
        {
            if (Magic.sMagic != null && Magic.sMagic.TryBeginMousePrimedSpellFromWand(this))
            {
                // primed — charge deferred until aim confirm
            }
            else if (UseEnchantedScrollOrPotion())
            {
                ConsumeChargeAfterUse();
            }
        }

        return EEquipAction.Nothing;
    }

    public void ConsumeChargeAfterUse()
    {
        if (--charges <= 0)
        {
            if (type == EObjectType.Sceptre)
            {
                Messages.Add("The sceptre is spent.");
                ClearMagicalState();
            }
            else
            {
                UUObject brokenWand = LevelLoader.CreateObjectOfType(type + 4);
                brokenWand.PostLoadInitialize();
                Inventory.sInv.ReplaceItemInInventory(this, brokenWand);
                Utils.DestroyItem(this);
                Messages.Add(1, 125); // snap
                Utils.PlayClip2d(snap);
            }
        }
    }

    protected override string GetIdentifiedName(string baseName)
    {
        string identifiedName = baseName;
        if (!String.IsNullOrEmpty(enchantmentName) && enchantmentName != "(Linked)")
        {
            identifiedName += " of " + enchantmentName;
        }

        if (charges > 0)
        {
            identifiedName = $"{identifiedName} with {charges} full charges";
        }

        return identifiedName;
    }

    protected override int GetQualityOffset()
    {
        if (IsBrokenWand)
        {
            return -1;
        }
        if (type == EObjectType.Sceptre)
        {
            // Use the set that includes "unblemished"
            return 36;
        }
        return 72;
    }

    // Worn/unblemished etc. from remaining charges over linked-spell quality (capacity).
    public override int GetQualityIndex()
    {
        int capacity = GetSpellQualityCapacity();
        if (BehavesAsBroken || capacity <= 0)
        {
            return 0;
        }

        // Cap at index 3 (unblemished / slightly worn). 48+ would be flawless/new.
        int effectiveQuality = Mathf.Clamp(charges * 47 / capacity, 0, 47);
        return effectiveQuality > 0 ? (1 + effectiveQuality / 16) : 0;
    }

    private int GetSpellQualityCapacity()
    {
        if (spellQuality > 0)
        {
            return spellQuality;
        }

        UUObject spell = GetLinkedSpell();
        if (spell != null && spell.type == EObjectType.Spell && spell.quality > 0)
        {
            spellQuality = spell.quality;
            return spellQuality;
        }

        return 0;
    }
    
    protected override void PopulateSaveData(LevelObjectSaveData data)
    {
        base.PopulateSaveData(data);
        
        if (data is WandSaveData wandData)
        {
            wandData.charges = charges;
            wandData.spellQuality = spellQuality;
        }
    }

    protected override void RestoreFromSaveData(LevelObjectSaveData data)
    {
        base.RestoreFromSaveData(data);
        
        if (data is WandSaveData wandData)
        {
            charges = wandData.charges;
            spellQuality = wandData.spellQuality;
        }
    }

    public override ObjectSaveData SaveToData()
    {
        WandSaveData data = new WandSaveData();
        PopulateSaveData(data);
        
        return new ObjectSaveData
        {
            objectTypeName = GetType().Name,
            objectType = (int)type,
            objectIndex = objectIndex,
            level = levelIndex,
            jsonData = JsonUtility.ToJson(data)
        };
    }

    public override void LoadFromData(ObjectSaveData objData)
    {
        if (objData == null || string.IsNullOrEmpty(objData.jsonData))
        {
            return;
        }
        
        WandSaveData data = JsonUtility.FromJson<WandSaveData>(objData.jsonData);
        RestoreFromSaveData(data);

        // objectTypeName is the class that wrote the save; non-Wand (e.g. Treasure) has no charges field.
        recoverChargesFromLinkedSpell = objData.objectTypeName != nameof(Wand);
    }
}
