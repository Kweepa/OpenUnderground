using UnityEngine;

public class Bones : UUObject
{
    public GameObject defaultMesh;
    public GameObject lizardmanSkullMesh;

    public override void PostLoadInitialize(bool restoredFromSave = false)
    {
        base.PostLoadInitialize(restoredFromSave);

        // Swap to LizardmanSkull if originalLevel is 3 and objectIndex matches specific values
        if (originalLevel == 3 && lizardmanSkullMesh != null && defaultMesh != null)
        {
            // TODO: Replace placeholder values with actual objectIndex values
            if (objectIndex is 935 or 716 or 718)
            {
                defaultMesh.SetActive(false);
                lizardmanSkullMesh.SetActive(true);
            }
        }
    }

    protected override bool TryUseAtAim()
    {
        if (Interaction.sInt == null)
        {
            return false;
        }

        float maxDist = Interaction.sInt.GetInteractionDistance();
        int layerMask = 1 << LayerMask.NameToLayer("Objects");
        if (!TryRaycastFromPlayerAim(out Gravestone grave, maxDist, layerMask))
        {
            return false;
        }

        grave.TryInteract(this, this, EAction.Use);
        return true;
    }

    public override EEquipAction Equip()
    {
        return TryUseAtAim() ? EEquipAction.Use : EEquipAction.Nothing;
    }

    public override string GetUseText()
    {
        return IsGravestoneInAim() ? "Bury" : null;
    }

    public override bool SupportsStuffGridMouseSecondaryUse => false;

    /// <summary>Cursor release uses <see cref="UUObject.Throw"/> before physics — bury at aim first (same ray as <see cref="Equip"/>).</summary>
    public override bool Throw()
    {
        if (GameInput.LastActiveDevice == GameInputDevice.MouseKeyboard)
        {
            return TryUseAtAim();
        }

        return false;
    }

    /// <summary>For inventory X hint when not using mouse UI — forward cast matches <see cref="Equip"/> on gamepad.</summary>
    private static bool IsGravestoneInAim()
    {
        if (Interaction.sInt == null)
        {
            return false;
        }

        float maxDist = Interaction.sInt.GetInteractionDistance();
        int layerMask = 1 << LayerMask.NameToLayer("Objects");
        return TryRaycastFromPlayerAim(out Gravestone _, maxDist, layerMask);
    }

    public override string GetGamepadChargeThrowVerbOrNull()
    {
        if (IsGravestoneInAim())
        {
            return "Bury";
        }

        return null;
    }

    public override void NameEnchantment(bool spawnParticleEffect = true)
    {
        base.NameEnchantment(spawnParticleEffect); // You see blah
        AddCloseLookLine();
    }

    /// <summary>
    /// Whose bones they are, as UW.EXE 0x7c32b (BonesLook, LOOK.C) says it, for the skulls, bones and
    /// pile 194-198 only: owners 40 and 60-62 say nothing, 63 is an adventurer, and the pile, or more
    /// than one, takes the plural. Printed by a look at hand's reach, in the inventory and by Name
    /// Enchantment, as the original's look prints it (it was Name Enchantment's alone).
    /// </summary>
    public override string GetCloseLookLine()
    {
        if (ownerIndex == 0 || ownerIndex == 40 || (ownerIndex >= 60 && ownerIndex != 63))
        {
            return null;
        }

        bool plural = type == EObjectType.PileOfBonesA || quantity > 1;
        string whose = ownerIndex == 63 ? "an adventurer" : DataLoader.GetCleanedObjectName(EObjectType.Rotworm + ownerIndex);
        return $"{StringLoader.GetString(1, plural ? 23 : 22)}{whose}.";
    }
}
