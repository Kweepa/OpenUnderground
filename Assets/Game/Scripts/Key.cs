using UnityEngine;

public class Key : UUObject
{
    public AudioClip lockpickBreak;
    public AudioClip lockpickFail;

    public string lookString;

    public GameObject[] variations;

    private void SelectVariation(int var)
    {
        if (var < variations.Length)
        {
            if (variations[0] != null && variations[1] != null && var < 2)
            {
                variations[1-var].SetActive(false);
                variations[var].SetActive(true);
            }
        }
    }

    private void InitStringsAndVariations()
    {
        switch (ownerIndex) // the lock it fits
        {
        case 3:
            // gray goblin key
            lookString = "This key smells faintly of damp earth.";
            break;
        case 4:
            // green goblin key
            lookString = "The handle of this key has a faint green patina.";
            break;
        case 25:
            // key for doors around the lava chasm
            lookString = "This key is molded from hardened lava.";
            break;
        case 28:
            // the actual bone key
            lookString = StringLoader.GetString(5, 125);
            break;
        default:
            lookString = StringLoader.GetString(5, 100 + ownerIndex);
            break;
        }
        
        switch (ownerIndex) // the lock it fits
        {
        case 3:
            // gray goblin key
            SelectVariation(0);
            break;
        case 4:
            // green goblin key
            SelectVariation(0);
            break;
        case 9:
            // lizard etching
            SelectVariation(1);
            break;
        case 10:
            // key to courage
            SelectVariation(0);
            break;
        case 23:
            // crude runes
            SelectVariation(1);
            break;
        case 25:
            // key for doors around the lava chasm
            SelectVariation(0);
            break;
        }
    }

    public override void Initialize(ushort[] objData, byte[] critterData)
    {
        base.Initialize(objData, critterData);
        InitStringsAndVariations();
    }

    public override void PostLoadInitialize(bool restoredFromSave)
    {
        base.PostLoadInitialize(restoredFromSave);

        TryRepairMissingOwnerIndex();

        // Peels set ownerIndex after Initialize(); always refresh look/variations from the final lock id.
        InitStringsAndVariations();
    }

    /// <summary>
    /// Peel/stack bugs could wipe lock id (ownerIndex) to 0. Prefer lev.ark when originalLevel remains;
    /// otherwise Key B → lock 2 is a huge hack for the common level-1 case.
    /// </summary>
    private void TryRepairMissingOwnerIndex()
    {
        if (ownerIndex != 0)
        {
            return;
        }

        if (originalLevel > 0
            && LevelLoader.sLevelLoader != null
            && LevelLoader.sLevelLoader.TryGetArkOwnerIndex(originalLevel, objectIndex, type, out int arkOwnerIndex))
        {
            ownerIndex = arkOwnerIndex;
            return;
        }

        // HUGE HACK: peeled Key B (type 258) saves lost originalLevel; assume the common level-1 lock id 2.
        // Cannot recover other Key B lock ids (10, 40, 45, 46, 52, …) once the ark link is gone.
        if (type == EObjectType.KeyB)
        {
            ownerIndex = 2;
        }
    }

    protected override bool TryUseAtAim()
    {
        if (Interaction.sInt == null)
        {
            return false;
        }

        float maxDist = Interaction.sInt.GetInteractionDistance();
        int layerMask = LayerMasks.EnvironmentAndCeiling;
        if (!TryRaycastFromPlayerAim(out Lockable lockable, maxDist, layerMask))
        {
            return false;
        }

        lockable.TryInteract(this, this, EAction.Use);
        return true;
    }

    public override EEquipAction Equip()
    {
        return TryUseAtAim() ? EEquipAction.Use : EEquipAction.Nothing;
    }

    public override string GetUseText()
    {
        if (Interaction.sInt == null)
        {
            return null;
        }

        float maxDist = Interaction.sInt.GetInteractionDistance();
        int layerMask = LayerMasks.EnvironmentAndCeiling;
        if (!TryRaycastFromPlayerAim(out Lockable lockable, maxDist, layerMask))
        {
            return null;
        }

        return lockable.TryGetKeyUseHint(this, out string hint) ? hint : null;
    }

    public override string GetGamepadChargeThrowVerbOrNull()
    {
        return null;
    }

    public override bool SupportsStuffGridMouseSecondaryUse => false;

    /// <summary>Mouse cursor release uses <see cref="UUObject.Throw"/> before physics — try door/chest use first (same ray as <see cref="Equip"/>).</summary>
    public override bool Throw()
    {
        if (GameInput.LastActiveDevice == GameInputDevice.MouseKeyboard)
        {
            EEquipAction r = Equip();
            return r == EEquipAction.Use || r == EEquipAction.Consume;
        }

        return false;
    }

    public override void TryInventoryUse()
    {
        // look at the key
        Messages.Add(lookString);
    }

    public override void TryInventorySecondaryUse()
    {
        Messages.Add(lookString);
    }
}
