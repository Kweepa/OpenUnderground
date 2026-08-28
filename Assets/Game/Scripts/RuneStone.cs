using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class RuneStone : UUObject
{
    public AudioClip equipSound;

    public override void Initialize(ushort[] objData, byte[] critterData)
    {
        base.Initialize(objData, critterData);

        foreach (var text in gameObject.GetComponentsInChildren<TMP_Text>())
        {
            char rune = (char)('A' + type - EObjectType.RunestoneAn);
            if (rune == 'X') rune = 'Y';
            text.text = rune.ToString();
        }
    }

    public override void PostLoadInitialize(bool restoredFromSave = false)
    {
        base.PostLoadInitialize(restoredFromSave);

        if (!restoredFromSave)
        {
            transform.rotation = Quaternion.AngleAxis(Random.Range(0.0f, 360.0f), Vector3.up);
        }
    }

    /// <summary>Moves a letter/base rune into the rune bag (used by Y / equip path and shared with <see cref="Inventory"/>).</summary>
    public static EEquipAction TryStowInRuneBag(UUObject stone)
    {
        if (stone == null || Inventory.sInv == null)
        {
            return EEquipAction.Nothing;
        }

        UUObject runeBag = Inventory.sInv.FindObjectInInventory(EObjectType.RuneBag);
        if (runeBag == null)
        {
            return EEquipAction.Nothing;
        }

        if (runeBag.contents == null)
        {
            runeBag.contents = new List<UUObject>();
        }

        foreach (UUObject rune in runeBag.contents)
        {
            if (rune.type != stone.type)
            {
                continue;
            }

            if (rune != stone)
            {
                Messages.Add($"The rune bag already contains {stone.GetLookName()}.");
            }

            return EEquipAction.Nothing;
        }

        runeBag.contents.Add(stone);
        PlayStowSound(stone);
        Magic.AddRunestone(stone.type);
        return EEquipAction.Consume;
    }

    public static void PlayStowSound(UUObject stone)
    {
        AudioClip sound = (stone as RuneStone)?.equipSound;
        if (sound == null && Inventory.sInv != null)
        {
            sound = Inventory.sInv.equip;
        }

        if (sound != null)
        {
            Utils.PlayClip2d(sound);
        }
    }

    public override EEquipAction Equip()
    {
        return TryStowInRuneBag(this);
    }

    public override string GetUseText()
    {
        if (Inventory.sInv == null)
        {
            return "";
        }

        UUObject runeBag = Inventory.sInv.FindObjectInInventory(EObjectType.RuneBag);
        if (runeBag == null)
        {
            return "";
        }

        if (runeBag.contents != null)
        {
            foreach (UUObject rune in runeBag.contents)
            {
                if (rune == this)
                {
                    return "";
                }
            }
        }

        return "Stow";
    }
}
