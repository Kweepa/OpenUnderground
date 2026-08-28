using System.Collections.Generic;
using UnityEngine;

// reading certain books will boost skills.
// treatise on fishing - +2 track
// thief's guide volume 1 - +2 lockpick
//               volume 2 - +2 stealth
//               volume 3 - nothing
//               volume 4 - +2 traps
// 

[System.Serializable]
public class BookSaveData : UUObjectSaveData
{
    public bool read;
}

public class Book : UUObject
{
    public CutscenePlayer mapCutscene;
    public AudioClip explosionClip;
    public AudioClip makeStew;

    private bool read;

    public bool IsRotwormStewRecipe() => special == 769;

    public override void Initialize(ushort[] objData, byte[] critterData)
    {
        base.Initialize(objData, critterData);

        if (!isLinked && isEnchanted)
        {
            int enchantmentIndex = special & 0x3f;
            enchantmentName = StringLoader.GetString(6, 256 + enchantmentIndex);
        }
        
        // identify book of honesty immediately
        if (type == EObjectType.BookOfHonesty)
        {
            loreResult = Skills.ESkillTestResult.CriticalSuccess;
        }
    }

    private void TryMakeRotwormStew()
    {
        List<UUObject> bowls = Inventory.sInv.FindObjectsInInventory(EObjectType.Bowl);
        if (bowls.Count == 0)
        {
            // need a bowl
            Messages.Add(1, 150);
        }
        else
        {
            UUObject correctBowl = null;
            foreach (UUObject bowl in bowls)
            {
                if (bowl.contents.Count == 3)
                {
                    List<UUObject> items = new List<UUObject>(bowl.contents);
                    items.Sort((a,b) => a.type.CompareTo(b.type));
                    if (items[0].type == EObjectType.Mushroom && items[1].type == EObjectType.FlaskOfPort && items[2].type == EObjectType.DeadRotworm)
                    {
                        // have the correct ingredients
                        correctBowl = bowl;
                        break;
                    }
                }
            }

            if (correctBowl != null)
            {
                Messages.Add(1, 149);

                // correct ingredients, swap for stew
                List<UUObject> parentContents = Inventory.sInv.FindObjectInInventory(correctBowl);
                int i = parentContents.IndexOf(correctBowl);
                parentContents.RemoveAt(i);

                UUObject stew = LevelLoader.CreateObjectOfType(EObjectType.RotwormStew);
                stew.PostLoadInitialize();
                parentContents.Insert(i, stew);

                Utils.PlayClip2d(makeStew);
            }
            else
            {
                // need to put correct ingredients in
                Messages.Add(1, 148);
            }
        }
    }

    public override EEquipAction Equip()
    {
        if (IsRotwormStewRecipe())
        {
            TryMakeRotwormStew();
        }
        else if (isEnchanted)
        {
            UseEnchantedScrollOrPotion();
            return EEquipAction.Consume;
        }
        else
        {
            ReadBookInInventory();
        }

        return EEquipAction.Use;
    }

    /// <summary>Gamepad A / mouse LMB — inspect (<see cref="UUObject.TryInventoryUse"/>). Read is <see cref="Equip"/> via Y / RMB.</summary>
    public override void TryInventoryUse()
    {
        base.TryInventoryUse();
        if (IsRotwormStewRecipe())
        {
            Messages.Add(3, 257);
        }
    }

    /// <summary>Inventory stuff grid: Y / RMB hint for read or recipe use (<see cref="Equip"/>).</summary>
    public override string GetUseText()
    {
        if (IsRotwormStewRecipe() || isEnchanted)
        {
            return "Use";
        }

        return "Read";
    }

    /// <summary>Inventory Y / RMB (<see cref="Inventory.RunStuffYButtonActionAtIndex"/> → <see cref="Equip"/>).</summary>
    private void ReadBookInInventory()
    {
        if (IsRotwormStewRecipe())
        {
            return;
        }

        if (!isLinked && special >= 512 && !isEnchanted)
        {
            if (special == 520 && mapCutscene != null)
            {
                Instantiate(mapCutscene);
            }
            else
            {
                int messageIndex = special - 512;
                Messages.Add(3, messageIndex);
                if (!read)
                {
                    ++PlayerData.sData.booksRead;
                    read = true;

                    bool advanceSkill = false;

                    switch (messageIndex)
                    {
                    case 99: // treatise on fishing
                        advanceSkill = Skills.AdvanceSkill(ESkill.Track);
                        break;
                    case 128: // thief's guide volume 1
                        advanceSkill = Skills.AdvanceSkill(ESkill.Picklock);
                        break;
                    case 129: // thief's guide volume 2
                        advanceSkill = Skills.AdvanceSkill(ESkill.Sneak);
                        break;
                    case 130: // thief's guide volume 3
                        advanceSkill = Skills.AdvanceSkill(ESkill.Acrobat);
                        break;
                    case 131: // thief's guide volume 4
                        advanceSkill = Skills.AdvanceSkill(ESkill.Traps);
                        break;
                    }

                    if (advanceSkill)
                    {
                        StatsPanel.sStatsPanel.Show();
                    }
                }
            }
        }
        else if (type == EObjectType.ExplodingBook)
        {
            Messages.Add("As you open the book, it explodes in your hands!");
            ++PlayerData.sData.questFlags[(int)EQuestFlag.BronusBookGoBoom];
            PlayerObject.Player.Damage(Skills.ESkillTestResult.Success, 5, EDamageType.Damage);
            ParticleSpawner.SpawnParticle(EParticleType.MagicBookExplosion, PlayerObject.Player.transform.position + PlayerObject.Player.transform.forward);
            Utils.PlayClip2d(explosionClip);
            Utils.DestroyItem(this);
        }
        else
        {
            base.TryInventoryUse();
        }
    }

    public override void TryInventorySecondaryUse()
    {
        ReadBookInInventory();
    }

    public override int GetQualityIndex()
    {
        if (type == EObjectType.BookOfHonesty)
        {
            return 5;
        }
        return base.GetQualityIndex();
    }

    protected override int GetQualityOffset()
    {
        if (type >= EObjectType.ScrollA)
        {
            return 12; // scrolls
        }
        return 54; // books
    }

    protected override string GetIdentifiedName(string baseName)
    {
        if (type == EObjectType.BookOfHonesty)
        {
            return StringLoader.GetString(1, 261);
        }

        if (isEnchanted)
        {
            return baseName + " of " + enchantmentName;
        }

        return base.GetIdentifiedName(baseName);
    }

    protected override void PopulateSaveData(LevelObjectSaveData data)
    {
        base.PopulateSaveData(data);

        if (data is BookSaveData bookData)
        {
            bookData.read = read;
        }
    }

    protected override void RestoreFromSaveData(LevelObjectSaveData data)
    {
        base.RestoreFromSaveData(data);

        if (data is BookSaveData bookData)
        {
            read = bookData.read;
        }
    }

    public override ObjectSaveData SaveToData()
    {
        BookSaveData data = new BookSaveData();
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

        BookSaveData data = JsonUtility.FromJson<BookSaveData>(objData.jsonData);
        if (data == null)
        {
            Debug.LogError($"Failed to deserialize JSON for book objectIndex={objData.objectIndex}, jsonData={objData.jsonData}");
            return;
        }
        RestoreFromSaveData(data);
    }
}
